//-----------------------------------------------------------------------
// <copyright file="EventsByPersistenceIdSource.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Persistence.Query;
using StackExchange.Redis;
using System;
using Akka.Streams;
using Akka.Streams.Stage;
using Akka.Actor;
using Akka.Configuration;
using System.Collections.Generic;
using System.Linq;
using Akka.Pattern;

namespace Akka.Persistence.Redis.Query.Stages
{
    internal class EventsByPersistenceIdSource : GraphStage<SourceShape<EventEnvelope>>
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly int _database;
        private readonly Config _config;
        private readonly string _persistenceId;
        private readonly long _fromSequenceNr;
        private readonly long _toSequenceNr;
        private readonly ActorSystem _system;
        private readonly bool _live;

        public EventsByPersistenceIdSource(ConnectionMultiplexer redis, int database, Config config, string persistenceId, long fromSequenceNr, long toSequenceNr, ActorSystem system, bool live)
        {
            _redis = redis;
            _database = database;
            _config = config;
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
            return new EventsByPersistenceIdLogic(_redis, _database, _config, _system, _persistenceId, _fromSequenceNr, _toSequenceNr, _live, Outlet, Shape);
        }

        private enum State
        {
            Idle = 0,
            Querying = 1,
            NotifiedWhenQuerying = 2,
            WaitingForNotification = 3,
            Initializing = 4,
            QueryWhenInitializing = 5
        }

        private class EventsByPersistenceIdLogic : GraphStageLogic
        {
            private State _state = State.Idle;
            private readonly Queue<EventEnvelope> _buffer = new Queue<EventEnvelope>();
            private ISubscriber _subscription;
            private readonly int _max;
            private readonly JournalHelper _journalHelper;
            private long _currentSequenceNr;
            private Action<IReadOnlyList<IPersistentRepresentation>> _callback;

            private readonly Outlet<EventEnvelope> _outlet;
            private readonly ConnectionMultiplexer _redis;
            private readonly int _database;
            private readonly ActorSystem _system;
            private readonly string _persistenceId;
            private long _toSequenceNr;
            private readonly bool _live;

            public EventsByPersistenceIdLogic(
                ConnectionMultiplexer redis,
                int database,
                Config config,
                ActorSystem system,
                string persistenceId,
                long fromSequenceNr,
                long toSequenceNr,
                bool live,
                Outlet<EventEnvelope> outlet, Shape shape) : base(shape)
            {
                _outlet = outlet;
                _redis = redis;
                _database = database;
                _system = system;
                _persistenceId = persistenceId;
                _toSequenceNr = toSequenceNr;
                _live = live;

                _max = config.GetInt("max-buffer-size");
                _journalHelper = new JournalHelper(system, system.Settings.Config.GetString("akka.persistence.journal.redis.key-prefix"));

                _currentSequenceNr = fromSequenceNr;
                SetHandler(outlet, onPull: () =>
                {
                    switch (_state)
                    {
                        case State.Initializing:
                            _state = State.QueryWhenInitializing;
                            break;
                        default:
                            Query();
                            break;
                    }
                });
            }

            public override void PreStart()
            {
                _callback = GetAsyncCallback<IReadOnlyList<IPersistentRepresentation>>(events =>
                {
                    if (events.Count == 0)
                    {
                        if (_currentSequenceNr > _toSequenceNr)
                        {
                            // end has been reached
                            CompleteStage();
                        }
                        else
                        {
                            switch (_state)
                            {
                                case State.NotifiedWhenQuerying:
                                    // maybe we missed some new event when querying, retry
                                    _state = State.Idle;
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
                                    Log.Error($"Unexpected source state: {_state}");
                                    FailStage(new IllegalStateException($"Unexpected source state: {_state}"));
                                    break;
                            }
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
                                tuple.Item1.Add(new EventEnvelope(new Sequence(pr.SequenceNr), pr.PersistenceId, pr.SequenceNr, pr.Payload));
                                tuple.Item2 = pr.SequenceNr + 1;
                            }
                            else
                            {
                                tuple.Item2 = pr.SequenceNr + 1;
                            }

                            return tuple;
                        });

                        _currentSequenceNr = maxSequenceNr;
                        Log.Debug($"Max sequence number is now {maxSequenceNr}");
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
                        if (data.channel.Equals(_journalHelper.GetJournalChannel(_persistenceId)))
                        {
                            Log.Debug("Message received");

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
                            Log.Debug($"Message from unexpected channel: {data.channel}");
                        }
                    });

                    _subscription = _redis.GetSubscriber();
                    _subscription.Subscribe(_journalHelper.GetJournalChannel(_persistenceId), (channel, value) =>
                    {
                        messageCallback.Invoke((channel, value));
                    });
                }
                else
                {
                    // start by first querying the current highest sequenceNr
                    // for the given persistent id
                    // stream will stop once this has been delivered
                    _state = State.Initializing;

                    var initCallback = GetAsyncCallback<long>(sn =>
                    {
                        if (_toSequenceNr > sn)
                        {
                            // the initially requested max sequence number is higher than the current
                            // one, restrict it to the current one
                            _toSequenceNr = sn;
                        }

                        switch (_state)
                        {
                            case State.QueryWhenInitializing:
                                // during initialization, downstream asked for an element,
                                // let’s query elements
                                _state = State.Idle;
                                Query();
                                break;
                            case State.Initializing:
                                // no request from downstream, just go idle
                                _state = State.Idle;
                                break;
                            default:
                                Log.Error($"Unexpected source state when initializing: {_state}");
                                FailStage(new IllegalStateException($"Unexpected source state when initializing: {_state}"));
                                break;
                        }
                    });

                    _redis.GetDatabase(_database).StringGetAsync(_journalHelper.GetHighestSequenceNrKey(_persistenceId)).ContinueWith(task =>
                    {
                        if (!task.IsCanceled || task.IsFaulted)
                        {
                            if (task.Result.IsNull == true)
                            {
                                // not found, close
                                CompleteStage();
                            }
                            else
                            {
                                initCallback(long.Parse(task.Result));
                            }
                        }
                        else
                        {
                            Log.Error(task.Exception, "Error while initializing current events by persistent id");
                            FailStage(task.Exception);
                        }
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

                            // Complete stage if fromSequenceNr is higher than toSequenceNr
                            if (_toSequenceNr < _currentSequenceNr)
                            {
                                CompleteStage();
                            }

                            _redis.GetDatabase(_database).SortedSetRangeByScoreAsync(
                                key: _journalHelper.GetJournalKey(_persistenceId),
                                start: _currentSequenceNr,
                                stop: Math.Min(_currentSequenceNr + _max - 1, _toSequenceNr),
                                order: Order.Ascending).ContinueWith(task =>
                            {
                                if (!task.IsCanceled || task.IsFaulted)
                                {
                                    var deserializedEvents = task.Result.Select(e => _journalHelper.PersistentFromBytes(e)).ToList();
                                    _callback(deserializedEvents);
                                }
                                else
                                {
                                    Log.Error(task.Exception, "Error while querying events by persistence identifier");
                                    FailStage(task.Exception);
                                }
                            });
                        }
                        else
                        {
                            // buffer is non empty, let’s deliver buffered data
                            Deliver();
                        }
                        break;
                    default:
                        Log.Error($"Unexpected source state when querying: {_state}");
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
