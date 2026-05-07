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

            // The 6-node cluster bootstraps asynchronously after the container starts. The
            // per-node "Cluster state changed: ok" log line fires for whichever node settles
            // first - waiting on it via Testcontainers' first-match log probe lets tests
            // start while the other nodes are still gossiping and the slot map is still in
            // flight. Probe the cluster directly until every slot is assigned and ok.
            await WaitForClusterReadyAsync(TimeSpan.FromSeconds(60));
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

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var probe = await ConnectionMultiplexer.ConnectAsync(probeConnectionString);
                    var server = probe.GetServer(probe.GetEndPoints().First());
                    var result = await server.ExecuteAsync("CLUSTER", "INFO");
                    var kv = ParseClusterInfo(result.ToString());

                    if (kv.TryGetValue("cluster_state", out var state) && state == "ok"
                        && kv.TryGetValue("cluster_slots_assigned", out var assigned) && assigned == "16384"
                        && kv.TryGetValue("cluster_slots_ok", out var slotsOk) && slotsOk == "16384")
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            throw new TimeoutException(
                $"Redis cluster at {ConnectionString} did not become healthy within {timeout}.",
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
