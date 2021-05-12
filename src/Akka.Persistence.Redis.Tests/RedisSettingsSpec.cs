// -----------------------------------------------------------------------
// <copyright file="RedisSettingsSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    public class RedisSettingsSpec : Akka.TestKit.Xunit2.TestKit
    {
        [Fact]
        public void Redis_JournalSettings_must_have_default_values()
        {
            var redisPersistence = RedisPersistence.Get(Sys);
            var settings = RedisSettings.Create(redisPersistence.DefaultJournalConfig);

            settings.ConfigurationString.Should().Be(string.Empty);
            settings.Database.Should().Be(0);
            settings.KeyPrefix.Should().Be(string.Empty);
        }

        [Fact]
        public void Redis_SnapshotStoreSettingsSettings_must_have_default_values()
        {
            var redisPersistence = RedisPersistence.Get(Sys);
            var settings = RedisSettings.Create(redisPersistence.DefaultSnapshotConfig);

            settings.ConfigurationString.Should().Be(string.Empty);
            settings.Database.Should().Be(0);
            settings.KeyPrefix.Should().Be(string.Empty);
        }
    }
}