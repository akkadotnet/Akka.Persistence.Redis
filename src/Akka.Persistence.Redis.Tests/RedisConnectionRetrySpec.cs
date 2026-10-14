// -----------------------------------------------------------------------
// <copyright file="RedisConnectionRetrySpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    /// <summary>
    /// A connection factory that fails at plugin startup must not leave the journal or the
    /// snapshot store dead until the process restarts.
    /// </summary>
    [Collection("RedisSpec")]
    public class RedisConnectionRetrySpec : Akka.TestKit.Xunit.TestKit
    {
        private const int Database = 6;
        private readonly RedisFixture _fixture;

        public RedisConnectionRetrySpec(ITestOutputHelper output, RedisFixture fixture)
            : base(ConfigurationFactory.Empty, nameof(RedisConnectionRetrySpec), output)
        {
            _fixture = fixture;
        }

        [Fact(DisplayName = "Journal and snapshot store must create a new connection after the connection factory failed")]
        public async Task Plugins_must_retry_a_failed_connection_factory()
        {
            var journalFactory = new FlakyFactory(_fixture.ConnectionString, failures: 2);
            var snapshotFactory = new FlakyFactory(_fixture.ConnectionString, failures: 2);
            var setup = new RedisConnectionMultiplexerSetup()
                .Add(RedisPersistence.JournalConfigPath, journalFactory.CreateAsync, RedisConnectionOwnership.PluginOwned)
                .Add(RedisPersistence.SnapshotConfigPath, snapshotFactory.CreateAsync, RedisConnectionOwnership.PluginOwned);
            var system = ActorSystem.Create(
                nameof(RedisConnectionRetrySpec) + "-side",
                ActorSystemSetup.Create(BootstrapSetup.Create().WithConfig(PluginConfig()), setup));

            try
            {
                var probe = new TestProbe(system, new XunitAssertions());
                var persistenceId = "connection-retry-" + Guid.NewGuid().ToString("N");

                // Each failing factory call fails the operation that is waiting for it (the actor
                // stops on recovery failure); a later operation must try the factory again.
                var actor = await RecoverAsync(system, probe, persistenceId, attempts: 5);

                actor.Tell("persist");
                probe.ExpectMsg("persisted", TimeSpan.FromSeconds(10));
                actor.Tell("snapshot");
                probe.ExpectMsg("snapshot-saved", TimeSpan.FromSeconds(10));

                journalFactory.Calls.Should().Be(3, "the journal must call the factory again after each failure, and stop once it succeeded");
                snapshotFactory.Calls.Should().Be(3, "the snapshot store must call the factory again after each failure, and stop once it succeeded");
            }
            finally
            {
                await system.Terminate();
            }
        }

        private static async Task<IActorRef> RecoverAsync(ActorSystem system, TestProbe probe, string persistenceId, int attempts)
        {
            for (var attempt = 1; ; attempt++)
            {
                var actor = system.ActorOf(Props.Create(() => new ProbeActor(persistenceId, probe.Ref)));
                probe.Watch(actor);
                var message = probe.ExpectMsg<object>(TimeSpan.FromSeconds(15));
                if (message is "recovered")
                {
                    probe.Unwatch(actor);
                    return actor;
                }

                message.Should().BeOfType<Terminated>("a persistent actor either recovers or stops on recovery failure");
                if (attempt == attempts)
                    throw new Exception($"Recovery did not succeed in {attempts} attempts.");
                await Task.Delay(200);
            }
        }

        private Config PluginConfig() => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis.database = {Database}
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.redis""
            akka.persistence.snapshot-store.redis.database = {Database}")
            .WithFallback(RedisPersistence.DefaultConfig());

        private sealed class FlakyFactory
        {
            private readonly string _connectionString;
            private readonly int _failures;
            private int _calls;

            public FlakyFactory(string connectionString, int failures)
            {
                _connectionString = connectionString;
                _failures = failures;
            }

            public int Calls => Volatile.Read(ref _calls);

            public async Task<IConnectionMultiplexer> CreateAsync()
            {
                if (Interlocked.Increment(ref _calls) <= _failures)
                    throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "transient startup failure");

                return await ConnectionMultiplexer.ConnectAsync(_connectionString);
            }
        }

        private sealed class ProbeActor : ReceivePersistentActor
        {
            public ProbeActor(string persistenceId, IActorRef probe)
            {
                PersistenceId = persistenceId;

                Recover<RecoveryCompleted>(_ => probe.Tell("recovered"));
                Command<string>(cmd => cmd == "persist", cmd => Persist(cmd, _ => probe.Tell("persisted")));
                Command<string>(cmd => cmd == "snapshot", _ => SaveSnapshot("state"));
                Command<SaveSnapshotSuccess>(_ => probe.Tell("snapshot-saved"));
                Command<SaveSnapshotFailure>(f => probe.Tell("snapshot-failed: " + f.Cause.Message));
            }

            public override string PersistenceId { get; }
        }
    }
}
