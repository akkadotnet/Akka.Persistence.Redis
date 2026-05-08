// -----------------------------------------------------------------------
//  <copyright file="RedisConnectionMultiplexerSetup.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis
{
    /// <summary>
    /// Per-<see cref="ActorSystem"/> setup that supplies an
    /// <see cref="IConnectionMultiplexer"/> factory to every Redis journal and snapshot
    /// store in the system. Use this to inject a multiplexer that needs custom construction
    /// — Azure Managed Identity / Entra ID, custom retry policies, RESP3 protocol, a
    /// pre-built instance, etc.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One Setup applies to every Redis plugin in the <see cref="ActorSystem"/>. Each plugin
    /// instance invokes <see cref="Factory"/> once during construction. Whether multiple
    /// plugins receive the same <see cref="IConnectionMultiplexer"/> instance is determined
    /// entirely by <see cref="Factory"/>: a delegate that caches its result (a closure-
    /// captured <see cref="Lazy{T}"/> for example) shares the multiplexer; a delegate that
    /// builds a fresh multiplexer on each call gives every plugin its own.
    /// </para>
    /// <para>
    /// <see cref="OwnedByPlugin"/> controls disposal. When <see langword="false"/> (the
    /// default), the plugin treats the multiplexer as caller-owned and never disposes it on
    /// <see cref="UntypedActor.PostStop"/>. When <see langword="true"/>, the plugin disposes
    /// whatever <see cref="Factory"/> returned — useful when the factory is purely a
    /// construction hook (e.g. async initialization) and the caller wants the plugin to
    /// manage lifetime.
    /// </para>
    /// <para>
    /// Akka.Hosting users normally do not construct this Setup directly — pass
    /// <c>multiplexerFactory:</c>, <c>multiplexer:</c>, or use
    /// <see cref="P:Akka.Persistence.Redis.Hosting.AzureRedisHostingExtensions"/> on the
    /// <c>WithRedisPersistence</c> overloads instead. Construct this class explicitly only
    /// when bootstrapping outside Akka.Hosting via
    /// <see cref="ActorSystem.Create(string, ActorSystemSetup)"/>.
    /// </para>
    /// </remarks>
    public sealed class RedisConnectionMultiplexerSetup : Setup
    {
        public RedisConnectionMultiplexerSetup(
            Func<Task<IConnectionMultiplexer>> factory,
            bool ownedByPlugin = false)
        {
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            OwnedByPlugin = ownedByPlugin;
        }

        public Func<Task<IConnectionMultiplexer>> Factory { get; }

        public bool OwnedByPlugin { get; }
    }
}
