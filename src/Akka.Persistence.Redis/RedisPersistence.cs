//-----------------------------------------------------------------------
// <copyright file="RedisSettings.cs" company="Akka.NET Project">
//     Copyright (C) 2017 Akka.NET Contrib <https://github.com/AkkaNetContrib/Akka.Persistence.Redis>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.Configuration;

namespace Akka.Persistence.Redis
{
    public class RedisSettings
    {
        public RedisSettings(string configurationString, string keyPrefix, int database)
        {
            ConfigurationString = configurationString;
            KeyPrefix = keyPrefix;
            Database = database;
        }

        public string ConfigurationString { get; }

        public string KeyPrefix { get; }

        public int Database { get; }

        public static RedisSettings Create(Config config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            return new RedisSettings(
              configurationString: config.GetString("configuration-string"),
              keyPrefix: config.GetString("key-prefix"),
              database: config.GetInt("database"));
        }
    }

    public class RedisPersistence : IExtension
    {
        public static RedisPersistence Get(ActorSystem system) => system.WithExtension<RedisPersistence, RedisPersistenceProvider>();
        public static Config DefaultConfig() => ConfigurationFactory.FromResource<RedisPersistence>("Akka.Persistence.Redis.reference.conf");

        public RedisSettings JournalSettings { get; }
        public RedisSettings SnapshotStoreSettings { get; }

        public RedisPersistence(ExtendedActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(DefaultConfig());

            JournalSettings = RedisSettings.Create(system.Settings.Config.GetConfig("akka.persistence.journal.redis"));
            SnapshotStoreSettings = RedisSettings.Create(system.Settings.Config.GetConfig("akka.persistence.snapshot-store.redis"));
        }
    }

    public class RedisPersistenceProvider : ExtensionIdProvider<RedisPersistence>
    {
        public override RedisPersistence CreateExtension(ExtendedActorSystem system)
        {
            return new RedisPersistence(system);
        }
    }
}
