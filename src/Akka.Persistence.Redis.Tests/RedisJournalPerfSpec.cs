// -----------------------------------------------------------------------
// <copyright file="RedisJournalPerfSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TestKit.Performance;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests
{
    [Collection("RedisSpec")]
    public class RedisJournalPerfSpec : JournalPerfSpec
    {
        public const int Database = 1;

        public static Config Config(RedisFixture fixture, int id)
        {
            DbUtils.Initialize(fixture);

            return ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                configuration-string = ""{fixture.ConnectionString}""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
                .WithFallback(RedisPersistence.DefaultConfig())
                .WithFallback(Persistence.DefaultConfig());
        }

        public RedisJournalPerfSpec(ITestOutputHelper output, RedisFixture fixture) : base(Config(fixture, Database),
            nameof(RedisJournalPerfSpec), output)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }
    }
}