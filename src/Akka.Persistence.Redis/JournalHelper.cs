//-----------------------------------------------------------------------
// <copyright file="JournalHelper.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;

namespace Akka.Persistence.Redis
{
    internal class JournalHelper
    {
        private readonly ActorSystem _system;

        public JournalHelper(ActorSystem system, string keyPrefix)
        {
            _system = system;
            KeyPrefix = keyPrefix;
        }

        public string KeyPrefix { get; }

        public byte[] PersistentToBytes(IPersistentRepresentation message)
        {
            var serializer = _system.Serialization.FindSerializerForType(typeof(IPersistentRepresentation));
            return serializer.ToBinary(message);
        }

        public IPersistentRepresentation PersistentFromBytes(byte[] bytes)
        {
            var serializer = _system.Serialization.FindSerializerForType(typeof(IPersistentRepresentation));
            return serializer.FromBinary<IPersistentRepresentation>(bytes);
        }

        public string GetIdentifiersKey() => $"{KeyPrefix}journal:persistenceIds";
        public string GetHighestSequenceNrKey(string persistenceId) => $"{KeyPrefix}journal:persisted:{persistenceId}:highestSequenceNr";
        public string GetJournalKey(string persistenceId) => $"{KeyPrefix}journal:persisted:{persistenceId}";
        public string GetJournalChannel(string persistenceId) => $"{KeyPrefix}journal:channel:persisted:{persistenceId}";
        public string GetTagKey(string tag) => $"{KeyPrefix}journal:tag:{tag}";
        public string GetTagsChannel() => $"{KeyPrefix}journal:channel:tags";
        public string GetIdentifiersChannel() => $"{KeyPrefix}journal:channel:ids";
    }
}
