//-----------------------------------------------------------------------
// <copyright file="CurrentPersistenceIdsSource.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
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
    internal class CurrentPersistenceIdsSource : GraphStage<SourceShape<string>>
    {
        private readonly ConnectionMultiplexer _redis;
        private readonly int _database;
        private readonly ExtendedActorSystem _system;

        public CurrentPersistenceIdsSource(ConnectionMultiplexer redis, int database, ExtendedActorSystem system)
        {
            _redis = redis;
            _database = database;
            _system = system;
        }

        public Outlet<string> Outlet { get; } = new Outlet<string>(nameof(CurrentPersistenceIdsSource));

        public override SourceShape<string> Shape => new SourceShape<string>(Outlet);

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new CurrentPersistenceIdsLogic(_redis.GetDatabase(_database), _system, Outlet, Shape);
        }

        private sealed class CurrentPersistenceIdsLogic : GraphStageLogic
        {
            private bool _start = true;
            private long _index = 0L;
            private readonly Queue<string> _buffer = new Queue<string>();
            private readonly Outlet<string> _outlet;
            private readonly string _keyPrefix;

            public CurrentPersistenceIdsLogic(IDatabase redisDatabase, ExtendedActorSystem system, Outlet<string> outlet, Shape shape) : base(shape)
            {
                _outlet = outlet;
                _keyPrefix = system.Settings.Config.GetString("akka.persistence.journal.redis.key-prefix");

                SetHandler(outlet, onPull: () =>
                {
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
                                // TODO: log.Error(e, "Error while querying persistence identifiers")
                                FailStage(e);
                            }

                            // deliver element
                            Deliver();
                        });

                        callback(redisDatabase.SetScan(GetIdentifiersKey(), cursor: _index));
                    }
                    else
                    {
                        Deliver();
                    }
                });
            }

            private void Deliver()
            {
                if (_buffer.Count > 0)
                {
                    var elem = _buffer.Dequeue();
                    Push(_outlet, elem);
                }
                else
                {
                    // we're done here, goodbye
                    CompleteStage();
                }
            }

            private string GetIdentifiersKey() => $"{_keyPrefix}journal:persistenceIds";
        }
    }
}
