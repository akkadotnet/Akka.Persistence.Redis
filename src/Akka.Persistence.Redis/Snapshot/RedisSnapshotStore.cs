// -----------------------------------------------------------------------
// <copyright file="RedisSnapshotStore.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Snapshot;
using StackExchange.Redis;

namespace Akka.Persistence.Redis.Snapshot
{
    public class RedisSnapshotStore : SnapshotStore
    {
        protected static readonly RedisPersistence Extension = RedisPersistence.Get(Context.System);

        private readonly RedisSettings _settings;
        private readonly Lazy<RedisConnection> _connection;
        private readonly ActorSystem _system;

        public IDatabase Database => _connection.Value.Database;

        public bool IsClustered { get; private set; }

        // Test seam: exposes the underlying multiplexer once the connection has been initialized.
        // Returns null before the first call to Database to keep PostStop side-effect-free during early termination.
        internal IConnectionMultiplexer ConnectionMultiplexer => _connection.IsValueCreated ? _connection.Value.Multiplexer : null;

        public RedisSnapshotStore(Config snapshotConfig)
        {
            _settings = RedisSettings.Create(snapshotConfig.WithFallback(Extension.DefaultSnapshotConfig));

            _system = Context.System;
            _connection = new Lazy<RedisConnection>(() =>
            {
                IConnectionMultiplexer redisConnection;
                bool ownsConnection;
                var supplied = TryResolveCallerSuppliedMultiplexer();
                if (supplied is not null)
                {
                    // Caller supplied a pre-configured multiplexer via ActorSystemSetup.
                    // Reuse it and leave its lifetime to the caller; never dispose from PostStop.
                    redisConnection = supplied;
                    ownsConnection = false;
                }
                else
                {
                    redisConnection = StackExchange.Redis.ConnectionMultiplexer.Connect(_settings.ConfigurationString);
                    ownsConnection = true;
                }

                IsClustered = redisConnection.IsClustered();

                IDatabase database;
                if (ownsConnection && _settings.DatabaseFromConnectionString && !IsClustered)
                {
                    // DatabaseFromConnectionString is only meaningful when we own the connection
                    // string ourselves; an injected multiplexer is opaque to us so we fall back
                    // to the explicitly configured _settings.Database.
                    var conf = ConfigurationOptions.Parse(_settings.ConfigurationString);
                    if (conf.DefaultDatabase.HasValue)
                        database = redisConnection.GetDatabase(conf.DefaultDatabase.Value);
                    else
                        database = redisConnection.GetDatabase(_settings.Database);
                }
                else if (IsClustered)
                {
                    // for Redis Cluster, the database is 0 https://redis.io/topics/cluster-spec#implemented-subset
                    database = redisConnection.GetDatabase(0);
                }
                else
                {
                    database = redisConnection.GetDatabase(_settings.Database);
                }

                return new RedisConnection(redisConnection, database, ownsConnection);
            });
        }

        // Looks up a caller-supplied multiplexer via ActorSystemSetup. Single-instance
        // RedisConnectionMultiplexerSetup applies to every plugin and takes precedence;
        // MultiRedisConnectionMultiplexerSetup is consulted as a fallback, keyed by this
        // plugin's full HOCON path (which equals Self.Path.Name for system plugin actors).
        // Returns null when neither setup applies, in which case the plugin opens its own
        // multiplexer from the HOCON connection-string.
        private IConnectionMultiplexer? TryResolveCallerSuppliedMultiplexer()
        {
            var setup = _system.Settings.Setup;

            var single = setup.Get<RedisConnectionMultiplexerSetup>();
            if (single.HasValue)
                return single.Value.ConnectionFactory().GetAwaiter().GetResult();

            var multi = setup.Get<MultiRedisConnectionMultiplexerSetup>();
            if (multi.HasValue && multi.Value.TryGetFactory(Self.Path.Name, out var factory) && factory is not null)
                return factory().GetAwaiter().GetResult();

            return null;
        }

