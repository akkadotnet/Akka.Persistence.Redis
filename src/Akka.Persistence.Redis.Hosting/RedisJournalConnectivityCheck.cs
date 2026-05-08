// -----------------------------------------------------------------------
//  <copyright file="RedisJournalConnectivityCheck.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

/// <summary>
/// Health check that verifies connectivity to the Redis instance used by the journal.
/// This is a liveness check that proactively verifies backend connectivity.
/// </summary>
/// <remarks>
/// When constructed with a connection string, every check opens a fresh
/// <see cref="ConnectionMultiplexer"/> and disposes it after the ping completes.
/// That is fine for occasional liveness probes but adds connect-time latency on
/// every check. When constructed with a factory delegate, the check reuses whatever
/// <see cref="IConnectionMultiplexer"/> the caller already owns and does <i>not</i>
/// dispose it — typically the same multiplexer the journal is using, so the probe is
/// effectively zero-cost.
/// </remarks>
public sealed class RedisJournalConnectivityCheck : IAkkaHealthCheck
{
    private readonly Func<Task<IConnectionMultiplexer>> _connectionFactory;
    private readonly bool _ownsConnection;
    private readonly string _journalId;

    /// <summary>
    /// Creates a connectivity check that opens a fresh <see cref="ConnectionMultiplexer"/>
    /// from <paramref name="connectionString"/> on every probe and disposes it after the
    /// ping completes.
    /// </summary>
    public RedisJournalConnectivityCheck(string connectionString, string journalId)
        : this(BuildFactoryFromConnectionString(connectionString), ownsConnection: true, journalId)
    {
    }

    /// <summary>
    /// Creates a connectivity check that reuses a caller-supplied
    /// <see cref="IConnectionMultiplexer"/> via <paramref name="connectionFactory"/> and
    /// does not dispose it. The factory should typically be the same delegate registered
    /// with <see cref="RedisConnectionProvider"/>; opening a separate multiplexer per check
    /// defeats the point of the extension point.
    /// </summary>
    public RedisJournalConnectivityCheck(Func<Task<IConnectionMultiplexer>> connectionFactory, string journalId)
        : this(connectionFactory, ownsConnection: false, journalId)
    {
    }

    private RedisJournalConnectivityCheck(
        Func<Task<IConnectionMultiplexer>> connectionFactory,
        bool ownsConnection,
        string journalId)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _ownsConnection = ownsConnection;
        _journalId = journalId ?? throw new ArgumentNullException(nameof(journalId));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context, CancellationToken cancellationToken = default)
    {
        IConnectionMultiplexer? connection = null;
        try
        {
            connection = await _connectionFactory().ConfigureAwait(false);
            var server = connection.GetServer(connection.GetEndPoints().First());
            await server.PingAsync().ConfigureAwait(false);
            return HealthCheckResult.Healthy($"Redis journal '{_journalId}' connection successful");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"Redis journal '{_journalId}' connectivity check timed out");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Redis journal '{_journalId}' connection failed", ex);
        }
        finally
        {
            if (_ownsConnection && connection is not null)
                connection.Dispose();
        }
    }

    private static Func<Task<IConnectionMultiplexer>> BuildFactoryFromConnectionString(string connectionString)
    {
        if (connectionString is null)
            throw new ArgumentNullException(nameof(connectionString));

        return async () => await ConnectionMultiplexer.ConnectAsync(connectionString).ConfigureAwait(false);
    }
}
