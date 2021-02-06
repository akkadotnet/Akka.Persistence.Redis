// -----------------------------------------------------------------------
// <copyright file="RedisJournalPerfSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.TestKit.Performance;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Cluster.Test
{
    [Collection("RedisClusterSpec")]
    public class RedisJournalPerfSpec : JournalPerfSpec
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
                .WithFallback(RedisPersistence.DefaultConfig())
                .WithFallback(Persistence.DefaultConfig());
        }

        public RedisJournalPerfSpec(ITestOutputHelper output, RedisClusterFixture fixture)
            : base(Config(fixture), nameof(RedisJournalPerfSpec), output)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean();
        }
    }
}