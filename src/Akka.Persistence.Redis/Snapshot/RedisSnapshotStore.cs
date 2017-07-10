//-----------------------------------------------------------------------
// <copyright file="RedisSnapshotStore.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Snapshot;
using StackExchange.Redis;

namespace Akka.Persistence.Redis.Snapshot
{
    public class RedisSnapshotStore : SnapshotStore
    {
        private readonly RedisSettings _settings;
        private Lazy<IDatabase> _database;
        private ActorSystem _system;
        public IDatabase Database => _database.Value;

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
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var snapshots = await Database.SortedSetRangeByScoreAsync(
              GetSnapshotKey(persistenceId),
              criteria.MaxSequenceNr,
              -1,
              Exclude.None,
              Order.Descending);

            var found = snapshots
              .Select(c => PersistentFromBytes(c))
              .FirstOrDefault(snapshot => snapshot.Metadata.Timestamp <= criteria.MaxTimeStamp && snapshot.Metadata.SequenceNr <= criteria.MaxSequenceNr);

            return found;
        }

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            return Database.SortedSetAddAsync(
              GetSnapshotKey(metadata.PersistenceId),
              PersistentToBytes(metadata, snapshot),
              metadata.SequenceNr);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            await Database.SortedSetRemoveRangeByScoreAsync(GetSnapshotKey(metadata.PersistenceId), metadata.SequenceNr, metadata.SequenceNr);
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            var snapshots = await Database.SortedSetRangeByScoreAsync(
              GetSnapshotKey(persistenceId),
              criteria.MaxSequenceNr,
              0L,
              Exclude.None,
              Order.Descending);

            var found = snapshots
              .Select(c => PersistentFromBytes(c))
              .Where(snapshot => snapshot.Metadata.Timestamp <= criteria.MaxTimeStamp && snapshot.Metadata.SequenceNr <= criteria.MaxSequenceNr)
              .Select(s => _database.Value.SortedSetRemoveRangeByScoreAsync(GetSnapshotKey(persistenceId), s.Metadata.SequenceNr, s.Metadata.SequenceNr))
              .ToArray();

            await Task.WhenAll(found);
        }

        private byte[] PersistentToBytes(SnapshotMetadata metadata, object snapshot)
        {
            var message = new SelectedSnapshot(metadata, snapshot);
            var serializer = _system.Serialization.FindSerializerForType(typeof(SelectedSnapshot));
            return serializer.ToBinary(message);
        }

        private SelectedSnapshot PersistentFromBytes(byte[] bytes)
        {
            var serializer = _system.Serialization.FindSerializerForType(typeof(SelectedSnapshot));
            return serializer.FromBinary<SelectedSnapshot>(bytes);
        }

        private string GetSnapshotKey(string persistenceId) => $"{_settings.KeyPrefix}snapshot:{persistenceId}";
    }
}
