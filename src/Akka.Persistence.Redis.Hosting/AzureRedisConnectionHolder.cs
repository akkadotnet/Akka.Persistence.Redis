// -----------------------------------------------------------------------
//  <copyright file="AzureRedisConnectionHolder.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

/// <summary>
/// Owns the lifecycle of a single <see cref="IConnectionMultiplexer"/> on behalf of one or
/// more Redis persistence plugins (typically a journal and a snapshot store sharing the
/// same backend). The first <see cref="GetAsync"/> call constructs the multiplexer; every
/// subsequent call returns the cached instance, recreating it only when the previously
/// cached multiplexer is no longer reporting <see cref="IConnectionMultiplexer.IsConnected"/>.
/// </summary>
/// <remarks>
/// <para>
/// Concurrency is handled by a single <see cref="SemaphoreSlim"/>: the fast path is a
/// volatile-free field load plus an <see cref="IConnectionMultiplexer.IsConnected"/> check;
/// the slow path serializes (re)creation. Because the synchronization happens inside one
/// well-defined critical section that runs through a runtime primitive with full-barrier
/// semantics on every architecture, there is no cross-thread memory-ordering reasoning to
/// do at the call sites — a virtue compared to hand-rolled double-checked locking with
/// volatile reads and writes.
/// </para>
/// <para>
/// The holder is intentionally not <see cref="IDisposable"/>: per the
/// <see cref="RedisJournalOptions.ConnectionMultiplexerFactory"/> /
/// <see cref="RedisSnapshotOptions.ConnectionMultiplexerFactory"/> contract, the plugin
/// treats every multiplexer it receives as caller-owned. The holder lives for the lifetime
/// of the <see cref="Akka.Actor.ActorSystem"/>'s setup container and is released by
/// garbage collection along with the <see cref="Microsoft.Extensions.DependencyInjection.IServiceProvider"/>.
/// </para>
/// </remarks>
internal sealed class AzureRedisConnectionHolder
{
    private readonly Func<Task<IConnectionMultiplexer>> _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnectionMultiplexer? _current;

    public AzureRedisConnectionHolder(Func<Task<IConnectionMultiplexer>> factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    /// <summary>
    /// Returns the cached <see cref="IConnectionMultiplexer"/> if one is currently available
    /// and connected; otherwise creates a new one under the holder's gate and caches it.
    /// </summary>
    public async Task<IConnectionMultiplexer> GetAsync()
    {
        var snapshot = _current;
        if (snapshot is { IsConnected: true })
            return snapshot;

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Re-check inside the gate — another waiter may have already finished
            // (re)connecting while we were queued.
            if (_current is { IsConnected: true })
                return _current;

            _current = await _factory().ConfigureAwait(false);
            return _current;
        }
        finally
        {
            _gate.Release();
        }
    }
}
