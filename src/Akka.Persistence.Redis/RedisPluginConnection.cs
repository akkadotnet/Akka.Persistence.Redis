// -----------------------------------------------------------------------
//  <copyright file="RedisPluginConnection.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Akka.Event;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis
{
    /// <summary>
    /// The Redis connection used by one journal or snapshot-store plugin instance, together
    /// with the cluster mode that the plugin's key names depend on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The connection is requested when the plugin is constructed, but the constructor does not
    /// wait for it, and a failed attempt is not cached: the next operation starts a new one.
    /// A transient failure at startup (an unreachable server with <c>abortConnect=true</c>, a
    /// token that cannot be acquired yet, a throwing factory) therefore fails the operations
    /// that run while it lasts instead of failing the plugin actor for good. The journal's and
    /// snapshot store's circuit breakers pace the retries.
    /// </para>
    /// <para>
    /// Whether the server is a Redis Cluster decides the key names (hash-tagged or not) and
    /// the database number, and it can only be read from a connected multiplexer:
    /// <see cref="IServer.ServerType"/> reports <see cref="ServerType.Standalone"/> until the
    /// handshake has completed. A multiplexer created with <c>abortConnect=false</c> (the
    /// StackExchange.Redis default for Azure hosts) is returned before it is connected, so
    /// the cluster mode is resolved by the first operation that runs after the connection is
    /// established, and is then fixed for the life of the plugin. Operations never run
    /// against a guessed cluster mode: until it is known they wait for the connection, and
    /// fail if it is not established within the multiplexer's timeout.
    /// </para>
    /// </remarks>
    internal sealed class RedisPluginConnection : IDisposable
    {
        private const int ConnectPollIntervalMs = 50;

        private readonly Func<Task<IConnectionMultiplexer>> _connect;
        private readonly RedisSettings _settings;
        private readonly ILoggingAdapter _log;
        private readonly object _lock = new();
        private Task<IConnectionMultiplexer> _connection;
        private RedisPluginTopology? _topology;
        private bool _disposed;

        public RedisPluginConnection(Func<Task<IConnectionMultiplexer>> connect, bool ownsConnection, RedisSettings settings, ILoggingAdapter log)
        {
            _connect = connect;
            OwnsConnection = ownsConnection;
            _settings = settings;
            _log = log;

            // Start connecting right away, as before, but don't block the actor constructor on it.
            _connection = StartConnect();
        }

        public bool OwnsConnection { get; }

        /// <summary>
        /// The resolved topology, or <c>null</c> while the connection has not been established yet.
        /// </summary>
        public RedisPluginTopology? Topology => Volatile.Read(ref _topology);

        /// <summary>
        /// Returns the resolved topology. While it is not known yet, this obtains the connection
        /// (starting a new attempt if the previous one failed) and waits for it to be established.
        /// </summary>
        /// <exception cref="RedisConnectionException">
        /// The connection was not established within the multiplexer's timeout.
        /// </exception>
        public Task<RedisPluginTopology> GetTopologyAsync()
        {
            var topology = Volatile.Read(ref _topology);
            return topology is not null ? Task.FromResult(topology) : ResolveAsync();
        }

        private async Task<RedisPluginTopology> ResolveAsync()
        {
            var connection = await GetConnectionAsync().ConfigureAwait(false);

            var timeoutMs = Math.Max(connection.TimeoutMilliseconds, 1000);
            var stopwatch = Stopwatch.StartNew();
            while (!connection.IsConnected)
            {
                if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                {
                    throw new RedisConnectionException(
                        ConnectionFailureType.UnableToConnect,
                        $"The Redis connection was not established within {timeoutMs} ms, so it is not known yet " +
                        "whether the server is a Redis Cluster. The operation was not attempted.");
                }

                await Task.Delay(ConnectPollIntervalMs).ConfigureAwait(false);
            }

            return Resolve(connection);
        }

        private Task<IConnectionMultiplexer> GetConnectionAsync()
        {
            lock (_lock)
            {
                if (_disposed)
                    throw new ObjectDisposedException(nameof(RedisPluginConnection));

                // Never hand out a failed attempt twice: retry on the next operation.
                if (_connection.IsFaulted || _connection.IsCanceled)
                    _connection = StartConnect();

                return _connection;
            }
        }

        private Task<IConnectionMultiplexer> StartConnect()
        {
            Task<IConnectionMultiplexer> attempt;
            try
            {
                attempt = ConnectAsync();
            }
            catch (Exception ex)
            {
                attempt = Task.FromException<IConnectionMultiplexer>(ex);
            }

            attempt.ContinueWith(
                (t, state) => ((ILoggingAdapter)state!).Warning(
                    t.Exception!.GetBaseException(),
                    "Failed to create the Redis connection. The next operation will try again."),
                _log,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return attempt;
        }

        private async Task<IConnectionMultiplexer> ConnectAsync()
        {
            var connection = await _connect().ConfigureAwait(false);
            if (connection is null)
                throw new InvalidOperationException("The Redis connection factory returned null.");

            if (!connection.IsConnected)
            {
                _log.Warning(
                    "Redis connection is not established yet; cluster-mode detection is deferred until it is. " +
                    "Operations wait for the connection and fail if it is not established within {0} ms.",
                    Math.Max(connection.TimeoutMilliseconds, 1000));
            }

            return connection;
        }

        private RedisPluginTopology Resolve(IConnectionMultiplexer connection)
        {
            lock (_lock)
            {
                if (_topology is not null)
                    return _topology;

                var isClustered = connection.IsClustered();
                var topology = new RedisPluginTopology(
                    connection.GetDatabase(ResolveDatabaseNumber(isClustered)),
                    isClustered,
                    RedisTopologyRefresher.Create(connection, _log));
                Volatile.Write(ref _topology, topology);
                _log.Debug("Redis cluster mode resolved: IsClustered={0}, database={1}", isClustered, topology.Database.Database);
                return topology;
            }
        }

        private int ResolveDatabaseNumber(bool isClustered)
        {
            if (isClustered)
            {
                // Redis Cluster pins everything to db 0 — https://redis.io/topics/cluster-spec#implemented-subset
                return 0;
            }

            // DatabaseFromConnectionString is only meaningful when we own the connection
            // string ourselves; an injected multiplexer is opaque so fall back to the
            // explicitly configured Database value.
            if (OwnsConnection && _settings.DatabaseFromConnectionString)
            {
                var conf = ConfigurationOptions.Parse(_settings.ConfigurationString);
                if (conf.DefaultDatabase.HasValue)
                    return conf.DefaultDatabase.Value;
            }

            return _settings.Database;
        }

        /// <summary>
        /// Disposes the connection if the plugin owns it, including one that is still being
        /// created: it is disposed as soon as it is available.
        /// </summary>
        public void Dispose()
        {
            Task<IConnectionMultiplexer> connection;
            lock (_lock)
            {
                if (_disposed)
                    return;
                _disposed = true;
                connection = _connection;
            }

            if (!OwnsConnection)
                return;

            connection.ContinueWith(
                t => t.Result.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    /// <summary>
    /// The cluster mode of a connected Redis plugin, the database it uses, and the topology
    /// refresher bound to its connection.
    /// </summary>
    internal sealed class RedisPluginTopology
    {
        public RedisPluginTopology(IDatabase database, bool isClustered, RedisTopologyRefresher refresher)
        {
            Database = database;
            IsClustered = isClustered;
            Refresher = refresher;
        }

        public IDatabase Database { get; }

        public bool IsClustered { get; }

        public RedisTopologyRefresher Refresher { get; }
    }
}
