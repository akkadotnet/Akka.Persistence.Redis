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

        public IDatabase Database { get; }
        public bool IsClustered { get; }

        // Test seam.
        internal IConnectionMultiplexer ConnectionMultiplexer => _connection;

        public RedisJournal(Config journalConfig)
        {
            _settings = RedisSettings.Create(journalConfig.WithFallback(Extension.DefaultJournalConfig));
            _journalHelper = new JournalHelper(Context.System, _settings.KeyPrefix);

            var setup = Context.System.Settings.Setup.Get<RedisConnectionMultiplexerSetup>();
            if (setup.HasValue)
            {
                _connection = setup.Value.Factory().GetAwaiter().GetResult();
                _ownsConnection = setup.Value.OwnedByPlugin;
            }
            else
            {
                _connection = StackExchange.Redis.ConnectionMultiplexer.Connect(_settings.ConfigurationString);
                _ownsConnection = true;
            }

            IsClustered = _connection.IsClustered();
            Database = _connection.GetDatabase(ResolveDatabaseNumber());
        }

        private int ResolveDatabaseNumber()
        {
            if (IsClustered)
            {
                // Redis Cluster pins everything to db 0 — https://redis.io/topics/cluster-spec#implemented-subset
                return 0;
            }

            // DatabaseFromConnectionString is only meaningful when we own the connection
            // string ourselves; an injected multiplexer is opaque so fall back to the
            // explicitly configured _settings.Database.
            if (_ownsConnection && _settings.DatabaseFromConnectionString)
            {
                var conf = ConfigurationOptions.Parse(_settings.ConfigurationString);
                if (conf.DefaultDatabase.HasValue)
                    return conf.DefaultDatabase.Value;
            }

            return _settings.Database;
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

            await Database.SortedSetRemoveRangeByScoreAsync(
                _journalHelper.GetJournalKey(persistenceId, IsClustered),
                -1,
                toSequenceNr,
                flags: CommandFlags.DemandMaster);
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

            if (!await transaction.ExecuteAsync())
                throw new Exception(
                    $"{nameof(WriteMessagesAsync)}: failed to write {nameof(IPersistentRepresentation)} to redis");
        }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
    }
}
