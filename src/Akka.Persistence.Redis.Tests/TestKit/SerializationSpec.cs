using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.TestKit;
using Akka.TestKit;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests.TestKit
{
    public abstract class SerializationSpec : PluginSpec
    {
        protected SerializationSpec(Config config, ITestOutputHelper output) : base(config, null, output)
        {
        }

        protected IActorRef Journal => Extension.JournalFor(null);
        protected IActorRef SnapshotStore => Extension.SnapshotStoreFor(null);

        [Fact]
        public void AtLeastOnceDeliveryActor_should_serialize_AtLeastOnceDeliverySnapshot()
        {
            var probe = CreateTestProbe();

            var deliveries = new UnconfirmedDelivery[]
            {
                new UnconfirmedDelivery(1, ActorPath.Parse("akka://my-sys/user/service-a/worker1"), "test1"),
                new UnconfirmedDelivery(2, ActorPath.Parse("akka://my-sys/user/service-a/worker2"), "test2"),
            };
            var atLeastOnceDeliverySnapshot = new AtLeastOnceDeliverySnapshot(2L, deliveries);

            var metadata = new SnapshotMetadata(Pid, 2);
            SnapshotStore.Tell(new SaveSnapshot(metadata, atLeastOnceDeliverySnapshot), probe.Ref);
            probe.ExpectMsg<SaveSnapshotSuccess>();

            SnapshotStore.Tell(new LoadSnapshot(Pid, SnapshotSelectionCriteria.Latest, long.MaxValue), probe.Ref);
            probe.ExpectMsg<LoadSnapshotResult>(s => s.Snapshot.Snapshot.Equals(atLeastOnceDeliverySnapshot));
        }

        [Fact]
        public void PersistenceFSM_should_serialize_StateChangeEvent()
        {
            var probe = CreateTestProbe();
            var stateChangeEvent = new Akka.Persistence.Fsm.PersistentFSMBase<string, string, string>.StateChangeEvent("init", TimeSpan.FromSeconds(342));
            
            var messages = new List<AtomicWrite>()
            {
                new AtomicWrite(new Persistent(stateChangeEvent, 1, Pid))
            };

            Journal.Tell(new WriteMessages(messages, probe.Ref, ActorInstanceId));
            probe.ExpectMsg<WriteMessagesSuccessful>();
            probe.ExpectMsg<WriteMessageSuccess>(m => m.ActorInstanceId == ActorInstanceId && m.Persistent.PersistenceId == Pid);

            Journal.Tell(new ReplayMessages(0, 1, long.MaxValue, Pid, probe.Ref));
            probe.ExpectMsg<ReplayedMessage>();
            probe.ExpectMsg<RecoverySuccess>();
        }

        [Fact(Skip = "Not implemented yet")]
        public void PersistenceFSM_should_serialize_PersistentFSMSnapshot()
        {

        }
    }
}
