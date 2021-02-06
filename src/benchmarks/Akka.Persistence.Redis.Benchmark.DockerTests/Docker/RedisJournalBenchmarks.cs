// -----------------------------------------------------------------------
// <copyright file="RedisJournalBenchmarks.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Configuration;
using Akka.Persistence.Redis.BenchmarkTests.Docker;
using Akka.Persistence.Redis.Tests;
using Akka.Persistence.TestKit.Performance;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Benchmark.DockerTests
{
    public class TestConstants
    {
        public const int NumMessages = 1000;
        public const int DockerNumMessages = 1000;
    }

    [Collection("RedisBenchmark")]
    public class RedisJournalPerfSpec : RedisJournalBenchmarkDefinitions, IClassFixture<RedisFixture>
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

        public RedisJournalPerfSpec(ITestOutputHelper output, RedisFixture fixture)
            : base(Config(fixture, Database), nameof(RedisJournalPerfSpec), output, 40, TestConstants.DockerNumMessages)
        {
        }

        [Fact]
        public void PersistenceActor_Must_measure_PersistGroup1000()
        {
            RunGroupBenchmark(1000, 10);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }
    }
}