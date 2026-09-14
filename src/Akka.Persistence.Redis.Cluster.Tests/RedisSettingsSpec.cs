using Xunit;

namespace Akka.Persistence.Redis.Cluster.Tests
{
    public class RedisSettingsSpec : Akka.TestKit.Xunit.TestKit
    {
        [Fact]
        public void Redis_JournalSettings_must_have_default_values()
        {
            var redisPersistence = RedisPersistence.Get(Sys);

            Assert.Equal(string.Empty, redisPersistence.JournalSettings.ConfigurationString);
            Assert.Equal(0, redisPersistence.JournalSettings.Database);
            Assert.Equal(string.Empty, redisPersistence.JournalSettings.KeyPrefix);
        }

        [Fact]
        public void Redis_SnapshotStoreSettingsSettings_must_have_default_values()
        {
            var redisPersistence = RedisPersistence.Get(Sys);

            Assert.Equal(string.Empty, redisPersistence.SnapshotStoreSettings.ConfigurationString);
            Assert.Equal(0, redisPersistence.SnapshotStoreSettings.Database);
            Assert.Equal(string.Empty, redisPersistence.SnapshotStoreSettings.KeyPrefix);
        }
    }
}