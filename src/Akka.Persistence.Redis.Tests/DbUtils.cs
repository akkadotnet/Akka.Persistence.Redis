//-----------------------------------------------------------------------
// <copyright file="DbUtils.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using StackExchange.Redis;

namespace Akka.Persistence.Redis.Tests
{
    public static class DbUtils
    {
        public static void Clean(int database)
        {
            var connectionString = "localhost,allowAdmin=true";

            var redisConnection = ConnectionMultiplexer.Connect(connectionString);
            var server = redisConnection.GetServer(redisConnection.GetEndPoints().First());
            server.FlushDatabase(database);
        }
    }
}
