using System;
using System.Collections.Generic;
using System.Text;
using Akka.Event;

namespace Akka.Persistence.Redis.Query
{
    /// <summary>
    /// TBD
    /// </summary>
    public interface ISubscriptionCommand { }

    [Serializable]
    public sealed class NewEventAppended : IDeadLetterSuppression
    {
        public static NewEventAppended Instance = new NewEventAppended();

        private NewEventAppended() { }
    }

    /// <summary>
    /// Subscribe the `sender` to new appended events.
    /// Used by query-side. The journal will send <see cref="NewEventAppended"/> messages to
    /// the subscriber when `asyncWriteMessages` has been called.
    /// </summary>
    [Serializable]
    public sealed class SubscribeNewEvents : ISubscriptionCommand
    {
        public static SubscribeNewEvents Instance = new SubscribeNewEvents();

        private SubscribeNewEvents() { }
    }

}
