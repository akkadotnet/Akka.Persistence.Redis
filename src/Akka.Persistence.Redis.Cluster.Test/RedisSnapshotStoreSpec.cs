//-----------------------------------------------------------------------
// <copyright file="RedisSnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Snapshot;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Cluster.Test
{
    [Collection("RedisClusterSpec")]
    public class RedisSnapshotStoreSpec : SnapshotStoreSpec
    {
        public static Config Config(RedisClusterFixture fixture)
        {
            DbUtils.Initialize(fixture);

            return ConfigurationFactory.ParseString($@"
                akka.test.single-expect-default = 3s
                akka.persistence {{
                    publish-plugin-commands = on
                    snapshot-store {{
                        plugin = ""akka.persistence.snapshot-store.redis""
                        redis {{
                            class = ""Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis""
                            configuration-string = ""{fixture.ConnectionString}""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                        }}
                    }}
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
                }}").WithFallback(RedisPersistence.DefaultConfig());
        }

        public RedisSnapshotStoreSpec(ITestOutputHelper output, RedisClusterFixture fixture)
            : base(Config(fixture), nameof(RedisSnapshotStoreSpec), output)
        {
            RedisPersistence.Get(Sys);
            Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean();
        }
    }
}