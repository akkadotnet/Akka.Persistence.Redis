// -----------------------------------------------------------------------
// <copyright file="RedisExtensions.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

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
        {
            return connection.GetEndPoints()
                .Select(endPoint => connection.GetServer(endPoint))
                .Any(server => server.ServerType == ServerType.Cluster);
        }
    }
}