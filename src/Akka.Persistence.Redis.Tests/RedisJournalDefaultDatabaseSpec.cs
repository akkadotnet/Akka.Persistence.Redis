﻿// -----------------------------------------------------------------------
// <copyright file="RedisJournalSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests
{
    [Collection("RedisSpec")]
    public class RedisJournalDefaultDatabaseSpec : JournalSpec
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
                configuration-string = ""{fixture.ConnectionString},defaultDatabase=1""
                use-database-number-from-connection-string = true
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
                .WithFallback(RedisPersistence.DefaultConfig());
        }

        public RedisJournalDefaultDatabaseSpec(ITestOutputHelper output, RedisFixture fixture) : base(Config(fixture, Database),
            nameof(RedisJournalSpec), output)
        {
            RedisPersistence.Get(Sys);
            Initialize();
        }

        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        protected override void AfterAll()
        {
            base.AfterAll();
            DbUtils.Clean(Database);
        }
    }
}