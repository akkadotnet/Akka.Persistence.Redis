//-----------------------------------------------------------------------
// <copyright file="RedisJournalSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TCK.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests.Serialization
{
    [Collection("RedisSpec")]
    public class RedisJournalSerializationSpec : JournalSerializationSpec
    {
        public const int Database = 1;

        public static Config SpecConfig(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                event-adapters {{
                    my-adapter = ""Akka.Persistence.TCK.Serialization.TestJournal+MyWriteAdapter, Akka.Persistence.TCK""
                }}
                event-adapter-bindings = {{
                    ""Akka.Persistence.TCK.Serialization.TestJournal+MyPayload3, Akka.Persistence.TCK"" = my-adapter
                }}
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                configuration-string = ""127.0.0.1:6379""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisReadJournal.DefaultConfiguration());

        public RedisJournalSerializationSpec(ITestOutputHelper output) : base(SpecConfig(Database), nameof(RedisJournalSerializationSpec), output)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }
    }
}