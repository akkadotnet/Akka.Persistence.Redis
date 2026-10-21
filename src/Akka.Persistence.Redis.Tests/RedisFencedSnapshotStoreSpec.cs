// -----------------------------------------------------------------------
// <copyright file="RedisFencedSnapshotStoreSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Snapshot;
using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    /// <summary>
    /// The snapshot-store TCK against a subclass that adds a transaction condition which always
    /// holds: it must behave exactly like the plain snapshot store.
    /// </summary>
    [Collection("RedisSpec")]
    public class RedisFencedSnapshotStoreSpec : SnapshotStoreSpec
    {
        public const int Database = 1;

        public static Config Config(RedisFixture fixture) => ConfigurationFactory.ParseString(@"
            akka.persistence.snapshot-store.redis {
                class = ""Akka.Persistence.Redis.Tests.FencedRedisSnapshotStore, Akka.Persistence.Redis.Tests""
                fence-key-format = ""fence""
                fence-token = ""1""
            }")
            .WithFallback(RedisSnapshotStoreSpec.Config(fixture, Database));

        public RedisFencedSnapshotStoreSpec(ITestOutputHelper output, RedisFixture fixture)
            : base(Config(fixture), nameof(RedisFencedSnapshotStoreSpec), output)
        {
            Fence.SetAsync(fixture, Database, "fence", "1").GetAwaiter().GetResult();
            RedisPersistence.Get(Sys);
            Initialize();
        }

        protected override void AfterAll()
        {
            base.AfterAll();
            DbUtils.Clean(Database);
        }
    }
}
