// -----------------------------------------------------------------------
//  <copyright file="FactoryRoutingSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Hosting;
using Akka.Persistence.Redis;
using Akka.Persistence.Redis.Hosting;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Hosting;

/// <summary>
/// Verifies the hosting extension's per-plugin factory routing semantics. Journal and
/// snapshot store can supply the same factory delegate (single shared multiplexer, the
/// common case), different factories (per-plugin instances against different Redis
/// backends), or a factory on only one side (HOCON connection-string for the other).
/// All three cases must be accepted at config time.
/// </summary>
public class FactoryRoutingSpec
{
    [Fact]
    public void WithRedisPersistence_should_accept_same_factory_reference_on_both()
    {
        Func<Task<IConnectionMultiplexer>> shared = () => Task.FromResult<IConnectionMultiplexer>(null!);

        var journalOptions = new RedisJournalOptions
        {
            ConfigurationString = "localhost:6379",
            ConnectionMultiplexerFactory = shared,
        };

        var snapshotOptions = new RedisSnapshotOptions
        {
            ConfigurationString = "localhost:6379",
            ConnectionMultiplexerFactory = shared,
        };

        var builder = NewBuilder();

        Action act = () => builder.WithRedisPersistence(journalOptions, snapshotOptions);

        act.Should().NotThrow();

        var multi = builder.Setups.OfType<MultiRedisConnectionMultiplexerSetup>().FirstOrDefault();
        multi.Should().NotBeNull();
        multi!.TryGetFactory(journalOptions.PluginId, out _).Should().BeTrue();
        multi.TryGetFactory(snapshotOptions.PluginId, out _).Should().BeTrue();
    }

    [Fact]
    public void WithRedisPersistence_should_accept_different_factories_for_journal_and_snapshot()
    {
        // Different multiplexers per plugin id — exactly what cluster-sharding / multi-tenant
        // setups need when journal events live on a different Redis from snapshots.
        Func<Task<IConnectionMultiplexer>> journalFactory = () => Task.FromResult<IConnectionMultiplexer>(null!);
        Func<Task<IConnectionMultiplexer>> snapshotFactory = () => Task.FromResult<IConnectionMultiplexer>(null!);

        var journalOptions = new RedisJournalOptions
        {
            ConfigurationString = "localhost:6379",
            ConnectionMultiplexerFactory = journalFactory,
        };

        var snapshotOptions = new RedisSnapshotOptions
        {
            ConfigurationString = "localhost:7000",
            ConnectionMultiplexerFactory = snapshotFactory,
        };

        var builder = NewBuilder();

        Action act = () => builder.WithRedisPersistence(journalOptions, snapshotOptions);

        act.Should().NotThrow();

        var multi = builder.Setups.OfType<MultiRedisConnectionMultiplexerSetup>().FirstOrDefault();
        multi.Should().NotBeNull();

        multi!.TryGetFactory(journalOptions.PluginId, out var journalRegistered).Should().BeTrue();
        ReferenceEquals(journalRegistered, journalFactory).Should().BeTrue();

        multi.TryGetFactory(snapshotOptions.PluginId, out var snapshotRegistered).Should().BeTrue();
        ReferenceEquals(snapshotRegistered, snapshotFactory).Should().BeTrue();
    }

    [Fact]
    public void WithRedisPersistence_should_accept_factory_on_only_one_side()
    {
        Func<Task<IConnectionMultiplexer>> factory = () => Task.FromResult<IConnectionMultiplexer>(null!);

        var journalOptions = new RedisJournalOptions
        {
            ConfigurationString = "localhost:6379",
            ConnectionMultiplexerFactory = factory,
        };

        var snapshotOptions = new RedisSnapshotOptions
        {
            ConfigurationString = "localhost:6379",
            // ConnectionMultiplexerFactory left null — snapshot store will use the HOCON
            // connection-string path, journal uses the supplied factory.
        };

        var builder = NewBuilder();

        Action act = () => builder.WithRedisPersistence(journalOptions, snapshotOptions);

        act.Should().NotThrow();

        var multi = builder.Setups.OfType<MultiRedisConnectionMultiplexerSetup>().FirstOrDefault();
        multi.Should().NotBeNull();
        multi!.TryGetFactory(journalOptions.PluginId, out _).Should().BeTrue();
        multi.TryGetFactory(snapshotOptions.PluginId, out _).Should().BeFalse();
    }

    private static AkkaConfigurationBuilder NewBuilder()
    {
        var services = new ServiceCollection();
        return new AkkaConfigurationBuilder(services, "factory-routing-test");
    }
}
