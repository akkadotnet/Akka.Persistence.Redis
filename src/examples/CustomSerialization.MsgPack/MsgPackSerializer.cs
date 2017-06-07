//-----------------------------------------------------------------------
// <copyright file="MsgPackSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Serialization;
using MessagePack;
using MessagePack.Resolvers;
using System;
using Akka.Util;
using Akka.Persistence;

namespace CustomSerialization.MsgPack
{
    public class MsgPackSerializer : Serializer
    {
        #region Messages
        [MessagePackObject]
        public class PersistenceMessage
        {
            [SerializationConstructor]
            public PersistenceMessage(string persistenceId, long sequenceNr, string writerGuid, int serializerId, string manifest, byte[] payload)
            {
                PersistenceId = persistenceId;
                SequenceNr = sequenceNr;
                WriterGuid = writerGuid;
                SerializerId = serializerId;
                Manifest = manifest;
                Payload = payload;
            }

            [Key(0)]
            public string PersistenceId { get; }

            [Key(1)]
            public long SequenceNr { get; }

            [Key(2)]
            public string WriterGuid { get; }

            [Key(3)]
            public int SerializerId { get; }

            [Key(4)]
            public string Manifest { get; }

            [Key(5)]
            public byte[] Payload { get; }
        }
        #endregion

        static MsgPackSerializer()
        {
            CompositeResolver.RegisterAndSetAsDefault(
                PrimitiveObjectResolver.Instance,
                ContractlessStandardResolver.Instance);
        }

        public MsgPackSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is IPersistentRepresentation repr) return PersistenceMessageSerializer(repr);

            return ObjectSerializer(obj);
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(IPersistentRepresentation)) return PersistenceMessageDeserializer(bytes);

            return ObjectDeserializer(bytes, type);
        }

        public override int Identifier => 30;

        public override bool IncludeManifest => true;

        private byte[] PersistenceMessageSerializer(IPersistentRepresentation obj)
        {
            var serializer = system.Serialization.FindSerializerFor(obj.Payload);

            // get manifest
            var manifestSerializer = serializer as SerializerWithStringManifest;
            var payloadManifest = "";

            if (manifestSerializer != null)
            {
                var manifest = manifestSerializer.Manifest(obj.Payload);
                if (!string.IsNullOrEmpty(manifest))
                    payloadManifest = manifest;
            }
            else
            {
                if (serializer.IncludeManifest)
                    payloadManifest = obj.Payload.GetType().TypeQualifiedName();
            }

            var persistenceMessage = new PersistenceMessage(
                obj.PersistenceId,
                obj.SequenceNr,
                obj.WriterGuid,
                serializer.Identifier,
                payloadManifest,
                serializer.ToBinary(obj.Payload));
            return ObjectSerializer(persistenceMessage);
        }

        private IPersistentRepresentation PersistenceMessageDeserializer(byte[] bytes)
        {
            var persistenceMessage = ObjectDeserializer(bytes, typeof(PersistenceMessage)) as PersistenceMessage;

            var payload = system.Serialization.Deserialize(
                persistenceMessage.Payload,
                persistenceMessage.SerializerId,
                persistenceMessage.Manifest);

            return new Persistent(
                payload,
                persistenceMessage.SequenceNr,
                persistenceMessage.PersistenceId,
                persistenceMessage.Manifest,
                false,
                null,
                persistenceMessage.WriterGuid);
        }

        private byte[] ObjectSerializer(object obj)
        {
            return MessagePackSerializer.NonGeneric.Serialize(obj.GetType(), obj);
        }

        private object ObjectDeserializer(byte[] bytes, Type type)
        {
            return MessagePackSerializer.NonGeneric.Deserialize(type, bytes);
        }
    }
}
