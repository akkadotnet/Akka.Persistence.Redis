//-----------------------------------------------------------------------
// <copyright file="EventsByTagSource.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Pattern;
using Akka.Persistence.Query;
using Akka.Persistence.Redis.Journal;
using Akka.Streams;
using Akka.Streams.Stage;
using Akka.Util.Internal;
using StackExchange.Redis;

namespace Akka.Persistence.Redis.Query.Stages
{
    internal class EventsByTagSource : GraphStage<SourceShape<EventEnvelope>>
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly int _database;
        private readonly Config _config;
        private readonly string _tag;
        private readonly long _offset;
        private readonly ExtendedActorSystem _system;
        private readonly bool _live;

        public EventsByTagSource(ConnectionMultiplexer redis, int database, Config config, string tag, long offset, ExtendedActorSystem system, bool live)
        {
            _redis = redis;
            _database = database;
            _config = config;
            _tag = tag;
            _offset = offset;
            _system = system;
            _live = live;

            Outlet = live
                ? new Outlet<EventEnvelope>("EventsByTagSource")
                : new Outlet<EventEnvelope>("CurrentEventsByTagSource");

            Shape = new SourceShape<EventEnvelope>(Outlet);
        }

        internal Outlet<EventEnvelope> Outlet { get; }

        public override SourceShape<EventEnvelope> Shape { get; }

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new EventsByTagLogic(_redis, _database, _config, _system, _tag, _offset, _live, Outlet, Shape);
        }

        private enum State
        {
            Idle = 0,
            Querying = 1,
            NotifiedWhenQuerying = 2,
            WaitingForNotification = 3
        }

        private class EventsByTagLogic : GraphStageLogic
        {
            private State _state = State.Idle;
            private readonly Queue<EventEnvelope> _buffer = new Queue<EventEnvelope>();
            private ISubscriber _subscription;
            private readonly int _max;
            private readonly JournalHelper _journalHelper;
            private Action<(int, IReadOnlyList<(string, IPersistentRepresentation)>)> _callback;

            private readonly Outlet<EventEnvelope> _outlet;
            private readonly ConnectionMultiplexer _redis;
            private readonly int _database;
            private readonly ActorSystem _system;
            private readonly long _offset;
            private readonly string _tag;
            private readonly bool _live;

            private long _currentOffset;

            public EventsByTagLogic(
                ConnectionMultiplexer redis,
                int database,
                Config config,
                ActorSystem system,
                string tag,
                long offset,
                bool live,
                Outlet<EventEnvelope> outlet, Shape shape) : base(shape)
            {
                _outlet = outlet;
                _redis = redis;
                _database = database;
                _system = system;
                _offset = offset;
                _tag = tag;
                _live = live;

                _max = config.GetInt("max-buffer-size");
                _journalHelper = new JournalHelper(system, system.Settings.Config.GetString("akka.persistence.journal.redis.key-prefix"));

                _currentOffset = offset > 0 ? offset + 1 : 0;

                SetHandler(outlet, Query);
            }

            public override void PreStart()
            {
                _callback = GetAsyncCallback<(int, IReadOnlyList<(string, IPersistentRepresentation)>)>(raw =>
                {
                    var nb = raw.Item1;
                    var events = raw.Item2;

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
                                Log.Error($"Unexpected source state: {_state}");
                                FailStage(new IllegalStateException($"Unexpected source state: {_state}"));
                                break;
                        }
                    }
                    else
                    {
                        var evts = events.ZipWithIndex().Select(c =>
                        {
                            var repr = c.Key.Item2;
                            if (repr != null && !repr.IsDeleted)
                            {
                                return new EventEnvelope(new Sequence(_currentOffset + c.Value), repr.PersistenceId, repr.SequenceNr, repr.Payload);
                            }

                            return null;
                        }).ToList();

                        _currentOffset += nb;
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
                        if (data.channel.Equals(_journalHelper.GetTagsChannel()) && data.bs == _tag)
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
                        else if (data.channel.Equals(_journalHelper.GetTagsChannel()))
                        {
                            // ignore other tags
                        }
                        else
                        {
                            Log.Debug($"Message from unexpected channel: {data.channel}");
                        }
                    });

                    _subscription = _redis.GetSubscriber();
                    _subscription.Subscribe(_journalHelper.GetTagsChannel(), (channel, value) =>
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

                            var refs = _redis.GetDatabase(_database).ListRange(
                                _journalHelper.GetTagKey(_tag),
                                _currentOffset,
                                _currentOffset + _max - 1);

                            var trans = _redis.GetDatabase(_database).CreateTransaction();

                            var events = refs.Select(bytes =>
                            {
                                var (sequenceNr, persistenceId) = bytes.Deserialize();
                                return trans.SortedSetRangeByScoreAsync(_journalHelper.GetJournalKey(persistenceId), sequenceNr, sequenceNr);
                            }).ToList();

                            var f = trans.Execute();

                            var callbackEvents = events.Select(bytes =>
                            {
                                var result = _journalHelper.PersistentFromBytes(bytes.Result.FirstOrDefault());
                                return (result.PersistenceId, result);
                            }).ToList();

                            _callback((refs.Length, callbackEvents));

                            // TODO: FailStage on Error
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
            }
        }
    }

    internal static class EventRefDeserializer
    {
        private static readonly Regex EventRef = new Regex("(\\d+):(.*)");

        public static (long, string) Deserialize(this RedisValue value)
        {
            var match = EventRef.Match(value);

            if (match.Success)
                return (long.Parse(match.Groups[1].Value), match.Groups[2].Value);
            else
                throw new SerializationException($"Unable to deserializer {value}");
        }

        public static Dictionary<T, int> ZipWithIndex<T>(this IEnumerable<T> collection)
        {
            var i = 0;
            var dict = new Dictionary<T, int>();
            foreach (var item in collection)
            {
                dict.Add(item, i);
                i++;
            }
            return dict;
        }
    }
}
