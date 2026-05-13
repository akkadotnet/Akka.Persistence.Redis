// -----------------------------------------------------------------------
// <copyright file="RedisTopologyRefresher.cs" company="Petabridge, LLC">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis
{
    /// <summary>
    /// Detects Redis Cluster topology-drift errors that StackExchange.Redis does not
    /// automatically recover from, and triggers a background topology refresh against
    /// the multiplexer when one occurs.
    ///
    /// SE.Redis 2.6.86+ already auto-refreshes on MOVED redirects (with a 5-second
    /// internal debounce). The one cluster-failover failure mode it does NOT handle is
    /// a node refusing a write with the literal server-side message
    /// "Command cannot be issued to a replica" — surfaced as a plain
    /// <see cref="RedisCommandException"/> with no typed subtype. That happens when a
    /// failover has demoted a node to replica but the multiplexer's slot-map cache is
    /// still routing writes there. This refresher detects that exact case, fires
    /// <see cref="IConnectionMultiplexer.ConfigureAsync(System.IO.TextWriter?)"/> in
    /// the background, and lets the journal's circuit breaker handle retry cadence as
    /// normal.
    /// </summary>
    internal sealed class RedisTopologyRefresher
    {
        // Literal message produced by SE.Redis when a write reaches a node that is
        // currently serving as a replica. Stable since SE.Redis 2.6.86. Re-verify on
        // any SE.Redis package bump.
        private const string ReplicaRefusalMarker = "Command cannot be issued to a replica";

        private readonly Func<Task> _refresh;
        private readonly ILoggingAdapter _log;

        // 0 = idle, 1 = refresh already in flight. Suppresses log spam and redundant
        // refresh dispatches when many concurrent writes all hit the same demoted node.
        private int _refreshInFlight;

        // Test seam: lets specs inject a Func<Task> in place of the real ConfigureAsync.
        internal RedisTopologyRefresher(Func<Task> refresh, ILoggingAdapter log)
        {
            _refresh = refresh;
            _log = log;
        }

        public static RedisTopologyRefresher Create(IConnectionMultiplexer connection, ILoggingAdapter log)
            => new(() => connection.ConfigureAsync(null), log);

        /// <summary>
        /// True when the given exception is the "Command cannot be issued to a replica"
        /// runtime refusal that this refresher reacts to. MOVED, ASK, CROSSSLOT and
        /// every other <see cref="RedisCommandException"/> shape returns false — they
        /// either auto-heal in SE.Redis or are user-mode programming errors.
        /// </summary>
        public static bool IsReplicaRefusal(RedisCommandException ex)
            => ex.Message.IndexOf(ReplicaRefusalMarker, StringComparison.Ordinal) >= 0;

        /// <summary>
        /// Fire-and-forget topology refresh against the multiplexer. Always returns
        /// synchronously; the caller should rethrow the original exception so the
        /// journal's circuit breaker handles retry timing normally. The next write
        /// attempt after the breaker's reset-timeout will see fresh topology.
        /// </summary>
        public void TriggerBackgroundRefresh(string persistenceId, string operation, Exception source)
        {
            if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0)
                return; // a refresh is already running; suppress duplicate

            _log.Warning(
                source,
                "Redis write for persistenceId '{0}' rejected by a node currently serving as a replica during operation '{1}'. Triggering background topology refresh. The next attempt after the circuit breaker's reset-timeout should succeed.",
                persistenceId,
                operation);

            _ = RunRefreshAsync();
        }

        private async Task RunRefreshAsync()
        {
            try
            {
                await _refresh().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warning(
                    ex,
                    "Background Redis topology refresh failed. StackExchange.Redis will retry on its next periodic configCheckSeconds tick.");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshInFlight, 0);
            }
        }
    }
}
