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

namespace Akka.Persistence.Redis
{
    /// <summary>
    /// Per-<see cref="ActorSystem"/> setup that supplies a single pre-configured
    /// <see cref="IConnectionMultiplexer"/> to <i>all</i> Redis journal and snapshot store
    /// instances in the system. Use this when one Redis instance backs every Redis
    /// persistence plugin in the application — the common case.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For applications that run multiple instances of the Redis journal or snapshot store
    /// against <i>different</i> Redis instances (multi-tenant sharding, journal vs snapshot
    /// on separate Redis tiers, migration cutovers, etc.), use
    /// <see cref="MultiRedisConnectionMultiplexerSetup"/> instead — it carries one factory
    /// per plugin id.
    /// </para>
    /// <para>
    /// <b>Lookup order.</b> When both this setup and <see cref="MultiRedisConnectionMultiplexerSetup"/>
    /// are present on the same <see cref="ActorSystem"/>, the single setup wins for every
    /// plugin instance. The multi setup is consulted only when the single setup is absent.
    /// </para>
    /// <para>
    /// <b>Lifetime contract.</b> The plugin treats the multiplexer returned by the factory
    /// as caller-owned and will <i>not</i> dispose it during <see cref="UntypedActor.PostStop"/>.
    /// The factory should typically cache and return the same
    /// <see cref="IConnectionMultiplexer"/> on every invocation.
    /// </para>
    /// <para>
    /// <b>Akka.Hosting users</b> normally do not construct this setup directly; setting
    /// <see cref="P:Akka.Persistence.Redis.Hosting.RedisJournalOptions.ConnectionMultiplexerFactory"/>
    /// (and/or its snapshot-store equivalent) on the hosting options causes the hosting
    /// extension to assemble an appropriate Setup. Construct this class explicitly only
    /// when bootstrapping an <see cref="ActorSystem"/> outside Akka.Hosting via
    /// <see cref="ActorSystem.Create(string, ActorSystemSetup)"/>.
    /// </para>
    /// </remarks>
    public sealed class RedisConnectionMultiplexerSetup : Setup
    {
        public RedisConnectionMultiplexerSetup(Func<Task<IConnectionMultiplexer>> connectionFactory)
        {
            ConnectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        /// <summary>
        /// Convenience factory method for constructing a setup from a synchronous factory
        /// (e.g. when the caller already has a fully-constructed multiplexer).
        /// </summary>
        public static RedisConnectionMultiplexerSetup Create(Func<IConnectionMultiplexer> connectionFactory)
        {
            if (connectionFactory is null) throw new ArgumentNullException(nameof(connectionFactory));
            return new RedisConnectionMultiplexerSetup(() => Task.FromResult(connectionFactory()));
        }

        public Func<Task<IConnectionMultiplexer>> ConnectionFactory { get; }
    }

    /// <summary>
    /// Per-<see cref="ActorSystem"/> setup that supplies a different
    /// <see cref="IConnectionMultiplexer"/> factory for each Redis journal or snapshot store
    /// instance, keyed by plugin id (the full HOCON path, e.g.
    /// <c>"akka.persistence.journal.redis"</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use this when an application runs multiple Redis persistence plugin instances against
    /// different Redis backends — for example: cluster sharding regions backed by different
    /// Redis clusters, journal events on a fast write-tuned Redis with snapshots on a
    /// separate durable Redis, or a temporary migration cutover where new writes go to one
    /// backend while reads still hit another.
    /// </para>
    /// <para>
    /// Plugin ids are the full HOCON paths of the journal or snapshot store configuration
    /// section — e.g. <c>"akka.persistence.journal.redis"</c> for the default journal,
    /// <c>"akka.persistence.snapshot-store.redis"</c> for the default snapshot store, or
    /// <c>"akka.persistence.journal.region-a"</c> for a custom-id journal. The key set is
    /// independent: a journal at <c>"akka.persistence.journal.redis"</c> and a snapshot
    /// store at <c>"akka.persistence.snapshot-store.redis"</c> can carry different factories.
    /// </para>
    /// <para>
    /// <b>Lookup order.</b> When both <see cref="RedisConnectionMultiplexerSetup"/> and
    /// this multi setup are present on the same <see cref="ActorSystem"/>, the single
    /// setup takes precedence and applies to every plugin instance. This multi setup is
    /// consulted only when the single setup is absent.
    /// </para>
    /// <para>
    /// <b>Lifetime contract.</b> Same as <see cref="RedisConnectionMultiplexerSetup"/>:
    /// caller-owned multiplexers, the plugin will not dispose them on
    /// <see cref="UntypedActor.PostStop"/>.
    /// </para>
    /// </remarks>
    public sealed class MultiRedisConnectionMultiplexerSetup : Setup
    {
        private readonly Dictionary<string, Func<Task<IConnectionMultiplexer>>> _factories = new();

        /// <summary>
        /// Registers <paramref name="connectionFactory"/> for the plugin at
        /// <paramref name="pluginId"/>. Existing registrations for the same plugin id are
        /// replaced.
        /// </summary>
        /// <param name="pluginId">
        /// The full HOCON path of the journal or snapshot store plugin (e.g.
        /// <c>"akka.persistence.journal.redis"</c>).
        /// </param>
        /// <param name="connectionFactory">A factory delegate returning a configured multiplexer.</param>
        public MultiRedisConnectionMultiplexerSetup AddFactory(
            string pluginId,
            Func<Task<IConnectionMultiplexer>> connectionFactory)
        {
            if (pluginId is null) throw new ArgumentNullException(nameof(pluginId));
            if (connectionFactory is null) throw new ArgumentNullException(nameof(connectionFactory));
            _factories[pluginId] = connectionFactory;
            return this;
        }

        /// <summary>
        /// Returns <see langword="true"/> and the registered factory if one exists for
        /// <paramref name="pluginId"/>; otherwise returns <see langword="false"/> with a
        /// <see langword="null"/> out parameter.
        /// </summary>
        public bool TryGetFactory(string pluginId, out Func<Task<IConnectionMultiplexer>>? connectionFactory)
        {
            if (pluginId is null) throw new ArgumentNullException(nameof(pluginId));
            return _factories.TryGetValue(pluginId, out connectionFactory);
        }
    }
}
