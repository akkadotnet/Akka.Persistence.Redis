//-----------------------------------------------------------------------
// <copyright file="RedisSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TCK.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests
{
    [Collection("RedisSpec")]
    public class RedisSerializationSpec : JournalSerializationSpec
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
            }}
            akka.test.single-expect-default = 25s")
            .WithFallback(RedisReadJournal.DefaultConfiguration());

        public RedisSerializationSpec(ITestOutputHelper output) : base(SpecConfig(Database), nameof(RedisSerializationSpec), output)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }
    }
}