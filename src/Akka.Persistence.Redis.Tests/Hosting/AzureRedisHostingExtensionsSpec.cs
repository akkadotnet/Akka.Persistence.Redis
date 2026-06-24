// -----------------------------------------------------------------------
//  <copyright file="AzureRedisHostingExtensionsSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Akka.Persistence.Redis;
using Akka.Persistence.Redis.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Hosting;

/// <summary>
/// Covers <see cref="AzureRedisHostingExtensions"/>'s auto-detect behavior and the
/// <see cref="RedisConnectionMultiplexerSetup"/> it registers when an Azure host is
/// recognized.
///
/// These tests do not connect to a real Azure Redis instance — they assert the helper's
/// configuration-time behavior. Acquiring an Azure access token requires a live
/// <see cref="Azure.Core.TokenCredential"/>, which is out of scope for unit tests.
/// </summary>
public class AzureRedisHostingExtensionsSpec
{
    [Theory]
    [InlineData("your-redis.swedencentral.redis.azure.net:10000", true)]
    [InlineData("your-redis.eastus.redis.azure.net:6380", true)]
    [InlineData("your-cache.redis.cache.windows.net:6380", true)]
    [InlineData("YOUR-CACHE.Redis.Cache.Windows.Net:6380", true)] // case-insensitive suffix match
    [InlineData("localhost:6379", false)]
    [InlineData("127.0.0.1:6379", false)]
    [InlineData("redis.example.com:6379", false)]
    public void IsAzureRedisHost_should_match_only_Azure_managed_redis_suffixes(string connectionString, bool expected)
    {
        Assert.Equal(expected, AzureRedisHostingExtensions.IsAzureRedisHost(connectionString));
    }

    [Fact]
    public void IsAzureRedisHost_should_return_false_when_password_is_set_even_for_Azure_host()
    {
        // Treat an explicit password as an opt-out from token-credential auth, regardless
        // of host suffix. The user clearly wants connection-string auth in that case.
        Assert.False(AzureRedisHostingExtensions.IsAzureRedisHost(
            "your-redis.swedencentral.redis.azure.net:10000,password=secret"));
    }

    [Fact]
    public void IsAzureRedisHost_should_swallow_malformed_connection_strings()
    {
        // The auto-detect runs from WithAzureRedisPersistence on the configuration path
        // and must never throw. A malformed string falls through as `false`; the actual
        // configuration error will surface later from ConnectionMultiplexer.Connect with
        // a clearer message.
        Assert.False(AzureRedisHostingExtensions.IsAzureRedisHost("definitely::not::a::valid::connection::string"));
    }

    [Fact]
    public void WithAzureRedisPersistence_against_Azure_host_should_register_setup()
    {
        var builder = NewBuilder();

        builder.WithAzureRedisPersistence("your-redis.swedencentral.redis.azure.net:10000");

        var setup = builder.Setups.OfType<RedisConnectionMultiplexerSetup>().FirstOrDefault();
        Assert.NotNull(setup);
        Assert.True(setup!.TryGetSource("akka.persistence.journal.redis", out var journalSource));
        Assert.Equal(RedisConnectionOwnership.ActorSystemOwned, journalSource.Ownership);
        Assert.True(setup.TryGetSource("akka.persistence.snapshot-store.redis", out var snapshotSource));
        Assert.Equal(RedisConnectionOwnership.ActorSystemOwned, snapshotSource.Ownership);
    }

    [Fact]
    public void WithAzureRedisPersistence_against_non_Azure_host_should_not_register_setup()
    {
        var builder = NewBuilder();

        // localhost falls through to the plain HOCON connection-string path; no Setup.
        builder.WithAzureRedisPersistence("localhost:6379");

        Assert.Empty(builder.Setups.OfType<RedisConnectionMultiplexerSetup>() ?? []);
    }

    [Fact]
    public void WithAzureRedisPersistence_should_not_register_sources_for_unrelated_plugin_ids()
    {
        var builder = NewBuilder();

        builder
            .WithAzureRedisPersistence("your-redis.swedencentral.redis.azure.net:10000")
            .WithRedisPersistence(
                "localhost:6379",
                mode: PersistenceMode.Journal,
                pluginIdentifier: "events",
                isDefaultPlugin: false);

        var setup = builder.Setups.OfType<RedisConnectionMultiplexerSetup>().Single();
        Assert.True(setup.TryGetSource("akka.persistence.journal.redis", out _));
        Assert.True(setup.TryGetSource("akka.persistence.snapshot-store.redis", out _));
        Assert.False(setup.TryGetSource("akka.persistence.journal.events", out _));
    }

    [Fact]
    public void WithAzureRedisPersistence_with_journal_mode_should_register_only_journal_plugin_id()
    {
        var builder = NewBuilder();

        builder.WithAzureRedisPersistence(
            "your-redis.swedencentral.redis.azure.net:10000",
            mode: PersistenceMode.Journal);

        var setup = builder.Setups.OfType<RedisConnectionMultiplexerSetup>().Single();
        Assert.True(setup.TryGetSource("akka.persistence.journal.redis", out _));
        Assert.False(setup.TryGetSource("akka.persistence.snapshot-store.redis", out _));
    }

    [Fact]
    public void WithAzureRedisPersistence_should_reject_empty_connection_string()
    {
        var builder = NewBuilder();
        var act = () => builder.WithAzureRedisPersistence(string.Empty);
        var ex = Assert.Throws<System.ArgumentException>(act);
        Assert.Contains("connection string", ex.Message);
        Assert.Equal("connectionString", ex.ParamName);
    }

    private static AkkaConfigurationBuilder NewBuilder()
    {
        var services = new ServiceCollection();
        return new AkkaConfigurationBuilder(services, "azure-redis-test");
    }
}
