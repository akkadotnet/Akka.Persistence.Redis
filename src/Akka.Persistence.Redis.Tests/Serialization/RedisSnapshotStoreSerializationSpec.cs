//-----------------------------------------------------------------------
// <copyright file="RedisSnapshotStoreSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TCK.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests.Serialization
{
    [Collection("RedisSpec")]
    public class RedisSnapshotStoreSerializationSpec : SnapshotStoreSerializationSpec
    {
        public const int Database = 1;

        public static Config SpecConfig(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.redis""
            akka.persistence.snapshot-store.redis {{
                class = ""Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis""
                configuration-string = ""127.0.0.1:6379""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisReadJournal.DefaultConfiguration());

        public RedisSnapshotStoreSerializationSpec(ITestOutputHelper output) : base(SpecConfig(Database), nameof(RedisSnapshotStoreSerializationSpec), output)
        {
        }

        [Fact(Skip = "Json.NET can't determine if a type is Int32 or Int64")]
        public override void SnapshotStore_should_serialize_AtLeastOnceDeliverySnapshot()
        {
        }

        [Fact(Skip = "JsonSerializer does not support it at the moment")]
        public override void SnapshotStore_should_serialize_Payload()
        {
        }

        [Fact(Skip = "JsonSerializer does not support it at the moment")]
        public override void SnapshotStore_should_serialize_Payload_with_string_manifest()
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }
    }
}