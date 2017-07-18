//-----------------------------------------------------------------------
// <copyright file="RedisReadJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

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
        IPersistenceIdsQuery,
        ICurrentPersistenceIdsQuery,
        IEventsByPersistenceIdQuery,
        ICurrentEventsByPersistenceIdQuery,
        ICurrentEventsByTagQuery,
        IEventsByTagQuery
    {
        private readonly ExtendedActorSystem _system;
        private readonly Config _config;

        private ConnectionMultiplexer _redis;
        private int _database;

        /// <summary>
        /// The default identifier for <see cref="RedisReadJournal" /> to be used with <see cref="PersistenceQueryExtensions.ReadJournalFor{TJournal}" />.
        /// </summary>
        public static string Identifier = "akka.persistence.query.journal.redis";

        internal static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<RedisReadJournal>("Akka.Persistence.Redis.reference.conf");
        }

        public RedisReadJournal(ExtendedActorSystem system, Config config)
        {
            _system = system;
            _config = config;
            var address = system.Settings.Config.GetString("akka.persistence.journal.redis.configuration-string");

            _database = system.Settings.Config.GetInt("akka.persistence.journal.redis.database");
            _redis = ConnectionMultiplexer.Connect(address);
        }

        /// <summary>
        /// Returns the live stream of persisted identifiers. Identifiers may appear several times in the stream.
        /// </summary>
        public Source<string, NotUsed> PersistenceIds() =>
            Source.FromGraph(new PersistenceIdsSource(_redis, _database, _system));

        /// <summary>
        /// Returns the stream of current persisted identifiers. This stream is not live, once the identifiers were all returned, it is closed.
        /// </summary>
        public Source<string, NotUsed> CurrentPersistenceIds() =>
            Source.FromGraph(new CurrentPersistenceIdsSource(_redis, _database, _system));

        /// <summary>
        /// Returns the live stream of events for the given <paramref name="persistenceId"/>.
        /// Events are ordered by <paramref name="fromSequenceNr"/>.
        /// When the <paramref name="toSequenceNr"/> has been delivered, the stream is closed.
        /// </summary>
        public Source<EventEnvelope, NotUsed> EventsByPersistenceId(string persistenceId, long fromSequenceNr = 0L,
            long toSequenceNr = long.MaxValue) =>
            Source.FromGraph(new EventsByPersistenceIdSource(_redis, _database, _config, persistenceId, fromSequenceNr,
                toSequenceNr, _system, true));

        /// <summary>
        /// Returns the stream of current events for the given <paramref name="persistenceId"/>.
        /// Events are ordered by <paramref name="fromSequenceNr"/>.
        /// When the <paramref name="toSequenceNr"/> has been delivered or no more elements are available at the current time, the stream is closed.
        /// </summary>
        public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(string persistenceId,
            long fromSequenceNr = 0L, long toSequenceNr = long.MaxValue) =>
            Source.FromGraph(new EventsByPersistenceIdSource(_redis, _database, _config, persistenceId, fromSequenceNr,
                toSequenceNr, _system, false));

        /// <summary>
        /// Returns the live stream of events with a given tag.
        /// The events are sorted in the order they occurred, you can rely on it.
        /// </summary>
        public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset)
        {
            offset = offset ?? new Sequence(0L);
            switch (offset)
            {
                case Sequence seq:
                    return Source.FromGraph(new EventsByTagSource(_redis, _database, _config, tag, seq.Value, _system, false));
                case NoOffset _:
                    return CurrentEventsByTag(tag, new Sequence(0L));
                default:
                    throw new ArgumentException($"RedisReadJournal does not support {offset.GetType().Name} offsets");
            }
        }

        /// <summary>
        /// Returns the stream of current events with a given tag.
        /// The events are sorted in the order they occurred, you can rely on it.
        /// Once there are no more events in the store, the stream is closed, not waiting for new ones.
        /// </summary>
        public Source<EventEnvelope, NotUsed> EventsByTag(string tag, Offset offset)
        {
            offset = offset ?? new Sequence(0L);
            switch (offset)
            {
                case Sequence seq:
                    return Source.FromGraph(new EventsByTagSource(_redis, _database, _config, tag, seq.Value, _system, true));
                case NoOffset _:
                    return EventsByTag(tag, new Sequence(0L));
                default:
                    throw new ArgumentException($"RedisReadJournal does not support {offset.GetType().Name} offsets");
            }
        }
    }
}