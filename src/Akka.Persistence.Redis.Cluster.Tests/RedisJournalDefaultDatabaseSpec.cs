// -----------------------------------------------------------------------
// <copyright file="RedisJournalSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Cluster.Tests
{
    [Collection("RedisClusterSpec")]
    public class RedisJournalDefaultDatabaseSpec : JournalSpec
    {
        private readonly RedisClusterFixture _fixture;

        public static Config Config(RedisClusterFixture fixture)
        {
            DbUtils.Initialize(fixture);

            return ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                configuration-string = ""{DbUtils.ConnectionString},defaultDatabase=0""
                use-database-number-from-connection-string = true
            }}
            akka.test.single-expect-default = 10s")
                .WithFallback(RedisPersistence.DefaultConfig());
        }

        public RedisJournalDefaultDatabaseSpec(ITestOutputHelper output, RedisClusterFixture fixture)
            : base(Config(fixture), nameof(RedisJournalSpec), output)
        {
            _fixture = fixture;

            RedisPersistence.Get(Sys);
            Initialize();
        }

        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        protected override void AfterAll()
        {
            base.AfterAll();
            DbUtils.Clean();
        }

        [Fact]
        public void Randomly_distributed_RedisKey_with_HashTag_should_be_distributed_relatively_equally_between_cluster_master()
        {
            var totalEntries = 10000;

            var redis = ConnectionMultiplexer.Connect(_fixture.ConnectionString);
            var db = redis.GetDatabase();
            var journalHelper = new JournalHelper(Sys, "foo");
            var dict = new Dictionary<EndPoint, int>();

            for (var i = 0; i < totalEntries; ++i)
            {
                var id = $"{Guid.NewGuid():N}-{i}";
                var ep = db.IdentifyEndpoint(journalHelper.GetJournalKey(id, true));
                if (!dict.TryGetValue(ep, out _))
                {
                    dict[ep] = 1;
                }
                else
                {
                    dict[ep]++;
                }
            }

            var values = dict.Values.AsEnumerable().ToArray();

            var standardDeviation = StandardDeviation(values);
            var mean = values.Average();
            var coefficientOfVariation = standardDeviation / mean;

            Output.WriteLine(
                $"Server assignment distribution: [{string.Join(",", values)}]. " +
                $"Mean: [{mean:F2}]. Standard deviation: [{standardDeviation:F2}]. " +
                $"Coefficient of variation: [{coefficientOfVariation:F4}]");

            // Coefficient of variation (stddev / mean) bounds the relative imbalance across
            // shards independently of totalEntries. For uniform multinomial(n, 1/k) the
            // expected CV is ~1/sqrt(n*p) — at n=10000 with k=3 buckets that is ~0.025; the
            // 99.9% upper bound is well below 0.10. A real distribution failure (one bucket
            // capturing >50% of keys) would push CV well above 0.10.
            coefficientOfVariation.Should().BeLessThan(0.10);
        }

        private double StandardDeviation(int[] values)
        {
            var mean = values.Average();
            var sum = values.Sum(d => Math.Pow(d - mean, 2));
            return Math.Sqrt((sum) / (values.Count() - 1));
        }
    }
}