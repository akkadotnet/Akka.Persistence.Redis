﻿//-----------------------------------------------------------------------
// <copyright file="RedisJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.Redis.Tests.TestKit;
using Akka.Persistence.TestKit.Journal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests
{
    [Collection("RedisSpec")]
    public class RedisJournalSpec : JournalSpec
    {
        public const int Database = 1;

        public static Config SpecConfig(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                configuration-string = ""127.0.0.1:6379""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisReadJournal.DefaultConfiguration());

        public RedisJournalSpec(ITestOutputHelper output) : base(SpecConfig(Database), nameof(RedisJournalSpec), output)
        {
            RedisPersistence.Get(Sys);
            Initialize();
        }

        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }
    }
}