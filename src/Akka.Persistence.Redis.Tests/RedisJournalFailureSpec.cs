//-----------------------------------------------------------------------
// <copyright file="RedisJournalFailureSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TCK;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests
{
    [Collection("RedisSpec")]
    public class RedisJournalFailureSpec : PluginSpec
    {
        public const int Database = 40;

        public static Config SpecConfig(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                configuration-string = ""127.0.0.1:6379""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisReadJournal.DefaultConfiguration());

        public RedisJournalFailureSpec(ITestOutputHelper output) : base(SpecConfig(Database), nameof(RedisJournalSpec), output)
        {

        }

        protected IActorRef Journal => Extension.JournalFor(null);

        [Fact(Skip = "Not implemented yet")]
        public void WriteMessages_should_return_WriteMessagesFailed_on_wrong_connection()
        {
            var probe = CreateTestProbe();

            var messages = new List<AtomicWrite>()
            {
                new AtomicWrite(new Persistent("a", 1, Pid))
            };

            Journal.Tell(new WriteMessages(messages, probe.Ref, ActorInstanceId));
            probe.ExpectMsg<WriteMessagesFailed>();
        }

        [Fact(Skip = "Not implemented yet")]
        public void WriteMessages_should_return_ReplayMessagesFailure_on_wrong_connection()
        {
            var probe = CreateTestProbe();

            var messages = new List<AtomicWrite>()
            {
                new AtomicWrite(new Persistent("a", 1, Pid))
            };

            Journal.Tell(new ReplayMessages(1, long.MaxValue, long.MaxValue, Pid, probe.Ref));
            probe.ExpectMsg<ReplayMessagesFailure>(x => x.Cause.GetType() == typeof(RedisCommandException));
        }

        [Fact(Skip = "Not implemented yet")]
        public void DeleteMessages_should_return_DeleteMessagesFailure_on_wrong_connection()
        {
            var probe = CreateTestProbe();

            var messages = new List<AtomicWrite>()
            {
                new AtomicWrite(new Persistent("a", 1, Pid))
            };

            Journal.Tell(new DeleteMessagesTo(Pid, 3, probe.Ref));
            probe.ExpectMsg<DeleteMessagesFailure>(x => x.Cause.GetType() == typeof(RedisCommandException));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }
    }
}