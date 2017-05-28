using Akka.Configuration;
using Akka.Persistence.Redis.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests.Performance
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
            .WithFallback(RedisReadJournal.DefaultConfiguration());


        public RedisJournalPerfSpec(ITestOutputHelper output) : base(Config(Database), output)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }
    }
}
