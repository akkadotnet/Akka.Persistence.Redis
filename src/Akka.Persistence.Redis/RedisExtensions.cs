using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using StackExchange.Redis;

namespace Akka.Persistence.Redis
{
    public static class RedisExtensions
    {
        public static bool IsClustered(this IConnectionMultiplexer connection)
            => connection.GetEndPoints()
                .Select(endPoint => connection.GetServer(endPoint))
                .Any(server => server.ServerType == ServerType.Cluster);
    }
}
