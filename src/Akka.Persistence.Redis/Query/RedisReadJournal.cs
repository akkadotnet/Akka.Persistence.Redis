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

namespace Akka.Persistence.Redis.Query
{
    public class RedisReadJournal :
        IPersistenceIdsQuery,
        ICurrentPersistenceIdsQuery,
        IEventsByPersistenceIdQuery,
        ICurrentEventsByPersistenceIdQuery,
        IEventsByTagQuery,
        ICurrentEventsByTagQuery,
        IAllEventsQuery,
        ICurrentAllEventsQuery
    {
        private readonly ExtendedActorSystem _system;
        private readonly Config _config;

        private ConnectionMultiplexer _redis;
        private int _database;

        /// <summary>
        /// The default identifier for <see cref="RedisReadJournal" /> to be used with <see cref="PersistenceQueryExtensions.ReadJournalFor{TJournal}" />.
        /// </summary>
        public static string Identifier = "akka.persistence.query.journal.redis";

        public RedisReadJournal(ExtendedActorSystem system, Config config)
        {
            _system = system;
            _config = config;
            var address = system.Settings.Config.GetString("akka.persistence.journal.redis.configuration-string");

            _database = system.Settings.Config.GetInt("akka.persistence.journal.redis.database");
            _redis = ConnectionMultiplexer.Connect(address);
        }

        /// <summary>
        /// <para>
        /// <see cref="PersistenceIds"/> is used for retrieving all `persistenceIds` of all
        /// persistent actors.
        /// </para>
        /// The returned event stream is unordered and you can expect different order for multiple
        /// executions of the query.
        /// <para>
        /// The stream is not completed when it reaches the end of the currently used `persistenceIds`,
        /// but it continues to push new `persistenceIds` when new persistent actors are created.
        /// Corresponding query that is completed when it reaches the end of the currently
        /// currently used `persistenceIds` is provided by <see cref="CurrentPersistenceIds"/>.
        /// </para>
        /// The SQL write journal is notifying the query side as soon as new `persistenceIds` are
        /// created and there is no periodic polling or batching involved in this query.
        /// <para>
        /// The stream is completed with failure if there is a failure in executing the query in the
        /// backend journal.
        /// </para>
        /// </summary>
        /// 

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

        /// <summary>
        /// <see cref="AllEvents"/> is used for retrieving all events
        /// <para></para>
        /// You can use <see cref="NoOffset"/> to retrieve all events or retrieve a subset of all
        /// events by specifying a <see cref="Sequence"/>. The `offset` corresponds to an ordered sequence number 
        /// Note that the corresponding offset of each event is provided in the
        /// <see cref="EventEnvelope"/>, which makes it possible to resume the
        /// stream at a later point from a given offset.
        /// <para></para>
        /// The `offset` is exclusive, i.e. the event with the exact same sequence number will not be included
        /// in the returned stream.This means that you can use the offset that is returned in <see cref="EventEnvelope"/>
        /// as the `offset` parameter in a subsequent query.
        /// <para></para>
        /// In addition to the <paramref name="offset"/> the <see cref="EventEnvelope"/> also provides `persistenceId` and `sequenceNr`
        /// for each event. The `sequenceNr` is the sequence number for the persistent actor with the
        /// `persistenceId` that persisted the event. The `persistenceId` + `sequenceNr` is an unique
        /// identifier for the event.
        /// <para></para>
        /// The stream is not completed when it reaches the end of the currently stored events,
        /// but it continues to push new events when new events are persisted.
        /// Corresponding query that is completed when it reaches the end of the currently
        /// stored events is provided by <see cref="CurrentAllEvents"/>.
        /// <para></para>
        /// The Redis write journal is notifying the query side as soon as events are persisted, but for
        /// efficiency reasons the query side retrieves the events in batches that sometimes can
        /// be delayed up to the configured `refresh-interval`.
        /// <para></para>
        /// The stream is completed with failure if there is a failure in executing the query in the
        /// backend journal.
        /// </summary>
        public Source<EventEnvelope, NotUsed> AllEvents(Offset offset)
        {
            offset = offset ?? new Sequence(0L);
            switch (offset)
            {
                case Sequence seq:
                    return Source.FromGraph(new AllEventsSource(_redis, _database, _config, seq.Value, _system, true));
                case NoOffset _:
                    return AllEvents(new Sequence(0L));
                default:
                    throw new ArgumentException($"RedisReadJournal does not support {offset.GetType().Name} offsets");
            }
        }

        /// <summary>
        /// <see cref="CurrentAllEvents"/> is used for retrieving all stored events, the event stream
        /// is completed immediately when it reaches the end of the "result set". Events that are
        /// stored after the query is completed are not included in the event stream
        /// <para></para>
        /// You can use <see cref="NoOffset"/> to retrieve all stored events or retrieve a subset of all stored
        /// events by specifying a <see cref="Sequence"/>. The `offset` corresponds to an ordered sequence number 
        /// Note that the corresponding offset of each event is provided in the
        /// <see cref="EventEnvelope"/>, which makes it possible to resume the
        /// stream at a later point from a given offset.
        /// <para></para>
        /// The `offset` is exclusive, i.e. the event with the exact same sequence number will not be included
        /// in the returned stream.This means that you can use the offset that is returned in <see cref="EventEnvelope"/>
        /// as the `offset` parameter in a subsequent query.
        /// <para></para>
        /// In addition to the <paramref name="offset"/> the <see cref="EventEnvelope"/> also provides `persistenceId` and `sequenceNr`
        /// for each event. The `sequenceNr` is the sequence number for the persistent actor with the
        /// `persistenceId` that persisted the event. The `persistenceId` + `sequenceNr` is an unique
        /// identifier for the event.        
        /// <para></para>
        /// The Redis write journal is notifying the query side as soon as events are persisted, but for
        /// efficiency reasons the query side retrieves the events in batches that sometimes can
        /// be delayed up to the configured `refresh-interval`.
        /// <para></para>
        /// The stream is completed with failure if there is a failure in executing the query in the
        /// backend journal.
        /// </summary>
        public Source<EventEnvelope, NotUsed> CurrentAllEvents(Offset offset)
        {
            offset = offset ?? new Sequence(0L);
            switch (offset) 
            {
                case Sequence seq:
                    return Source.FromGraph(new AllEventsSource(_redis, _database, _config, seq.Value, _system, false));
                case NoOffset _:
                    return CurrentAllEvents(new Sequence(0L));
                default:
                    throw new ArgumentException($"RedisReadJournal does not support {offset.GetType().Name} offsets");
            }
        }
    }
}