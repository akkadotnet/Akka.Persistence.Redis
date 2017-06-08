//-----------------------------------------------------------------------
// <copyright file="JsonSerializer.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Reflection;
using Akka.Actor;
using System.Text;
using Akka.Persistence.Redis.Snapshot;
using Newtonsoft.Json;
using Akka.Serialization;
using Akka.Util.Internal;
using Newtonsoft.Json.Linq;

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
            string data = JsonConvert.SerializeObject(obj, Formatting.None, Settings);
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
            var serializeed = actorPath.ToSerializationFormat();

            writer.WriteStartObject();
            writer.WritePropertyName("$path");
            writer.WriteValue(serializeed);
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, Newtonsoft.Json.JsonSerializer serializer)
        {
            var jObject = JObject.Load(reader);
            var deserialized = jObject.Value<string>("$path");
            var actorPath = ActorPath.Parse(deserialized);
            return actorPath;
        }

        public override bool CanConvert(Type objectType)
        {
            return typeof(ActorPath).GetTypeInfo().IsAssignableFrom(objectType);
        }
    }
}