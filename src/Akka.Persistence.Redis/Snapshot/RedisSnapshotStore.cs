// -----------------------------------------------------------------------
// <copyright file="RedisSnapshotStore.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
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
        private readonly Lazy<IDatabase> _database;
        private readonly ActorSystem _system;
        public IDatabase Database => _database.Value;

        public bool IsClustered { get; private set; }

        public RedisSnapshotStore(Config snapshotConfig)
        {
            _settings = RedisSettings.Create(snapshotConfig.WithFallback(Extension.DefaultSnapshotConfig));

            _system = Context.System;
            _database = new Lazy<IDatabase>(() =>
            {
                var redisConnection = ConnectionMultiplexer.Connect(_settings.ConfigurationString);
                IsClustered = redisConnection.IsClustered();
                return redisConnection.GetDatabase(_settings.Database);
            });
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId,
            SnapshotSelectionCriteria criteria)
        {
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

        protected override Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            return Database.SortedSetAddAsync(
                GetSnapshotKey(metadata.PersistenceId, IsClustered),
                PersistentToBytes(metadata, snapshot),
                metadata.SequenceNr);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            await Database.SortedSetRemoveRangeByScoreAsync(GetSnapshotKey(metadata.PersistenceId, IsClustered), metadata.SequenceNr,
                metadata.SequenceNr);
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
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
                .Select(s => _database.Value.SortedSetRemoveRangeByScoreAsync(GetSnapshotKey(persistenceId, IsClustered),
                    s.Metadata.SequenceNr, s.Metadata.SequenceNr))
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