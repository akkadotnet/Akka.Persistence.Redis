// -----------------------------------------------------------------------
// <copyright file="RedisDeferredClusterDetectionSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Redis.Cluster.Tests
{
    /// <summary>
    /// The journal and snapshot store must not pick their key names (hash-tagged for Redis
    /// Cluster, untagged otherwise) from a multiplexer that is not connected yet: with
    /// <c>abortConnect=false</c> (the StackExchange.Redis default for Azure hosts),
    /// <c>ConnectionMultiplexer.Connect</c> returns a disconnected multiplexer whose
    /// endpoints all report <c>ServerType.Standalone</c>.
    /// </summary>
    [Collection("RedisClusterSpec")]
    public class RedisDeferredClusterDetectionSpec : Akka.TestKit.Xunit.TestKit
    {
        private readonly RedisClusterFixture _fixture;

        public RedisDeferredClusterDetectionSpec(ITestOutputHelper output, RedisClusterFixture fixture)
            : base(ConfigurationFactory.Empty, nameof(RedisDeferredClusterDetectionSpec), output)
        {
            _fixture = fixture;
        }

        [Fact(DisplayName = "Journal and snapshot store started before the cluster connection is established must recover from, and write to, the cluster keys")]
        public async Task Plugins_started_before_connect_must_use_cluster_keys()
        {
            var persistenceId = "deferred-cluster-" + Guid.NewGuid().ToString("N");

            // Seed state through plugins whose multiplexer is connected at construction: the
            // event and snapshot land in the hash-tagged cluster keys.
            var seedSystem = ActorSystem.Create("seed", PluginConfig(_fixture.ConnectionString));
            try
            {
                var seedProbe = new TestProbe(seedSystem, new XunitAssertions());
                var seed = seedSystem.ActorOf(Props.Create(() => new RecordingActor(persistenceId, seedProbe.Ref)));
                seedProbe.ExpectMsg<RecoveredState>(TimeSpan.FromSeconds(15)).Events.Should().BeEmpty();
                seed.Tell("persist:a");
                seedProbe.ExpectMsg("persisted:a", TimeSpan.FromSeconds(10));
                seed.Tell("snapshot");
                seedProbe.ExpectMsg("snapshot-saved", TimeSpan.FromSeconds(10));
                seed.Tell("persist:b");
                seedProbe.ExpectMsg("persisted:b", TimeSpan.FromSeconds(10));
            }
            finally
            {
                await seedSystem.Terminate();
            }

            // Start the plugins while their only seed endpoint is unreachable.
            var node = _fixture.ConnectionString.Split(',')[0].Split(':');
            using var proxy = new GatedTcpProxy(node[0], int.Parse(node[1]));
            var coldConnectionString = $"127.0.0.1:{proxy.Port},abortConnect=false,connectTimeout=500,connectRetry=1";
            var coldSystem = ActorSystem.Create("cold", PluginConfig(coldConnectionString));
            try
            {
                var probe = new TestProbe(coldSystem, new XunitAssertions());
                var persistence = Persistence.Instance.Apply(coldSystem);

                // ActorIdentity is only answered after the plugin constructors have returned.
                persistence.JournalFor(null).Tell(new Identify(1), probe.Ref);
                probe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(30)).Subject.Should().NotBeNull();
                persistence.SnapshotStoreFor(null).Tell(new Identify(2), probe.Ref);
                probe.ExpectMsg<ActorIdentity>(TimeSpan.FromSeconds(30)).Subject.Should().NotBeNull();

                proxy.Open();

                // Until the multiplexer reconnects, recovery may fail (the plugin refuses to guess
                // the cluster mode); it must never succeed with empty state.
                var recovered = await RecoverAsync(coldSystem, probe, persistenceId, TimeSpan.FromSeconds(60));
                recovered.SnapshotEvents.Should().Equal(new[] { "a" },
                    "the snapshot saved under the cluster key must be offered on recovery");
                recovered.Events.Should().Equal(new[] { "a", "b" },
                    "the event persisted after the snapshot must be replayed from the cluster key");

                // The journal write is a multi-key operation; with untagged keys it is rejected
                // by the cluster with CROSSSLOT.
                recovered.Actor.Tell("persist:c");
                probe.ExpectMsg("persisted:c", TimeSpan.FromSeconds(10));
            }
            finally
            {
                await coldSystem.Terminate();
            }
        }

        private static async Task<RecoveredState> RecoverAsync(ActorSystem system, TestProbe probe, string persistenceId, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (true)
            {
                var actor = system.ActorOf(Props.Create(() => new RecordingActor(persistenceId, probe.Ref)));
                probe.Watch(actor);
                var message = probe.ExpectMsg<object>(TimeSpan.FromSeconds(30));
                if (message is RecoveredState state)
                {
                    probe.Unwatch(actor);
                    state.Actor = actor;
                    return state;
                }

                message.Should().BeOfType<Terminated>("a persistent actor either recovers or stops on recovery failure");
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException("Recovery did not succeed before the deadline.");
                await Task.Delay(500);
            }
        }

        private static Config PluginConfig(string connectionString) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis.configuration-string = ""{connectionString}""
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.redis""
            akka.persistence.snapshot-store.redis.configuration-string = ""{connectionString}""")
            .WithFallback(RedisPersistence.DefaultConfig());

        private sealed class RecoveredState
        {
            public RecoveredState(IReadOnlyList<string> snapshotEvents, IReadOnlyList<string> events)
            {
                SnapshotEvents = snapshotEvents;
                Events = events;
            }

            public IReadOnlyList<string> SnapshotEvents { get; }

            public IReadOnlyList<string> Events { get; }

            public IActorRef Actor { get; set; } = ActorRefs.Nobody;
        }

        private sealed class RecordingActor : ReceivePersistentActor
        {
            private readonly IActorRef _probe;
            private List<string> _events = new();
            private string[] _snapshotEvents = Array.Empty<string>();

            public RecordingActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;
                _probe = probe;

                Recover<SnapshotOffer>(offer =>
                {
                    _snapshotEvents = (string[])offer.Snapshot;
                    _events = _snapshotEvents.ToList();
                });
                Recover<string>(e => _events.Add(e));
                Recover<RecoveryCompleted>(_ => probe.Tell(new RecoveredState(_snapshotEvents, _events.ToArray())));

                Command<string>(cmd => cmd.StartsWith("persist:"), cmd =>
                {
                    Persist(cmd.Substring("persist:".Length), e =>
                    {
                        _events.Add(e);
                        probe.Tell("persisted:" + e);
                    });
                });
                Command<string>(cmd => cmd == "snapshot", _ => SaveSnapshot(_events.ToArray()));
                Command<SaveSnapshotSuccess>(_ => probe.Tell("snapshot-saved"));
                Command<SaveSnapshotFailure>(f => probe.Tell("snapshot-failed: " + f.Cause.Message));
            }

            public override string PersistenceId { get; }

            protected override void OnPersistRejected(Exception cause, object @event, long sequenceNr)
            {
                _probe.Tell("rejected: " + cause.Message);
                base.OnPersistRejected(cause, @event, sequenceNr);
            }
        }
    }
}
