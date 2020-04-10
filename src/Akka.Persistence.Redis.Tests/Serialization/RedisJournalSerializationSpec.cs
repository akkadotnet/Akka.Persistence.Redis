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

        public static Config Config(RedisFixture fixture, int id)
        {
            DbUtils.Initialize(fixture);

            return ConfigurationFactory.ParseString($@"
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
                configuration-string = ""{fixture.ConnectionString}""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisPersistence.DefaultConfig());
        }

        public RedisJournalSerializationSpec(ITestOutputHelper output, RedisFixture fixture) : base(Config(fixture, Database), nameof(RedisJournalSerializationSpec), output)
        {
        }

        [Fact(Skip = "Unknown whether this test is correct or not, skip for now.")]
        public override void Journal_should_serialize_Persistent_with_EventAdapter_manifest()
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }

        protected override bool SupportsSerialization => false;
    }
}