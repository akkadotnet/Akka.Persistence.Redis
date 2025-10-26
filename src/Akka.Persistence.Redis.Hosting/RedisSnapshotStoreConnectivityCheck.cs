// -----------------------------------------------------------------------
//  <copyright file="RedisSnapshotStoreConnectivityCheck.cs" company="Akka.NET Project">
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
/// Health check that verifies connectivity to the Redis instance used by the snapshot store.
/// This is a liveness check that proactively verifies backend connectivity.
/// </summary>
public sealed class RedisSnapshotStoreConnectivityCheck : IAkkaHealthCheck
{
    private readonly string _connectionString;
    private readonly string _snapshotStoreId;

    public RedisSnapshotStoreConnectivityCheck(string connectionString, string snapshotStoreId)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _snapshotStoreId = snapshotStoreId ?? throw new ArgumentNullException(nameof(snapshotStoreId));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var connection = await ConnectionMultiplexer.ConnectAsync(_connectionString);
            var server = connection.GetServer(connection.GetEndPoints().First());
            await server.PingAsync();
            return HealthCheckResult.Healthy($"Redis snapshot store '{_snapshotStoreId}' connection successful");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"Redis snapshot store '{_snapshotStoreId}' connectivity check timed out");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Redis snapshot store '{_snapshotStoreId}' connection failed", ex);
        }
    }
}
