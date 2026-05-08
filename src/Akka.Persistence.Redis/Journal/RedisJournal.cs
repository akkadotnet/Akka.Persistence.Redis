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

namespace Akka.Persistence.Redis.Journal
{
    public class RedisJournal : AsyncWriteJournal
    {
        protected static readonly RedisPersistence Extension = RedisPersistence.Get(Context.System);

        private readonly RedisSettings _settings;
        private readonly JournalHelper _journalHelper;
        private readonly Lazy<RedisConnection> _connection;
        private readonly ActorSystem _system;

        public IDatabase Database => _connection.Value.Database;
        public bool IsClustered { get; private set; }

        // Test seam: exposes the underlying multiplexer once the connection has been initialized.
        // Returns null before the first call to Database to keep PostStop side-effect-free during early termination.
        internal IConnectionMultiplexer ConnectionMultiplexer => _connection.IsValueCreated ? _connection.Value.Multiplexer : null;

        public RedisJournal(Config journalConfig)
        {
            _settings = RedisSettings.Create(journalConfig.WithFallback(Extension.DefaultJournalConfig));
            _journalHelper = new JournalHelper(Context.System, _settings.KeyPrefix);
            _system = Context.System;
            _connection = new Lazy<RedisConnection>(() =>
            {
                IConnectionMultiplexer redisConnection;
                bool ownsConnection;
                var supplied = TryResolveCallerSuppliedMultiplexer();
                if (supplied is not null)
                {
                    // Caller supplied a pre-configured multiplexer via ActorSystemSetup.
                    // Reuse it and leave its lifetime to the caller; never dispose from PostStop.
                    redisConnection = supplied;
                    ownsConnection = false;
                }
                else
                {
                    redisConnection = StackExchange.Redis.ConnectionMultiplexer.Connect(_settings.ConfigurationString);
                    ownsConnection = true;
                }

                IsClustered = redisConnection.IsClustered();

                IDatabase database;
                if (ownsConnection && _settings.DatabaseFromConnectionString && !IsClustered)
                {
                    // DatabaseFromConnectionString is only meaningful when we own the connection
                    // string ourselves; an injected multiplexer is opaque to us so we fall back
                    // to the explicitly configured _settings.Database.
                    var conf = ConfigurationOptions.Parse(_settings.ConfigurationString);
                    if (conf.DefaultDatabase.HasValue)
                        database = redisConnection.GetDatabase(conf.DefaultDatabase.Value);
                    else
                        database = redisConnection.GetDatabase(_settings.Database);
                }
                else if (IsClustered)
                {
                    // for Redis Cluster, the database is 0 https://redis.io/topics/cluster-spec#implemented-subset
                    database = redisConnection.GetDatabase(0);
                }
                else
                {
                    database = redisConnection.GetDatabase(_settings.Database);
                }

                return new RedisConnection(redisConnection, database, ownsConnection);
            });
        }

        // Looks up a caller-supplied multiplexer via ActorSystemSetup. Single-instance
        // RedisConnectionMultiplexerSetup applies to every plugin and takes precedence;
        // MultiRedisConnectionMultiplexerSetup is consulted as a fallback, keyed by this
        // plugin's full HOCON path (which equals Self.Path.Name for system plugin actors).
        // Returns null when neither setup applies, in which case the plugin opens its own
        // multiplexer from the HOCON connection-string.
        private IConnectionMultiplexer? TryResolveCallerSuppliedMultiplexer()
        {
            var setup = _system.Settings.Setup;

            var single = setup.Get<RedisConnectionMultiplexerSetup>();
            if (single.HasValue)
                return single.Value.ConnectionFactory().GetAwaiter().GetResult();

            var multi = setup.Get<MultiRedisConnectionMultiplexerSetup>();
            if (multi.HasValue && multi.Value.TryGetFactory(Self.Path.Name, out var factory) && factory is not null)
                return factory().GetAwaiter().GetResult();

            return null;
        }

        protected override void PostStop()
        {
            if (_connection.IsValueCreated)
            {
                var connection = _connection.Value;
                if (connection.OwnsConnection)
                    connection.Multiplexer.Dispose();
            }

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

        private sealed class RedisConnection
        {
            public RedisConnection(IConnectionMultiplexer multiplexer, IDatabase database, bool ownsConnection)
            {
                Multiplexer = multiplexer;
                Database = database;
                OwnsConnection = ownsConnection;
            }

            public IConnectionMultiplexer Multiplexer { get; }
            public IDatabase Database { get; }
            public bool OwnsConnection { get; }
        }
    }
}
