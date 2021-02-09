// -----------------------------------------------------------------------
// <copyright file="ProtobufSerializer.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using Akka.Actor;
using Akka.Serialization;
using System;
using System.Runtime.Serialization;
using Akka.Util;
using Akka.Persistence;
using Google.Protobuf;

namespace CustomSerialization.Protobuf.Serialization
{
    public class ProtobufSerializer : Serializer
    {
        public ProtobufSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override int Identifier => 110;

        public override bool IncludeManifest => true;

        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case IPersistentRepresentation p:
                    return PersistentToProto(p).ToByteArray();
                case Stored s:
                    return StoredToProto(s).ToByteArray();
                default:
                    throw new ArgumentException($"Can't serialize object of type {obj.GetType()}");
            }
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(Persistent) || type == typeof(IPersistentRepresentation))
                return PersistentFromProto(bytes);
            else if (type == typeof(Stored)) return StoredFromProto(bytes);

            throw new SerializationException($"Can't serialize object of type {type}");
        }

        // IPersistentRepresentation
        private Msg.PersistentMessage PersistentToProto(IPersistentRepresentation p)
        {
            var message = new Msg.PersistentMessage();

            message.PersistenceId = p.PersistenceId;
            message.SequenceNr = p.SequenceNr;
            message.WriterGuid = p.WriterGuid;
            message.Payload = PersistentPayloadBuilder(p.Payload);

            return message;
        }

        private IPersistentRepresentation PersistentFromProto(byte[] bytes)
        {
            var persistentMessage = Msg.PersistentMessage.Parser.ParseFrom(bytes);

            return new Persistent(
                PayloadFromProto(persistentMessage.Payload),
                persistentMessage.SequenceNr,
                persistentMessage.PersistenceId,
                writerGuid: persistentMessage.WriterGuid);
        }

        // Stored
        private CustomSerialization.Protobuf.Msg.Stored StoredToProto(Stored stored)
        {
            var msg = new CustomSerialization.Protobuf.Msg.Stored();
            msg.Value = stored.Value;
            return msg;
        }

        private object StoredFromProto(byte[] bytes)
        {
            var persistentMessage = Msg.Stored.Parser.ParseFrom(bytes);
            return new Stored(persistentMessage.Value);
        }

        // PersistentPayload
        private Msg.PersistentPayload PersistentPayloadBuilder(object payload)
        {
            var persistentPayload = new Msg.PersistentPayload();

            if (payload == null)
                return persistentPayload;

            var serializer = system.Serialization.FindSerializerFor(payload);
            if (serializer is SerializerWithStringManifest serializerManifest)
            {
                var manifest = serializerManifest.Manifest(payload);
                if (!manifest.Equals(Persistent.Undefined))
                    persistentPayload.MessageManifest = ByteString.CopyFromUtf8(manifest);
            }
            else
            {
                if (serializer.IncludeManifest)
                    persistentPayload.MessageManifest = ByteString.CopyFromUtf8(payload.GetType().TypeQualifiedName());
            }

            persistentPayload.Message = ByteString.CopyFrom(serializer.ToBinary(payload));
            persistentPayload.SerializerId = serializer.Identifier;
            return persistentPayload;
        }

        private object PayloadFromProto(Msg.PersistentPayload persistentPayload)
        {
            return system.Serialization.Deserialize(
                persistentPayload.Message.ToByteArray(),
                persistentPayload.SerializerId,
                persistentPayload.MessageManifest.ToStringUtf8());
        }
    }
}