        protected override void PostStop()
        {
            if (_connection.IsValueCreated)
            {
                var connection = _connection.Value;
                if (connection.OwnsConnection)
                    connection.Multiplexer.Dispose();
            }

            base.PostStop();
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId,
            SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            var snapshots = await Database.SortedSetRangeByScoreAsync(
                GetSnapshotKey(persistenceId, IsClustered),
                criteria.MaxSequenceNr,
                -1,
                Exclude.None,
                Order.Descending);

            var found = snapshots
                .Select(c => PersistentFromBytes(c))
                .Where(c => criteria.Matches(c.Metadata))
                .OrderByDescending(x => x.Metadata.SequenceNr)
                .ThenByDescending(x => x.Metadata.Timestamp)
                .FirstOrDefault();

            return found;
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            return Database.SortedSetAddAsync(
                GetSnapshotKey(metadata.PersistenceId, IsClustered),
                PersistentToBytes(metadata, snapshot),
                metadata.SequenceNr,
                flags: CommandFlags.DemandMaster);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            if(metadata.Timestamp == DateTime.MinValue)
            {
                await Database.SortedSetRemoveRangeByScoreAsync(
                    GetSnapshotKey(metadata.PersistenceId, IsClustered),
                    metadata.SequenceNr,
                    metadata.SequenceNr,
                    flags: CommandFlags.DemandMaster);
                return;
            }

            var snapshots = await Database.SortedSetRangeByScoreAsync(
                key: GetSnapshotKey(metadata.PersistenceId, IsClustered),
                start: metadata.SequenceNr,
                stop: 0L,
                exclude: Exclude.None,
                order: Order.Descending);

            var found = snapshots
                .Select(c => PersistentFromBytes(c))
                .Where(snapshot => snapshot.Metadata.Timestamp <= metadata.Timestamp &&
                                   snapshot.Metadata.SequenceNr == metadata.SequenceNr)
                .Select(s => Database.SortedSetRemoveRangeByScoreAsync(
                    key: GetSnapshotKey(metadata.PersistenceId, IsClustered),
                    start: s.Metadata.SequenceNr,
                    stop: s.Metadata.SequenceNr,
                    flags: CommandFlags.DemandMaster))
                .ToArray();

            await Task.WhenAll(found);
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            var snapshots = await Database.SortedSetRangeByScoreAsync(
                GetSnapshotKey(persistenceId, IsClustered),
                criteria.MaxSequenceNr,
                0L,
                Exclude.None,
                Order.Descending);

            var found = snapshots
                .Select(c => PersistentFromBytes(c))
                .Where(snapshot => snapshot.Metadata.Timestamp <= criteria.MaxTimeStamp &&
                                   snapshot.Metadata.SequenceNr <= criteria.MaxSequenceNr)
                .Select(s => Database.SortedSetRemoveRangeByScoreAsync(
                    GetSnapshotKey(persistenceId, IsClustered),
                    s.Metadata.SequenceNr,
                    s.Metadata.SequenceNr,
                    flags: CommandFlags.DemandMaster))
                .ToArray();

            await Task.WhenAll(found);
        }

        private byte[] PersistentToBytes(SnapshotMetadata metadata, object snapshot)
        {
            var message = new SelectedSnapshot(metadata, snapshot);
            var serializer = _system.Serialization.FindSerializerForType(typeof(SelectedSnapshot));
            return Akka.Serialization.Serialization.WithTransport(_system as ExtendedActorSystem,
                () => serializer.ToBinary(message));
            //return serializer.ToBinary(message);
        }

        private SelectedSnapshot PersistentFromBytes(byte[] bytes)
        {
            var serializer = _system.Serialization.FindSerializerForType(typeof(SelectedSnapshot));
            return serializer.FromBinary<SelectedSnapshot>(bytes);
        }

        public string GetSnapshotKey(string persistenceId, bool withHashTag)
        {
            return withHashTag
                ? $"{{__{persistenceId}}}.{_settings.KeyPrefix}snapshot:{persistenceId}"
                : $"{_settings.KeyPrefix}snapshot:{persistenceId}";
        }

        private sealed class RedisConnection
        {
            public RedisConnection(IConnectionMultiplexer multiplexer, IDatabase database, bool ownsConnection)
            {
                Multiplexer = multiplexer;
                Database = database;
                OwnsConnection = ownsConnection;
            }

            public IConnectionMultiplexer Multiplexer { get; }
            public IDatabase Database { get; }
            public bool OwnsConnection { get; }
        }
    }

    internal static class SnapshotMetadataExtensions
    {
        public static bool Matches(this SnapshotSelectionCriteria criteria, SnapshotMetadata metadata)
        {
            return metadata.SequenceNr <= criteria.MaxSequenceNr && metadata.Timestamp <= criteria.MaxTimeStamp
                                                                 && metadata.SequenceNr >= criteria.MinSequenceNr &&
                                                                 metadata.Timestamp >= criteria.MinTimestamp;
        }
    }
}
