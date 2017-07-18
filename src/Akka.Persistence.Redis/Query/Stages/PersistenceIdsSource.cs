//-----------------------------------------------------------------------
// <copyright file="PersistenceIdsSource.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Streams.Stage;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Persistence.Redis.Journal;
using Akka.Streams;
using Akka.Util.Internal;
using StackExchange.Redis;

namespace Akka.Persistence.Redis.Query.Stages
{
    internal class PersistenceIdsSource : GraphStage<SourceShape<string>>
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly int _database;
        private readonly ExtendedActorSystem _system;

        public PersistenceIdsSource(ConnectionMultiplexer redis, int database, ExtendedActorSystem system)
        {
            _redis = redis;
            _database = database;
            _system = system;
        }

        public Outlet<string> Outlet { get; } = new Outlet<string>(nameof(PersistenceIdsSource));

        public override SourceShape<string> Shape => new SourceShape<string>(Outlet);

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new PersistenceIdsLogic(_redis, _database, _system, Outlet, Shape);
        }

        private class PersistenceIdsLogic : GraphStageLogic
        {
            private bool _start = true;
            private long _index = 0;
            private readonly Queue<string> _buffer = new Queue<string>();
            private bool _downstreamWaiting = false;
            private ISubscriber _subscription;

            private readonly Outlet<string> _outlet;
            private readonly ConnectionMultiplexer _redis;
            private readonly int _database;
            private readonly JournalHelper _journalHelper;

            public PersistenceIdsLogic(ConnectionMultiplexer redis, int database, ExtendedActorSystem system, Outlet<string> outlet, Shape shape) : base(shape)
            {
                _redis = redis;
                _database = database;
                _journalHelper = new JournalHelper(system, system.Settings.Config.GetString("akka.persistence.journal.redis.key-prefix"));
                _outlet = outlet;

                SetHandler(outlet, onPull: () =>
                {
                    _downstreamWaiting = true;
                    if (_buffer.Count == 0 && (_start || _index > 0))
                    {
                        var callback = GetAsyncCallback<IEnumerable<RedisValue>>(data =>
                        {
                            // save the index for further initialization if needed
                            _index = data.AsInstanceOf<IScanningCursor>().Cursor;

                            // it is not the start anymore
                            _start = false;

                            // enqueue received data
                            try
                            {
                                foreach (var item in data)
                                {
                                    _buffer.Enqueue(item);
                                }
                            }
                            catch (Exception e)
                            {
                                Log.Error(e, "Error while querying persistence identifiers");
                                FailStage(e);
                            }

                            // deliver element
                            Deliver();
                        });

                        callback(_redis.GetDatabase(_database).SetScan(_journalHelper.GetIdentifiersKey(), cursor: _index));
                    }
                    else if (_buffer.Count == 0)
                    {
                        // wait for asynchornous notification and mark dowstream
                        // as waiting for data
                    }
                    else
                    {
                        Deliver();
                    }
                });
            }

            public override void PreStart()
            {
                var callback = GetAsyncCallback<(RedisChannel channel, string bs)>(data =>
                {
                    if (data.channel.Equals(_journalHelper.GetIdentifiersChannel()))
                    {
                        Log.Debug("Message received");

                        // enqueue the element
                        _buffer.Enqueue(data.bs);

                        // deliver element
                        Deliver();
                    }
                    else
                    {
                        Log.Debug($"Message from unexpected channel: {data.channel}");
                    }
                });

                _subscription = _redis.GetSubscriber();
                _subscription.Subscribe(_journalHelper.GetIdentifiersChannel(), (channel, value) =>
                {
                    callback.Invoke((channel, value));
                });
            }

            public override void PostStop()
            {
                _subscription?.UnsubscribeAll();
            }

            private void Deliver()
            {
                if (_downstreamWaiting)
                {
                    _downstreamWaiting = false;
                    var elem = _buffer.Dequeue();
                    Push(_outlet, elem);
                }
            }
        }
    }
}
