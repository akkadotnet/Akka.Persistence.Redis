// -----------------------------------------------------------------------
// <copyright file="RedisPersistence.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

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
                config.GetString("configuration-string", string.Empty),
                config.GetString("key-prefix", string.Empty),
                config.GetInt("database", 0));
        }
    }

    public class RedisPersistence : IExtension
    {
        public static RedisPersistence Get(ActorSystem system)
        {
            return system.WithExtension<RedisPersistence, RedisPersistenceProvider>();
        }

        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<RedisPersistence>("Akka.Persistence.Redis.reference.conf");
        }

        public RedisSettings JournalSettings { get; }
        public RedisSettings SnapshotStoreSettings { get; }

        public RedisPersistence(ExtendedActorSystem system)
        {
            system.Settings.InjectTopLevelFallback(DefaultConfig());

            JournalSettings = RedisSettings.Create(system.Settings.Config.GetConfig("akka.persistence.journal.redis"));
            SnapshotStoreSettings =
                RedisSettings.Create(system.Settings.Config.GetConfig("akka.persistence.snapshot-store.redis"));
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