// -----------------------------------------------------------------------
// <copyright file="RedisJournalDeferSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests;

[Collection("RedisSpec")]
public class RedisJournalDeferSpec: Akka.TestKit.Xunit2.TestKit
{
    private const int Database = 1;

    private static Config Config(RedisFixture fixture, int id)
    {
        DbUtils.Initialize(fixture);

        return ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                configuration-string = ""{fixture.ConnectionString}""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisPersistence.DefaultConfig());
    }
    
    public RedisJournalDeferSpec(ITestOutputHelper output, RedisFixture fixture) 
        : base(Config(fixture, Database), nameof(RedisJournalDeferSpec), output)
    {
        RedisPersistence.Get(Sys);
    }
    
    // Reproduction / regression test for https://github.com/akkadotnet/Akka.Persistence.Redis/issues/455
    [Fact(DisplayName = "DeferAsync must not cause circuit breaker to fail")]
    public async Task DeferAsyncCircuitBreakerFailure()
    {
        var actor = Sys.ActorOf(Props.Create(() => new DeferAsyncActor()));

        foreach (var i in Enumerable.Range(1, 10))
        {
            actor.Tell(i.ToString());
            await ExpectMsgAsync($"Done-{i}");
        }
        
        // 1 message that will result in a Persist call to trigger the failure.
        actor.Tell("persist");
        await ExpectMsgAsync("Done-persist");
    }
    
    private class DeferAsyncActor: ReceivePersistentActor
    {
        public override string PersistenceId => "defer-async-actor";

        public DeferAsyncActor()
        {
            var log = Context.GetLogger();

            Command<string>(msg => {
                if (msg.Contains("persist"))
                {
                    Persist(msg, @event => {
                        log.Info($"Persisted: {@event}");
                    });
                }
                DeferAsync(msg, @event => {
                    log.Info($"Deferring: {@event}");
                    Sender.Tell($"Done-{msg}", Self);
                    return Task.CompletedTask;
                });
            });
        }
    }    
}