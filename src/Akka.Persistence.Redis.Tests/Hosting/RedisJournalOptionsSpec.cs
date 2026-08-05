using System;
using System.IO;
using System.Text;
using Akka.Configuration;
using Akka.Persistence.Redis.Hosting;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Hosting
{
    public class RedisJournalOptionsSpec
    {
        [Fact(DisplayName = "RedisJournalOptions as default plugin should generate plugin setting")]
        public void DefaultPluginJournalOptionsTest()
        {
            var options = new RedisJournalOptions(true);
            var config = options.ToConfig();

            Assert.Equal("akka.persistence.journal.redis", config.GetString("akka.persistence.journal.plugin"));
            Assert.True(config.HasPath("akka.persistence.journal.redis"));
        }

        [Fact(DisplayName = "Empty RedisJournalOptions should equal empty config with default fallback")]
        public void DefaultJournalOptionsTest()
        {
            var options = new RedisJournalOptions(false);
            var emptyRootConfig = options.ToConfig().WithFallback(options.DefaultConfig);
            var baseRootConfig = Config.Empty
                .WithFallback(RedisPersistence.DefaultConfig());

            Assert.Equal(baseRootConfig.GetString("akka.persistence.journal.plugin"), emptyRootConfig.GetString("akka.persistence.journal.plugin"));

            var config = emptyRootConfig.GetConfig("akka.persistence.journal.redis");
            var baseConfig = baseRootConfig.GetConfig("akka.persistence.journal.redis");
            Assert.NotNull(config);
            Assert.NotNull(baseConfig);

            Assert.Equal(baseConfig.GetString("class"), config.GetString("class"));
            Assert.Equal(baseConfig.GetString("configuration-string"), config.GetString("configuration-string"));
            Assert.Equal(baseConfig.GetBoolean("auto-initialize"), config.GetBoolean("auto-initialize"));
            Assert.Equal(baseConfig.GetString("key-prefix"), config.GetString("key-prefix"));
            Assert.Equal(baseConfig.GetInt("database"), config.GetInt("database"));
            Assert.Equal(baseConfig.GetBoolean("use-database-number-from-connection-string"), config.GetBoolean("use-database-number-from-connection-string"));
        }

        [Fact(DisplayName = "Empty RedisJournalOptions with custom identifier should equal empty config with default fallback")]
        public void CustomIdJournalOptionsTest()
        {
            var options = new RedisJournalOptions(false, "custom");
            var emptyRootConfig = options.ToConfig().WithFallback(options.DefaultConfig);
            var baseRootConfig = Config.Empty
                .WithFallback(RedisPersistence.DefaultConfig());

            Assert.Equal(baseRootConfig.GetString("akka.persistence.journal.plugin"), emptyRootConfig.GetString("akka.persistence.journal.plugin"));

            var config = emptyRootConfig.GetConfig("akka.persistence.journal.custom");
            var baseConfig = baseRootConfig.GetConfig("akka.persistence.journal.redis");
            Assert.NotNull(config);
            Assert.NotNull(baseConfig);

            Assert.Equal(baseConfig.GetString("class"), config.GetString("class"));
            Assert.Equal(baseConfig.GetString("configuration-string"), config.GetString("configuration-string"));
            Assert.Equal(baseConfig.GetBoolean("auto-initialize"), config.GetBoolean("auto-initialize"));
            Assert.Equal(baseConfig.GetString("key-prefix"), config.GetString("key-prefix"));
            Assert.Equal(baseConfig.GetInt("database"), config.GetInt("database"));
            Assert.Equal(baseConfig.GetBoolean("use-database-number-from-connection-string"), config.GetBoolean("use-database-number-from-connection-string"));
        }

        [Fact(DisplayName = "RedisJournalOptions should generate proper config")]
        public void JournalOptionsTest()
        {
            var options = new RedisJournalOptions(true)
            {
                Identifier = "custom",
                AutoInitialize = true,
                ConfigurationString = "testConfigurationString",
                KeyPrefix = "testKeyPrefix",
                Database = 999123,
                UseDatabaseFromConnectionString = true
            };

            var baseConfig = options.ToConfig();

            Assert.Equal("akka.persistence.journal.custom", baseConfig.GetString("akka.persistence.journal.plugin"));

            var config = baseConfig.GetConfig("akka.persistence.journal.custom");
            Assert.NotNull(config);
            Assert.Equal(options.AutoInitialize, config.GetBoolean("auto-initialize"));
            Assert.Equal(options.ConfigurationString, config.GetString("configuration-string"));
            Assert.Equal(options.KeyPrefix, config.GetString("key-prefix"));
            Assert.Equal(options.Database, config.GetInt("database"));
            Assert.Equal(options.UseDatabaseFromConnectionString.Value, config.GetBoolean("use-database-number-from-connection-string"));
        }

        const string Json = @"
        {
          ""Logging"": {
            ""LogLevel"": {
              ""Default"": ""Information"",
              ""Microsoft.AspNetCore"": ""Warning""
            }
          },
          ""Akka"": {
            ""JournalOptions"": {
              ""Identifier"": ""customRedis"",
              ""AutoInitialize"": true,
              ""IsDefaultPlugin"": false,
              ""ConfigurationString"": ""ConfigurationStringFromConfigJson"",
              ""KeyPrefix"": ""KeyPrefixFromConfigJson"",
              ""Database"": 123456,
              ""UseDatabaseFromConnectionString"": true,
              ""Serializer"": ""TestSerializer"",
            }
          }
        }";

        [Fact(DisplayName = "RedisJournalOptions should be bindable to IConfiguration")]
        public void JournalOptionsIConfigurationBindingTest()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json));
            var jsonConfig = new ConfigurationBuilder().AddJsonStream(stream).Build();

            var options = jsonConfig.GetSection("Akka:JournalOptions").Get<RedisJournalOptions>();
            Assert.Equal("customRedis", options.Identifier);
            Assert.True(options.AutoInitialize);
            Assert.False(options.IsDefaultPlugin);
            Assert.Equal("ConfigurationStringFromConfigJson", options.ConfigurationString);
            Assert.Equal("KeyPrefixFromConfigJson", options.KeyPrefix);
            Assert.Equal(123456, options.Database);
            Assert.True(options.UseDatabaseFromConnectionString);
            Assert.Equal("TestSerializer", options.Serializer);
        }
    }
}