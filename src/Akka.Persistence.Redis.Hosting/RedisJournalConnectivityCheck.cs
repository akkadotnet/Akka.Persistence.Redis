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
public sealed class RedisJournalConnectivityCheck : IAkkaHealthCheck
{
    private readonly string _connectionString;
    private readonly string _journalId;

    public RedisJournalConnectivityCheck(string connectionString, string journalId)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _journalId = journalId ?? throw new ArgumentNullException(nameof(journalId));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await ConnectionMultiplexer.ConnectAsync(_connectionString);
            var server = connection.GetServer(connection.GetEndPoints().First());
            await server.PingAsync();
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
    }
}
