// -----------------------------------------------------------------------
// <copyright file="DbUtils.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System.Linq;
using StackExchange.Redis;

namespace Akka.Persistence.Redis.Cluster.Test
{
    public static class DbUtils
    {
        public static string ConnectionString { get; private set; }

        public static void Initialize(RedisClusterFixture fixture)
        {
            ConnectionString = fixture.ConnectionString;
        }

        public static void Initialize(string connectionString)
        {
            ConnectionString = connectionString;
        }

        public static void Clean(int database = -1)
        {
            var connectionString = $"{ConnectionString},allowAdmin=true";

            var redisConnection = ConnectionMultiplexer.Connect(connectionString);
            foreach (var endPoint in redisConnection.GetEndPoints(false))
            {
                var server = redisConnection.GetServer(endPoint);
                if (!server.IsReplica)
                    server.FlushAllDatabases();
            }
        }
    }
}