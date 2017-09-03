//-----------------------------------------------------------------------
// <copyright file="BackwardCompatibleSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Serialization;

namespace Akka.Persistence.Redis.Serialization
{
    public class BackwardCompatibleSerializer : Serializer
    {
        public BackwardCompatibleSerializer(ExtendedActorSystem system) : base(system)
        {
        }

        public override byte[] ToBinary(object obj)
        {
            if (obj is IPersistentRepresentation repr)
            {
                var entry = new JournalEntry
                {
                    PersistenceId = repr.PersistenceId,
                    SequenceNr = repr.SequenceNr,
                    IsDeleted = repr.IsDeleted,
                    Payload = repr.Payload,
                    Manifest = repr.Manifest
                };

                var serializer = system.Serialization.FindSerializerForType(typeof(JournalEntry));
                return serializer.ToBinary(entry);
            }

            throw new ArgumentException($"Can't serialize object of type [{obj.GetType()}] in [{GetType()}]");
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(IPersistentRepresentation) || type == typeof(Persistent))
            {
                var serializer = system.Serialization.FindSerializerForType(typeof(JournalEntry));
                return serializer.FromBinary(bytes, typeof(JournalEntry));
            }

            throw new ArgumentException($"Can't deserialize object of type [{type}] in [{GetType()}]");
        }

        public override int Identifier => 41;

        public override bool IncludeManifest => true;
    }

    internal class JournalEntry
    {
        public string PersistenceId { get; set; }

        public long SequenceNr { get; set; }

        public bool IsDeleted { get; set; }

        public object Payload { get; set; }

        public string Manifest { get; set; }
    }
}
