// -----------------------------------------------------------------------
//  <copyright file="RedisConnectivityCheckBase.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Hosting;
using Akka.Persistence.Redis;
using Akka.Util;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

/// <summary>
/// Shared body of the journal and snapshot store connectivity checks. Resolves the
/// optional <see cref="RedisConnectionMultiplexerSetup"/> at probe time so the probe
/// reuses the plugin's multiplexer when one is registered, and falls back to
/// open-and-dispose against the configured connection string otherwise.
/// </summary>
public abstract class RedisConnectivityCheckBase : IAkkaHealthCheck
{
    private readonly string _connectionString;
    private readonly string _componentLabel;
    private readonly string _componentId;
    private readonly string _pluginId;

    protected RedisConnectivityCheckBase(string connectionString, string componentLabel, string componentId, string pluginId)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _componentLabel = componentLabel;
        _componentId = componentId;
        _pluginId = pluginId;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(AkkaHealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var setup = context.ActorSystem?.Settings.Setup.Get<RedisConnectionMultiplexerSetup>()
            ?? Option<RedisConnectionMultiplexerSetup>.None;
        RedisConnectionSource? registeredSource = null;
        var hasRegisteredSource = setup.HasValue && setup.Value.TryGetSource(_pluginId, out registeredSource);
        var ownsConnection = !hasRegisteredSource;
        IConnectionMultiplexer? connection = null;
        try
        {
            connection = hasRegisteredSource
                ? await registeredSource!.Factory()
                : await ConnectionMultiplexer.ConnectAsync(_connectionString);

            var server = connection.GetServer(connection.GetEndPoints().First());
            await server.PingAsync();
            return HealthCheckResult.Healthy($"Redis {_componentLabel} '{_componentId}' connection successful");
        }
        catch (OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy($"Redis {_componentLabel} '{_componentId}' connectivity check timed out");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Redis {_componentLabel} '{_componentId}' connection failed", ex);
        }
        finally
        {
            if (ownsConnection && connection is not null)
                connection.Dispose();
        }
    }
}
