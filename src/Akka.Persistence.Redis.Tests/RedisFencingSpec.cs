// -----------------------------------------------------------------------
// <copyright file="RedisFencingSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Redis.Journal;
using Akka.TestKit;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    /// <summary>
    /// A subclass can add conditions to the transactions of the journal and snapshot store, for
    /// example a fencing token that a stale incarnation of a persistent actor no longer holds.
    /// When a condition is not met, every write and delete must fail and apply nothing.
    /// </summary>
    [Collection("RedisSpec")]
    public class RedisFencingSpec : Akka.TestKit.Xunit.TestKit
    {
        private const int Database = 9;
        private const string Token = "1";
        private readonly RedisFixture _fixture;
        private readonly IActorRef _journal;
        private readonly IActorRef _store;
        private readonly TestProbe _probe;

        public RedisFencingSpec(ITestOutputHelper output, RedisFixture fixture)
            : base(Config(fixture), nameof(RedisFencingSpec), output)
        {
            _fixture = fixture;
            RedisPersistence.Get(Sys);
            _journal = Persistence.Instance.Apply(Sys).JournalFor(null);
            _store = Persistence.Instance.Apply(Sys).SnapshotStoreFor(null);
            _probe = CreateTestProbe();
        }

        // The replay filter is off so that replays show exactly what is stored.
        private static Config Config(RedisFixture fixture) => ConfigurationFactory.ParseString($@"
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Tests.FencedRedisJournal, Akka.Persistence.Redis.Tests""
                configuration-string = ""{fixture.ConnectionString}""
                database = {Database}
                replay-filter.mode = off
                fence-key-format = ""fence:{{0}}""
                fence-token = ""{Token}""
            }}
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.redis""
            akka.persistence.snapshot-store.redis {{
                class = ""Akka.Persistence.Redis.Tests.FencedRedisSnapshotStore, Akka.Persistence.Redis.Tests""
                configuration-string = ""{fixture.ConnectionString}""
                database = {Database}
                fence-key-format = ""fence:{{0}}""
                fence-token = ""{Token}""
            }}
            akka.test.single-expect-default = 5s")
            .WithFallback(RedisPersistence.DefaultConfig());

        [Fact(DisplayName = "A write whose fence condition fails must fail and store nothing")]
        public void Fenced_write_must_fail()
        {
            var pid = NewPersistenceId();

            var failure = Write(pid, 1, 2);

            failure.Should().BeOfType<RedisTransactionNotCommittedException>()
                .Which.PersistenceId.Should().Be(pid);
            var (events, highest) = Replay(pid);
            events.Should().BeEmpty();
            highest.Should().Be(0);
        }

        [Fact(DisplayName = "A write must succeed again once the fence condition holds")]
        public async Task Write_must_succeed_once_fence_holds()
        {
            var pid = NewPersistenceId();
            Write(pid, 1, 1).Should().BeOfType<RedisTransactionNotCommittedException>();

            await SetFenceAsync(pid, Token);

            Write(pid, 1, 2).Should().BeNull();
            Replay(pid).Events.Should().Equal("e-1", "e-2");
        }

        [Fact(DisplayName = "A fenced write of a persistent actor must call OnPersistFailure and stop the actor")]
        public async Task Fenced_persistent_actor_must_fail_and_stop()
        {
            var pid = NewPersistenceId();
            await SetFenceAsync(pid, Token);
            var actor = Sys.ActorOf(Props.Create(() => new WriterActor(pid, _probe.Ref)));
            _probe.ExpectMsg("recovered:0");
            _probe.Watch(actor);

            // A newer incarnation took over and moved the fence on.
            await SetFenceAsync(pid, "2");

            actor.Tell("e-1");
            _probe.ExpectMsg($"failed:{nameof(RedisTransactionNotCommittedException)}");
            _probe.ExpectTerminated(actor);
            Replay(pid).Events.Should().BeEmpty();
        }

        [Fact(DisplayName = "A sequence-number conflict must still be reported as a write conflict when the fence holds")]
        public async Task Conflict_must_be_reported_as_conflict_when_fence_holds()
        {
            var pid = NewPersistenceId();
            await SetFenceAsync(pid, Token);
            Write(pid, 1, 2).Should().BeNull();

            Write(pid, 2, 2).Should().BeOfType<RedisJournalWriteConflictException>();
        }

        [Fact(DisplayName = "A write must succeed after the server's script cache was flushed")]
        public async Task Write_must_succeed_after_script_flush()
        {
            // The append script runs inside the transaction; it must be re-sent when the server no
            // longer knows it (restart, failover, SCRIPT FLUSH), not fail with NOSCRIPT.
            var pid = NewPersistenceId();
            await SetFenceAsync(pid, Token);
            Write(pid, 1, 1).Should().BeNull();

            foreach (var endpoint in _fixture.ConnectionString.Split(','))
            {
                using var connection = await ConnectionMultiplexer.ConnectAsync(endpoint + ",allowAdmin=true");
                await connection.GetServer(endpoint).ScriptFlushAsync();
            }

            Write(pid, 2, 2).Should().BeNull();
            Replay(pid).Events.Should().Equal("e-1", "e-2");
        }

        [Fact(DisplayName = "DeleteMessagesTo whose fence condition fails must fail and delete nothing")]
        public async Task Fenced_delete_messages_must_fail()
        {
            var pid = NewPersistenceId();
            await SetFenceAsync(pid, Token);
            Write(pid, 1, 3).Should().BeNull();
            await SetFenceAsync(pid, "2");

            _journal.Tell(new DeleteMessagesTo(pid, 3, _probe.Ref));

            _probe.ExpectMsg<DeleteMessagesFailure>().Cause.Should().BeOfType<RedisTransactionNotCommittedException>();
            Replay(pid).Events.Should().Equal("e-1", "e-2", "e-3");
        }

        [Fact(DisplayName = "SaveSnapshot whose fence condition fails must fail and store nothing")]
        public void Fenced_save_snapshot_must_fail()
        {
            var pid = NewPersistenceId();

            _store.Tell(new SaveSnapshot(new SnapshotMetadata(pid, 1, DateTime.UtcNow), "s-1"), _probe.Ref);

            _probe.ExpectMsg<SaveSnapshotFailure>().Cause.Should().BeOfType<RedisTransactionNotCommittedException>();
            LoadLatest(pid).Should().BeNull();
        }

        [Fact(DisplayName = "DeleteSnapshot without a timestamp whose fence condition fails must fail and delete nothing")]
        public async Task Fenced_delete_snapshot_without_timestamp_must_fail()
        {
            var pid = NewPersistenceId();
            await SaveFencedAsync(pid, 3, DateTime.UtcNow);

            _store.Tell(new DeleteSnapshot(new SnapshotMetadata(pid, 3, DateTime.MinValue)), _probe.Ref);

            _probe.ExpectMsg<DeleteSnapshotFailure>().Cause.Should().BeOfType<RedisTransactionNotCommittedException>();
            LoadLatest(pid).Should().Be("s-3");
        }

        [Fact(DisplayName = "DeleteSnapshot with a timestamp whose fence condition fails must fail and delete nothing")]
        public async Task Fenced_delete_snapshot_with_timestamp_must_fail()
        {
            var pid = NewPersistenceId();
            var metadata = await SaveFencedAsync(pid, 3, DateTime.UtcNow);

            _store.Tell(new DeleteSnapshot(metadata), _probe.Ref);

            _probe.ExpectMsg<DeleteSnapshotFailure>().Cause.Should().BeOfType<RedisTransactionNotCommittedException>();
            LoadLatest(pid).Should().Be("s-3");
        }

        [Fact(DisplayName = "DeleteSnapshots by sequence number whose fence condition fails must fail and delete nothing")]
        public async Task Fenced_delete_snapshots_by_sequence_number_must_fail()
        {
            var pid = NewPersistenceId();
            await SaveFencedAsync(pid, 3, DateTime.UtcNow);

            _store.Tell(new DeleteSnapshots(pid, new SnapshotSelectionCriteria(3)), _probe.Ref);

            _probe.ExpectMsg<DeleteSnapshotsFailure>().Cause.Should().BeOfType<RedisTransactionNotCommittedException>();
            LoadLatest(pid).Should().Be("s-3");
        }

        [Fact(DisplayName = "DeleteSnapshots with a timestamp bound whose fence condition fails must fail and delete nothing")]
        public async Task Fenced_delete_snapshots_with_timestamp_must_fail()
        {
            var pid = NewPersistenceId();
            var metadata = await SaveFencedAsync(pid, 3, DateTime.UtcNow);

            _store.Tell(new DeleteSnapshots(pid, new SnapshotSelectionCriteria(3, metadata.Timestamp)), _probe.Ref);

            _probe.ExpectMsg<DeleteSnapshotsFailure>().Cause.Should().BeOfType<RedisTransactionNotCommittedException>();
            LoadLatest(pid).Should().Be("s-3");
        }

        private static string NewPersistenceId() => "fencing-" + Guid.NewGuid().ToString("N");

        private Task SetFenceAsync(string pid, string token) => Fence.SetAsync(_fixture, Database, $"fence:{pid}", token);

        /// <summary>
        /// Saves snapshot <c>s-{sequenceNr}</c> while the fence holds, then moves the fence on.
        /// </summary>
        private async Task<SnapshotMetadata> SaveFencedAsync(string pid, long sequenceNr, DateTime timestamp)
        {
            await SetFenceAsync(pid, Token);
            var metadata = new SnapshotMetadata(pid, sequenceNr, timestamp);
            _store.Tell(new SaveSnapshot(metadata, $"s-{sequenceNr}"), _probe.Ref);
            _probe.ExpectMsg<SaveSnapshotSuccess>();
            await SetFenceAsync(pid, "2");
            return metadata;
        }

        private object? LoadLatest(string pid)
        {
            _store.Tell(new LoadSnapshot(pid, SnapshotSelectionCriteria.Latest, long.MaxValue), _probe.Ref);
            return _probe.ExpectMsg<LoadSnapshotResult>().Snapshot?.Snapshot;
        }

        /// <summary>
        /// Writes events [from..to] in one atomic write.
        /// Returns null on success, the rejection or failure cause otherwise.
        /// </summary>
        private Exception? Write(string pid, long from, long to)
        {
            var events = Enumerable.Range((int)from, (int)(to - from + 1))
                .Select(seq => (IPersistentRepresentation)new Persistent(
                    $"e-{seq}", seq, pid, string.Empty, false, ActorRefs.NoSender, "writer"))
                .ToImmutableList();
            _journal.Tell(new WriteMessages(new[] { new AtomicWrite(events) }, _probe.Ref, 1));

            switch (_probe.ExpectMsg<object>())
            {
                case WriteMessagesSuccessful _:
                    var results = Enumerable.Range(0, events.Count).Select(_ => _probe.ExpectMsg<object>()).ToArray();
                    var rejected = results.OfType<WriteMessageRejected>().FirstOrDefault();
                    if (rejected is not null)
                        return rejected.Cause;
                    results.Should().AllBeOfType<WriteMessageSuccess>();
                    return null;
                case WriteMessagesFailed failed:
                    for (var i = 0; i < events.Count; i++)
                        _probe.ExpectMsg<WriteMessageFailure>();
                    return failed.Cause;
                case var other:
                    throw new Exception($"Unexpected reply {other}");
            }
        }

        private (IReadOnlyList<string> Events, long Highest) Replay(string pid)
        {
            _journal.Tell(new ReplayMessages(1, long.MaxValue, long.MaxValue, pid, _probe.Ref));
            var events = new List<string>();
            while (true)
            {
                switch (_probe.ExpectMsg<object>())
                {
                    case ReplayedMessage replayed:
                        events.Add((string)replayed.Persistent.Payload);
                        break;
                    case RecoverySuccess success:
                        return (events, success.HighestSequenceNr);
                    case var other:
                        throw new Exception($"Unexpected reply {other}");
                }
            }
        }

        private sealed class WriterActor : ReceivePersistentActor
        {
            private readonly IActorRef _probe;

            public WriterActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;

                Recover<RecoveryCompleted>(_ => probe.Tell($"recovered:{LastSequenceNr}"));
                Command<string>(e => Persist(e, _ => probe.Tell($"persisted:{e}")));
            }

            public override string PersistenceId { get; }

            protected override void OnPersistFailure(Exception cause, object @event, long sequenceNr)
            {
                _probe.Tell($"failed:{cause.GetType().Name}");
                base.OnPersistFailure(cause, @event, sequenceNr);
            }

            protected override void OnPersistRejected(Exception cause, object @event, long sequenceNr)
            {
                _probe.Tell($"rejected:{cause.GetType().Name}");
                base.OnPersistRejected(cause, @event, sequenceNr);
            }
        }
    }
}
