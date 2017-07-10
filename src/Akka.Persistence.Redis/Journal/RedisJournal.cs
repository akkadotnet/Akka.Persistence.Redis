//-----------------------------------------------------------------------
// <copyright file="RedisJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.Util.Internal;
using StackExchange.Redis;

namespace Akka.Persistence.Redis.Journal
{
    public class RedisJournal : AsyncWriteJournal
    {
        private readonly RedisSettings _settings;
        private readonly JournalHelper _journalHelper;
        private Lazy<IDatabase> _database;
        private ActorSystem _system;

        public IDatabase Database => _database.Value;

        public RedisJournal()
        {
            _settings = RedisPersistence.Get(Context.System).JournalSettings;
            _journalHelper = new JournalHelper(Context.System, _settings.KeyPrefix);
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
            var highestSequenceNr = await Database.StringGetAsync(_journalHelper.GetHighestSequenceNrKey(persistenceId));
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
            RedisValue[] journals = await Database.SortedSetRangeByScoreAsync(_journalHelper.GetJournalKey(persistenceId), fromSequenceNr, toSequenceNr, skip: 0L, take: max);

            foreach (var journal in journals)
            {
                recoveryCallback(_journalHelper.PersistentFromBytes(journal));
            }
        }

        protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            await Database.SortedSetRemoveRangeByScoreAsync(_journalHelper.GetJournalKey(persistenceId), -1, toSequenceNr);
        }

        protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var writeTasks = messages.Select(WriteBatchAsync).ToList();

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
                var (bytes, tags) = Extract(payload);

                // save the payload
                transaction.SortedSetAddAsync(_journalHelper.GetJournalKey(payload.PersistenceId), bytes, payload.SequenceNr);

                // notify about a new event being appended for this persistence id
                transaction.PublishAsync(_journalHelper.GetJournalChannel(payload.PersistenceId), payload.SequenceNr);

                // save tags
                foreach (var tag in tags)
                {
                    transaction.ListRightPushAsync(_journalHelper.GetTagKey(tag), $"{payload.SequenceNr}:{payload.PersistenceId}");
                    transaction.PublishAsync(_journalHelper.GetTagsChannel(), tag);
                }
            }

            // set highest sequence number key
            transaction.StringSetAsync(_journalHelper.GetHighestSequenceNrKey(aw.PersistenceId), aw.HighestSequenceNr);

            // add persistenceId
            transaction.SetAddAsync(_journalHelper.GetIdentifiersKey(), aw.PersistenceId).ContinueWith(task =>
            {
                if (task.Result)
                {
                    // notify about a new persistenceId
                    Database.Publish(_journalHelper.GetIdentifiersChannel(), aw.PersistenceId);
                }
            });

            if (!await transaction.ExecuteAsync())
            {
                throw new Exception($"{nameof(WriteMessagesAsync)}: failed to write {typeof(IPersistentRepresentation).Name} to redis");
            }
        }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

        private (byte[], IImmutableSet<string>) Extract(IPersistentRepresentation pr)
        {
            if (pr.Payload is Tagged tag)
            {
                return (_journalHelper.PersistentToBytes(pr.WithPayload(tag.Payload)), tag.Tags);
            }
            else
            {
                return (_journalHelper.PersistentToBytes(pr), ImmutableHashSet<string>.Empty);
            }
        }
    }
}