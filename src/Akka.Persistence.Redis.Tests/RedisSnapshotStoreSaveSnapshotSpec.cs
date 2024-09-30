// -----------------------------------------------------------------------
// <copyright file="RedisSnapshotStoreSaveSnapshotSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Snapshot;
using Xunit;
using Xunit.Abstractions;

#nullable enable
namespace Akka.Persistence.Redis.Tests;

[Collection("RedisSpec")]
public class RedisSnapshotStoreSaveSnapshotSpec: SnapshotStoreSaveSnapshotSpec
{
    public const int Database = 1;

    public static Config Config(RedisFixture fixture, int id)
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
                            database = ""{id}""
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

    public RedisSnapshotStoreSaveSnapshotSpec(ITestOutputHelper output, RedisFixture fixture)
        : base(Config(fixture, Database), nameof(RedisSnapshotStoreSpec), output)
    {
        RedisPersistence.Get(Sys);
    }

    protected override void AfterAll()
    {
        base.AfterAll();
        DbUtils.Clean(Database);
    }

}