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
        private readonly RedisPluginConnection _connection;
        private readonly ILoggingAdapter _log;

        /// <summary>
        /// The Redis database used by this journal. Throws <see cref="InvalidOperationException"/>
        /// while the connection has not been established yet, because the database depends on
        /// whether the server is a Redis Cluster.
        /// </summary>
        public IDatabase Database => ResolvedTopology.Database;

        /// <summary>
        /// Whether the server is a Redis Cluster, which decides the key names this journal uses.
        /// Throws <see cref="InvalidOperationException"/> while the connection has not been
        /// established yet.
        /// </summary>
        public bool IsClustered => ResolvedTopology.IsClustered;

        private RedisPluginTopology ResolvedTopology => _connection.Topology
            ?? throw new InvalidOperationException("The Redis connection has not been established yet.");

        public RedisJournal(Config journalConfig)
        {
            _settings = RedisSettings.Create(journalConfig.WithFallback(Extension.DefaultJournalConfig));
            _journalHelper = new JournalHelper(Context.System, _settings.KeyPrefix);

            _log = Context.GetLogger();
            _connection = RedisConnectionResolver.Resolve(Context.System, _settings, Self.Path.Name, _log);
        }

        protected override void PostStop()
        {
            _connection.Dispose();
            base.PostStop();
        }

        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            var topology = await _connection.GetTopologyAsync();
            var highestSequenceNr =
                await topology.Database.StringGetAsync(_journalHelper.GetHighestSequenceNrKey(persistenceId, topology.IsClustered));
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
            var topology = await _connection.GetTopologyAsync();
            var journals = await topology.Database.SortedSetRangeByScoreAsync(
                _journalHelper.GetJournalKey(persistenceId, topology.IsClustered),
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

            var topology = await _connection.GetTopologyAsync();
            try
            {
                await topology.Database.SortedSetRemoveRangeByScoreAsync(
                    _journalHelper.GetJournalKey(persistenceId, topology.IsClustered),
                    -1,
                    toSequenceNr,
                    flags: CommandFlags.DemandMaster);
            }
            catch (RedisCommandException ex) when (topology.Refresher.IsReplicaRefusal(ex))
            {
                topology.Refresher.TriggerBackgroundRefresh(persistenceId, nameof(DeleteMessagesToAsync), ex);
                throw;
            }
        }

        protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            var writes = messages.ToArray();

            // Just return immediately if there are no message to persist
            if (writes.Length == 0)
                return ImmutableList<Exception>.Empty;

            // Resolve the topology before issuing any write. The writes of one batch are sent
            // without awaiting each other, and must reach Redis in order: none of them may
            // wait for the connection on its own.
            var topology = await _connection.GetTopologyAsync();
            var writeTasks = writes.Select(aw => WriteBatchAsync(aw, topology)).ToArray();

            var result = await Task<IImmutableList<Exception>>
                .Factory
                .ContinueWhenAll(
                    writeTasks,
                    tasks => tasks.Select(t => t.IsFaulted ? TryUnwrapException(t.Exception) : null)
                        .ToImmutableList(), cancellationToken);

            return result;
        }

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
        private async Task WriteBatchAsync(AtomicWrite aw, RedisPluginTopology topology)
        {
            var eventList = new List<SortedSetEntry>();
            var payloads = aw.Payload.AsInstanceOf<IImmutableList<IPersistentRepresentation>>();
            foreach (var payload in payloads)
            {
                var bytes = _journalHelper.PersistentToBytes(payload.WithTimestamp(DateTime.UtcNow.Ticks));

                // save the payload
                eventList.Add(new SortedSetEntry(bytes, payload.SequenceNr));
            }

            var transaction = topology.Database.CreateTransaction();
            transaction.SortedSetAddAsync(
                _journalHelper.GetJournalKey(aw.PersistenceId, topology.IsClustered),
                eventList.ToArray(),
                flags: CommandFlags.DemandMaster);

            // set highest sequence number key
            transaction.StringSetAsync(
                _journalHelper.GetHighestSequenceNrKey(aw.PersistenceId, topology.IsClustered),
                aw.HighestSequenceNr,
                flags: CommandFlags.DemandMaster);

            bool transactionSucceeded;
            try
            {
                transactionSucceeded = await transaction.ExecuteAsync();
            }
            catch (RedisCommandException ex) when (topology.Refresher.IsReplicaRefusal(ex))
            {
                topology.Refresher.TriggerBackgroundRefresh(aw.PersistenceId, nameof(WriteBatchAsync), ex);
                throw;
            }

            if (!transactionSucceeded)
                throw new Exception(
                    $"{nameof(WriteMessagesAsync)}: failed to write {nameof(IPersistentRepresentation)} to redis");
        }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }
}
