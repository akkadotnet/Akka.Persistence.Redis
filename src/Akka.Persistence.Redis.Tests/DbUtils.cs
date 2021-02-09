// -----------------------------------------------------------------------
// <copyright file="DbUtils.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using StackExchange.Redis;

namespace Akka.Persistence.Redis.Tests
{
    public static class DbUtils
    {
        public static string ConnectionString { get; private set; }

        public static void Initialize(RedisFixture fixture)
        {
            ConnectionString = fixture.ConnectionString;
        }

        public static void Clean(int database)
        {
            var connectionString = $"{ConnectionString},allowAdmin=true";

            var redisConnection = ConnectionMultiplexer.Connect(connectionString);
            foreach (var endPoint in redisConnection.GetEndPoints())
            {
                var server = redisConnection.GetServer(endPoint);
                if (!server.IsReplica)
                    server.FlushAllDatabases();
            }
        }
    }
}