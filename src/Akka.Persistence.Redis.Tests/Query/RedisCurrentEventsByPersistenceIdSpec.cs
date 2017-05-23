using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Redis.Query;
using Akka.Streams;
using Akka.Util.Internal;
using Akka.Persistence.TestKit.Query;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Query
{
    [Collection("RedisSpec")]
    public class RedisCurrentEventsByPersistenceIdSpec : EventsByPersistenceIdSpec
    {
        public static readonly AtomicCounter Counter = new AtomicCounter(0);
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

        private readonly ActorMaterializer _materializer;

        public RedisCurrentEventsByPersistenceIdSpec() : base(Config(Counter.GetAndIncrement()))
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