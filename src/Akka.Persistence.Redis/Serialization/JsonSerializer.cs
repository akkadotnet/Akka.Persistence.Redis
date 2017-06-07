//-----------------------------------------------------------------------
// <copyright file="JsonSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using System.Text;
using Newtonsoft.Json;
using Akka.Serialization;

namespace Akka.Persistence.Redis.Serialization
{
    public class JsonSerializer : Serializer
    {
        private JsonSerializerSettings Settings { get; }

        public JsonSerializer(ExtendedActorSystem system) : base(system)
        {
            Settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                DefaultValueHandling = DefaultValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore
            };
        }

        public override byte[] ToBinary(object obj)
        {
            switch (obj)
            {
                case IPersistentRepresentation _:
                case AtLeastOnceDeliverySnapshot _:
                case Akka.Persistence.Serialization.Snapshot _:
                    return ObjectSerializer(obj);
                default:
                    return ObjectSerializer(obj);
            }
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(Persistent) || type == typeof(IPersistentRepresentation))
            {
                return ObjectDeserializer(bytes, typeof(Persistent));
            }
            else if (type == typeof(Akka.Persistence.Serialization.Snapshot))
            {
                return ObjectDeserializer(bytes, type);
            }

            return ObjectDeserializer(bytes, type);
        }

        public override int Identifier => 41;

        public override bool IncludeManifest => true;

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
    }
}