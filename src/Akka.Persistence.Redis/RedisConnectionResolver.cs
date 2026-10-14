// -----------------------------------------------------------------------
//  <copyright file="RedisConnectionResolver.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Event;
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
        public static RedisPluginConnection Resolve(ActorSystem system, RedisSettings settings, string pluginId, ILoggingAdapter log)
        {
            IConnectionMultiplexer connection;
            bool ownsConnection;

            var setup = system.Settings.Setup.Get<RedisConnectionMultiplexerSetup>();
            if (setup.HasValue && setup.Value.TryGetSource(pluginId, out var source))
            {
                connection = source!.Factory().GetAwaiter().GetResult();
                ownsConnection = source.Ownership == RedisConnectionOwnership.PluginOwned;
            }
            else
            {
                connection = ConnectionMultiplexer.Connect(settings.ConfigurationString);
                ownsConnection = true;
            }

            // The cluster mode is not read here: a multiplexer created with abortConnect=false
            // may not be connected yet, and would report every endpoint as Standalone.
            return new RedisPluginConnection(connection, ownsConnection, settings, log);
        }
    }
}
