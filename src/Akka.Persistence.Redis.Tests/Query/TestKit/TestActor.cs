using System;
using Akka.Actor;

namespace Akka.Persistence.TestKit.Query
{
    public class TestActor : PersistentActor
    {
        [Serializable]
        public sealed class DeleteCommand
        {
            public readonly long ToSequenceNr;

            public DeleteCommand(long toSequenceNr)
            {
                ToSequenceNr = toSequenceNr;
            }
        }

        public static Props Props(string persistenceId) => Actor.Props.Create(() => new TestActor(persistenceId));

        public TestActor(string persistenceId)
        {
            PersistenceId = persistenceId;
        }

        public override string PersistenceId { get; }
        protected override bool ReceiveRecover(object message) => true;
        private IActorRef _parentTestActor;

        protected override bool ReceiveCommand(object message) => message.Match()
            .With<DeleteCommand>(delete =>
            {
                _parentTestActor = Sender;
                DeleteMessages(delete.ToSequenceNr);
            })
            .With<DeleteMessagesSuccess>(deleteSuccess =>
            {
                ActorRefImplicitSenderExtensions.Tell(_parentTestActor, deleteSuccess.ToSequenceNr.ToString() + "-deleted");
            })
            .With<string>(cmd =>
            {
                var sender = Sender;
                Persist(cmd, e => ActorRefImplicitSenderExtensions.Tell(sender, e + "-done"));
            })
            .WasHandled;
    }
}