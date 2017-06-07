//-----------------------------------------------------------------------
// <copyright file="RedisUtils.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

namespace Akka.Persistence.Redis.Journal
{
    internal static class RedisUtils
    {
        public static byte[] PersistentToBytes(IPersistentRepresentation message, Akka.Serialization.Serialization serialization)
        {
            var serializer = serialization.FindSerializerForType(typeof(IPersistentRepresentation));
            return serializer.ToBinary(message);
        }

        public static IPersistentRepresentation PersistentFromBytes(byte[] bytes, Akka.Serialization.Serialization serialization)
        {
            var serializer = serialization.FindSerializerForType(typeof(IPersistentRepresentation));
            return serializer.FromBinary<IPersistentRepresentation>(bytes);
        }

        public static string GetIdentifiersKey() => "journal:persistenceIds";
        public static string GetTagsKey() => "journal:tags";
        public static string GetHighestSequenceNrKey(string persistenceId) => $"journal:persisted:{persistenceId}:highestSequenceNr";
        public static string GetJournalKey(string persistenceId) => $"journal:persisted:{persistenceId}";
        public static string GetJournalChannel(string persistenceId) => $"journal:channel:persisted:{persistenceId}";
        public static string GetTagKey(string tag) => $"journal:tag:{tag}";
        public static string GetTagsChannel() => "journal:channel:tags";
        public static string GetIdentifiersChannel() => "journal:channel:ids";
    }
}

