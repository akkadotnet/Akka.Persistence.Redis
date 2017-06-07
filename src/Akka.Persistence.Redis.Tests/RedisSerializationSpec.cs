//-----------------------------------------------------------------------
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
    public class RedisSerializationSpec : SerializationSpec
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
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.redis""
            akka.persistence.snapshot-store.redis {{
                class = ""Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis""
                configuration-string = ""127.0.0.1:6379""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                database = {id}
                key-prefix = ""snapshots""
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisReadJournal.DefaultConfiguration());

        public RedisSerializationSpec(ITestOutputHelper output) : base(SpecConfig(Database), output)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }
    }
}