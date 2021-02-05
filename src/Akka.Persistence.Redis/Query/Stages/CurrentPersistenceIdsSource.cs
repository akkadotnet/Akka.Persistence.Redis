//-----------------------------------------------------------------------
// <copyright file="CurrentPersistenceIdsSource.cs" company="Akka.NET Project">
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
            return new CurrentPersistenceIdsLogic(this);
        }

        private sealed class CurrentPersistenceIdsLogic : GraphStageLogic
        {
            private bool _start = true;
            private long _index = 0L;
            private readonly Queue<string> _buffer = new Queue<string>();
            private readonly Outlet<string> _outlet;
            private readonly JournalHelper _journalHelper;
            private readonly bool _isClustered;
            private readonly IDatabase _database;

            //public CurrentPersistenceIdsLogic(IDatabase redisDatabase, ExtendedActorSystem system, Outlet<string> outlet, Shape shape) : base(shape)
            public CurrentPersistenceIdsLogic(CurrentPersistenceIdsSource parent) : base(parent.Shape)
            {
                _outlet = parent.Outlet;
                _journalHelper = new JournalHelper(parent._system, parent._system.Settings.Config.GetString("akka.persistence.journal.redis.key-prefix"));
                _isClustered = parent._redis.IsClustered();
                _database = parent._redis.GetDatabase(parent._database);

                SetHandler(_outlet, onPull: () =>
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
                                Log.Error(e, "Error while querying persistence identifiers");
                                FailStage(e);
                            }

                            // deliver element
                            Deliver();
                        });

                        callback(_database.SetScan(_journalHelper.GetIdentifiersKey(), cursor: _index));
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
        }
    }
}
