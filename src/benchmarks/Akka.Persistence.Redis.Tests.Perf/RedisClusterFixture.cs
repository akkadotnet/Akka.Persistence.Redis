// -----------------------------------------------------------------------
// <copyright file="RedisClusterFixture.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Util;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Cluster.Tests
{
    [CollectionDefinition("RedisClusterSpec")]
    public sealed class RedisSpecsFixture : ICollectionFixture<RedisClusterFixture>
    {
    }

    public class RedisClusterFixture : IAsyncLifetime
    {
        // grokzen/redis-cluster gossips its node addresses based on INITIAL_PORT / IP, so the
        // container's published ports must match the host ports SE.Redis later connects to.
        private readonly int _basePort = ThreadLocalRandom.Current.Next(9000, 10000);

        private IContainer _container = null!;

        public string ConnectionString { get; private set; } = string.Empty;

        public async ValueTask InitializeAsync()
        {
            var builder = new ContainerBuilder("grokzen/redis-cluster:6.0.13")
                .WithEnvironment("IP", "0.0.0.0")
                .WithEnvironment("INITIAL_PORT", _basePort.ToString());

            for (var offset = 0; offset < 6; offset++)
            {
                var port = _basePort + offset;
                builder = builder.WithPortBinding(port, port);
            }

            _container = builder.Build();
            await _container.StartAsync();

            ConnectionString = $"127.0.0.1:{_basePort}";

            // The 6-node cluster bootstraps asynchronously after the container starts.
            // Each Redis node computes cluster_state from its own view; a single node
            // returning "ok" while a peer still has the cluster marked FAIL is enough to
            // surface CLUSTERDOWN to the journal during its first command. Wait until
            // every node we know about reports cluster_state:ok with full slot coverage.
            await WaitForClusterReadyAsync(TimeSpan.FromSeconds(90));
        }

        public async ValueTask DisposeAsync()
        {
            if (_container is not null)
                await _container.DisposeAsync();
        }

        private async Task WaitForClusterReadyAsync(TimeSpan timeout)
        {
            // Probe via the CLUSTER INFO command, which is part of the cluster command suite
            // and does not require allowAdmin. The probe stays inside this fixture; production
            // connection strings are unaffected.
            var probeConnectionString = $"{ConnectionString},abortConnect=false,connectRetry=5,connectTimeout=2000";
            var deadline = DateTime.UtcNow + timeout;
            Exception? lastError = null;
            string? lastNotReadyReason = null;

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var probe = await ConnectionMultiplexer.ConnectAsync(probeConnectionString);
                    var endpoints = probe.GetEndPoints();

                    // SE.Redis discovers topology after connect; until it has found all 6
                    // grokzen nodes, retry rather than declare ready.
                    if (endpoints.Length < 6)
                    {
                        lastNotReadyReason = $"only {endpoints.Length}/6 endpoints discovered";
                    }
                    else
                    {
                        var allHealthy = true;
                        foreach (var endpoint in endpoints)
                        {
                            var server = probe.GetServer(endpoint);
                            var result = await server.ExecuteAsync("CLUSTER", "INFO");
                            var kv = ParseClusterInfo(result.ToString());
                            kv.TryGetValue("cluster_state", out var state);
                            kv.TryGetValue("cluster_slots_assigned", out var assigned);
                            kv.TryGetValue("cluster_slots_ok", out var slotsOk);

                            if (state != "ok" || assigned != "16384" || slotsOk != "16384")
                            {
                                allHealthy = false;
                                lastNotReadyReason = $"endpoint {endpoint} reports cluster_state={state ?? "?"} slots_assigned={assigned ?? "?"} slots_ok={slotsOk ?? "?"}";
                                break;
                            }
                        }

                        if (allHealthy)
                            return;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    lastNotReadyReason = ex.Message;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            var detail = lastNotReadyReason ?? "no probe attempted";
            throw new TimeoutException(
                $"Redis cluster at {ConnectionString} did not become healthy within {timeout}. Last status: {detail}.",
                lastError);
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseClusterInfo(string raw)
        {
            var kv = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(raw))
                return kv;

            foreach (var line in raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var sep = line.IndexOf(':');
                if (sep <= 0) continue;
                kv[line.Substring(0, sep)] = line.Substring(sep + 1);
            }

            return kv;
        }
    }
}
