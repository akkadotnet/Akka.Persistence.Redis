using System;
using Akka.Actor;
using System.Text;
using Newtonsoft.Json;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Persistence.Redis.Serialization
{
    public class JsonSerializer : Serializer
    {
        private JsonSerializerSettings Settings { get; }

        public JsonSerializer(ExtendedActorSystem system) : base(system)
        {
            Settings = new JsonSerializerSettings()
            {
                TypeNameHandling = TypeNameHandling.Auto
            };
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is IPersistentRepresentation) return PersistenceMessageSerializer((IPersistentRepresentation)obj);
            if (obj is IActorRef) return IActorRefSerializer((IActorRef)obj);
            if (obj is ActorPath) return ActorPathSerializer((ActorPath)obj);

            return ObjectSerializer(obj);
        }
        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(IPersistentRepresentation)) return PersistenceMessageDeserializer(bytes);
            if (type == typeof(IActorRef)) return IActorRefDeserializer(bytes);
            if (type == typeof(ActorPath)) return ActorPathDeserializer(bytes);

            return ObjectDeserializer(bytes, type);
        }

        public override int Identifier => 34;

        public override bool IncludeManifest => false;

        
        private byte[] PersistenceMessageSerializer(IPersistentRepresentation obj)
        {
            var persistenceMessage = new PersistenceMessage();
            persistenceMessage.PersistenceId = obj.PersistenceId;
            persistenceMessage.SequenceNr = obj.SequenceNr;
            persistenceMessage.WriterGuid = obj.WriterGuid;
            persistenceMessage.Payload = PayloadToBytes(obj.Payload);
            return ObjectSerializer(persistenceMessage);
        }

        private IPersistentRepresentation PersistenceMessageDeserializer(byte[] bytes)
        {
            var persistenceMessage = ObjectDeserializer(bytes, typeof(PersistenceMessage)) as PersistenceMessage;

            var payload = system.Serialization.Deserialize(
                persistenceMessage.Payload.Payload,
                persistenceMessage.Payload.SerializerId,
                persistenceMessage.Payload.Manifest);

            return new Persistent(
                payload,
                persistenceMessage.SequenceNr,
                persistenceMessage.PersistenceId,
                persistenceMessage.Payload.Manifest,
                false,
                null,
                persistenceMessage.WriterGuid);
        }

        private byte[] ObjectSerializer(object obj)
        {
            string data = JsonConvert.SerializeObject(obj, Formatting.None, Settings);
            return Encoding.UTF8.GetBytes(data);
        }

        private object ObjectDeserializer(byte[] bytes, Type type)
        {
            string data = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject(data, type, Settings);
        }

        private byte[] IActorRefSerializer(IActorRef actorRef)
        {
            var str = Akka.Serialization.Serialization.SerializedActorPath(actorRef);
            return ObjectSerializer(str);
        }

        private object IActorRefDeserializer(byte[] bytes)
        {
            var path = (string)ObjectDeserializer(bytes, typeof(string));
            return system.Provider.ResolveActorRef(path);
        }

        private byte[] ActorPathSerializer(ActorPath obj)
        {
            var str = obj.ToSerializationFormat();
            return ObjectSerializer(str);
        }

        private object ActorPathDeserializer(byte[] bytes)
        {
            var path = (string)ObjectDeserializer(bytes, typeof(string));
            ActorPath actorPath;
            if (ActorPath.TryParse(path, out actorPath))
            {
                return actorPath;
            }

            return null;
        }

        public PersistencePayload PayloadToBytes(object payload)
        {
            if (payload == null)
                return null;

            var persistencePayload = new PersistencePayload();
            var serializer = this.system.Serialization.FindSerializerFor(payload);

            persistencePayload.SerializerId = serializer.Identifier;
            persistencePayload.Payload = serializer.ToBinary(payload);

            // get manifest
            var manifestSerializer = serializer as SerializerWithStringManifest;

            if (manifestSerializer != null)
            {
                var manifest = manifestSerializer.Manifest(payload);
                if (!string.IsNullOrEmpty(manifest))
                    persistencePayload.Manifest = manifest;
            }
            else
            {
                if (serializer.IncludeManifest)
                    persistencePayload.Manifest = payload.GetType().TypeQualifiedName();
            }

            return persistencePayload;
        }
    }
}