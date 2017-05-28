﻿using System.Collections.Immutable;
using System.Linq;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.Query;
using Akka.Persistence.Redis.Query;
using Akka.Persistence.TestKit.Query;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Query
{
    [Collection("RedisSpec")]
    public sealed class RedisCurrentEventsByTagSpec : CurrentEventsByTagSource
    {
        public const int Database = 1;

        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                event-adapters {{
                  color-tagger  = ""Akka.Persistence.Redis.Tests.Query.ColorTagger, Akka.Persistence.Redis.Tests""
                }}
                event-adapter-bindings = {{
                  ""System.String"" = color-tagger
                }}
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                configuration-string = ""127.0.0.1:6379,allowAdmin:true""
                database = {id}
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisReadJournal.DefaultConfiguration());

        public RedisCurrentEventsByTagSpec() : base(Config(Database))
        {
            ReadJournal = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);
        }

        protected override void Dispose(bool disposing)
        {
            DbUtils.Clean(Database);
            base.Dispose(disposing);
        }
    }

    public class ColorTagger : IWriteEventAdapter
    {
        public static readonly IImmutableSet<string> Colors = ImmutableHashSet.CreateRange(new[] { "green", "black", "blue" });
        public string Manifest(object evt) => string.Empty;

        public object ToJournal(object evt)
        {
            var s = evt as string;
            if (s != null)
            {
                var tags = Colors.Aggregate(ImmutableHashSet<string>.Empty, (acc, color) => s.Contains(color) ? acc.Add(color) : acc);
                return tags.IsEmpty
                    ? evt
                    : new Tagged(evt, tags);
            }
            else return evt;
        }
    }
}