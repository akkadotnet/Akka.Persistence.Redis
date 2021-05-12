using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Sharding;
using Akka.Configuration;
using Akka.Persistence.Redis.Journal;
using Akka.Persistence.TCK;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests
{
    public class RedisMultiConfigurationSpec : PluginSpec
    {
        private static Config _config = ConfigurationFactory.ParseString(@"
akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
akka.cluster.sharding {
  journal-plugin-id = ""akka.persistence.journal.sharding""
  snapshot-plugin-id = ""akka.persistence.snapshot-store.sharding""
}
akka.persistence {
  journal {
    plugin = ""akka.persistence.journal.redis""
    redis {
      class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
      configuration-string = ""example""
      plugin-dispatcher = ""akka.actor.default-dispatcher""
      key-prefix = ""akka:persistence:journal""
    }
    sharding {
      class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
      configuration-string = ""example""
      plugin-dispatcher = ""akka.actor.default-dispatcher""
      key-prefix = ""akka:persistence:sharding:journal""
    }
  }
  snapshot-store {
    plugin = ""akka.persistence.snapshot-store.redis""
    redis {
      class = ""Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis""
      configuration-string = ""example""
      plugin-dispatcher = ""akka.actor.default-dispatcher""
      key-prefix = ""akka:persistence:snapshot""
    }                    
    sharding {
      class = ""Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis""
      configuration-string = ""example""
      plugin-dispatcher = ""akka.actor.default-dispatcher""
      key-prefix = ""akka:persistence:sharding:snapshot""
    }
  }
}");

        public RedisMultiConfigurationSpec(ITestOutputHelper output)
        : base(_config, nameof(RedisMultiConfigurationSpec), output)
        { }

        [Fact]
        public void PluginMustBeAbleToUseDifferentConfigurationForPersistence()
        {
            var cluster = ClusterSharding.Get(Sys);
            cluster.Settings.JournalPluginId.Should().Be("akka.persistence.journal.sharding");
            cluster.Settings.SnapshotPluginId.Should().Be("akka.persistence.snapshot-store.sharding");

            var persistence = Persistence.Instance.Get(Sys);
            
            var @ref = persistence.JournalFor("akka.persistence.journal.redis");
            @ref.Should().NotBeNull();

            @ref = persistence.SnapshotStoreFor("akka.persistence.snapshot-store.redis");
            @ref.Should().NotBeNull();
            
            @ref = persistence.JournalFor("akka.persistence.journal.sharding");
            @ref.Should().NotBeNull();

            @ref = persistence.SnapshotStoreFor("akka.persistence.snapshot-store.sharding");
            @ref.Should().NotBeNull();
        }
    }
}
