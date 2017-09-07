//-----------------------------------------------------------------------
// <copyright file="RedisSnapshotStoreSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TCK.Snapshot;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests
{
    [Collection("RedisSpec")]
    public class RedisSnapshotStoreSpec : SnapshotStoreSpec
    {
        private static readonly Config SpecConfig;
        public const int Database = 1;

        static RedisSnapshotStoreSpec()
        {
            var connectionString = "127.0.0.1:6379";

            SpecConfig = ConfigurationFactory.ParseString(@"
                akka.test.single-expect-default = 3s
                akka.persistence {
                    publish-plugin-commands = on
                    snapshot-store {
                        plugin = ""akka.persistence.snapshot-store.redis""
                        redis {
                            class = ""Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis""
                            configuration-string = """ + connectionString + @"""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            database = """ + Database + @"""
                        }
                    }
                }
                akka.actor {
                    serializers {
                        persistence-snapshot = ""Akka.Persistence.Redis.Serialization.PersistentSnapshotSerializer, Akka.Persistence.Redis""
                    }
                    serialization-bindings {
                        ""Akka.Persistence.SelectedSnapshot, Akka.Persistence"" = persistence-snapshot
                    }
                    serialization-identifiers {
                        ""Akka.Persistence.Redis.Serialization.PersistentSnapshotSerializer, Akka.Persistence.Redis"" = 48
                    }
                }").WithFallback(RedisReadJournal.DefaultConfiguration());
        }

        public RedisSnapshotStoreSpec(ITestOutputHelper output)
            : base(SpecConfig, typeof(RedisSnapshotStoreSpec).Name, output)
        {
            RedisPersistence.Get(Sys);
            Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(Database);
        }
    }
}