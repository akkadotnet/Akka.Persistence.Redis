//-----------------------------------------------------------------------
// <copyright file="RedisReadJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams.Dsl;
using System;

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
        private readonly TimeSpan _refreshInterval;
        private readonly string _writeJournalPluginId;
        private readonly int _maxBufferSize;
        private readonly ExtendedActorSystem _system;
        private readonly Config _config;

        /// <summary>
        /// The default identifier for <see cref="RedisReadJournal" /> to be used with <see cref="PersistenceQueryExtensions.ReadJournalFor{TJournal}" />.
        /// </summary>
        public static string Identifier = "akka.persistence.query.journal.redis";

        public RedisReadJournal(ExtendedActorSystem system, Config config)
        {
            if (config == null || config.IsEmpty)
                throw new ConfigurationException($"Could not find sub-configuration [{Identifier}]");

            _config = config;
            _system = system;
            _refreshInterval = config.GetTimeSpan("refresh-interval", TimeSpan.FromSeconds(3));
            _writeJournalPluginId = config.GetString("write-plugin", "");
            _maxBufferSize = config.GetInt("max-buffer-size", 100);
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
        public Source<string, NotUsed> PersistenceIds()
            => throw new NotImplementedException("PersistenceIds query is not implemented for Redis.");

        /// <summary>
        /// Returns the stream of current persisted identifiers. This stream is not live, once the identifiers were all returned, it is closed.
        /// </summary>
        public Source<string, NotUsed> CurrentPersistenceIds() 
            => throw new NotImplementedException("CurrentPersistenceIds query is not implemented for Redis.");

        /// <summary>
        /// Returns the live stream of events for the given <paramref name="persistenceId"/>.
        /// Events are ordered by <paramref name="fromSequenceNr"/>.
        /// When the <paramref name="toSequenceNr"/> has been delivered, the stream is closed.
        /// </summary>
        public Source<EventEnvelope, NotUsed> EventsByPersistenceId(
            string persistenceId, 
            long fromSequenceNr = 0L,
            long toSequenceNr = long.MaxValue)
            => Source.ActorPublisher<EventEnvelope>(
                    EventsByPersistenceIdPublisher.Props(
                        persistenceId, 
                        fromSequenceNr, 
                        toSequenceNr, 
                        _refreshInterval, 
                        _maxBufferSize, 
                        _writeJournalPluginId))
                    .MapMaterializedValue(_ => NotUsed.Instance)
                    .Named("EventsByPersistenceId-" + persistenceId);

        /// <summary>
        /// Returns the stream of current events for the given <paramref name="persistenceId"/>.
        /// Events are ordered by <paramref name="fromSequenceNr"/>.
        /// When the <paramref name="toSequenceNr"/> has been delivered or no more elements are available at the current time, the stream is closed.
        /// </summary>
        public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(
            string persistenceId,
            long fromSequenceNr = 0L, 
            long toSequenceNr = long.MaxValue)
            => Source.ActorPublisher<EventEnvelope>(
                    EventsByPersistenceIdPublisher.Props(
                        persistenceId, 
                        fromSequenceNr, 
                        toSequenceNr, 
                        null, 
                        _maxBufferSize, 
                        _writeJournalPluginId))
                    .MapMaterializedValue(_ => NotUsed.Instance)
                    .Named("CurrentEventsByPersistenceId-" + persistenceId);

        /// <summary>
        /// Returns the live stream of events with a given tag.
        /// The events are sorted in the order they occurred, you can rely on it.
        /// </summary>
        public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset)
            => throw new NotImplementedException("CurrentEventsByTag query is not implemented for Redis.");

        /// <summary>
        /// Returns the stream of current events with a given tag.
        /// The events are sorted in the order they occurred, you can rely on it.
        /// Once there are no more events in the store, the stream is closed, not waiting for new ones.
        /// </summary>
        public Source<EventEnvelope, NotUsed> EventsByTag(string tag, Offset offset)
            => throw new NotImplementedException("EventsByTag query is not implemented for Redis.");

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
            => throw new NotImplementedException("AllEvents query is not implemented for Redis.");

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
            => throw new NotImplementedException("CurrentAllEvents query is not implemented for Redis.");
    }
}