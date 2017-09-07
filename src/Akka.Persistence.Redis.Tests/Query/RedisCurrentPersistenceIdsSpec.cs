//-----------------------------------------------------------------------
// <copyright file="RedisCurrentPersistenceIdsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TCK.Query;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests.Query
{
    [Collection("RedisSpec")]
    public sealed class RedisCurrentPersistenceIdsSpec : CurrentPersistenceIdsSpec
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
            .WithFallback(RedisReadJournal.DefaultConfiguration());

        public RedisCurrentPersistenceIdsSpec(ITestOutputHelper output) : base(Config(Database), nameof(RedisCurrentPersistenceIdsSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);
        }

        [Fact(Skip = "Not implemented yet")]
        public override void ReadJournal_query_CurrentPersistenceIds_should_not_see_new_events_after_complete()
        {
        }

        protected override void Dispose(bool disposing)
        {
            DbUtils.Clean(Database);
            base.Dispose(disposing);
        }
    }
}
