using System;
using Akka.Streams.Stage;
using System.Collections.Generic;
using Akka.Persistence.Redis.Journal;
using Akka.Streams;
using Akka.Util.Internal;
using StackExchange.Redis;

namespace Akka.Persistence.Redis.Query.Stages
{
    internal class CurrentPersistenceIdsSource : GraphStage<SourceShape<string>>
    {
        private readonly ConnectionMultiplexer _redis;
        private int _database;

        public CurrentPersistenceIdsSource(ConnectionMultiplexer redis, int database)
        {
            _redis = redis;
            _database = database;
        }

        public Outlet<string> Outlet { get; } = new Outlet<string>(nameof(CurrentPersistenceIdsSource));

        public override SourceShape<string> Shape => new SourceShape<string>(Outlet);

        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
        {
            return new CurrentPersistenceIdsLogic(_redis.GetDatabase(_database), Outlet, Shape);
        }

        private sealed class CurrentPersistenceIdsLogic : GraphStageLogic
        {
            private bool _start = true;
            private long _index = 0L;
            private readonly Queue<string> _buffer = new Queue<string>();
            private readonly Outlet<string> _outlet;

            public CurrentPersistenceIdsLogic(IDatabase redisDatabase, Outlet<string> outlet, Shape shape) : base(shape)
            {
                _outlet = outlet;

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
                            foreach (var item in data)
                            {
                                _buffer.Enqueue(item);
                            }

                            // deliver element
                            Deliver();
                        });

                        try
                        {
                            var cursor = redisDatabase.SetScan(RedisUtils.GetIdentifiersKey(), cursor: _index);
                            callback(cursor);
                        }
                        catch (Exception e)
                        {
                            // TODO: log it
                            FailStage(e);
                        }
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
