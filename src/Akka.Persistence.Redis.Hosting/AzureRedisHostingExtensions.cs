// -----------------------------------------------------------------------
//  <copyright file="AzureRedisHostingExtensions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Akka.Persistence.Redis;
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
public static class AzureRedisHostingExtensions
{
    /// <summary>
    /// Adds Akka.Persistence.Redis with automatic Azure Managed Identity authentication when
    /// the connection string targets an Azure-managed Redis host. The host suffix is
    /// auto-detected (<c>.redis.azure.net</c> for Azure Managed Redis,
    /// <c>.redis.cache.windows.net</c> for Azure Cache for Redis); for any other host the
    /// helper falls through to the plain connection-string <c>WithRedisPersistence</c> overload.
    /// </summary>
    /// <remarks>
    /// The Azure helper builds a single <see cref="IConnectionMultiplexer"/> and registers a
    /// <see cref="RedisConnectionMultiplexerSetup"/> so every Redis plugin in the
    /// <see cref="Akka.Actor.ActorSystem"/> shares one multiplexer (one Azure auth handshake,
    /// one token refresh cycle, one TCP pool). A failed connection attempt, such as a token
    /// that cannot be acquired yet, is not cached: the next request for the connection starts
    /// a new attempt. The multiplexer is owned by the <see cref="Akka.Actor.ActorSystem"/> and
    /// disposed during <see cref="CoordinatedShutdown"/>, not by the plugins.
    /// </remarks>
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

        if (IsAzureRedisHost(connectionString))
        {
            var resolvedCredential = credential ?? new ManagedIdentityCredential();
            var source = CreateAzureConnectionSource(connectionString, resolvedCredential);
            builder.AddStartup((system, _) =>
            {
                CoordinatedShutdown.Get(system).AddTask(
                    CoordinatedShutdown.PhaseBeforeActorSystemTerminate,
                    $"dispose-redis-azure-multiplexer-{pluginIdentifier}-{mode}",
                    source.DisposeAsync);
            });

            return builder.WithRedisPersistence(
                multiplexerFactory: source.Factory,
                ownership: RedisConnectionOwnership.ActorSystemOwned,
                mode: mode,
                autoInitialize: autoInitialize,
                journalBuilder: journalBuilder,
                snapshotBuilder: snapshotBuilder,
                pluginIdentifier: pluginIdentifier,
                isDefaultPlugin: isDefaultPlugin);
        }

        return builder.WithRedisPersistence(
            connectionString,
            mode: mode,
            autoInitialize: autoInitialize,
            journalBuilder: journalBuilder,
            snapshotBuilder: snapshotBuilder,
            pluginIdentifier: pluginIdentifier,
            isDefaultPlugin: isDefaultPlugin);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="connectionString"/> targets an
    /// Azure Managed Redis (<c>.redis.azure.net</c>) or Azure Cache for Redis
    /// (<c>.redis.cache.windows.net</c>) host with no explicit password set. A password
    /// is treated as an opt-out from Entra auth regardless of host suffix.
    /// </summary>
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
            // Auto-detection runs on the configuration path and must never throw — a
            // malformed connection string falls through as `false`; the real error will
            // surface at ConnectionMultiplexer.Connect with a clearer message.
            return false;
        }
    }

    private static AzureConnectionSource CreateAzureConnectionSource(
        string connectionString, TokenCredential credential)
    {
        // Single shared multiplexer so every Redis plugin in the system uses one Azure auth
        // handshake and one token-refresh cycle. Without this caching, each plugin instance
        // would do its own ConfigureForAzureWithTokenCredentialAsync call, which is the
        // opposite of what users asking for the Azure helper expect.
        return new AzureConnectionSource(() => ConnectAsync(connectionString, credential));
    }

    private static async Task<IConnectionMultiplexer> ConnectAsync(
        string connectionString, TokenCredential credential)
    {
        var options = ConfigurationOptions.Parse(connectionString);
        options.Ssl = true;
        options.Protocol = RedisProtocol.Resp3;

        // SE.Redis only validates the SSL certificate hostname when SslHost is set. For
        // Azure-managed Redis the hostname embedded in the connection string is the right
        // value, so default it when the caller hasn't already set one.
        var host = GetRedisHost(options);
        if (host is not null)
            options.SslHost ??= host;

        await options.ConfigureForAzureWithTokenCredentialAsync(credential);
        return await ConnectionMultiplexer.ConnectAsync(options);
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

    private sealed class AzureConnectionSource
    {
        private readonly Func<Task<IConnectionMultiplexer>> _connect;
        private readonly object _lock = new();
        private Task<IConnectionMultiplexer>? _shared;
        private bool _disposed;

        public AzureConnectionSource(Func<Task<IConnectionMultiplexer>> connect)
        {
            _connect = connect;
        }

        /// <summary>
        /// Returns the shared connection. The first call starts connecting; later calls share
        /// that attempt while it is in flight or once it has succeeded. A faulted or canceled
        /// attempt is never handed out twice: the next call starts a new one.
        /// </summary>
        public Task<IConnectionMultiplexer> Factory()
        {
            lock (_lock)
            {
                if (_disposed)
                    return Task.FromException<IConnectionMultiplexer>(new ObjectDisposedException(nameof(AzureConnectionSource)));

                if (_shared is null || _shared.IsFaulted || _shared.IsCanceled)
                {
                    try
                    {
                        _shared = _connect();
                    }
                    catch (Exception ex)
                    {
                        _shared = Task.FromException<IConnectionMultiplexer>(ex);
                    }
                }

                return _shared;
            }
        }

        public Task<Done> DisposeAsync()
        {
            Task<IConnectionMultiplexer>? shared;
            lock (_lock)
            {
                if (_disposed)
                    return Task.FromResult(Done.Instance);
                _disposed = true;
                shared = _shared;
            }

            // Dispose the multiplexer once it exists, including one that is still connecting.
            shared?.ContinueWith(
                t => t.Result.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return Task.FromResult(Done.Instance);
        }
    }
}
