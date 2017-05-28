using Akka.Actor;
using Akka.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests.TestKit
{
    public abstract class SerializationSpec : Akka.TestKit.Xunit2.TestKit
    {
        #region Actors
        public class Msg
        {
            public Msg(long deliveryId, string message)
            {
                DeliveryId = deliveryId;
                Message = message;
            }

            public long DeliveryId { get; }

            public string Message { get; }
        }

        public class Confirm
        {
            public Confirm(long deliveryId)
            {
                DeliveryId = deliveryId;
            }

            public long DeliveryId { get; }
        }

        public class MsgSent
        {
            public MsgSent(string message)
            {
                Message = message;
            }

            public string Message { get; }
        }

        public class MsgConfirmed
        {
            public MsgConfirmed(long deliveryId)
            {
                DeliveryId = deliveryId;
            }

            public long DeliveryId { get; }
        }

        public class SourceActor : AtLeastOnceDeliveryActor
        {
            private readonly IActorRef _replyTo;
            private readonly IActorRef _destionationActor = Context.ActorOf<DestinationActor>();

            public SourceActor(string persistenceId, IActorRef replyTo)
            {
                _replyTo = replyTo;
                PersistenceId = persistenceId;
            }

            protected override bool ReceiveRecover(object message)
            {
                if (message is SnapshotOffer offer && offer.Snapshot is AtLeastOnceDeliverySnapshot atLeast)
                {
                    SetDeliverySnapshot(atLeast);
                }
                else if (message is RecoveryCompleted)
                {
                    _replyTo.Tell("done");
                }

                return true;
            }

            protected override bool ReceiveCommand(object message)
            {
                switch (message)
                {
                    case string str when str.Equals("stop"):
                        Context.Stop(Self);
                        return true;
                    case string str when str.Equals("snap"):
                        SaveSnapshot(GetDeliverySnapshot());
                        return true;
                    case string str:
                        Persist(new MsgSent(str), msgSent =>
                            Deliver(_destionationActor.Path, l => new Msg(l, msgSent.Message)));
                        return true;
                    case Confirm confirm:
                        Persist(new MsgConfirmed(confirm.DeliveryId), msgConfirmed =>
                            ConfirmDelivery(msgConfirmed.DeliveryId));
                        return true;
                    default:
                        return true;
                }
            }

            public override string PersistenceId { get; }
        }

        public class DestinationActor : UntypedActor
        {
            protected override void OnReceive(object message)
            {
                switch (message)
                {
                    case Msg msg when msg.Message.Equals("unconfirmed"):
                        break;
                    case Msg msg:
                        Sender.Tell(new Confirm(msg.DeliveryId), Self);
                        break;
                }
            }
        }
        #endregion

        protected SerializationSpec(Config config, ITestOutputHelper output) : base(config, null, output)
        {
        }

        [Fact]
        public void AtLeastOnceDeliveryActor_should_serialize_AtLeastOnceDeliverySnapshot()
        {
            var sourceActor = Sys.ActorOf(Props.Create<SourceActor>("SourceActor", TestActor));
            ExpectMsg("done");
            sourceActor.Tell("a");
            sourceActor.Tell("b");
            sourceActor.Tell("unconfirmed");
            sourceActor.Tell("snap");
            sourceActor.Tell("stop");
            Watch(sourceActor);
            ExpectTerminated(sourceActor);

            var sourceActor2 = Sys.ActorOf(Props.Create<SourceActor>("SourceActor", TestActor));
            ExpectMsg("done");
        }

        [Fact(Skip = "Not implemented yet")]
        public void PersistenceFSM_should_serialize_StateChangeEvent()
        {

        }

        [Fact(Skip = "Not implemented yet")]
        public void PersistenceFSM_should_serialize_PersistentFSMSnapshot()
        {

        }
    }
}
