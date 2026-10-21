// -----------------------------------------------------------------------
// <copyright file="RedisFencedJournalSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TCK.Journal;
using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    /// <summary>
    /// The journal TCK against a subclass that adds a transaction condition which always holds:
    /// it must behave exactly like the plain journal.
    /// </summary>
    [Collection("RedisSpec")]
    public class RedisFencedJournalSpec : JournalSpec
    {
        public const int Database = 1;

        public static Config Config(RedisFixture fixture) => ConfigurationFactory.ParseString(@"
            akka.persistence.journal.redis {
                class = ""Akka.Persistence.Redis.Tests.FencedRedisJournal, Akka.Persistence.Redis.Tests""
                fence-key-format = ""fence""
                fence-token = ""1""
            }")
            .WithFallback(RedisJournalSpec.Config(fixture, Database));

        public RedisFencedJournalSpec(ITestOutputHelper output, RedisFixture fixture)
            : base(Config(fixture), nameof(RedisFencedJournalSpec), output)
        {
            Fence.SetAsync(fixture, Database, "fence", "1").GetAwaiter().GetResult();
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
