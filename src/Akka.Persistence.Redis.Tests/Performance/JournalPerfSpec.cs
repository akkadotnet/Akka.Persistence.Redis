using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests.Performance
{
    public abstract class JournalPerfSpec : Akka.TestKit.Xunit2.TestKit
    {
        private TestProbe testProbe;

        // Number of messages sent to the PersistentActor under test for each test iteration
        private const int EventsCount = 10 * 1000;

        // Number of measurement iterations each test will be run.
        private const int MeasurementIterations = 10;

        private IReadOnlyList<int> Commands => Enumerable.Range(1, EventsCount).ToList();

        private TimeSpan ExpectDuration = TimeSpan.FromSeconds(10);

        protected JournalPerfSpec(Config config, ITestOutputHelper output) : base(config, null, output)
        {
            testProbe = CreateTestProbe();
        }

        internal IActorRef BenchActor(string pid, int replyAfter)
        {
            return Sys.ActorOf(Props.Create(() => new BenchActor(pid, testProbe, EventsCount)));;
        }

        internal void FeedAndExpectLast(IActorRef actor, string mode, IReadOnlyList<int> commands)
        {
            commands.ForEach(c => actor.Tell(new Cmd(mode, c)));
            testProbe.ExpectMsg(commands.Last(), ExpectDuration);
        }

        internal void Measure(Func<TimeSpan, string> msg, Action block)
        {
            var measurements = new List<TimeSpan>(MeasurementIterations);

            block(); //warm-up

            int i = 0;
            while (i < MeasurementIterations)
            {
                var sw = Stopwatch.StartNew();
                block();
                sw.Stop();
                measurements.Add(sw.Elapsed);
                Output.WriteLine(msg(sw.Elapsed));
                i++;
            }

            double avgTime = measurements.Select(c => c.TotalMilliseconds).Sum() / MeasurementIterations;
            double msgPerSec = (EventsCount / avgTime) * 1000;

            Output.WriteLine($"Average time: {avgTime} ms, {msgPerSec} msg/sec");
        }

        [Fact]
        public void PersistenceActor_performance_must_measure_Persist()
        {
            var p1 = BenchActor("PersistPid", EventsCount);
            Measure(d => $"Persist()-ing {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
                FeedAndExpectLast(p1, "p", Commands);
                p1.Tell(ResetCounter.Instance);
            });
        }

        [Fact]
        public void PersistenceActor_performance_must_measure_PersistAll()
        {
            var p1 = BenchActor("PersistAllPid", EventsCount);
            Measure(d => $"PersistAll()-ing {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
                FeedAndExpectLast(p1, "pb", Commands);
                p1.Tell(ResetCounter.Instance);
            });
        }

        [Fact]
        public void PersistenceActor_performance_must_measure_PersistAsync()
        {
            var p1 = BenchActor("PersistAsyncPid", EventsCount);
            Measure(d => $"PersistAsync()-ing {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
                FeedAndExpectLast(p1, "pa", Commands);
                p1.Tell(ResetCounter.Instance);
            });
        }

        [Fact]
        public void PersistenceActor_performance_must_measure_PersistAllAsync()
        {
            var p1 = BenchActor("PersistAllAsyncPid", EventsCount);
            Measure(d => $"PersistAllAsync()-ing {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
                FeedAndExpectLast(p1, "pba", Commands);
                p1.Tell(ResetCounter.Instance);
            });
        }

        [Fact]
        public void PersistenceActor_performance_must_measure_Recovering()
        {
            var p1 = BenchActor("PersistRecoverPid", EventsCount);

            FeedAndExpectLast(p1, "p", Commands);
            Measure(d => $"Recovering {EventsCount} took {d.TotalMilliseconds} ms", () =>
            {
                BenchActor("PersistRecoverPid", EventsCount);
                testProbe.ExpectMsg(Commands.Last(), ExpectDuration);
            });
        }
    }
}
