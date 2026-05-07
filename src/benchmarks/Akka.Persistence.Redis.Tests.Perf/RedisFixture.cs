// -----------------------------------------------------------------------
// <copyright file="RedisFixture.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System.Threading.Tasks;
using Testcontainers.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    [CollectionDefinition("RedisSpec")]
    public sealed class RedisSpecsFixture : ICollectionFixture<RedisFixture>
    {
    }

    public class RedisFixture : IAsyncLifetime
    {
        private readonly RedisContainer _container1 = new RedisBuilder("redis:latest").Build();

        private readonly RedisContainer _container2 = new RedisBuilder("redis:latest").Build();

        public string ConnectionString { get; private set; } = string.Empty;

        public async ValueTask InitializeAsync()
        {
            await Task.WhenAll(_container1.StartAsync(), _container2.StartAsync());

            ConnectionString = $"{_container1.GetConnectionString()},{_container2.GetConnectionString()}";
        }

        public async ValueTask DisposeAsync()
        {
            await Task.WhenAll(
                _container1.DisposeAsync().AsTask(),
                _container2.DisposeAsync().AsTask());
        }
    }
}
