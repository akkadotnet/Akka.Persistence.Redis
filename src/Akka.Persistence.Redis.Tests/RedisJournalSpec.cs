//-----------------------------------------------------------------------
// <copyright file="RedisJournalSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Configuration;
using Akka.Persistence.TestKit.Journal;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests
{
    [Collection("RedisSpec")]
    public class RedisJournalSpec : JournalSpec
    {
        private static readonly Config SpecConfig;
        private static readonly string KeyPrefix;

        static RedisJournalSpec()
        {
            var connectionString = "127.0.0.1:6379";
            var database = 1;

            SpecConfig = ConfigurationFactory.ParseString(@"
                akka.test.single-expect-default = 3s
                akka.persistence {
                    publish-plugin-commands = on
                    journal {
                        plugin = ""akka.persistence.journal.redis""
                        redis {
                            class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                            configuration-string = """ + connectionString + @"""
                            plugin-dispatcher = ""akka.actor.default-dispatcher""
                            database = """ + database + @"""
                            key-prefix = ""akka:persistence:journal""
                        }
                    }
                }");

            KeyPrefix = SpecConfig.GetString("akka.persistence.journal.redis.key-prefix");
        }

        public RedisJournalSpec(ITestOutputHelper output)
            : base(SpecConfig, typeof(RedisJournalSpec).Name, output)
        {
            RedisPersistence.Get(Sys);
            Initialize();
        }

        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DbUtils.Clean(KeyPrefix);
        }
    }
}