// -----------------------------------------------------------------------
// <copyright file="RedisFencingSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Redis.Journal;
using Akka.Persistence.Redis.Snapshot;
using Akka.TestKit;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Cluster.Tests
{
    /// <summary>
    /// With Redis Cluster, a key that a transaction condition reads must share the persistence
    /// id's hash slot, like the plugin's own keys.
    /// </summary>
    [Collection("RedisClusterSpec")]
    public class RedisFencingSpec : Akka.TestKit.Xunit.TestKit
    {
        private const string Token = "1";
        private readonly RedisClusterFixture _fixture;
        private readonly IActorRef _journal;
        private readonly IActorRef _store;
        private readonly TestProbe _probe;

        public RedisFencingSpec(ITestOutputHelper output, RedisClusterFixture fixture)
            : base(Config(fixture), nameof(RedisFencingSpec), output)
        {
            _fixture = fixture;
            RedisPersistence.Get(Sys);
            _journal = Persistence.Instance.Apply(Sys).JournalFor(null);
            _store = Persistence.Instance.Apply(Sys).SnapshotStoreFor(null);
            _probe = CreateTestProbe();
        }

        // The fence key format is set per test through the persistence id: a persistence id that
        // starts with "tagged-" uses a hash-tagged fence key, any other one a plain key.
        private static Config Config(RedisClusterFixture fixture) => ConfigurationFactory.ParseString($@"
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Cluster.Tests.FencedRedisJournal, Akka.Persistence.Redis.Cluster.Tests""
                configuration-string = ""{fixture.ConnectionString}""
                fence-token = ""{Token}""
            }}
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.redis""
            akka.persistence.snapshot-store.redis {{
                class = ""Akka.Persistence.Redis.Cluster.Tests.FencedRedisSnapshotStore, Akka.Persistence.Redis.Cluster.Tests""
                configuration-string = ""{fixture.ConnectionString}""
                fence-token = ""{Token}""
            }}
            akka.test.single-expect-default = 10s")
            .WithFallback(RedisPersistence.DefaultConfig());

        [Fact(DisplayName = "A write and a snapshot save whose fence key carries the persistence id's hash tag must succeed")]
        public async Task Hash_tagged_fence_key_must_work()
        {
            var pid = "tagged-" + Guid.NewGuid().ToString("N");
            await SetAsync(Fence.KeyFor(pid), Token);

            Write(pid).Should().BeNull();

            _store.Tell(new SaveSnapshot(new SnapshotMetadata(pid, 1, DateTime.UtcNow), "s-1"), _probe.Ref);
            _probe.ExpectMsg<SaveSnapshotSuccess>();
        }

        [Fact(DisplayName = "A write whose fence key is in another hash slot must fail")]
        public async Task Fence_key_in_another_slot_must_fail()
        {
            var pid = "plain-" + Guid.NewGuid().ToString("N");
            await SetAsync(Fence.KeyFor(pid), Token);

            var failure = Write(pid);

            failure.Should().NotBeNull();
            failure!.Message.Should().ContainEquivalentOf("slot");
        }

        private async Task SetAsync(string key, string token)
        {
            using var connection = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);
            await connection.GetDatabase().StringSetAsync(key, token);
        }

        /// <summary>
        /// Writes event 1. Returns null on success, the failure cause otherwise.
        /// </summary>
        private Exception? Write(string pid)
        {
            var events = ImmutableList.Create<IPersistentRepresentation>(
                new Persistent("e-1", 1, pid, string.Empty, false, ActorRefs.NoSender, "writer"));
            _journal.Tell(new WriteMessages(new[] { new AtomicWrite(events) }, _probe.Ref, 1));

            switch (_probe.ExpectMsg<object>())
            {
                case WriteMessagesSuccessful _:
                    _probe.ExpectMsg<WriteMessageSuccess>();
                    return null;
                case WriteMessagesFailed failed:
                    _probe.ExpectMsg<WriteMessageFailure>();
                    return failed.Cause;
                case var other:
                    throw new Exception($"Unexpected reply {other}");
            }
        }
    }

    public sealed class FencedRedisJournal : RedisJournal
    {
        private readonly Fence _fence;

        public FencedRedisJournal(Config journalConfig) : base(journalConfig)
        {
            _fence = new Fence(journalConfig);
        }

        protected override ITransaction CreateTransaction(string persistenceId)
            => _fence.AddTo(base.CreateTransaction(persistenceId), persistenceId);
    }

    public sealed class FencedRedisSnapshotStore : RedisSnapshotStore
    {
        private readonly Fence _fence;

        public FencedRedisSnapshotStore(Config snapshotConfig) : base(snapshotConfig)
        {
            _fence = new Fence(snapshotConfig);
        }

        protected override ITransaction CreateTransaction(string persistenceId)
            => _fence.AddTo(base.CreateTransaction(persistenceId), persistenceId);
    }

    internal sealed class Fence
    {
        private readonly string _token;

        public Fence(Config config)
        {
            _token = config.GetString("fence-token");
        }

        /// <summary>
        /// The fence key of a persistence id: hash-tagged like the plugin's keys when the
        /// persistence id starts with "tagged-", a plain key otherwise.
        /// </summary>
        public static string KeyFor(string persistenceId) => persistenceId.StartsWith("tagged-", StringComparison.Ordinal)
            ? $"{{__{persistenceId}}}.fence"
            : $"fence:{persistenceId}";

        public ITransaction AddTo(ITransaction transaction, string persistenceId)
        {
            transaction.AddCondition(Condition.StringEqual(KeyFor(persistenceId), _token));
            return transaction;
        }
    }
}
