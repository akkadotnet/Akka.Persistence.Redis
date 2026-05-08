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
using Akka.Persistence.Redis;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

/// <summary>
/// Health check that verifies connectivity to the Redis instance used by the journal.
/// </summary>
/// <remarks>
/// At probe time the check looks for a <see cref="RedisConnectionMultiplexerSetup"/> on the
/// <see cref="Akka.Actor.ActorSystem"/>. If present, the check uses that factory (caller-
/// owned, never disposed) so the probe shares the journal's multiplexer when the factory
/// caches its result. If absent, the check opens a fresh <see cref="ConnectionMultiplexer"/>
/// from the configured connection string and disposes it after the ping completes.
/// </remarks>
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
        Akka.Util.Option<RedisConnectionMultiplexerSetup> setup = default;
        if (context.ActorSystem is not null)
            setup = context.ActorSystem.Settings.Setup.Get<RedisConnectionMultiplexerSetup>();

        var ownsConnection = !setup.HasValue;
        IConnectionMultiplexer? connection = null;
        try
        {
            connection = setup.HasValue
                ? await setup.Value.Factory()
                : await ConnectionMultiplexer.ConnectAsync(_connectionString);

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
        finally
        {
            if (ownsConnection && connection is not null)
                connection.Dispose();
        }
    }
}
