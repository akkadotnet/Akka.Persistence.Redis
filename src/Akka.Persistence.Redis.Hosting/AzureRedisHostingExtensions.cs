// -----------------------------------------------------------------------
//  <copyright file="AzureRedisHostingExtensions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

/// <summary>
/// Convenience extensions for configuring Akka.Persistence.Redis against
/// <a href="https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/">Azure Managed Redis</a>
/// or <a href="https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/cache-overview">Azure Cache for Redis</a>
/// with Entra ID / Managed Identity authentication.
/// </summary>
/// <remarks>
/// Internally this builds an <see cref="IConnectionMultiplexer"/> factory wired through the
/// existing <see cref="RedisJournalOptions.ConnectionMultiplexerFactory"/> /
/// <see cref="RedisSnapshotOptions.ConnectionMultiplexerFactory"/> extension point — no new
/// Setup type, no new wiring path. For non-Azure hosts (e.g. local development with
/// <c>localhost:6379</c>), the helper falls through to the plain HOCON connection-string
/// path and behaves identically to <see cref="AkkaPersistenceRedisHostingExtensions.WithRedisPersistence(AkkaConfigurationBuilder, string, PersistenceMode, bool, Action{AkkaPersistenceJournalBuilder}, Action{AkkaPersistenceSnapshotBuilder}, string, bool)"/>.
/// </remarks>
public static class AzureRedisHostingExtensions
{
    /// <summary>
    /// Adds Akka.Persistence.Redis with automatic Azure Managed Identity authentication when
    /// the connection string targets an Azure-managed Redis host. The host suffix is
    /// auto-detected (<c>.redis.azure.net</c> for Azure Managed Redis,
    /// <c>.redis.cache.windows.net</c> for Azure Cache for Redis); for any other host the
    /// helper behaves exactly like <see cref="AkkaPersistenceRedisHostingExtensions.WithRedisPersistence(AkkaConfigurationBuilder, string, PersistenceMode, bool, Action{AkkaPersistenceJournalBuilder}, Action{AkkaPersistenceSnapshotBuilder}, string, bool)"/>.
    /// </summary>
    /// <param name="builder">The builder instance being configured.</param>
    /// <param name="connectionString">
    /// Redis connection string. For Azure Managed Redis this is typically
    /// <c>"your-redis.{region}.redis.azure.net:10000"</c>; for Azure Cache for Redis,
    /// <c>"your-cache.redis.cache.windows.net:6380"</c>.
    /// </param>
    /// <param name="credential">
    /// The <see cref="TokenCredential"/> to use when the host is detected as Azure-managed.
    /// Defaults to <see cref="ManagedIdentityCredential"/>. Set this explicitly when the
    /// app needs a user-assigned managed identity, a workload identity, or any other
    /// credential type from <c>Azure.Identity</c> (e.g. <see cref="DefaultAzureCredential"/>
    /// for local-dev fallbacks). Ignored for non-Azure hosts.
    /// </param>
    /// <param name="mode">
    /// Determines which plugin sections are configured. Default <see cref="PersistenceMode.Both"/>.
    /// </param>
    /// <param name="autoInitialize">
    /// Whether the Redis storage should be initialized automatically. Default <c>true</c>.
    /// </param>
    /// <param name="journalBuilder">
    /// Optional configurator for the journal's <see cref="AkkaPersistenceJournalBuilder"/>
    /// (event adapters, health checks).
    /// </param>
    /// <param name="snapshotBuilder">
    /// Optional configurator for the snapshot store's <see cref="AkkaPersistenceSnapshotBuilder"/>
    /// (health checks).
    /// </param>
    /// <param name="pluginIdentifier">
    /// Plugin identifier, used to form the HOCON path (e.g. <c>akka.persistence.journal.{identifier}</c>).
    /// Default <c>"redis"</c>.
    /// </param>
    /// <param name="isDefaultPlugin">
    /// Whether this plugin is the default journal/snapshot store for the
    /// <see cref="Akka.Actor.ActorSystem"/>. Default <c>true</c>.
    /// </param>
    /// <example>
    /// <code>
    /// // Azure Managed Redis with system-assigned Managed Identity — one-liner.
    /// builder.WithAzureRedisPersistence("your-redis.swedencentral.redis.azure.net:10000");
    ///
    /// // Pin a specific user-assigned Managed Identity.
    /// builder.WithAzureRedisPersistence(
    ///     "your-redis.swedencentral.redis.azure.net:10000",
    ///     credential: new ManagedIdentityCredential("your-client-id"));
    ///
    /// // Local development — falls through to the plain connection string path.
    /// builder.WithAzureRedisPersistence("localhost:6379");
    /// </code>
    /// </example>
    public static AkkaConfigurationBuilder WithAzureRedisPersistence(
        this AkkaConfigurationBuilder builder,
        string connectionString,
        TokenCredential? credential = null,
        PersistenceMode mode = PersistenceMode.Both,
        bool autoInitialize = true,
        Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
        Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null,
        string pluginIdentifier = "redis",
        bool isDefaultPlugin = true)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("Connection string must not be empty.", nameof(connectionString));

