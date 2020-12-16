//-----------------------------------------------------------------------
// <copyright file="RedisSettingsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    [Collection("RedisSpec")]
    public class RedisSettingsSpec : Akka.TestKit.Xunit2.TestKit
    {
        [Fact]
        public void Redis_JournalSettings_must_have_default_values()
        {
            var redisPersistence = RedisPersistence.Get(Sys);

            redisPersistence.JournalSettings.ConfigurationString.Should().Be(string.Empty);
            redisPersistence.JournalSettings.Database.Should().Be(0);
            redisPersistence.JournalSettings.KeyPrefix.Should().Be(string.Empty);
        }

        [Fact]
        public void Redis_SnapshotStoreSettingsSettings_must_have_default_values()
        {
            var redisPersistence = RedisPersistence.Get(Sys);

            redisPersistence.SnapshotStoreSettings.ConfigurationString.Should().Be(string.Empty);
            redisPersistence.SnapshotStoreSettings.Database.Should().Be(0);
            redisPersistence.SnapshotStoreSettings.KeyPrefix.Should().Be(string.Empty);
        }
    }
}
