using System;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TestKit.Query;
using Akka.Streams.TestKit;
using Akka.Util.Internal;
using StackExchange.Redis;
using Xunit;

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
                configuration-string = ""127.0.0.1:6379,allowAdmin:true""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisReadJournal.DefaultConfiguration());

        public RedisCurrentPersistenceIdsSpec() : base(Config(Database))
        {
            ReadJournal = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);
        }

        [Fact]
        public void ReadJournal_CurrentPersistenceIds_should_fail_the_stage_on_connection_error()
        {
            // setup redis
            var address = Sys.Settings.Config.GetString("akka.persistence.journal.redis.configuration-string");
            var database = Sys.Settings.Config.GetInt("akka.persistence.journal.redis.database");

            var redis = ConnectionMultiplexer.Connect(address).GetDatabase(database);

            var queries = ReadJournal.AsInstanceOf<ICurrentPersistenceIdsQuery>();

            Setup("a", 1);

            var source = queries.CurrentPersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), Materializer);

            // change type of value
            redis.StringSet("journal:persistenceIds", "1");

            probe.Within(TimeSpan.FromSeconds(10), () => probe.Request(1).ExpectError());
        }

        protected override void Dispose(bool disposing)
        {
            //DbUtils.Clean(Database);
            base.Dispose(disposing);
        }
    }
}
