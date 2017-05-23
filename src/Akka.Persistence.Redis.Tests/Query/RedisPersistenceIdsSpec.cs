using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TestKit.Query;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Query
{
    [Collection("RedisSpec")]
    public sealed class RedisPersistenceIdsSpec : PersistenceIdsSpec
    {
        public const int Database = 1;

        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                auto-initialize = on
                configuration-string = ""127.0.0.1:6379,allowAdmin:true""
                database = {id}
                key-prefix = ""akka:persistence:journal""
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisReadJournal.DefaultConfiguration());

        public RedisPersistenceIdsSpec() : base(Config(Database))
        {
            ReadJournal = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);
        }

        protected override void Dispose(bool disposing)
        {
            DbUtils.Clean(Database);
            base.Dispose(disposing);
        }
    }
}
