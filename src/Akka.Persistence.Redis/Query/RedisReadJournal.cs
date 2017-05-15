using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams.Dsl;
using StackExchange.Redis;
using System;
using Akka.Persistence.Redis.Query.Stages;
using Akka.Streams;

namespace Akka.Persistence.Redis.Query
{
    public class RedisReadJournal :
        IReadJournal,
        IAllPersistenceIdsQuery,
        ICurrentPersistenceIdsQuery,
        IEventsByPersistenceIdQuery,
        ICurrentEventsByPersistenceIdQuery
    {
        private readonly TimeSpan _refreshInterval;
        private readonly string _writeJournalPluginId;
        private readonly int _maxBufferSize;

        private ConnectionMultiplexer redis;
        private IDatabase redisDatabase;

        /// <summary>
        /// The default identifier for <see cref="RedisReadJournal" /> to be used with <see cref="PersistenceQueryExtensions.ReadJournalFor{TJournal}" />.
        /// </summary>
        public static string Identifier = "﻿akka.persistence.query.journal.redis";

        internal static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<RedisReadJournal>("Akka.Persistence.Redis.reference.conf");
        }

        public RedisReadJournal(ExtendedActorSystem system, Config config)
        {
            _refreshInterval = config.GetTimeSpan("refresh-interval");
            _writeJournalPluginId = config.GetString("write-plugin");
            _maxBufferSize = config.GetInt("max-buffer-size");

            var address = system.Settings.Config.GetString("akka.persistence.journal.redis.configuration-string");
            var database = system.Settings.Config.GetInt("akka.persistence.journal.redis.database");

            redis = ConnectionMultiplexer.Connect(address);
            redisDatabase = redis.GetDatabase(database);
        }

        /// <summary>
        /// Returns the live stream of persisted identifiers. Identifiers may appear several times in the stream.
        /// </summary>
        public Source<string, NotUsed> AllPersistenceIds() =>
            Source.FromGraph(new PersistenceIdsSource(redis));

        /// <summary>
        /// Returns the stream of current persisted identifiers. This stream is not live, once the identifiers were all returned, it is closed.
        /// </summary>
        public Source<string, NotUsed> CurrentPersistenceIds() =>
            Source.FromGraph(new CurrentPersistenceIdsSource(redisDatabase));

        /// <summary>
        /// Returns the live stream of events for the given <paramref name="persistenceId"/>.
        /// Events are ordered by <paramref name="fromSequenceNr"/>.
        /// When the <paramref name="toSequenceNr"/> has been delivered, the stream is closed.
        /// </summary>
        public Source<EventEnvelope, NotUsed> EventsByPersistenceId(string persistenceId, long fromSequenceNr = 0L, long toSequenceNr = long.MaxValue) =>
            Source.FromGraph(new EventsByPersistenceIdSource(null, redis, persistenceId, fromSequenceNr, toSequenceNr, null, true));

        /// <summary>
        /// Returns the stream of current events for the given <paramref name="persistenceId"/>.
        /// Events are ordered by <paramref name="fromSequenceNr"/>.
        /// When the <paramref name="toSequenceNr"/> has been delivered or no more elements are available at the current time, the stream is closed.
        /// </summary>
        public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(string persistenceId, long fromSequenceNr = 0L, long toSequenceNr = long.MaxValue) =>
            Source.FromGraph(new EventsByPersistenceIdSource(null, redis, persistenceId, fromSequenceNr, toSequenceNr, null, false));
    }
}
