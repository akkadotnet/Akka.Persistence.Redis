using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams.Dsl;
using StackExchange.Redis;
using System;

namespace Akka.Persistence.Redis.Query
{
    public class RedisReadJournal :
        IReadJournal,
        IAllPersistenceIdsQuery,
        ICurrentPersistenceIdsQuery
    {
        private readonly TimeSpan _refreshInterval;
        private readonly string _writeJournalPluginId;
        private readonly int _maxBufferSize;

        private ConnectionMultiplexer redis;
        private IDatabase redisDatabase;

        public static string Identifier = "﻿akka.persistence.query.journal.redis";

        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<RedisReadJournal>("Akka.Persistence.Redis.reference.conf");
        }

        public RedisReadJournal(ExtendedActorSystem system, Config config)
        {
            _refreshInterval = config.GetTimeSpan("refresh-interval");
            _writeJournalPluginId = config.GetString("write-plugin");
            _maxBufferSize = config.GetInt("max-buffer-size");

            var address = system.Settings.Config.GetString("akka.persistence.journal.redis.configuration-string");
            var database = system.Settings.Config.GetInt("akka.persistence.journal.redis.database");

            redis = ConnectionMultiplexer.Connect(address);
            redisDatabase = redis.GetDatabase(database);
        }

        /// <summary>
        /// Returns the live stream of persisted identifiers. Identifiers may appear several times in the stream.
        /// </summary>
        public Source<string, NotUsed> AllPersistenceIds() => Source.FromGraph(new Stages.PersistenceIdsSource(redis));

        /// <summary>
        /// Returns the stream of current persisted identifiers. This stream is not live, once the identifiers were all returned, it is closed.
        /// </summary>
        public Source<string, NotUsed> CurrentPersistenceIds() => Source.FromGraph(new Stages.CurrentPersistenceIdsSource(redisDatabase));
    }
}
