//-----------------------------------------------------------------------
// <copyright file="RedisSnapshotStoreSerializationSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Serialization;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Cluster.Test.Serialization
{
    [Collection("RedisClusterSpec")]
    public class RedisSnapshotStoreSerializationSpec : SnapshotStoreSerializationSpec
    {
        public static Config Config(RedisClusterFixture fixture)
        {
            DbUtils.Initialize(fixture);

            return ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.redis""
            akka.persistence.snapshot-store.redis {{
                class = ""Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis""
                configuration-string = ""{fixture.ConnectionString}""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
            }}
            akka.actor {{
                serializers {{
                    persistence-snapshot = ""Akka.Persistence.Redis.Serialization.PersistentSnapshotSerializer, Akka.Persistence.Redis""
                }}
                serialization-bindings {{
                    ""Akka.Persistence.SelectedSnapshot, Akka.Persistence"" = persistence-snapshot
                }}
                serialization-identifiers {{
                    ""Akka.Persistence.Redis.Serialization.PersistentSnapshotSerializer, Akka.Persistence.Redis"" = 48
                }}
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisPersistence.DefaultConfig());
        }

        public RedisSnapshotStoreSerializationSpec(ITestOutputHelper output, RedisClusterFixture fixture) 
            : base(Config(fixture), nameof(RedisSnapshotStoreSerializationSpec), output)
        {
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean();
        }
    }
}