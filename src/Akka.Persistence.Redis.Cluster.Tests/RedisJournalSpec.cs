//-----------------------------------------------------------------------
// <copyright file="RedisJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Cluster.Test
{
    [Collection("RedisClusterSpec")]
    public class RedisJournalSpec : JournalSpec
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
                configuration-string = ""{DbUtils.ConnectionString}""
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisPersistence.DefaultConfig());
        }

        public RedisJournalSpec(ITestOutputHelper output, RedisClusterFixture fixture)
            : base(Config(fixture), nameof(RedisJournalSpec), output)
        {
            RedisPersistence.Get(Sys);
            Initialize();
        }

        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean();
        }
    }
}