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
        private readonly ActorSystem _system;
        private readonly bool _live;

        public EventsByPersistenceIdSource(Config conf, ConnectionMultiplexer redis, string persistenceId, long fromSequenceNr, long toSequenceNr, ActorSystem system, bool live)
        {
            _redis = redis;
            _persistenceId = persistenceId;
            _fromSequenceNr = fromSequenceNr;
            _toSequenceNr = toSequenceNr;
            _system = system;
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
            return new EventsByPersistenceIdLogic(_redis, _system, _persistenceId, _fromSequenceNr, _toSequenceNr, _live, Outlet, Shape);
        }

        private enum State
        {
            Idle = 0,
            Querying = 1,
            NotifiedWhenQuerying = 2,
            WaitingForNotification = 3
        }

        private class EventsByPersistenceIdLogic : GraphStageLogic
        {
            private State _state = State.Idle;

            private readonly Queue<EventEnvelope> _buffer = new Queue<EventEnvelope>();
            private ISubscriber _subscription;
            private int max = 100; //TODO: take from config
            private long _currentSequenceNr;
            private Action<IReadOnlyList<IPersistentRepresentation>> _callback;

            private readonly Outlet<EventEnvelope> _outlet;
            private readonly ConnectionMultiplexer _redis;
            private readonly ActorSystem _system;
            private readonly string _persistenceId;
            private readonly long _toSequenceNr;
            private readonly bool _live;

            public EventsByPersistenceIdLogic(
                ConnectionMultiplexer redis,
                ActorSystem system,
                string persistenceId,
                long fromSequenceNr,
                long toSequenceNr,
                bool live,
                Outlet<EventEnvelope> outlet, Shape shape) : base(shape)
            {
                _outlet = outlet;
                _redis = redis;
                _system = system;
                _persistenceId = persistenceId;
                _toSequenceNr = toSequenceNr;
                _live = live;

                _currentSequenceNr = fromSequenceNr;
                SetHandler(outlet, Query);
            }

            public override void PreStart()
            {
                _callback = GetAsyncCallback<IReadOnlyList<IPersistentRepresentation>>(events =>
                {
                    if (events.Count == 0)
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
                        var (evts, maxSequenceNr) = events.Aggregate((new List<EventEnvelope>(), _currentSequenceNr), (tuple, pr) =>
                        {
                            if (!pr.IsDeleted &&
                                pr.SequenceNr >= _currentSequenceNr &&
                                pr.SequenceNr <= _toSequenceNr)
                            {
                                tuple.Item1.Add(new EventEnvelope(pr.SequenceNr, pr.PersistenceId, pr.SequenceNr, pr.Payload));
                                tuple.Item2 = pr.SequenceNr + 1;
                            }
                            else
                            {
                                tuple.Item2 = pr.SequenceNr + 1;
                            }

                            return tuple;
                        });

                        _currentSequenceNr = maxSequenceNr;
                        // TODO: log.debug(f"Max sequence number is now $maxSequenceNr")
                        if (evts.Count > 0)
                        {
                            evts.ForEach(_buffer.Enqueue);
                            Deliver();
                        }
                        else
                        {
                            // requery immediately
                            _state = State.Idle;
                            Query();
                        }
                    }
                });

                if (_live)
                {
                    // subscribe to notification stream only if live stream was required
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

                    _subscription = _redis.GetSubscriber();
                    _subscription.Subscribe(RedisUtils.GetJournalChannel(_persistenceId), (channel, value) =>
                    {
                        messageCallback.Invoke((channel, value));
                    });
                }
            }

            public override void PostStop()
            {
                _subscription?.UnsubscribeAll();
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

                            var events = _redis.GetDatabase(1).SortedSetRangeByScore(RedisUtils.GetJournalKey(_persistenceId), _currentSequenceNr,
                                Math.Min(_currentSequenceNr + max - 1, _toSequenceNr)).Select(e => (byte[])e).ToList();

                            var deserializedEvents = events.Select(e => RedisUtils.PersistentFromBytes(e, _system.Serialization)).ToList();
                            _callback(deserializedEvents);
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
                if (_buffer.Count == 0 && _currentSequenceNr > _toSequenceNr)
                {
                    // we delivered last buffered event and the upper bound was reached, complete 
                    CompleteStage();
                }
            }
        }
    }
}
