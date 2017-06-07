//-----------------------------------------------------------------------
// <copyright file="SnapshotEntry.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.Redis.Snapshot
{
    /// <summary>
    /// Class used for storing a Snapshot
    /// </summary>
    internal class SnapshotEntry
    {
        public string PersistenceId { get; set; }

        public long SequenceNr { get; set; }

        public long Timestamp { get; set; }

        public object Snapshot { get; set; }
    }
}
