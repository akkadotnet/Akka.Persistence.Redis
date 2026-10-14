// -----------------------------------------------------------------------
// <copyright file="RedisJournalConditionalWriteSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Redis.Journal;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Persistence.Redis.Cluster.Tests
{
    /// <summary>
    /// A journal write must not be applied when events with the same or higher sequence numbers
    /// have already been stored. That is what happens when a write times out on the client but
    /// is still applied by the server after a new incarnation of the persistent actor, on
    /// another connection, has recovered and written the same sequence numbers. The cluster
    /// variant checks that the conditional write runs on the persistence id's hash slot.
    /// </summary>
    [Collection("RedisClusterSpec")]
    public class RedisJournalConditionalWriteSpec : Akka.TestKit.Xunit.TestKit
    {
        private readonly IActorRef _journal;
        private readonly TestProbe _probe;

        public RedisJournalConditionalWriteSpec(ITestOutputHelper output, RedisClusterFixture fixture)
            : base(Config(fixture), nameof(RedisJournalConditionalWriteSpec), output)
        {
            RedisPersistence.Get(Sys);
            _journal = Persistence.Instance.Apply(Sys).JournalFor(null);
            _probe = CreateTestProbe();
        }

        // The replay filter is off so that replays show exactly what is stored.
        private static Config Config(RedisClusterFixture fixture) => ConfigurationFactory.ParseString($@"
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                configuration-string = ""{fixture.ConnectionString}""
                replay-filter.mode = off
            }}
            akka.test.single-expect-default = 10s")
            .WithFallback(RedisPersistence.DefaultConfig());

        [Fact(DisplayName = "A late write of a sequence number that another writer already stored must fail and not be stored")]
        public void Late_write_with_duplicate_sequence_number_must_fail()
        {
            var pid = NewPersistenceId();
            Write(pid, "B", 1, 2).Should().BeNull();

            var failure = Write(pid, "A", 2, 2);

            failure.Should().BeOfType<RedisJournalWriteConflictException>();
            var (events, highest) = Replay(pid);
            events.Should().Equal("B-1", "B-2");
            highest.Should().Be(2);
        }

        [Fact(DisplayName = "A late write must not move the highest sequence number backwards")]
        public void Late_write_must_not_move_highest_sequence_number_backwards()
        {
            var pid = NewPersistenceId();
            Write(pid, "B", 1, 5).Should().BeNull();

            Write(pid, "A", 4, 4).Should().BeOfType<RedisJournalWriteConflictException>();

            var (events, highest) = Replay(pid);
            highest.Should().Be(5, "an acknowledged event must stay visible to recovery");
            events.Should().Equal("B-1", "B-2", "B-3", "B-4", "B-5");
        }

        private static string NewPersistenceId() => "conditional-write-" + Guid.NewGuid().ToString("N");

        /// <summary>
        /// Writes events [from..to] in one atomic write, as writer <paramref name="writer"/>.
        /// Returns null on success, the rejection or failure cause otherwise.
        /// </summary>
        private Exception? Write(string pid, string writer, long from, long to)
        {
            var events = Enumerable.Range((int)from, (int)(to - from + 1))
                .Select(seq => (IPersistentRepresentation)new Persistent(
                    $"{writer}-{seq}", seq, pid, string.Empty, false, ActorRefs.NoSender, writer))
                .ToImmutableList();
            _journal.Tell(new WriteMessages(new[] { new AtomicWrite(events) }, _probe.Ref, 1));

            switch (_probe.ExpectMsg<object>())
            {
                case WriteMessagesSuccessful _:
                    var results = Enumerable.Range(0, events.Count).Select(_ => _probe.ExpectMsg<object>()).ToArray();
                    var rejected = results.OfType<WriteMessageRejected>().FirstOrDefault();
                    if (rejected is not null)
                        return rejected.Cause;
                    results.Should().AllBeOfType<WriteMessageSuccess>();
                    return null;
                case WriteMessagesFailed failed:
                    for (var i = 0; i < events.Count; i++)
                        _probe.ExpectMsg<WriteMessageFailure>();
                    return failed.Cause;
                case var other:
                    throw new Exception($"Unexpected reply {other}");
            }
        }

        private (IReadOnlyList<string> Events, long Highest) Replay(string pid)
        {
            _journal.Tell(new ReplayMessages(1, long.MaxValue, long.MaxValue, pid, _probe.Ref));
            var events = new List<string>();
            while (true)
            {
                switch (_probe.ExpectMsg<object>())
                {
                    case ReplayedMessage replayed:
                        events.Add((string)replayed.Persistent.Payload);
                        break;
                    case RecoverySuccess success:
                        return (events, success.HighestSequenceNr);
                    case var other:
                        throw new Exception($"Unexpected reply {other}");
                }
            }
        }
    }
}
