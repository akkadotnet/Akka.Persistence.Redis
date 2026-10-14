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
    /// Whether the server is a Redis Cluster decides the key names (hash-tagged or not) and
    /// the database number, and it can only be read from a connected multiplexer:
    /// <see cref="IServer.ServerType"/> reports <see cref="ServerType.Standalone"/> until the
    /// handshake has completed. A multiplexer created with <c>abortConnect=false</c> (the
    /// StackExchange.Redis default for Azure hosts) is returned before it is connected, so
    /// the cluster mode is resolved by the first operation that runs after the connection is
    /// established, and is then fixed for the life of the plugin. Operations never run
    /// against a guessed cluster mode: until it is known they wait for the connection, and
    /// fail if it is not established within the multiplexer's timeout.
    /// </remarks>
    internal sealed class RedisPluginConnection
    {
        private const int ConnectPollIntervalMs = 50;

        private readonly RedisSettings _settings;
        private readonly ILoggingAdapter _log;
        private readonly object _lock = new();
        private RedisPluginTopology? _topology;

        public RedisPluginConnection(IConnectionMultiplexer connection, bool ownsConnection, RedisSettings settings, ILoggingAdapter log)
        {
            Connection = connection;
            OwnsConnection = ownsConnection;
            _settings = settings;
            _log = log;

            if (connection.IsConnected)
            {
                Resolve();
            }
            else
            {
                _log.Warning(
                    "Redis connection is not established yet; cluster-mode detection is deferred until it is. " +
                    "Operations wait for the connection and fail if it is not established within {0} ms.",
                    WaitTimeoutMs);
            }
        }

        public IConnectionMultiplexer Connection { get; }

        public bool OwnsConnection { get; }

        /// <summary>
        /// The resolved topology, or <c>null</c> while the connection has not been established yet.
        /// </summary>
        public RedisPluginTopology? Topology => Volatile.Read(ref _topology);

        private int WaitTimeoutMs => Math.Max(Connection.TimeoutMilliseconds, 1000);

        /// <summary>
        /// Returns the resolved topology, waiting for the connection to be established when
        /// the cluster mode is not known yet.
        /// </summary>
        /// <exception cref="RedisConnectionException">
        /// The connection was not established within the multiplexer's timeout.
        /// </exception>
        public Task<RedisPluginTopology> GetTopologyAsync()
        {
            var topology = Volatile.Read(ref _topology);
            return topology is not null ? Task.FromResult(topology) : WaitForTopologyAsync();
        }

        private async Task<RedisPluginTopology> WaitForTopologyAsync()
        {
            var timeoutMs = WaitTimeoutMs;
            var stopwatch = Stopwatch.StartNew();
            while (!Connection.IsConnected)
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

            return Resolve();
        }

        private RedisPluginTopology Resolve()
        {
            lock (_lock)
            {
                if (_topology is not null)
                    return _topology;

                var isClustered = Connection.IsClustered();
                var topology = new RedisPluginTopology(Connection.GetDatabase(ResolveDatabaseNumber(isClustered)), isClustered);
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
    }

    /// <summary>
    /// The cluster mode of a connected Redis plugin and the database it uses.
    /// </summary>
    internal sealed class RedisPluginTopology
    {
        public RedisPluginTopology(IDatabase database, bool isClustered)
        {
            Database = database;
            IsClustered = isClustered;
        }

        public IDatabase Database { get; }

        public bool IsClustered { get; }
    }
}
