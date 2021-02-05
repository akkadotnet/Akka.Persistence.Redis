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
        private readonly Akka.Serialization.Serialization _serialization;
        private readonly Akka.Serialization.Serializer _serializer;

        public JournalHelper(ActorSystem system, string keyPrefix)
        {
            _system = system;
            _serialization = system.Serialization;
            _serializer = _serialization.FindSerializerForType(typeof(Persistent));
            KeyPrefix = keyPrefix;
        }

        public string KeyPrefix { get; }

        public byte[] PersistentToBytes(IPersistentRepresentation message)
        {
            return _serialization.Serialize(message);
        }

        public IPersistentRepresentation PersistentFromBytes(byte[] bytes)
        {
            var p = (IPersistentRepresentation)_serialization.Deserialize(bytes, _serializer.Identifier, typeof(Persistent));
            return p;
        }

        public string GetIdentifiersKey() => $"{KeyPrefix}journal:persistenceIds";
        public string GetTagsChannel() => $"{KeyPrefix}journal:channel:tags";
        public string GetEventsChannel() => $"{KeyPrefix}journal:channel:events";
        public string GetEventsKey() => $"{KeyPrefix}journal:events";
        public string GetHighestSequenceNrKey(string persistenceId, bool withHashTag) 
            => withHashTag
                ? $"{{__{persistenceId}}}.{KeyPrefix}journal:persisted:{persistenceId}:highestSequenceNr"
                : $"{KeyPrefix}journal:persisted:{persistenceId}:highestSequenceNr";
        public string GetJournalKey(string persistenceId, bool withHasTag) 
            => withHasTag 
                ? $"{{__{persistenceId}}}.{KeyPrefix}journal:persisted:{persistenceId}"
                : $"{KeyPrefix}journal:persisted:{persistenceId}";
        public string GetJournalChannel(string persistenceId, bool withHasTag) 
            => withHasTag
                ? $"{{__journal_channel}}.{KeyPrefix}journal:channel:persisted:{persistenceId}"
                : $"{KeyPrefix}journal:channel:persisted:{persistenceId}";
        public string GetTagKey(string tag, bool withHashTag) 
            => withHashTag
                ? $"{{__tags}}.{KeyPrefix}journal:tag:{tag}"
                : $"{KeyPrefix}journal:tag:{tag}";
        public string GetIdentifiersChannel() => $"{KeyPrefix}journal:channel:ids";

    }
}
