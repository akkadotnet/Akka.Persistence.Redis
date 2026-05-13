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
    /// a write that lands on a node SE.Redis has just observed serve a
    /// <c>-READONLY</c> response (i.e. been demoted to replica). SE.Redis flips that
    /// node's <see cref="IServer.IsReplica"/> flag and then refuses the next write to
    /// it with a <see cref="RedisCommandException"/>, but does not refresh the cluster
    /// slot map, so subsequent writes keep routing to the same demoted node until the
    /// next periodic <c>configCheckSeconds</c> tick.
    /// </summary>
    internal sealed class RedisTopologyRefresher
    {
        // Fallback message marker for the rare case where IncludeDetailInExceptions
        // is set to false on the multiplexer's ConfigurationOptions — then SE.Redis
        // omits the "redis-server" data key, and the only signal left is the message
        // text. Stable since SE.Redis 2.6.86. Re-verify on any SE.Redis package bump.
        private const string ReplicaRefusalMarker = "Command cannot be issued to a replica";

        // SE.Redis populates Exception.Data with this key for every server-attributed
        // exception when IncludeDetailInExceptions is true (the default). See
        // StackExchange.Redis.ExceptionFactory.AddExceptionDetail.
        private const string RedisServerDataKey = "redis-server";

        private readonly Func<Task> _refresh;
        private readonly IConnectionMultiplexer? _connection;
        private readonly ILoggingAdapter _log;

        // 0 = idle, 1 = refresh already in flight. Suppresses log spam and redundant
        // refresh dispatches when many concurrent writes all hit the same demoted node.
        private int _refreshInFlight;

        // Test seam: lets specs inject a Func<Task> in place of the real ConfigureAsync
        // and an optional multiplexer for IsReplicaRefusal's typed check.
        internal RedisTopologyRefresher(Func<Task> refresh, ILoggingAdapter log, IConnectionMultiplexer? connection = null)
        {
            _refresh = refresh;
            _log = log;
            _connection = connection;
        }

        public static RedisTopologyRefresher Create(IConnectionMultiplexer connection, ILoggingAdapter log)
            => new(() => connection.ConfigureAsync(null), log, connection);

        /// <summary>
        /// True when this exception was produced because a write was routed to a node
        /// that is currently flagged as a replica. Primary signal is the typed check:
        /// <see cref="Exception.Data"/>[<c>"redis-server"</c>] identifies the endpoint
        /// that refused, and <see cref="IServer.IsReplica"/> tells us its actual state.
        /// Falls back to the literal SE.Redis message text only when
        /// <c>ConfigurationOptions.IncludeDetailInExceptions</c> is false (the data
        /// dictionary is then empty).
        /// </summary>
        public bool IsReplicaRefusal(RedisCommandException ex)
        {
            if (_connection is not null
                && ex.Data[RedisServerDataKey] is string endpointString
                && EndPointCollection.TryParse(endpointString) is { } endpoint)
            {
                try
                {
                    var server = _connection.GetServer(endpoint);
                    if (server.IsReplica)
                        return true;
                }
                catch
                {
                    // Endpoint not recognized by the multiplexer (e.g. stale entry).
                    // Fall through to the message-text fallback.
                }
            }

            return ex.Message.IndexOf(ReplicaRefusalMarker, StringComparison.Ordinal) >= 0;
        }

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
