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
using Akka.Event;
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
        private readonly RedisPluginConnection _connection;
        private readonly ILoggingAdapter _log;

        /// <summary>
        /// The Redis database used by this snapshot store. Throws <see cref="InvalidOperationException"/>
        /// while the connection has not been established yet, because the database depends on
        /// whether the server is a Redis Cluster.
        /// </summary>
        public IDatabase Database => ResolvedTopology.Database;

        /// <summary>
        /// Whether the server is a Redis Cluster, which decides the key names this snapshot store
        /// uses. Throws <see cref="InvalidOperationException"/> while the connection has not been
        /// established yet.
        /// </summary>
        public bool IsClustered => ResolvedTopology.IsClustered;

        private RedisPluginTopology ResolvedTopology => _connection.Topology
            ?? throw new InvalidOperationException("The Redis connection has not been established yet.");

        public RedisSnapshotStore(Config snapshotConfig)
        {
            _settings = RedisSettings.Create(snapshotConfig.WithFallback(Extension.DefaultSnapshotConfig));
            _system = Context.System;

            _log = Context.GetLogger();
            _connection = RedisConnectionResolver.Resolve(_system, _settings, Self.Path.Name, _log);
        }

        protected override void PostStop()
        {
            _connection.Dispose();
            base.PostStop();
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId,
            SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            var topology = await _connection.GetTopologyAsync();
            var snapshots = await topology.Database.SortedSetRangeByScoreAsync(
                GetSnapshotKey(persistenceId, topology.IsClustered),
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

        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            var topology = await _connection.GetTopologyAsync();
            try
            {
                await topology.Database.SortedSetAddAsync(
                    GetSnapshotKey(metadata.PersistenceId, topology.IsClustered),
                    PersistentToBytes(metadata, snapshot),
                    metadata.SequenceNr,
                    flags: CommandFlags.DemandMaster);
            }
            catch (RedisCommandException ex) when (topology.Refresher.IsReplicaRefusal(ex))
            {
                topology.Refresher.TriggerBackgroundRefresh(metadata.PersistenceId, nameof(SaveAsync), ex);
                throw;
            }
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            var topology = await _connection.GetTopologyAsync();
            var database = topology.Database;
            var key = GetSnapshotKey(metadata.PersistenceId, topology.IsClustered);

            if(metadata.Timestamp == DateTime.MinValue)
            {
                try
                {
                    await database.SortedSetRemoveRangeByScoreAsync(
                        key,
                        metadata.SequenceNr,
                        metadata.SequenceNr,
                        flags: CommandFlags.DemandMaster);
                }
                catch (RedisCommandException ex) when (topology.Refresher.IsReplicaRefusal(ex))
                {
                    topology.Refresher.TriggerBackgroundRefresh(metadata.PersistenceId, nameof(DeleteAsync), ex);
                    throw;
                }
                return;
            }

            var snapshots = await database.SortedSetRangeByScoreAsync(
                key: key,
                start: metadata.SequenceNr,
                stop: 0L,
                exclude: Exclude.None,
                order: Order.Descending);

            var found = snapshots
                .Select(c => PersistentFromBytes(c))
                .Where(snapshot => snapshot.Metadata.Timestamp <= metadata.Timestamp &&
                                   snapshot.Metadata.SequenceNr == metadata.SequenceNr)
                .Select(s => database.SortedSetRemoveRangeByScoreAsync(
                    key: key,
                    start: s.Metadata.SequenceNr,
                    stop: s.Metadata.SequenceNr,
                    flags: CommandFlags.DemandMaster))
                .ToArray();

            try
            {
                await Task.WhenAll(found);
            }
            catch (RedisCommandException ex) when (topology.Refresher.IsReplicaRefusal(ex))
            {
                topology.Refresher.TriggerBackgroundRefresh(metadata.PersistenceId, nameof(DeleteAsync), ex);
                throw;
            }
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            var topology = await _connection.GetTopologyAsync();
            var database = topology.Database;
            var key = GetSnapshotKey(persistenceId, topology.IsClustered);

            var snapshots = await database.SortedSetRangeByScoreAsync(
                key,
                criteria.MaxSequenceNr,
                0L,
                Exclude.None,
                Order.Descending);

            var found = snapshots
                .Select(c => PersistentFromBytes(c))
                .Where(snapshot => snapshot.Metadata.Timestamp <= criteria.MaxTimeStamp &&
                                   snapshot.Metadata.SequenceNr <= criteria.MaxSequenceNr)
                .Select(s => database.SortedSetRemoveRangeByScoreAsync(
                    key,
                    s.Metadata.SequenceNr,
                    s.Metadata.SequenceNr,
                    flags: CommandFlags.DemandMaster))
                .ToArray();

            try
            {
                await Task.WhenAll(found);
            }
            catch (RedisCommandException ex) when (topology.Refresher.IsReplicaRefusal(ex))
            {
                topology.Refresher.TriggerBackgroundRefresh(persistenceId, nameof(DeleteAsync), ex);
                throw;
            }
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
