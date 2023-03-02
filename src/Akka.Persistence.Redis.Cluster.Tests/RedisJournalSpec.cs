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
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Cluster.Tests
{
    [Collection("RedisClusterSpec")]
    public class RedisJournalSpec : JournalSpec
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
                configuration-string = ""{DbUtils.ConnectionString}""
            }}
            akka.test.single-expect-default = 3s")
                .WithFallback(RedisPersistence.DefaultConfig());
        }

        public RedisJournalSpec(ITestOutputHelper output, RedisClusterFixture fixture)
            : base(Config(fixture), nameof(RedisJournalSpec), output)
        {
            _fixture = fixture;

            RedisPersistence.Get(Sys);
            Initialize();
        }

        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
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

            // Evaluate standard deviation
            var standardDeviation = StandardDeviation(values);
            Output.WriteLine($"Server assignment distribution: [{string.Join(",", values)}]. Standard deviation: [{standardDeviation}]");

            // Should be less than 1 percent of total keys
            StandardDeviation(values).Should().BeLessThan(totalEntries * 0.01);
        }

        private double StandardDeviation(int[] values)
        {
            var mean = values.Average();
            var sum = values.Sum(d => Math.Pow(d - mean, 2));
            return Math.Sqrt((sum) / (values.Count() - 1));
        }
    }
}