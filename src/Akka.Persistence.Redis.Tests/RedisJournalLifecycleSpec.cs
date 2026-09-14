// -----------------------------------------------------------------------
// <copyright file="RedisJournalLifecycleSpec.cs" company="Akka.NET Project">
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
    public class RedisJournalLifecycleSpec : Akka.TestKit.Xunit.TestKit, IClassFixture<RedisFixture>
    {
        private const int Database = 5;
        private readonly RedisFixture _fixture;

        public RedisJournalLifecycleSpec(ITestOutputHelper output, RedisFixture fixture)
            : base(ConfigurationFactory.Empty, nameof(RedisJournalLifecycleSpec), output)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task RedisJournal_should_dispose_owned_multiplexer_on_ActorSystem_shutdown()
        {
            // Independent admin observer — does not participate in the connection lifecycle under test.
            var observerConnString = $"{_fixture.ConnectionString},allowAdmin=true";
            using var observer = await ConnectionMultiplexer.ConnectAsync(observerConnString);

            var baseline = await TotalClientCountAsync(observer);

            var sideConfig = BuildSideSystemConfig(_fixture);
            var sideSystem = ActorSystem.Create(nameof(RedisJournalLifecycleSpec) + "-side", sideConfig);

            try
            {
                RedisPersistence.Get(sideSystem);

                // Force the journal to materialize its connection by issuing a no-op replay.
                var probe = new TestProbe(sideSystem, new XunitAssertions());
                var journal = Persistence.Instance.Apply(sideSystem).JournalFor(null);

                journal.Tell(
                    new ReplayMessages(0, 0, 0, "lifecycle-probe-" + Guid.NewGuid().ToString("N"), probe.Ref),
                    probe.Ref);
                probe.ExpectMsg<RecoverySuccess>(TimeSpan.FromSeconds(10));

                var withJournal = await TotalClientCountAsync(observer);
                Assert.True((withJournal) > (baseline));
            }
            finally
            {
                await sideSystem.Terminate();
            }

            // After CoordinatedShutdown, the journal's PostStop should dispose the multiplexer.
            // Redis updates CLIENT LIST asynchronously after the client TCP close, so allow a moment.
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
                akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
                akka.persistence.journal.redis {{
                    class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                    plugin-dispatcher = ""akka.actor.default-dispatcher""
                    configuration-string = ""{fixture.ConnectionString}""
                    database = {Database}
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
