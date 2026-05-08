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

#nullable enable
namespace Akka.Persistence.Redis.Snapshot
{
    public class RedisSnapshotStore : SnapshotStore
    {
        protected static readonly RedisPersistence Extension = RedisPersistence.Get(Context.System);

        private readonly RedisSettings _settings;
        private readonly ActorSystem _system;
        private readonly IConnectionMultiplexer _connection;
        private readonly bool _ownsConnection;

        public IDatabase Database { get; }
        public bool IsClustered { get; }

        // Test seam.
        internal IConnectionMultiplexer ConnectionMultiplexer => _connection;

        public RedisSnapshotStore(Config snapshotConfig)
        {
            _settings = RedisSettings.Create(snapshotConfig.WithFallback(Extension.DefaultSnapshotConfig));
            _system = Context.System;

            var setup = _system.Settings.Setup.Get<RedisConnectionMultiplexerSetup>();
            if (setup.HasValue)
            {
                _connection = setup.Value.Factory().GetAwaiter().GetResult();
                _ownsConnection = setup.Value.OwnedByPlugin;
            }
            else
            {
                _connection = StackExchange.Redis.ConnectionMultiplexer.Connect(_settings.ConfigurationString);
                _ownsConnection = true;
            }

            IsClustered = _connection.IsClustered();
            Database = _connection.GetDatabase(ResolveDatabaseNumber());
        }

        private int ResolveDatabaseNumber()
        {
            if (IsClustered)
            {
                // Redis Cluster pins everything to db 0 — https://redis.io/topics/cluster-spec#implemented-subset
                return 0;
            }

            // DatabaseFromConnectionString is only meaningful when we own the connection
            // string ourselves; an injected multiplexer is opaque so fall back to the
            // explicitly configured _settings.Database.
            if (_ownsConnection && _settings.DatabaseFromConnectionString)
            {
                var conf = ConfigurationOptions.Parse(_settings.ConfigurationString);
                if (conf.DefaultDatabase.HasValue)
                    return conf.DefaultDatabase.Value;
            }

            return _settings.Database;
        }

        protected override void PostStop()
        {
            if (_ownsConnection)
                _connection.Dispose();
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
