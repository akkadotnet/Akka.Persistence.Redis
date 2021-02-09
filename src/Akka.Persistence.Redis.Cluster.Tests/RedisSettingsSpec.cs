// -----------------------------------------------------------------------
// <copyright file="RedisSettingsSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Redis.Cluster.Tests
{
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