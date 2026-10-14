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
            var snapshots = await topology.Database.SortedSetRangeByScoreWithScoresAsync(
                GetSnapshotKey(persistenceId, topology.IsClustered),
                criteria.MaxSequenceNr,
                criteria.MinSequenceNr,
                Exclude.None,
                Order.Descending);

            // The score is the sequence number, so candidates can be visited newest first without
            // decoding them. Only the snapshots at the newest sequence number that has a match are
            // decoded (all of them, to pick the newest timestamp): an older snapshot that can no
            // longer be deserialized must not fail the load of a newer one. A snapshot that is
            // needed but cannot be deserialized still fails the load, as before.
            var i = 0;
            while (i < snapshots.Length)
            {
                var sequenceNr = snapshots[i].Score;
                SelectedSnapshot? found = null;
                for (; i < snapshots.Length && snapshots[i].Score == sequenceNr; i++)
                {
                    var candidate = PersistentFromBytes(snapshots[i].Element!);
                    if (criteria.Matches(candidate.Metadata) &&
                        (found is null || candidate.Metadata.Timestamp > found.Metadata.Timestamp))
                    {
                        found = candidate;
                    }
                }

                if (found is not null)
                    return found;
            }

            return null!;
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

            // Only the snapshots at this sequence number can match. Decode all of them before
            // deleting anything, so a snapshot that cannot be deserialized fails the request
            // without a partial delete.
            var snapshots = await database.SortedSetRangeByScoreAsync(
                key: key,
                start: metadata.SequenceNr,
                stop: metadata.SequenceNr);

            var found = snapshots
                .Select(c => PersistentFromBytes(c!))
                .Where(snapshot => snapshot.Metadata.Timestamp <= metadata.Timestamp &&
                                   snapshot.Metadata.SequenceNr == metadata.SequenceNr)
                .ToArray();

            if (found.Length == 0)
                return;

            try
            {
                await database.SortedSetRemoveRangeByScoreAsync(
                    key: key,
                    start: metadata.SequenceNr,
                    stop: metadata.SequenceNr,
                    flags: CommandFlags.DemandMaster);
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

            try
            {
                if (criteria.MaxTimeStamp == DateTime.MaxValue)
                {
                    // Bounded by sequence number only: the score is the sequence number, so
                    // delete by score without deserializing anything. This also removes
                    // snapshots that can no longer be deserialized.
                    await database.SortedSetRemoveRangeByScoreAsync(
                        key,
                        0L,
                        criteria.MaxSequenceNr,
                        flags: CommandFlags.DemandMaster);
                    return;
                }

                // The timestamp bound needs the deserialized metadata. Decode every candidate
                // before deleting any, so a snapshot that cannot be deserialized fails the
                // request without a partial delete.
                var snapshots = await database.SortedSetRangeByScoreAsync(
                    key,
                    criteria.MaxSequenceNr,
                    0L,
                    Exclude.None,
                    Order.Descending);

                var found = snapshots
                    .Select(c => PersistentFromBytes(c!))
                    .Where(snapshot => snapshot.Metadata.Timestamp <= criteria.MaxTimeStamp &&
                                       snapshot.Metadata.SequenceNr <= criteria.MaxSequenceNr)
                    .ToArray();

                await Task.WhenAll(found
                    .Select(s => s.Metadata.SequenceNr)
                    .Distinct()
                    .Select(sequenceNr => database.SortedSetRemoveRangeByScoreAsync(
                        key,
                        sequenceNr,
                        sequenceNr,
                        flags: CommandFlags.DemandMaster)));
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
