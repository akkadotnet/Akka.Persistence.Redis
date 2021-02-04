//-----------------------------------------------------------------------
// <copyright file="RedisPersistenceIdsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TCK.Query;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Cluster.Test.Query
{
    [Collection("RedisClusterSpec")]
    public sealed class RedisPersistenceIdsSpec : PersistenceIdsSpec
    {
        public const int Database = 1;

        public static Config Config(RedisClusterFixture fixture, int id)
        {
            DbUtils.Initialize(fixture);

            return ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                configuration-string = ""{DbUtils.ConnectionString}""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisPersistence.DefaultConfig());
        }

        public RedisPersistenceIdsSpec(ITestOutputHelper output, RedisClusterFixture fixture)
            : base(Config(fixture, Database), nameof(RedisPersistenceIdsSpec), output)
        {
            ReadJournal = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);
        }
        [Fact(Skip = "Not implemented in Redis plugin")]
        public override Task ReadJournal_should_deallocate_AllPersistenceIds_publisher_when_the_last_subscriber_left()
        {
            return Task.CompletedTask;
        }

        [Fact]
        public void ReadJournal_AllPersistenceIds_should_fail_the_stage_on_connection_error()
        {
            // setup redis
            var address = Sys.Settings.Config.GetString("akka.persistence.journal.redis.configuration-string");
            var database = Sys.Settings.Config.GetInt("akka.persistence.journal.redis.database");

            var redis = ConnectionMultiplexer.Connect(address).GetDatabase(database);

            var queries = ReadJournal.AsInstanceOf<IPersistenceIdsQuery>();

            Setup("a", 1);

            var source = queries.PersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);

            //// change type of value
            redis.StringSet("journal:persistenceIds", "1");

            probe.Within(TimeSpan.FromSeconds(10), () => probe.Request(1).ExpectError());
        }

        protected override void Dispose(bool disposing)
        {
            DbUtils.Clean(Database);
            base.Dispose(disposing);
        }
    }
}
