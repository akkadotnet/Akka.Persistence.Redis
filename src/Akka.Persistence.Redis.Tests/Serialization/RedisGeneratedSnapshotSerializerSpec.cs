// -----------------------------------------------------------------------
// <copyright file="RedisGeneratedSnapshotSerializerSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Serialization.Proto.Msg;
using Akka.Serialization.V2;
using Akka.TestKit;
using Akka.TestKit.Xunit;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Serialization
{
    public interface IGeneratedSnapshotProtocol
    {
    }

    [AkkaSerializable(Manifest = "explicit-snapshot-v1")]
    public sealed record GeneratedSnapshotState(
        [property: AkkaField(0)] string Name,
        [property: AkkaField(1)] int Count) : IGeneratedSnapshotProtocol;

    [AkkaSerializer<IGeneratedSnapshotProtocol>("redis-generated-snapshot", GeneratedSnapshotSerializer.SerializerIdValue)]
    public sealed partial class GeneratedSnapshotSerializer : AkkaSerializer
    {
        public const int SerializerIdValue = 190884;

        public static partial SerializerRegistration CreateRegistration();
    }

    /// <summary>
    /// Regression test for akkadotnet/akka.net#8784. Snapshots of types handled by a source-generated
    /// (SerializerV2) serializer must keep the serializer's own manifest and load again.
    /// </summary>
    [Collection("RedisSpec")]
    public class RedisGeneratedSnapshotSerializerSpec : Akka.TestKit.Xunit.TestKit
    {
        private const int Database = 7;
        private readonly RedisFixture _fixture;

        public RedisGeneratedSnapshotSerializerSpec(ITestOutputHelper output, RedisFixture fixture)
            : base(BuildConfig(fixture), nameof(RedisGeneratedSnapshotSerializerSpec), output)
        {
            _fixture = fixture;
        }

        [Fact(DisplayName = "Should_StoreExplicitManifestAndLoadSnapshot_When_SnapshotTypeUsesSourceGeneratedSerializer")]
        public async Task Should_StoreExplicitManifestAndLoadSnapshot_When_SnapshotTypeUsesSourceGeneratedSerializer()
        {
            var persistenceId = "generated-snapshot-" + Guid.NewGuid().ToString("N");
            var metadata = new SnapshotMetadata(persistenceId, 1L, DateTime.UtcNow);
            var state = new GeneratedSnapshotState("order-42", 7);

            using var observer = await ConnectionMultiplexer.ConnectAsync(_fixture.ConnectionString);
            var db = observer.GetDatabase(Database);
            var key = $"snapshot:{persistenceId}";

            try
            {
                // save through the snapshot store
                var store = Persistence.Instance.Apply(Sys).SnapshotStoreFor(null);
                store.Tell(new SaveSnapshot(metadata, state), TestActor);
                await ExpectMsgAsync<SaveSnapshotSuccess>(TimeSpan.FromSeconds(10));

                // the stored payload must carry the serializer's explicit manifest, not a CLR type name
                var stored = (await db.SortedSetRangeByRankAsync(key)).Single();
                var message = SnapshotMessage.Parser.ParseFrom((byte[])stored!);
                message.Payload.SerializerId.Should().Be(GeneratedSnapshotSerializer.SerializerIdValue);
                message.Payload.PayloadManifest.ToStringUtf8().Should().Be("explicit-snapshot-v1");

                // a fresh ActorSystem must read the snapshot back
                var secondSystem = ActorSystem.Create(nameof(RedisGeneratedSnapshotSerializerSpec) + "-second", BuildConfig(_fixture));
                try
                {
                    var probe = new TestProbe(secondSystem, new XunitAssertions());
                    var secondStore = Persistence.Instance.Apply(secondSystem).SnapshotStoreFor(null);
                    secondStore.Tell(new LoadSnapshot(persistenceId, SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);

                    var result = await probe.ExpectMsgAsync<LoadSnapshotResult>(TimeSpan.FromSeconds(10));
                    result.Snapshot.Should().NotBeNull();
                    result.Snapshot!.Metadata.SequenceNr.Should().Be(1L);
                    result.Snapshot.Snapshot.Should().Be(state);
                }
                finally
                {
                    await secondSystem.Terminate();
                }
            }
            finally
            {
                await db.KeyDeleteAsync(key);
            }
        }

        private static Config BuildConfig(RedisFixture fixture) =>
            ConfigurationFactory.ParseString($@"
                akka.loglevel = INFO
                akka.test.single-expect-default = 5s
                akka.persistence {{
                    snapshot-store {{
                        plugin = ""akka.persistence.snapshot-store.redis""
                        redis {{
                            class = ""Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            configuration-string = ""{fixture.ConnectionString}""
                            database = {Database}
                        }}
                    }}
                }}
                akka.actor {{
                    serializers {{
                        generated-snapshot = ""Akka.Persistence.Redis.Tests.Serialization.GeneratedSnapshotSerializer, Akka.Persistence.Redis.Tests""
                    }}
                    serialization-bindings {{
                        ""Akka.Persistence.Redis.Tests.Serialization.IGeneratedSnapshotProtocol, Akka.Persistence.Redis.Tests"" = generated-snapshot
                    }}
                }}")
                .WithFallback(RedisPersistence.DefaultConfig());
    }
}
