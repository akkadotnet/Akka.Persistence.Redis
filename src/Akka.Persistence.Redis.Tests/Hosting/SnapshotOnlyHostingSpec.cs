// -----------------------------------------------------------------------
//  <copyright file="SnapshotOnlyHostingSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Akka.Persistence.Redis.Hosting;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Hosting;

/// <summary>
/// Regression coverage for the snapshot-only HOCON gap: configuring the plugin via
/// <see cref="AkkaPersistenceRedisHostingExtensions.WithRedisPersistence(AkkaConfigurationBuilder, string, PersistenceMode, bool, Action{AkkaPersistenceJournalBuilder}, Action{AkkaPersistenceSnapshotBuilder}, string, bool)"/>
/// with <see cref="PersistenceMode.SnapshotStore"/> previously skipped the
/// <c>AddHocon(RedisPersistence.DefaultConfig(), HoconAddMode.Append)</c> call,
/// leaving the snapshot store class without its HOCON section at runtime.
/// </summary>
[Collection("RedisSpec")]
public class SnapshotOnlyHostingSpec : Akka.Hosting.TestKit.TestKit, IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;

    public SnapshotOnlyHostingSpec(ITestOutputHelper output, RedisFixture fixture)
        : base(nameof(SnapshotOnlyHostingSpec), output: output)
    {
        _fixture = fixture;
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        builder.WithRedisPersistence(
            configurationString: _fixture.ConnectionString,
            mode: PersistenceMode.SnapshotStore);
    }

    [Fact]
    public void Snapshot_only_configuration_should_load_redis_snapshot_store_HOCON()
    {
        var snapshotStoreConfig = Sys.Settings.Config.GetConfig("akka.persistence.snapshot-store.redis");

        Assert.NotNull(snapshotStoreConfig);

        Assert.Contains("RedisSnapshotStore", snapshotStoreConfig.GetString("class"));
    }

    [Fact]
    public void Snapshot_only_configuration_should_set_snapshot_store_plugin_to_redis()
    {
        Assert.Equal("akka.persistence.snapshot-store.redis", Sys.Settings.Config.GetString("akka.persistence.snapshot-store.plugin"));
    }
}
