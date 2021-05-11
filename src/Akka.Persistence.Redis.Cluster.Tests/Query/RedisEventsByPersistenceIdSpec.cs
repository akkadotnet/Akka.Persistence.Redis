//-----------------------------------------------------------------------
// <copyright file="RedisEventsByPersistenceIdSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TCK.Query;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Cluster.Tests.Query
{
    [Collection("RedisClusterSpec")]
    public class RedisEventsByPersistenceIdSpec : EventsByPersistenceIdSpec
    {
        public static Config Config(RedisClusterFixture fixture)
        {
            DbUtils.Initialize(fixture);

            return ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                configuration-string = ""{fixture.ConnectionString}""
            }}
            akka.test.single-expect-default = 3s")
                .WithFallback(RedisPersistence.DefaultConfig());
        }

        public RedisEventsByPersistenceIdSpec(ITestOutputHelper output, RedisClusterFixture fixture) 
            : base(Config(fixture), nameof(RedisEventsByPersistenceIdSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);
        }

        protected override void Dispose(bool disposing)
        {
            DbUtils.Clean();
            base.Dispose(disposing);
        }
    }
}