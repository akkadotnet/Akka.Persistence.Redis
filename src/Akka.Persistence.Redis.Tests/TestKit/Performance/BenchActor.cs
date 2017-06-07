using System;
using System.Collections.Generic;
using Akka.Actor;

namespace Akka.Persistence.TestKit.Performance
{
    internal class ResetCounter
    {
        public static ResetCounter Instance { get; } = new ResetCounter();
        private ResetCounter() { }
    }

    public class Cmd
    {
        public Cmd(string mode, int payload)
        {
            Mode = mode;
            Payload = payload;
        }

        public string Mode { get; }

        public int Payload { get; }
    }

    internal class BenchActor : UntypedPersistentActor
    {
        private int _counter = 0;
        private const int BatchSize = 50;
        private List<Cmd> _batch = new List<Cmd>(BatchSize);

        public BenchActor(string persistenceId, IActorRef replyTo, int replyAfter)
        {
            PersistenceId = persistenceId;
            ReplyTo = replyTo;
            ReplyAfter = replyAfter;
        }

        public override string PersistenceId { get; }
        public IActorRef ReplyTo { get; }
        public int ReplyAfter { get; }

        protected override void OnRecover(object message)
        {
            switch (message)
            {
                case Cmd c:
                    _counter++;
                    if (c.Payload != _counter) throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{c.Payload}]");
                    if (_counter == ReplyAfter) ReplyTo.Tell(c.Payload);
                    break;
            }
        }

        protected override void OnCommand(object message)
        {
            switch (message)
            {
                case Cmd c when c.Mode == "p":
                    Persist(c, d =>
                    {
                        _counter += 1;
                        if (d.Payload != _counter) throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                        if (_counter == ReplyAfter) ReplyTo.Tell(d.Payload);
                    });
                    break;
                case Cmd c when c.Mode == "pb":
                    _batch.Add(c);

                    if (_batch.Count % BatchSize == 0)
                    {
                        PersistAll(_batch, d =>
                        {
                            _counter += 1;
                            if (d.Payload != _counter) throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                            if (_counter == ReplyAfter) ReplyTo.Tell(d.Payload);
                        });
                        _batch = new List<Cmd>(BatchSize);
                    }
                    break;
                case Cmd c when c.Mode == "pa":
                    PersistAsync(c, d =>
                    {
                        _counter += 1;
                        if (d.Payload != _counter) throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                        if (_counter == ReplyAfter) ReplyTo.Tell(d.Payload);
                    });
                    break;
                case Cmd c when c.Mode == "pba":
                    _batch.Add(c);

                    if (_batch.Count % BatchSize == 0)
                    {
                        PersistAllAsync(_batch, d =>
                        {
                            _counter += 1;
                            if (d.Payload != _counter) throw new ArgumentException($"Expected to receive [{_counter}] yet got: [{d.Payload}]");
                            if (_counter == ReplyAfter) ReplyTo.Tell(d.Payload);
                        });
                        _batch = new List<Cmd>(BatchSize);
                    }
                    break;
                case ResetCounter _:
                    _counter = 0;
                    break;
            }
        }
    }
}