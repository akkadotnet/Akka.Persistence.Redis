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

        // Appends one AtomicWrite only if no write with an equal or higher sequence number has been
        // stored yet, the same first-writer-wins rule a unique (persistence_id, sequence_nr)
        // constraint gives a SQL journal. Both keys share the persistence id's hash tag in cluster
        // mode, so the script runs on a single slot.
        //   KEYS[1] journal sorted set, KEYS[2] highest sequence number
        //   ARGV[1] lowest sequence number of the write, ARGV[2] its highest, then score/member pairs
        // Returns -1 when the write was applied, otherwise the stored highest sequence number.
        private const string ConditionalWriteScript = @"
local highest = tonumber(redis.call('GET', KEYS[2]) or '0')
local lowest = tonumber(ARGV[1])
if highest >= lowest or redis.call('ZCOUNT', KEYS[1], lowest, '+inf') > 0 then
  return highest
end
for i = 3, #ARGV, 2 do
  redis.call('ZADD', KEYS[1], ARGV[i], ARGV[i + 1])
end
redis.call('SET', KEYS[2], ARGV[2])
return -1";

        protected override async Task<IImmutableList<Exception>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages, CancellationToken cancellationToken)
        {
            // Redis driver does not support cancellation token
            cancellationToken.ThrowIfCancellationRequested();

            var writes = messages.ToArray();

            // Just return immediately if there are no message to persist
            if (writes.Length == 0)
                return ImmutableList<Exception>.Empty;

            // Serialize first. A write that cannot be serialized is rejected: it was never sent,
            // so it is certain that it was not stored, and the persistent actor can carry on.
            var rejections = new Exception?[writes.Length];
            var arguments = new RedisValue[]?[writes.Length];
            for (var i = 0; i < writes.Length; i++)
            {
                try
                {
                    arguments[i] = ToScriptArguments(writes[i]);
                }
                catch (Exception ex)
                {
                    rejections[i] = ex;
                }
            }

            // Resolve the topology before issuing any write. The writes of one batch are sent
            // without awaiting each other, and must reach Redis in order: none of them may
            // wait for the connection on its own.
            var topology = await _connection.GetTopologyAsync();
            var writeTasks = new List<Task>(writes.Length);
            for (var i = 0; i < writes.Length; i++)
            {
                if (arguments[i] is { } args)
                    writeTasks.Add(WriteBatchAsync(writes[i], args, topology));
            }

            // Any other error fails the whole call (OnPersistFailure, and the actor stops) instead of
            // rejecting the write: after a timeout or a lost connection the write may still be
            // applied, and after a sequence-number conflict another writer owns the persistence id.
            // Neither may be reported as "not stored".
            await Task.WhenAll(writeTasks);

            return rejections.ToImmutableList()!;
        }

        private RedisValue[] ToScriptArguments(AtomicWrite aw)
        {
            var payloads = aw.Payload.AsInstanceOf<IImmutableList<IPersistentRepresentation>>();
            var arguments = new RedisValue[2 + payloads.Count * 2];
            arguments[0] = aw.LowestSequenceNr;
            arguments[1] = aw.HighestSequenceNr;
            var i = 2;
            foreach (var payload in payloads)
            {
                arguments[i++] = payload.SequenceNr;
                arguments[i++] = _journalHelper.PersistentToBytes(payload.WithTimestamp(DateTime.UtcNow.Ticks));
            }

            return arguments;
        }

        private async Task WriteBatchAsync(AtomicWrite aw, RedisValue[] arguments, RedisPluginTopology topology)
        {
            RedisResult result;
            try
            {
                result = await topology.Database.ScriptEvaluateAsync(
                    ConditionalWriteScript,
                    new RedisKey[]
                    {
                        _journalHelper.GetJournalKey(aw.PersistenceId, topology.IsClustered),
                        _journalHelper.GetHighestSequenceNrKey(aw.PersistenceId, topology.IsClustered)
                    },
                    arguments,
                    CommandFlags.DemandMaster);
            }
            catch (RedisCommandException ex) when (topology.Refresher.IsReplicaRefusal(ex))
            {
                topology.Refresher.TriggerBackgroundRefresh(aw.PersistenceId, nameof(WriteBatchAsync), ex);
                throw;
            }

            var storedHighest = (long)result;
            if (storedHighest != -1)
                throw new RedisJournalWriteConflictException(aw.PersistenceId, aw.LowestSequenceNr, aw.HighestSequenceNr, storedHighest);
        }
    }
}
