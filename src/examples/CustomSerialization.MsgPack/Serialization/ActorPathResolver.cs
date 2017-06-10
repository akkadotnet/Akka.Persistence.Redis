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
        public int Serialize(ref byte[] bytes, int offset, T value, IFormatterResolver formatterResolver)
        {
            if (value == null)
            {
                return MessagePackBinary.WriteNil(ref bytes, offset);
            }

            var startOffset = offset;
            offset += MessagePackBinary.WriteString(ref bytes, offset, value.ToSerializationFormat());
            return offset - startOffset;
        }

        public T Deserialize(byte[] bytes, int offset, IFormatterResolver formatterResolver, out int readSize)
        {
            if (MessagePackBinary.IsNil(bytes, offset))
            {
                readSize = 1;
                return null;
            }

            var path = MessagePackBinary.ReadString(bytes, offset, out readSize);
            return ActorPath.TryParse(path, out var actorPath) ? (T)actorPath : null;
        }
    }
}
