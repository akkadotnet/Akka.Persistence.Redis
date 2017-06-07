//-----------------------------------------------------------------------
// <copyright file="PerformanceActors.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka;
using Akka.Actor;
using Akka.Persistence;
using MessagePack;

namespace CustomSerialization.MsgPack
{
    public sealed class Init
    {
        public static Init Instance { get; } = new Init();
        private Init() { }
    }

    public sealed class Finish
    {
        public static Finish Instance { get; } = new Finish();
        private Finish() { }
    }

    public sealed class Done
    {
        public static Done Instance { get; } = new Done();
        private Done() { }
    }

    public sealed class Finished
    {
        public Finished(long state)
        {
            State = state;
        }

        public long State { get; }
    }

    public sealed class Store
    {
        public Store(int value)
        {
            Value = value;
        }

        public int Value { get; }
    }

    [MessagePackObject]
    public sealed class Stored
    {
        [SerializationConstructor]
        public Stored(int value)
        {
            Value = value;
        }

        [Key(0)]
        public int Value { get; }
    }

    public class PerformanceTestActor : UntypedPersistentActor
    {
        private long _state = 0L;

        public PerformanceTestActor(string persistenceId)
        {
            PersistenceId = persistenceId;
        }

        public sealed override string PersistenceId { get; }

        protected override void OnCommand(object message)
        {
            switch (message)
            {
                case Init i:
                    var sender = Sender;
                    Persist(new Stored(0), s =>
                    {
                        _state += s.Value;
                        sender.Tell(Done.Instance);
                    });
                    break;
                case Store store:
                    Persist(new Stored(store.Value), s =>
                    {
                        _state += s.Value;
                    });
                    break;
                case Finish _:
                    Sender.Tell(new Finished(_state));
                    break;
            }
        }

        protected override void OnRecover(object message)
        {
            switch (message)
            {
                case Stored s:
                    _state += s.Value;
                    break;
            }
        }
    }
}