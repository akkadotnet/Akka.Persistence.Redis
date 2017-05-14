//-----------------------------------------------------------------------
// <copyright file="RedisJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.Serialization;
using Akka.Util.Internal;
using StackExchange.Redis;
using static Akka.Persistence.Redis.Journal.RedisUtils;

namespace Akka.Persistence.Redis.Journal
{
    public class RedisJournal : AsyncWriteJournal
    {
        private readonly RedisSettings _settings;
        private Lazy<IDatabase> _database;
        private ActorSystem _system;

        public IDatabase Database => _database.Value;

        public RedisJournal()
        {
            _settings = RedisPersistence.Get(Context.System).JournalSettings;
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

        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            var highestSequenceNr = await Database.StringGetAsync(GetHighestSequenceNrKey(persistenceId));
            return highestSequenceNr.IsNull ? 0L : (long)highestSequenceNr;
        }

        public override async Task ReplayMessagesAsync(
            IActorContext context,
            string persistenceId,
            long fromSequenceNr,
            long toSequenceNr,
            long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            RedisValue[] journals = await Database.SortedSetRangeByScoreAsync(GetJournalKey(persistenceId), fromSequenceNr, toSequenceNr, skip: 0L, take: max);

            foreach (var journal in journals)
            {
                recoveryCallback(PersistentFromBytes(journal, _system.Serialization));
            }
        }

        protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            await Database.SortedSetRemoveRangeByScoreAsync(GetJournalKey(persistenceId), -1, toSequenceNr);
        }

        protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var writeTasks = messages.Select(WriteBatchAsync).ToArray();

            foreach (var writeTask in writeTasks)
            {
                await writeTask;
            }

            return writeTasks
                .Select(t => t.IsFaulted ? TryUnwrapException(t.Exception) : null)
                .ToImmutableList();
        }

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        private async Task WriteBatchAsync(AtomicWrite aw)
        {
            var transaction = Database.CreateTransaction();

            var payloads = aw.Payload.AsInstanceOf<IImmutableList<IPersistentRepresentation>>();
            foreach (var payload in payloads)
            {
                transaction.SortedSetAddAsync(
                    GetJournalKey(payload.PersistenceId),
                    PersistentToBytes(payload, _system.Serialization),
                    payload.SequenceNr);

                // notify about new event being appended for this persistence id
                transaction.PublishAsync(GetJournalChannel(payload.PersistenceId), payload.SequenceNr);

                transaction.StringSetAsync(GetHighestSequenceNrKey(payload.PersistenceId), aw.HighestSequenceNr);

                transaction.SetAddAsync(GetIdentifiersKey(), aw.PersistenceId);
            }

            if (!await transaction.ExecuteAsync())
            {
                throw new Exception($"{nameof(WriteMessagesAsync)}: failed to write {typeof(JournalEntry).Name} to redis");
            }
        }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }
}