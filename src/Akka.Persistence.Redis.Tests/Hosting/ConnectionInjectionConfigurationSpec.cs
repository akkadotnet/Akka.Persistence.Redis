// -----------------------------------------------------------------------
//  <copyright file="ConnectionInjectionConfigurationSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Akka.Persistence.Redis;
using Akka.Persistence.Redis.Hosting;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

#nullable enable
namespace Akka.Persistence.Redis.Tests.Hosting;

public class ConnectionInjectionConfigurationSpec
{
    [Fact]
    public void Connection_string_overload_should_not_register_setup()
    {
        var builder = NewBuilder();

        builder.WithRedisPersistence("localhost:6379");

        builder.Setups.OfType<RedisConnectionMultiplexerSetup>().Should().BeEmpty();
    }

    [Theory]
    [InlineData(PersistenceMode.Journal, true, false)]
    [InlineData(PersistenceMode.SnapshotStore, false, true)]
    [InlineData(PersistenceMode.Both, true, true)]
    public void Multiplexer_factory_overload_should_register_sources_for_selected_mode(
        PersistenceMode mode,
        bool expectJournal,
        bool expectSnapshot)
    {
        var builder = NewBuilder();
        var factory = FakeFactory();

        builder.WithRedisPersistence(
            multiplexerFactory: factory,
            ownership: RedisConnectionOwnership.PluginOwned,
            mode: mode,
            pluginIdentifier: "custom",
            isDefaultPlugin: false);

        var setup = builder.Setups.OfType<RedisConnectionMultiplexerSetup>().Single();
        setup.TryGetSource("akka.persistence.journal.custom", out var journalSource).Should().Be(expectJournal);
        setup.TryGetSource("akka.persistence.snapshot-store.custom", out var snapshotSource).Should().Be(expectSnapshot);

        if (expectJournal)
            journalSource!.Ownership.Should().Be(RedisConnectionOwnership.PluginOwned);

        if (expectSnapshot)
            snapshotSource!.Ownership.Should().Be(RedisConnectionOwnership.PluginOwned);
    }

    [Fact]
    public void Multiplexer_factory_overload_should_register_factory_with_selected_ownership()
    {
        var builder = NewBuilder();
        var factory = FakeFactory();

        builder.WithRedisPersistence(
            multiplexerFactory: factory,
            ownership: RedisConnectionOwnership.CallerOwned,
            mode: PersistenceMode.Journal,
            pluginIdentifier: "factory",
            isDefaultPlugin: false);

        var setup = builder.Setups.OfType<RedisConnectionMultiplexerSetup>().Single();
        setup.TryGetSource("akka.persistence.journal.factory", out var source).Should().BeTrue();
        source!.Ownership.Should().Be(RedisConnectionOwnership.CallerOwned);
        source.Factory.Should().BeSameAs(factory);
        setup.TryGetSource("akka.persistence.snapshot-store.factory", out _).Should().BeFalse();
    }

    [Fact]
    public void Options_overload_should_accept_setup_entry_for_matching_plugin_without_connection_string()
    {
        var builder = NewBuilder();
        var factory = FakeFactory();
        var journalOptions = new RedisJournalOptions(isDefault: false, identifier: "setup-only");

        builder.Setups.Add(new RedisConnectionMultiplexerSetup()
            .Add(
                journalOptions.PluginId,
                factory,
                RedisConnectionOwnership.CallerOwned));

        var act = () => builder.WithRedisPersistence(journalOptions, snapshotOptions: null);

        act.Should().NotThrow();
    }

    [Fact]
    public void Options_overload_should_reject_plugin_without_connection_string_or_matching_setup_entry()
    {
        var builder = NewBuilder();
        var journalOptions = new RedisJournalOptions(isDefault: false, identifier: "missing-source");

        var act = () => builder.WithRedisPersistence(journalOptions, snapshotOptions: null);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*requires a connection source*");
    }

    [Fact]
    public void Duplicate_setup_entries_for_same_plugin_id_should_fail_fast()
    {
        var setup = new RedisConnectionMultiplexerSetup()
            .Add(
                "akka.persistence.journal.redis",
                FakeFactory(),
                RedisConnectionOwnership.CallerOwned);

        var act = () => setup.Add(
            "akka.persistence.journal.redis",
            FakeFactory(),
            RedisConnectionOwnership.PluginOwned);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*akka.persistence.journal.redis*");
    }

    private static AkkaConfigurationBuilder NewBuilder()
    {
        var services = new ServiceCollection();
        return new AkkaConfigurationBuilder(services, "connection-injection-test");
    }

    private static Func<Task<IConnectionMultiplexer>> FakeFactory() => () => Task.FromResult<IConnectionMultiplexer>(null!);
}
