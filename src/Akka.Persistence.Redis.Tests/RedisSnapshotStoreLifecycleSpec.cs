// -----------------------------------------------------------------------
// <copyright file="RedisSnapshotStoreLifecycleSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    [Collection("RedisSpec")]
    public class RedisSnapshotStoreLifecycleSpec : Akka.TestKit.Xunit.TestKit, IClassFixture<RedisFixture>
    {
        private const int Database = 5;
        private readonly RedisFixture _fixture;

        public RedisSnapshotStoreLifecycleSpec(ITestOutputHelper output, RedisFixture fixture)
            : base(ConfigurationFactory.Empty, nameof(RedisSnapshotStoreLifecycleSpec), output)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task RedisSnapshotStore_should_dispose_owned_multiplexer_on_ActorSystem_shutdown()
        {
            var observerConnString = $"{_fixture.ConnectionString},allowAdmin=true";
            using var observer = await ConnectionMultiplexer.ConnectAsync(observerConnString);

            var baseline = await TotalClientCountAsync(observer);

            var sideConfig = BuildSideSystemConfig(_fixture);
            var sideSystem = ActorSystem.Create(nameof(RedisSnapshotStoreLifecycleSpec) + "-side", sideConfig);

            try
            {
                RedisPersistence.Get(sideSystem);

                // Force the snapshot store to materialize by issuing a load against an unused id.
                var probe = new TestProbe(sideSystem, new XunitAssertions());
                var snapshotStore = Persistence.Instance.Apply(sideSystem).SnapshotStoreFor(null);

                snapshotStore.Tell(
                    new LoadSnapshot(
                        "lifecycle-probe-" + Guid.NewGuid().ToString("N"),
                        SnapshotSelectionCriteria.Latest,
                        long.MaxValue),
                    probe.Ref);
                probe.ExpectMsg<LoadSnapshotResult>(TimeSpan.FromSeconds(10));

                var withSnapshotStore = await TotalClientCountAsync(observer);
                Assert.True((withSnapshotStore) > (baseline));
            }
            finally
            {
                await sideSystem.Terminate();
            }

            AwaitAssert(
                () =>
                {
                    var afterShutdown = TotalClientCountAsync(observer).GetAwaiter().GetResult();
                    Assert.True(afterShutdown <= baseline,
                        "the owned multiplexer should be disposed on PostStop, releasing every connection it opened");
                },
                TimeSpan.FromSeconds(10));
        }

        private static Config BuildSideSystemConfig(RedisFixture fixture) =>
            ConfigurationFactory.ParseString($@"
                akka.loglevel = INFO
                akka.persistence {{
                    snapshot-store {{
                        plugin = ""akka.persistence.snapshot-store.redis""
                        redis {{
                            class = ""Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            configuration-string = ""{fixture.ConnectionString}""
                            database = {Database}
                        }}
                    }}
                }}")
                .WithFallback(RedisPersistence.DefaultConfig());

        private static async Task<int> TotalClientCountAsync(IConnectionMultiplexer observer)
        {
            var counts = await Task.WhenAll(
                observer.GetEndPoints().Select(async endpoint =>
                {
                    var server = observer.GetServer(endpoint);
                    var clients = await server.ClientListAsync();
                    return clients.Length;
                }));

            return counts.Sum();
        }
    }
}
