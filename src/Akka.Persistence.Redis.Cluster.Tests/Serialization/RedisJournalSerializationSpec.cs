// -----------------------------------------------------------------------
// <copyright file="RedisJournalSerializationSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Cluster.Test.Serialization
{
    [Collection("RedisClusterSpec")]
    public class RedisJournalSerializationSpec : JournalSerializationSpec
    {
        public static Config Config(RedisClusterFixture fixture)
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
            }}
            akka.test.single-expect-default = 3s")
                .WithFallback(RedisPersistence.DefaultConfig());
        }

        public RedisJournalSerializationSpec(ITestOutputHelper output, RedisClusterFixture fixture)
            : base(Config(fixture), nameof(RedisJournalSerializationSpec), output)
        {
        }

        [Fact(Skip = "Unknown whether this test is correct or not, skip for now.")]
        public override void Journal_should_serialize_Persistent_with_EventAdapter_manifest()
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean();
        }

        protected override bool SupportsSerialization => false;
    }
}