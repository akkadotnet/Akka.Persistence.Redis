//-----------------------------------------------------------------------
// <copyright file="JsonSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using Akka.Actor;
using System.Text;
using Newtonsoft.Json;
using Akka.Serialization;
using Akka.Util.Internal;

namespace Akka.Persistence.Redis.Serialization
{
    public class JsonSerializer : Serializer
    {
        private JsonSerializerSettings Settings { get; }

        public JsonSerializer(ExtendedActorSystem system) : base(system)
        {
            Settings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            };
            
            Settings.Converters.Add(new ActorPathConverter());
        }

        public override byte[] ToBinary(object obj)
        {
            return ObjectSerializer(obj);
        }

        public override object FromBinary(byte[] bytes, Type type)
        {
            if (type == typeof(Persistent) || type == typeof(IPersistentRepresentation))
            {
                return ObjectDeserializer(bytes, typeof(Persistent));
            }

            return ObjectDeserializer(bytes, type);
        }

        public override int Identifier => 41;

        public override bool IncludeManifest => true;

        private byte[] ObjectSerializer(object obj)
        {
            string data = JsonConvert.SerializeObject(obj, Settings);
            return Encoding.UTF8.GetBytes(data);
        }

        private object ObjectDeserializer(byte[] bytes, Type type)
        {
            string data = Encoding.UTF8.GetString(bytes);
            return JsonConvert.DeserializeObject(data, type, Settings);
        }
    }

    internal class ActorPathConverter : JsonConverter
    {
        public override void WriteJson(JsonWriter writer, object value, Newtonsoft.Json.JsonSerializer serializer)
        {
            var actorPath = value.AsInstanceOf<ActorPath>();
            writer.WriteValue(actorPath.ToSerializationFormat());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            var deserialized = reader.Value.ToString();
            return ActorPath.TryParse(deserialized, out var path) ? path : null;
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(ActorPath).GetTypeInfo().IsAssignableFrom(objectType);
        }
    }
}
