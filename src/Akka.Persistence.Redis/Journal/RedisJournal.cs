// -----------------------------------------------------------------------
// <copyright file="RedisJournal.cs" company="Petabridge, LLC">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Event;
using Akka.Persistence.Journal;
using Akka.Util.Internal;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis.Journal
{
    public class RedisJournal : AsyncWriteJournal
    {
        protected static readonly RedisPersistence Extension = RedisPersistence.Get(Context.System);

        private readonly RedisSettings _settings;
        private readonly JournalHelper _journalHelper;
        private readonly IConnectionMultiplexer _connection;
        private readonly bool _ownsConnection;
        private readonly ILoggingAdapter _log;
        private readonly RedisTopologyRefresher _topology;

        public IDatabase Database { get; }
        public bool IsClustered { get; }

        public RedisJournal(Config journalConfig)
        {
            _settings = RedisSettings.Create(journalConfig.WithFallback(Extension.DefaultJournalConfig));
            _journalHelper = new JournalHelper(Context.System, _settings.KeyPrefix);

            var resolved = RedisConnectionResolver.Resolve(Context.System, _settings, Self.Path.Name);
            _connection = resolved.Connection;
            _ownsConnection = resolved.OwnsConnection;
            Database = resolved.Database;
            IsClustered = resolved.IsClustered;

            _log = Context.GetLogger();
            _topology = RedisTopologyRefresher.Create(_connection, _log);
        }

        protected override void PostStop()
        {
            if (_ownsConnection)
                _connection.Dispose();
            base.PostStop();
        }

        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            var highestSequenceNr =
                await Database.StringGetAsync(_journalHelper.GetHighestSequenceNrKey(persistenceId, IsClustered));
            return highestSequenceNr.IsNull ? 0L : (long) highestSequenceNr;
        }

        public override async Task ReplayMessagesAsync(
            IActorContext context,
            string persistenceId,
            long fromSequenceNr,
            long toSequenceNr,
            long max,
            Action<IPersistentRepresentation> recoveryCallback)
        {
            var journals = await Database.SortedSetRangeByScoreAsync(
                _journalHelper.GetJournalKey(persistenceId, IsClustered),
                fromSequenceNr,
                toSequenceNr,
                skip: 0L,
                take: max);

            foreach (var journal in journals)
                recoveryCallback(_journalHelper.PersistentFromBytes(journal));
        }

        protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await Database.SortedSetRemoveRangeByScoreAsync(
                    _journalHelper.GetJournalKey(persistenceId, IsClustered),
                    -1,
                    toSequenceNr,
                    flags: CommandFlags.DemandMaster);
            }
            catch (RedisCommandException ex) when (RedisTopologyRefresher.IsReplicaRefusal(ex))
            {
                _topology.TriggerBackgroundRefresh(persistenceId, nameof(DeleteMessagesToAsync), ex);
                throw;
            }
        }

        protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            var writeTasks = messages.Select(WriteBatchAsync).ToArray();

            // Just return immediately if there are no message to persist
            if (writeTasks.Length == 0)
                return ImmutableList<Exception>.Empty;

            var result = await Task<IImmutableList<Exception>>
                .Factory
                .ContinueWhenAll(
                    writeTasks,
                    tasks => tasks.Select(t => t.IsFaulted ? TryUnwrapException(t.Exception) : null)
                        .ToImmutableList(), cancellationToken);

            return result;
        }

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        private async Task WriteBatchAsync(AtomicWrite aw)
        {
            var eventList = new List<SortedSetEntry>();
            var payloads = aw.Payload.AsInstanceOf<IImmutableList<IPersistentRepresentation>>();
            foreach (var payload in payloads)
            {
                var bytes = _journalHelper.PersistentToBytes(payload.WithTimestamp(DateTime.UtcNow.Ticks));

                // save the payload
                eventList.Add(new SortedSetEntry(bytes, payload.SequenceNr));
            }

            var transaction = Database.CreateTransaction();
            transaction.SortedSetAddAsync(
                _journalHelper.GetJournalKey(aw.PersistenceId, IsClustered),
                eventList.ToArray(),
                flags: CommandFlags.DemandMaster);

            // set highest sequence number key
            transaction.StringSetAsync(
                _journalHelper.GetHighestSequenceNrKey(aw.PersistenceId, IsClustered),
                aw.HighestSequenceNr,
                flags: CommandFlags.DemandMaster);

            bool transactionSucceeded;
            try
            {
                transactionSucceeded = await transaction.ExecuteAsync();
            }
            catch (RedisCommandException ex) when (RedisTopologyRefresher.IsReplicaRefusal(ex))
            {
                _topology.TriggerBackgroundRefresh(aw.PersistenceId, nameof(WriteBatchAsync), ex);
                throw;
            }

            if (!transactionSucceeded)
                throw new Exception(
                    $"{nameof(WriteMessagesAsync)}: failed to write {nameof(IPersistentRepresentation)} to redis");
        }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }
}
