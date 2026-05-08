// -----------------------------------------------------------------------
//  <copyright file="RedisConnectionResolver.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis
{
    /// <summary>
    /// Shared connection resolution for <c>RedisJournal</c> and <c>RedisSnapshotStore</c>.
    /// Reads the optional <see cref="RedisConnectionMultiplexerSetup"/> once at plugin
    /// construction; falls back to <see cref="ConnectionMultiplexer.Connect(string, System.IO.TextWriter)"/>
    /// against the HOCON connection string when none is registered.
    /// </summary>
    internal static class RedisConnectionResolver
    {
        public static (IConnectionMultiplexer Connection, IDatabase Database, bool IsClustered, bool OwnsConnection)
            Resolve(ActorSystem system, RedisSettings settings)
        {
            IConnectionMultiplexer connection;
            bool ownsConnection;

            var setup = system.Settings.Setup.Get<RedisConnectionMultiplexerSetup>();
            if (setup.HasValue)
            {
                connection = setup.Value.Factory().GetAwaiter().GetResult();
                ownsConnection = setup.Value.OwnedByPlugin;
            }
            else
            {
                connection = ConnectionMultiplexer.Connect(settings.ConfigurationString);
                ownsConnection = true;
            }

            var isClustered = connection.IsClustered();
            var database = connection.GetDatabase(ResolveDatabaseNumber(settings, ownsConnection, isClustered));
            return (connection, database, isClustered, ownsConnection);
        }

        private static int ResolveDatabaseNumber(RedisSettings settings, bool ownsConnection, bool isClustered)
        {
            if (isClustered)
            {
                // Redis Cluster pins everything to db 0 — https://redis.io/topics/cluster-spec#implemented-subset
                return 0;
            }

            // DatabaseFromConnectionString is only meaningful when we own the connection
            // string ourselves; an injected multiplexer is opaque so fall back to the
            // explicitly configured Database value.
            if (ownsConnection && settings.DatabaseFromConnectionString)
            {
                var conf = ConfigurationOptions.Parse(settings.ConfigurationString);
                if (conf.DefaultDatabase.HasValue)
                    return conf.DefaultDatabase.Value;
            }

            return settings.Database;
        }
    }
}
