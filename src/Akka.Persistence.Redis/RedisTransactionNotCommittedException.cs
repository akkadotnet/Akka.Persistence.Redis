// -----------------------------------------------------------------------
// <copyright file="RedisTransactionNotCommittedException.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;

#nullable enable
namespace Akka.Persistence.Redis
{
    /// <summary>
    /// Thrown by the Redis journal and snapshot store when the transaction of a write or delete
    /// was not committed because one of its conditions was not met. Conditions are added by a
    /// subclass that overrides <c>CreateTransaction</c>, for example to check a fencing token,
    /// so nothing of the operation was applied.
    /// </summary>
    /// <remarks>
    /// A journal write that fails this way is reported as a write failure, so the persistent
    /// actor's <c>OnPersistFailure</c> is called and the actor stops. Deletes and snapshot
    /// operations are reported with their usual failure messages.
    /// </remarks>
    public sealed class RedisTransactionNotCommittedException : Exception
    {
        public RedisTransactionNotCommittedException(string persistenceId, string operation)
            : base($"{operation} for persistenceId [{persistenceId}] was not applied: " +
                   "a condition of its Redis transaction was not met.")
        {
            PersistenceId = persistenceId;
            Operation = operation;
        }

        public string PersistenceId { get; }

        /// <summary>
        /// The plugin operation whose transaction was not committed, such as <c>WriteMessagesAsync</c>.
        /// </summary>
        public string Operation { get; }
    }
}
