//-----------------------------------------------------------------------
// <copyright file="RedisJournalPerfSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TestKit.Performance;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests
{
    public class RedisJournalPerfSpec : JournalPerfSpec
    {
        public const int Database = 1;

        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                configuration-string = ""127.0.0.1:6379""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisReadJournal.DefaultConfiguration())
            .WithFallback(Persistence.DefaultConfig());


        public RedisJournalPerfSpec(ITestOutputHelper output) : base(Config(Database), nameof(RedisJournalPerfSpec), output)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }
    }
}
