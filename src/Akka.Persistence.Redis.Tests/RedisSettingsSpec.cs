using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    public class RedisSettingsSpec : Akka.TestKit.Xunit.TestKit
    {
        [Fact]
        public void Redis_JournalSettings_must_have_default_values()
        {
            var redisPersistence = RedisPersistence.Get(Sys);
            var settings = RedisSettings.Create(redisPersistence.DefaultJournalConfig);

            Assert.Equal(string.Empty, settings.ConfigurationString);
            Assert.Equal(0, settings.Database);
            Assert.Equal(string.Empty, settings.KeyPrefix);
        }

        [Fact]
        public void Redis_SnapshotStoreSettingsSettings_must_have_default_values()
        {
            var redisPersistence = RedisPersistence.Get(Sys);
            var settings = RedisSettings.Create(redisPersistence.DefaultSnapshotConfig);

            Assert.Equal(string.Empty, settings.ConfigurationString);
            Assert.Equal(0, settings.Database);
            Assert.Equal(string.Empty, settings.KeyPrefix);
        }
    }
}