// -----------------------------------------------------------------------
// <copyright file="RedisJournalWriteConflictException.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;

#nullable enable
namespace Akka.Persistence.Redis.Journal
{
    /// <summary>
    /// Thrown by <see cref="RedisJournal"/> when a write was not applied because events with
    /// the same or higher sequence numbers had already been stored for the persistence id,
    /// typically by another incarnation of the persistent actor, or by an earlier write of
    /// this one that timed out on the client but was still applied by the server.
    /// </summary>
    /// <remarks>
    /// It is reported as a write failure, so the persistent actor's <c>OnPersistFailure</c>
    /// is called and the actor stops; a new incarnation recovers the events that were stored.
    /// </remarks>
    public sealed class RedisJournalWriteConflictException : Exception
    {
        public RedisJournalWriteConflictException(string persistenceId, long lowestSequenceNr, long highestSequenceNr, long storedHighestSequenceNr)
            : base($"Write of events [{lowestSequenceNr}..{highestSequenceNr}] for persistenceId [{persistenceId}] was not applied: " +
                   $"the journal already holds events up to sequence number [{storedHighestSequenceNr}] (or at or above [{lowestSequenceNr}]). " +
                   "Another writer has written to this persistence id, or an earlier write was applied after it timed out.")
        {
            PersistenceId = persistenceId;
            LowestSequenceNr = lowestSequenceNr;
            HighestSequenceNr = highestSequenceNr;
            StoredHighestSequenceNr = storedHighestSequenceNr;
        }

        public string PersistenceId { get; }

        public long LowestSequenceNr { get; }

        public long HighestSequenceNr { get; }

        public long StoredHighestSequenceNr { get; }
    }
}
