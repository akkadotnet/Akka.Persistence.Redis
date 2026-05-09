// -----------------------------------------------------------------------
//  <copyright file="RedisConnectionMultiplexerSetup.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis
{
    public enum RedisConnectionOwnership
    {
        CallerOwned,
        PluginOwned,
        ActorSystemOwned
    }

    /// <summary>
    /// Per-<see cref="ActorSystem"/> setup that supplies Redis connection factories keyed
    /// by journal or snapshot-store plugin id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Connection injection is plugin-scoped. A factory registered for
    /// <c>akka.persistence.journal.redis</c> does not affect any other Redis plugin unless
    /// that plugin id is explicitly registered to the same factory.
    /// </para>
    /// <para>
    /// Ownership controls disposal. Caller-owned connections are never disposed by the
    /// plugin. Plugin-owned connections are disposed from <see cref="UntypedActor.PostStop"/>.
    /// ActorSystem-owned connections are expected to be disposed once by the code that
    /// registered the setup, typically via CoordinatedShutdown.
    /// </para>
    /// </remarks>
    public sealed class RedisConnectionMultiplexerSetup : Setup
    {
        private readonly Dictionary<string, RedisConnectionSource> _sources = new();

        public RedisConnectionMultiplexerSetup Add(
            string pluginId,
            Func<Task<IConnectionMultiplexer>> factory,
            RedisConnectionOwnership ownership = RedisConnectionOwnership.CallerOwned)
        {
            if (string.IsNullOrWhiteSpace(pluginId)) throw new ArgumentException("Plugin id must not be empty.", nameof(pluginId));
            if (factory is null) throw new ArgumentNullException(nameof(factory));

            if (_sources.ContainsKey(pluginId))
                throw new InvalidOperationException($"A Redis connection source is already registered for plugin id [{pluginId}].");

            _sources.Add(pluginId, new RedisConnectionSource(factory, ownership));
            return this;
        }

        public bool TryGetSource(string pluginId, out RedisConnectionSource? source)
        {
            if (pluginId is null) throw new ArgumentNullException(nameof(pluginId));
            return _sources.TryGetValue(pluginId, out source);
        }
    }

    public sealed class RedisConnectionSource
    {
        internal RedisConnectionSource(
            Func<Task<IConnectionMultiplexer>> factory,
            RedisConnectionOwnership ownership)
        {
            Factory = factory;
            Ownership = ownership;
        }

        public Func<Task<IConnectionMultiplexer>> Factory { get; }

        public RedisConnectionOwnership Ownership { get; }
    }
}
