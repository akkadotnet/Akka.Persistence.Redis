// -----------------------------------------------------------------------
// <copyright file="RedisClusterFixture.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Util;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
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
                .WithEnvironment("INITIAL_PORT", _basePort.ToString())
                .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Cluster state changed: ok"));

            for (var offset = 0; offset < 6; offset++)
            {
                var port = _basePort + offset;
                builder = builder.WithPortBinding(port, port);
            }

            _container = builder.Build();

            await _container.StartAsync();

            ConnectionString = $"127.0.0.1:{_basePort}";
        }

        public async ValueTask DisposeAsync()
        {
            if (_container is not null)
                await _container.DisposeAsync();
        }
    }
}
