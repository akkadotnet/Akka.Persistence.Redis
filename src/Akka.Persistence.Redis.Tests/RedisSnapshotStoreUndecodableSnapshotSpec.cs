// -----------------------------------------------------------------------
// <copyright file="RedisSnapshotStoreUndecodableSnapshotSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    /// <summary>
    /// A snapshot that can no longer be deserialized (its type was renamed or removed, or the
    /// value is corrupt) must only affect the operations that actually need it.
    /// </summary>
    [Collection("RedisSpec")]
    public class RedisSnapshotStoreUndecodableSnapshotSpec : Akka.TestKit.Xunit.TestKit
    {
        private const int Database = 7;
        private readonly RedisFixture _fixture;
        private readonly IActorRef _store;
        private readonly TestProbe _probe;

        public RedisSnapshotStoreUndecodableSnapshotSpec(ITestOutputHelper output, RedisFixture fixture)
            : base(Config(fixture), nameof(RedisSnapshotStoreUndecodableSnapshotSpec), output)
        {
            _fixture = fixture;
            RedisPersistence.Get(Sys);
            _store = Persistence.Instance.Apply(Sys).SnapshotStoreFor(null);
            _probe = CreateTestProbe();
        }

        private static Config Config(RedisFixture fixture) => ConfigurationFactory.ParseString($@"
            akka.persistence.snapshot-store.plugin = ""akka.persistence.snapshot-store.redis""
            akka.persistence.snapshot-store.redis {{
                configuration-string = ""{fixture.ConnectionString}""
                database = {Database}
            }}
            akka.actor {{
                serializers.undecodable = ""Akka.Persistence.Redis.Tests.RedisSnapshotStoreUndecodableSnapshotSpec+UndecodableSerializer, Akka.Persistence.Redis.Tests""
                serialization-bindings {{
                    ""Akka.Persistence.Redis.Tests.RedisSnapshotStoreUndecodableSnapshotSpec+UndecodableState, Akka.Persistence.Redis.Tests"" = undecodable
                }}
            }}")
            .WithFallback(RedisPersistence.DefaultConfig());

        [Fact(DisplayName = "Load must return the newest snapshot when an older one cannot be deserialized")]
        public void Load_must_ignore_older_undecodable_snapshot()
        {
            var pid = NewPersistenceId();
            Save(pid, 1, new UndecodableState());
            Save(pid, 5, "five");

            Load(pid, SnapshotSelectionCriteria.Latest).Snapshot.Snapshot.Should().Be("five");
            Load(pid, new SnapshotSelectionCriteria(long.MaxValue, DateTime.MaxValue, minSequenceNr: 5))
                .Snapshot.Metadata.SequenceNr.Should().Be(5);
        }

        [Fact(DisplayName = "Load must still fail when the selected snapshot cannot be deserialized")]
        public void Load_must_fail_on_selected_undecodable_snapshot()
        {
            // Silently falling back to an older snapshot could recover wrong state when the
            // events between the two snapshots have been deleted.
            var pid = NewPersistenceId();
            Save(pid, 1, "one");
            Save(pid, 5, new UndecodableState());

            _store.Tell(new LoadSnapshot(pid, SnapshotSelectionCriteria.Latest, long.MaxValue), _probe.Ref);
            _probe.ExpectMsg<LoadSnapshotFailed>().Cause.Should().BeOfType<SerializationException>();
        }

        [Fact(DisplayName = "DeleteSnapshots by sequence number must delete snapshots that cannot be deserialized")]
        public async Task Criteria_delete_by_sequence_number_must_not_deserialize()
        {
            var pid = NewPersistenceId();
            Save(pid, 1, new UndecodableState());
            Save(pid, 5, "five");

            _store.Tell(new DeleteSnapshots(pid, new SnapshotSelectionCriteria(1)), _probe.Ref);
            _probe.ExpectMsg<DeleteSnapshotsSuccess>();

            (await CountAsync(pid)).Should().Be(1);
            Load(pid, SnapshotSelectionCriteria.Latest).Snapshot.Metadata.SequenceNr.Should().Be(5);
        }

        [Fact(DisplayName = "DeleteSnapshots with a timestamp bound must not delete anything when a candidate cannot be deserialized")]
        public async Task Criteria_delete_with_timestamp_must_not_partially_delete()
        {
            var pid = NewPersistenceId();
            Save(pid, 1, new UndecodableState());
            Save(pid, 5, "five");

            _store.Tell(new DeleteSnapshots(pid, new SnapshotSelectionCriteria(10, DateTime.UtcNow.AddHours(1))), _probe.Ref);
            _probe.ExpectMsg<DeleteSnapshotsFailure>();

            (await CountAsync(pid)).Should().Be(2, "a failed delete must not have deleted anything");
        }

        [Fact(DisplayName = "DeleteSnapshot with a timestamp must not deserialize snapshots at other sequence numbers")]
        public async Task Metadata_delete_must_ignore_other_sequence_numbers()
        {
            var pid = NewPersistenceId();
            Save(pid, 1, new UndecodableState());
            var five = Save(pid, 5, "five");

            _store.Tell(new DeleteSnapshot(five), _probe.Ref);
            _probe.ExpectMsg<DeleteSnapshotSuccess>();

            (await CountAsync(pid)).Should().Be(1, "only the snapshot at seq 5 must have been deleted");
        }

        private static string NewPersistenceId() => "undecodable-" + Guid.NewGuid().ToString("N");

        private SnapshotMetadata Save(string pid, long sequenceNr, object snapshot)
        {
            var metadata = new SnapshotMetadata(pid, sequenceNr, DateTime.UtcNow);
            _store.Tell(new SaveSnapshot(metadata, snapshot), _probe.Ref);
            _probe.ExpectMsg<SaveSnapshotSuccess>();
            return metadata;
        }

        private LoadSnapshotResult Load(string pid, SnapshotSelectionCriteria criteria)
        {
            _store.Tell(new LoadSnapshot(pid, criteria, long.MaxValue), _probe.Ref);
            var result = _probe.ExpectMsg<object>();
            result.Should().BeOfType<LoadSnapshotResult>();
            return (LoadSnapshotResult)result;
        }

        /// <summary>
        /// Counts the stored snapshots on every server of the fixture: it has two standalone
        /// primaries, and the plugin's multiplexer writes to one of them.
        /// </summary>
        private async Task<long> CountAsync(string pid)
        {
            long total = 0;
            foreach (var endpoint in _fixture.ConnectionString.Split(','))
            {
                using var connection = await ConnectionMultiplexer.ConnectAsync(endpoint);
                total += await connection.GetDatabase(Database).SortedSetLengthAsync($"snapshot:{pid}");
            }

            return total;
        }

        public sealed class UndecodableState
        {
        }

        /// <summary>
        /// Writes <see cref="UndecodableState"/> but cannot read it back, like a snapshot whose
        /// type was removed in a refactoring.
        /// </summary>
        public sealed class UndecodableSerializer : Akka.Serialization.Serializer
        {
            public UndecodableSerializer(ExtendedActorSystem system) : base(system)
            {
            }

            public override int Identifier => 4711;

            public override bool IncludeManifest => false;

            public override byte[] ToBinary(object obj) => new byte[] { 1 };

            public override object FromBinary(byte[] bytes, Type type) =>
                throw new SerializationException("The snapshot type no longer exists.");
        }
    }
}
