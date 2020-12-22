//-----------------------------------------------------------------------
// <copyright file="ActorPathResolver.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using MessagePack;
using MessagePack.Formatters;

namespace CustomSerialization.MsgPack.Serialization
{
    public class ActorPathResolver : IFormatterResolver
    {
        public static IFormatterResolver Instance = new ActorPathResolver();
        private ActorPathResolver() { }
        public IMessagePackFormatter<T> GetFormatter<T>() => FormatterCache<T>.Formatter;

        private static class FormatterCache<T>
        {
            public static readonly IMessagePackFormatter<T> Formatter;
            static FormatterCache() => Formatter = (IMessagePackFormatter<T>)ActorPathResolverGetFormatterHelper.GetFormatter(typeof(T));
        }
    }

    internal static class ActorPathResolverGetFormatterHelper
    {
        private static readonly Dictionary<Type, object> FormatterMap = new Dictionary<Type, object>
        {
            {typeof(ActorPath), new ActorPathFormatter<ActorPath>()},
            {typeof(ChildActorPath), new ActorPathFormatter<ChildActorPath>()},
            {typeof(RootActorPath), new ActorPathFormatter<RootActorPath>()}
        };

        internal static object GetFormatter(Type t) => FormatterMap.TryGetValue(t, out var formatter) ? formatter : null;
    }

    public class ActorPathFormatter<T> : IMessagePackFormatter<T> where T : ActorPath
    {
        
        public void Serialize(ref MessagePackWriter writer, T value, MessagePackSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNil();

                return;
            }
            writer.Write(value.ToSerializationFormat());
        }

        public T Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
        {
            if (reader.IsNil)
            {
                return null;
            }

            var path = reader.ReadString();
            return ActorPath.TryParse(path, out var actorPath) ? (T)actorPath : null;
        }
    }
}