        Func<Task<IConnectionMultiplexer>>? factory = null;
        if (IsAzureRedisHost(connectionString))
        {
            var resolvedCredential = credential ?? new ManagedIdentityCredential();
            factory = CreateAzureConnectionFactory(connectionString, resolvedCredential);
        }

        var journalOpt = new RedisJournalOptions(isDefaultPlugin, pluginIdentifier)
        {
            ConfigurationString = connectionString,
            AutoInitialize = autoInitialize,
            ConnectionMultiplexerFactory = factory,
        };

        var snapshotOpt = new RedisSnapshotOptions(isDefaultPlugin, pluginIdentifier)
        {
            ConfigurationString = connectionString,
            AutoInitialize = autoInitialize,
            ConnectionMultiplexerFactory = factory,
        };

        return mode switch
        {
            PersistenceMode.Journal       => builder.WithRedisPersistence(journalOpt, null, journalBuilder, snapshotBuilder),
            PersistenceMode.SnapshotStore => builder.WithRedisPersistence(null, snapshotOpt, journalBuilder, snapshotBuilder),
            PersistenceMode.Both          => builder.WithRedisPersistence(journalOpt, snapshotOpt, journalBuilder, snapshotBuilder),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid PersistenceMode."),
        };
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="connectionString"/> targets an
    /// Azure Managed Redis (<c>.redis.azure.net</c>) or Azure Cache for Redis
    /// (<c>.redis.cache.windows.net</c>) host with no explicit password set. A password
    /// in the connection string is treated as an opt-out (the user wants connection-string
    /// auth) regardless of host suffix.
    /// </summary>
    /// <remarks>
    /// The bare <c>catch</c> below is intentional: this auto-detection runs from
    /// <see cref="WithAzureRedisPersistence"/> on the configuration path and must never
    /// throw. A malformed connection string falling out as <see langword="false"/> is the
    /// safe default — the configuration error will surface from <see cref="ConnectionMultiplexer.Connect(string, System.IO.TextWriter)"/>
    /// at first connect with a clearer message.
    /// </remarks>
    internal static bool IsAzureRedisHost(string connectionString)
    {
        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            if (!string.IsNullOrEmpty(options.Password))
                return false;

            var host = GetRedisHost(options);
            return host is not null && (
                host.EndsWith(".redis.cache.windows.net", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".redis.azure.net", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a <see cref="Func{TResult}"/> that lazily constructs and caches the
    /// <see cref="IConnectionMultiplexer"/> for an Azure-managed Redis. The factory is
    /// idempotent across multiple calls — every journal or snapshot store actor that
    /// invokes it gets the same multiplexer instance, with the lifecycle owned by an
    /// internal <see cref="AzureRedisConnectionHolder"/>. The holder serializes
    /// (re)creation through a single <see cref="SemaphoreSlim"/>, which gives correct
    /// cross-thread visibility on every supported architecture without requiring
    /// hand-rolled double-checked locking.
    /// </summary>
    private static Func<Task<IConnectionMultiplexer>> CreateAzureConnectionFactory(
        string connectionString, TokenCredential credential)
    {
        var holder = new AzureRedisConnectionHolder(ConnectAsync);
        return holder.GetAsync;

        async Task<IConnectionMultiplexer> ConnectAsync()
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.Ssl = true;
            options.Protocol = RedisProtocol.Resp3;

            // SE.Redis only validates the SSL certificate hostname when SslHost is set.
            // For Azure-managed Redis the hostname embedded in the connection string is
            // the right value, so default it when the caller hasn't already set one.
            var host = GetRedisHost(options);
            if (host is not null)
                options.SslHost ??= host;

            await options.ConfigureForAzureWithTokenCredentialAsync(credential).ConfigureAwait(false);
            return await ConnectionMultiplexer.ConnectAsync(options).ConfigureAwait(false);
        }
    }

    private static string? GetRedisHost(ConfigurationOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.SslHost))
            return options.SslHost;

        if (options.EndPoints.FirstOrDefault() is DnsEndPoint dnsEndpoint)
            return dnsEndpoint.Host;

        if (options.EndPoints.FirstOrDefault() is IPEndPoint ipEndpoint)
            return ipEndpoint.Address.ToString();

        return null;
    }
}
