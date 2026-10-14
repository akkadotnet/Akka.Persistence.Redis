// -----------------------------------------------------------------------
// <copyright file="RedisSnapshotStoreSameSequenceNrSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    /// <summary>
    /// Every save adds a member to the snapshot sorted set, so a persistent actor that saves
    /// twice without persisting in between stores two snapshots at one sequence number.
    /// Deleting one of them by its timestamp must not delete the other.
    /// </summary>
    [Collection("RedisSpec")]
    public class RedisSnapshotStoreSameSequenceNrSpec : Akka.TestKit.Xunit.TestKit
    {
        private const int Database = 7;
        private readonly RedisFixture _fixture;
        private readonly IActorRef _store;
        private readonly TestProbe _probe;

        public RedisSnapshotStoreSameSequenceNrSpec(ITestOutputHelper output, RedisFixture fixture)
            : base(Config(fixture), nameof(RedisSnapshotStoreSameSequenceNrSpec), output)
        {
            _fixture = fixture;
            RedisPersistence.Get(Sys);
            _store = Persistence.Instance.Apply(Sys).SnapshotStoreFor(null);
            _probe = CreateTestProbe();
        }

        private static Config Config(RedisFixture fixture) => ConfigurationFactory.ParseString($@"
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.redis""
            akka.persistence.snapshot-store.redis {{
                configuration-string = ""{fixture.ConnectionString}""
                database = {Database}
            }}")
            .WithFallback(RedisPersistence.DefaultConfig());

        [Fact(DisplayName = "DeleteSnapshot with a timestamp must keep a newer snapshot at the same sequence number")]
        public async Task Metadata_delete_must_keep_newer_snapshot_at_same_sequence_number()
        {
            var pid = NewPersistenceId();
            var t1 = DateTime.UtcNow;
            var older = Save(pid, 3, t1, "older");
            Save(pid, 3, t1.AddSeconds(5), "newer");

            _store.Tell(new DeleteSnapshot(older), _probe.Ref);
            _probe.ExpectMsg<DeleteSnapshotSuccess>();

            (await CountAsync(pid)).Should().Be(1);
            LoadLatest(pid).Should().Be("newer");
        }

        [Fact(DisplayName = "DeleteSnapshots with a timestamp bound must keep a newer snapshot at the same sequence number")]
        public async Task Criteria_delete_must_keep_newer_snapshot_at_same_sequence_number()
        {
            var pid = NewPersistenceId();
            var t1 = DateTime.UtcNow;
            Save(pid, 3, t1, "older");
            Save(pid, 3, t1.AddSeconds(5), "newer");

            _store.Tell(new DeleteSnapshots(pid, new SnapshotSelectionCriteria(3, t1)), _probe.Ref);
            _probe.ExpectMsg<DeleteSnapshotsSuccess>();

            (await CountAsync(pid)).Should().Be(1);
            LoadLatest(pid).Should().Be("newer");
        }

        [Fact(DisplayName = "DeleteSnapshot without a timestamp must delete every snapshot at the sequence number")]
        public async Task Metadata_delete_without_timestamp_must_delete_all_at_sequence_number()
        {
            // PersistentActor.DeleteSnapshot(seqNr) sends no timestamp; it identifies the
            // sequence number, as in the local snapshot store.
            var pid = NewPersistenceId();
            var t1 = DateTime.UtcNow;
            Save(pid, 3, t1, "older");
            Save(pid, 3, t1.AddSeconds(5), "newer");

            _store.Tell(new DeleteSnapshot(new SnapshotMetadata(pid, 3, DateTime.MinValue)), _probe.Ref);
            _probe.ExpectMsg<DeleteSnapshotSuccess>();

            (await CountAsync(pid)).Should().Be(0);
        }

        private static string NewPersistenceId() => "same-seq-" + Guid.NewGuid().ToString("N");

        private SnapshotMetadata Save(string pid, long sequenceNr, DateTime timestamp, object snapshot)
        {
            var metadata = new SnapshotMetadata(pid, sequenceNr, timestamp);
            _store.Tell(new SaveSnapshot(metadata, snapshot), _probe.Ref);
            _probe.ExpectMsg<SaveSnapshotSuccess>();
            return metadata;
        }

        private object? LoadLatest(string pid)
        {
            _store.Tell(new LoadSnapshot(pid, SnapshotSelectionCriteria.Latest, long.MaxValue), _probe.Ref);
            return _probe.ExpectMsg<LoadSnapshotResult>().Snapshot?.Snapshot;
        }

        /// <summary>
        /// Counts the stored snapshots on every server of the fixture: it has two standalone
        /// primaries, and the plugin's multiplexer writes to one of them.
        /// </summary>
        private async Task<long> CountAsync(string pid)
        {
            long total = 0;
            foreach (var endpoint in _fixture.ConnectionString.Split(','))
            {
                using var connection = await ConnectionMultiplexer.ConnectAsync(endpoint);
                total += await connection.GetDatabase(Database).SortedSetLengthAsync($"snapshot:{pid}");
            }

            return total;
        }
    }
}
