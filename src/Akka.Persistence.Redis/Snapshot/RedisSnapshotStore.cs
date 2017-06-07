//-----------------------------------------------------------------------
// <copyright file="RedisSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Snapshot;
using Akka.Serialization;
using Akka.Util.Internal;
using StackExchange.Redis;

namespace Akka.Persistence.Redis.Snapshot
{
    public class RedisSnapshotStore : SnapshotStore
    {
        private readonly RedisSettings _settings;
        private Lazy<Serializer> _serializer;
        private Lazy<IDatabase> _database;
        private ActorSystem _system;

        public RedisSnapshotStore()
        {
            _settings = RedisPersistence.Get(Context.System).SnapshotStoreSettings;
        }

        protected override void PreStart()
        {
            base.PreStart();

            _system = Context.System;
            _database = new Lazy<IDatabase>(() =>
            {
                var redisConnection = ConnectionMultiplexer.Connect(_settings.ConfigurationString);
                return redisConnection.GetDatabase(_settings.Database);
            });
            _serializer = new Lazy<Serializer>(() => _system.Serialization.FindSerializerForType(typeof(SnapshotEntry)));
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var snapshots = await _database.Value.SortedSetRangeByScoreAsync(
              SnapshotKey(persistenceId),
              criteria.MaxSequenceNr,
              -1,
              Exclude.None,
              Order.Descending);

            var found = snapshots
              .Select(c => ToSelectedSnapshot(_serializer.Value.FromBinary<SnapshotEntry>(c)))
              .FirstOrDefault(snapshot => snapshot.Metadata.Timestamp <= criteria.MaxTimeStamp && snapshot.Metadata.SequenceNr <= criteria.MaxSequenceNr);

            return found;
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            return _database.Value.SortedSetAddAsync(
              SnapshotKey(metadata.PersistenceId),
              _serializer.Value.ToBinary(ToSnapshotEntry(metadata, snapshot)),
              metadata.SequenceNr);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            await _database.Value.SortedSetRemoveRangeByScoreAsync(SnapshotKey(metadata.PersistenceId), metadata.SequenceNr, metadata.SequenceNr);
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var snapshots = await _database.Value.SortedSetRangeByScoreAsync(
              SnapshotKey(persistenceId),
              criteria.MaxSequenceNr,
              0L,
              Exclude.None,
              Order.Descending);

            var found = snapshots
              .Select(c => ToSelectedSnapshot(_serializer.Value.FromBinary<SnapshotEntry>(c)))
              .Where(snapshot => snapshot.Metadata.Timestamp <= criteria.MaxTimeStamp && snapshot.Metadata.SequenceNr <= criteria.MaxSequenceNr)
              .Select(s => _database.Value.SortedSetRemoveRangeByScoreAsync(SnapshotKey(persistenceId), s.Metadata.SequenceNr, s.Metadata.SequenceNr))
              .ToArray();

            await Task.WhenAll(found);
        }

        private string SnapshotKey(string persistenceId) => $"{_settings.KeyPrefix}:{persistenceId}";

        private static SnapshotEntry ToSnapshotEntry(SnapshotMetadata metadata, object snapshot)
        {
            return new SnapshotEntry
            {
                PersistenceId = metadata.PersistenceId,
                SequenceNr = metadata.SequenceNr,
                Snapshot = snapshot,
                Timestamp = metadata.Timestamp.Ticks
            };
        }

        private static SelectedSnapshot ToSelectedSnapshot(SnapshotEntry entry)
        {
            return new SelectedSnapshot(new SnapshotMetadata(entry.PersistenceId, entry.SequenceNr, new DateTime(entry.Timestamp)), entry.Snapshot);
        }
    }
}
