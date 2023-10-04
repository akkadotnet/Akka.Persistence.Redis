using System;
using System.IO;
using System.Text;
using Akka.Configuration;
using Akka.Persistence.Redis.Hosting;
using FluentAssertions;
using FluentAssertions.Extensions;
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

            config.GetString("akka.persistence.journal.plugin").Should().Be("akka.persistence.journal.redis");
            config.HasPath("akka.persistence.journal.redis").Should().BeTrue();
        }

        [Fact(DisplayName = "Empty RedisJournalOptions should equal empty config with default fallback")]
        public void DefaultJournalOptionsTest()
        {
            var options = new RedisJournalOptions(false);
            var emptyRootConfig = options.ToConfig().WithFallback(options.DefaultConfig);
            var baseRootConfig = Config.Empty
                .WithFallback(RedisPersistence.DefaultConfig());

            emptyRootConfig.GetString("akka.persistence.journal.plugin").Should().Be(baseRootConfig.GetString("akka.persistence.journal.plugin"));

            var config = emptyRootConfig.GetConfig("akka.persistence.journal.redis");
            var baseConfig = baseRootConfig.GetConfig("akka.persistence.journal.redis");
            config.Should().NotBeNull();
            baseConfig.Should().NotBeNull();

            config.GetString("class").Should().Be(baseConfig.GetString("class"));
            config.GetString("configuration-string").Should().Be(baseConfig.GetString("configuration-string"));
            config.GetBoolean("auto-initialize").Should().Be(baseConfig.GetBoolean("auto-initialize"));
            config.GetString("key-prefix").Should().Be(baseConfig.GetString("key-prefix"));
            config.GetInt("database").Should().Be(baseConfig.GetInt("database"));
            config.GetBoolean("use-database-number-from-connection-string").Should().Be(baseConfig.GetBoolean("use-database-number-from-connection-string"));
        }

        [Fact(DisplayName = "Empty RedisJournalOptions with custom identifier should equal empty config with default fallback")]
        public void CustomIdJournalOptionsTest()
        {
            var options = new RedisJournalOptions(false, "custom");
            var emptyRootConfig = options.ToConfig().WithFallback(options.DefaultConfig);
            var baseRootConfig = Config.Empty
                .WithFallback(RedisPersistence.DefaultConfig());

            emptyRootConfig.GetString("akka.persistence.journal.plugin").Should().Be(baseRootConfig.GetString("akka.persistence.journal.plugin"));

            var config = emptyRootConfig.GetConfig("akka.persistence.journal.custom");
            var baseConfig = baseRootConfig.GetConfig("akka.persistence.journal.redis");
            config.Should().NotBeNull();
            baseConfig.Should().NotBeNull();

            config.GetString("class").Should().Be(baseConfig.GetString("class"));
            config.GetString("configuration-string").Should().Be(baseConfig.GetString("configuration-string"));
            config.GetBoolean("auto-initialize").Should().Be(baseConfig.GetBoolean("auto-initialize"));
            config.GetString("key-prefix").Should().Be(baseConfig.GetString("key-prefix"));
            config.GetInt("database").Should().Be(baseConfig.GetInt("database"));
            config.GetBoolean("use-database-number-from-connection-string").Should().Be(baseConfig.GetBoolean("use-database-number-from-connection-string"));
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

            baseConfig.GetString("akka.persistence.journal.plugin").Should().Be("akka.persistence.journal.custom");

            var config = baseConfig.GetConfig("akka.persistence.journal.custom");
            config.Should().NotBeNull();
            config.GetBoolean("auto-initialize").Should().Be(options.AutoInitialize);
            config.GetString("configuration-string").Should().Be(options.ConfigurationString);
            config.GetString("key-prefix").Should().Be(options.KeyPrefix);
            config.GetInt("database").Should().Be(options.Database);
            config.GetBoolean("use-database-number-from-connection-string").Should().Be(options.UseDatabaseFromConnectionString.Value);
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
            options.Identifier.Should().Be("customRedis");
            options.AutoInitialize.Should().BeTrue();
            options.IsDefaultPlugin.Should().BeFalse();
            options.ConfigurationString.Should().Be("ConfigurationStringFromConfigJson");
            options.KeyPrefix.Should().Be("KeyPrefixFromConfigJson");
            options.Database.Should().Be(123456);
            options.UseDatabaseFromConnectionString.Should().BeTrue();
            options.Serializer.Should().Be("TestSerializer");
        }
    }
}