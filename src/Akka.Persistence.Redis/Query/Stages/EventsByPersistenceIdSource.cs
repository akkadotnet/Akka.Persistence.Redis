using Akka.Persistence.Query;
using StackExchange.Redis;
using System;
using System.Collections;
using Akka.Streams;
using Akka.Streams.Stage;
using Akka.Actor;
using Akka.Configuration;
using System.Collections.Generic;
using System.Linq;
using Akka.Pattern;
using Akka.Util.Internal;
using Akka.Persistence.Redis.Journal;

namespace Akka.Persistence.Redis.Query.Stages
{
    internal class EventsByPersistenceIdSource : GraphStage<SourceShape<EventEnvelope>>
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly string _persistenceId;
        private readonly long _fromSequenceNr;
        private readonly long _toSequenceNr;
        private readonly bool _live;

        public EventsByPersistenceIdSource(Config conf, ConnectionMultiplexer redis, string persistenceId, long fromSequenceNr, long toSequenceNr, ActorSystem system, bool live)
        {
            _redis = redis;
            _persistenceId = persistenceId;
            _fromSequenceNr = fromSequenceNr;
            _toSequenceNr = toSequenceNr;
            _live = live;

            Outlet = live 
                ? new Outlet<EventEnvelope>("EventsByPersistenceIdSource") 
                : new Outlet<EventEnvelope>("CurrentEventsByPersistenceIdSource");

            Shape = new SourceShape<EventEnvelope>(Outlet);
        }

        internal Outlet<EventEnvelope> Outlet { get; }

        public override SourceShape<EventEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new EventsByPersistenceIdLogic(_redis, _persistenceId, _fromSequenceNr, _toSequenceNr, _live, Outlet, Shape);
        }

        private enum State
        {
            Idle = 0,
            Querying = 1,
            NotifiedWhenQuerying = 2,
            WaitingForNotification = 3
        }

        private class EventRef
        {
            public EventRef(long sequenceNr, string persistenceId)
            {
                SequenceNr = sequenceNr;
                PersistenceId = persistenceId;
            }

            public long SequenceNr { get; }

            public string PersistenceId { get; }
        }

        private class EventsByPersistenceIdLogic : GraphStageLogic
        {
            private State _state = State.Idle;

            private readonly Queue<string> _buffer = new Queue<string>();
            private ISubscriber subscription;
            private int max = 0;
            private long currentSequenceNr;
            private Action<IEnumerable<IPersistentRepresentation>> _callback;

            private readonly Outlet<EventEnvelope> _outlet;
            private readonly ConnectionMultiplexer _redis;
            private readonly string _persistenceId;
            private readonly long _fromSequenceNr;
            private readonly long _toSequenceNr;
            private readonly bool _live;

            public EventsByPersistenceIdLogic(
                ConnectionMultiplexer redis,
                string persistenceId,
                long fromSequenceNr,
                long toSequenceNr,
                bool live,
                Outlet<EventEnvelope> outlet, Shape shape) : base(shape)
            {
                _outlet = outlet;
                _redis = redis;
                _persistenceId = persistenceId;
                _fromSequenceNr = fromSequenceNr;
                _toSequenceNr = toSequenceNr;
                _live = live;

                currentSequenceNr = fromSequenceNr;
                SetHandler(outlet, Query);
            }

            public override void PreStart()
            {
                _callback = GetAsyncCallback<IEnumerable<IPersistentRepresentation>>(events =>
                {
                    if (events.Count() == 0)
                    {
                        switch (_state)
                        {
                            case State.NotifiedWhenQuerying:
                                // maybe we missed some new event when querying, retry
                                Query();
                                break;
                            case State.Querying:
                                if (_live)
                                {
                                    // nothing new, wait for notification
                                    _state = State.WaitingForNotification;
                                }
                                else
                                {
                                    // not a live stream, nothing else currently in the database, close the stream
                                    CompleteStage();
                                }
                                break;
                            default:
                                // TODO: log.error(f"Unexpected source state: $state")
                                FailStage(new IllegalStateException($"Unexpected source state: {_state}"));
                                break;
                        }
                    }
                    else
                    {
                        // TODO
                    }
                });

                if (_live)
                {
                    var messageCallback = GetAsyncCallback<(RedisChannel channel, string bs)>(data =>
                    {
                        if (data.channel.Equals(RedisUtils.GetJournalChannel(_persistenceId)))
                        {
                            // TODO: log.Debug("Message received")

                            switch (_state)
                            {
                                case State.Idle:
                                    // do nothing, no query is running and no client request was performed
                                    break;
                                case State.Querying:
                                    _state = State.NotifiedWhenQuerying;
                                    break;
                                case State.NotifiedWhenQuerying:
                                    // do nothing we already know that some new events may exist
                                    break;
                                case State.WaitingForNotification:
                                    _state = State.Idle;
                                    Query();
                                    break;
                            }
                        }
                        else
                        {
                            // TODO: log.Debug($"Message from unexpected channel: {channel}")
                        }
                    });

                    subscription = _redis.GetSubscriber();
                    subscription.Subscribe(RedisUtils.GetJournalChannel(_persistenceId), (channel, value) =>
                    {
                        messageCallback.Invoke((channel, value));
                    });
                }
            }

            public override void PostStop()
            {
                subscription?.UnsubscribeAll();
            }

            private void Query()
            {
                switch (_state)
                {
                    case State.Idle:
                        if (_buffer.Count == 0)
                        {
                            // so, we need to fill this buffer
                            _state = State.Querying;

                            // TODO
                        }
                        else
                        {
                            // buffer is non empty, let’s deliver buffered data
                            Deliver();
                        }
                        break;
                    default:
                        // TODO: log.error(f"Unexpected source state when querying: $state")
                        FailStage(new IllegalStateException($"Unexpected source state when querying: {_state}"));
                        break;
                }
            }

            private void Deliver()
            {
                // go back to idle state, waiting for more client request
                _state = State.Idle;
                var elem = _buffer.Dequeue();
                Push(_outlet, elem);
                if (_buffer.Count == 0 && currentSequenceNr > _toSequenceNr)
                {
                    // we delivered last buffered event and the upper bound was reached, complete 
                    CompleteStage();
                }
            }
        }
    }
}
