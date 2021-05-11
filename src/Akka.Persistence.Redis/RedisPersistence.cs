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
        public const string JournalConfigPath = "akka.persistence.journal.redis";
        public const string SnapshotConfigPath = "akka.persistence.snapshot-store.redis";

        public static RedisPersistence Get(ActorSystem system)
        {
            return system.WithExtension<RedisPersistence, RedisPersistenceProvider>();
        }

        public static Config DefaultConfig()
        {
            return ConfigurationFactory.FromResource<RedisPersistence>("Akka.Persistence.Redis.reference.conf");
        }

        [Obsolete("This returns the default journal settings, not the current journal settings.")]
        public RedisSettings JournalSettings { get; }

        [Obsolete("This returns the default snapshot settings, not the current snapshot settings.")]
        public RedisSettings SnapshotStoreSettings { get; }

        public Config DefaultJournalConfig { get; }
        public Config DefaultSnapshotConfig { get; }

        public RedisPersistence(ExtendedActorSystem system)
        {
            var defaultConfig = DefaultConfig();
            system.Settings.InjectTopLevelFallback(defaultConfig);

            DefaultJournalConfig = defaultConfig.GetConfig(JournalConfigPath);
            DefaultSnapshotConfig = defaultConfig.GetConfig(SnapshotConfigPath);

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