# Akka.Persistence.Redis 

![Akka.NET logo](https://github.com/akkadotnet/Akka.Persistence.Redis/blob/dev/docs/images/AkkaNetLogo.Normal.png)

[![NuGet Version](http://img.shields.io/nuget/v/Akka.Persistence.Redis.svg?style=flat)](https://www.nuget.org/packages/Akka.Persistence.Redis)

Akka Persistence Redis Plugin is a plugin for `Akka persistence` that provides several components:
 - a journal store and
 - a snapshot store.

 > NOTE: in Akka.Persistence.Redis v1.4.16 we removed [Akka.Persistence.Query](https://getakka.net/articles/persistence/persistence-query.html) support. Please read more about that decision and comment here: https://github.com/akkadotnet/Akka.Persistence.Redis/issues/126

This plugin stores data in a [redis](https://redis.io) database and based on [Stackexchange.Redis](https://github.com/StackExchange/StackExchange.Redis) library.

## Installation
From `Nuget Package Manager`
```
Install-Package Akka.Persistence.Redis
```
From `.NET CLI`
```
dotnet add package Akka.Persistence.Redis
```

## Journal
To activate the journal plugin, add the following line to your HOCON config:
```
akka.persistence.journal.plugin = "akka.persistence.journal.redis"
```
This will run the journal with its default settings. The default settings can be changed with the configuration properties defined in your HOCON config:

```
akka.persistence.journal.redis {
  # qualified type name of the Redis persistence journal actor
  class = "Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis"

  # connection string, as described here: https://stackexchange.github.io/StackExchange.Redis/Configuration#basic-configuration-strings
  configuration-string = ""

  # Redis journals key prefixes. Leave it for default or change it to appropriate value. WARNING: don't change it on production instances.
  key-prefix = ""
}  
```

### Configuration
- `configuration-string` - connection string, as described here: https://stackexchange.github.io/StackExchange.Redis/Configuration#basic-configuration-strings
- `key-prefix` - Redis journals key prefixes. Leave it for default or change it to customized value. WARNING: don't change this value after you've started persisting data in production.
- `database` - Set the Redis default database to use. If you added `defaultDatabase` to the `connection-strings`, you have to set `database` to the value of `defaultDatabase`.
- `use-database-number-from-connection-string` - determines redis database precedence when a user adds defaultDatabase to the connection-strings. For Redis Cluster, the `defaultDatabase` is 0! See below:

NOTE: Redis Standalone supports deploying multiple instances, but Redis cluster does not. The default database with Redis Cluster is always 0 - If you are deploying Redis Cluster, you don't need to add the `defaultDatabase` to the `connection-string`'! [cluster-spec](https://redis.io/topics/cluster-spec#implemented-subset)

## Snapshot Store
To activate the snapshot plugin, add the following line to your HOCON config:
```
akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.redis"
```
This will run the snapshot-store with its default settings. The default settings can be changed with the configuration properties defined in your HOCON config:


```
akka.persistence.snapshot-store.redis {
  # qualified type name of the Redis persistence journal actor
  class = "Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis"

  # connection string, as described here: https://stackexchange.github.io/StackExchange.Redis/Configuration#basic-configuration-strings
  configuration-string = ""

  # Redis journals key prefixes. Leave it for default or change it to appropriate value. WARNING: don't change it on production instances.
  key-prefix = ""
}  
```

### Configuration
- `configuration-string` - connection string, as described here: https://stackexchange.github.io/StackExchange.Redis/Configuration#basic-configuration-strings
- `key-prefix` - Redis journals key prefixes. Leave it for default or change it to appropriate value. WARNING: don't change it on production instances.

## Security and Access Control
You can secure the Redis server Akka.Persistence.Redis connects to by leveraging Redis ACL and requiring users to use AUTH to connect to the Redis server.

1. Redis ACL
  You can use [Redis ACL](https://redis.io/topics/acl) to:
  - Create users
  - Set user passwords
  - Limit the set of Redis commands a user can use
  - Allow/disallow pub/sub channels
  - Allow/disallow certain keys
  - etc.

2. Redis SSL/TLS
  You can use [redis-cli](https://redis.io/topics/rediscli) to enable SSL/TLS feature in Redis.

3. StackExchange.Redis Connection string
  To connect to ACL enabled Redis server, you will need to set the user and password option in the [connection string](https://stackexchange.github.io/StackExchange.Redis/Configuration#basic-configuration-strings):
  "myServer.net:6380,user=\<username\>,password=\<password\>"

All of these features are supported via `StackExchange.Redis`, which Akka.Persistence.Redis uses internally, and you only need to customize your `akka.persistence.journal.redis.configuration-string` and `akka.persistence.snapshot-store.redis.configuration-string` values to customize it.

### Enabling TLS
For instance, if you want to enable TLS on your Akka.Persistence.Redis instance:

```
akka.persistence.journal.redis.configuration-string = "contoso5.redis.cache.windows.net,ssl=true,password=..."
```

Or if you need to connect to multiple redis instances in a cluster:

```
akka.persistence.journal.redis.configuration-string = "contoso5.redis.cache.windows.net, contoso4.redis.cache.windows.net,ssl=true,password=..."
```

### Enabling ACL
To connect to your redis instance with access control (ACL) support for Akka.Persistence.Redis, all you need to do is specify the user name and password in your connection string and this will restrict the `StackExchange.Redis` client used internally by Akka.Persistence.Redis to whatever permissions you specified in your cluster:

```
akka.persistence.journal.redis.configuration-string = "contoso5.redis.cache.windows.net, contoso4.redis.cache.windows.net,user=akka-persistence,password=..."
```

#### Minimum command set
These are the minimum Redis commands that are needed by Akka.Persistence.Redis to work properly.

| Redis Command    | StackExchange.Redis Command      |
|------------------|----------------------------------|
| MULTI            | Transaction                      |
| EXEC             |                                  |
| DISCARD          |                                  |
| SET              | String Set                       |
| SETNX            |                                  |
| SETEX            |                                  |
| GET              | String Get                       |
| LLEN             | List Length                      |
| LRANGE           | List Range                       |
| RPUSH            | List Right Push                  |
| RPUSHX           |                                  |
| LLEN             |                                  |
| ZADD             | Sorted Set Add                   |
| ZREMRANGEBYSCORE | Delete Sorted Set by Score Range |
| ZREVRANGEBYSCORE | Get Sorted Set by Score Range    |
| ZRANGEBYSCORE    |                                  |
| WITHSCORES       |                                  |
| LIMIT            |                                  |
| SSCAN            | Scan                             |
| SMEMBERS         |                                  |
| PUBSUB           | Pub/Sub                          |
| PING             |                                  |
| UNSUBSCRIBE      |                                  |
| SUBSCRIBE        |                                  |
| PSUBSCRIBE       |                                  |
| PUNSUBSCRIBE     |                                  |
| PUBLISH          | Pub/Sub Publish                  |


## Akka.Hosting Integration

[Akka.Persistence.Redis.Hosting](https://github.com/akkadotnet/Akka.Persistence.Redis/tree/dev/src/Akka.Persistence.Redis.Hosting) provides a set of extension methods for integrating Akka.Persistence.Redis with [Akka.Hosting](https://github.com/akkadotnet/Akka.Hosting), making it easy to configure Redis persistence and health checks using Microsoft's dependency injection and hosting model.

### Installation
From `Nuget Package Manager`
```
Install-Package Akka.Persistence.Redis.Hosting
```
From `.NET CLI`
```
dotnet add package Akka.Persistence.Redis.Hosting
```

### Basic Configuration

```csharp
using var host = new HostBuilder()
    .ConfigureServices((context, services) =>
    {
        services.AddAkka("redisDemo", (builder, provider) =>
        {
            builder
                .WithRedisPersistence("your-redis-connection-string");
        });
    }).Build();

await host.RunAsync();
```

### Supplying a Pre-Configured `IConnectionMultiplexer`

A plain HOCON connection string is enough for most deployments, but some scenarios need an `IConnectionMultiplexer` that has been authored programmatically: Azure Managed Redis with Entra ID / Managed Identity, Redis Sentinel, a custom `ReconnectRetryPolicy` (e.g. `ExponentialRetry`), a tighter `ConfigCheckSeconds` for clustered Redis, or any other `ConfigurationOptions` knob that does not have a connection-string equivalent.

For these cases, set a `ConnectionMultiplexerFactory` on the journal and/or snapshot options. The factory is a `Func<Task<IConnectionMultiplexer>>` that returns the multiplexer the plugin should use. The plugin treats the returned multiplexer as **caller-owned** — it will not dispose it on actor shutdown. Cache the multiplexer in your factory and dispose it yourself when the application terminates.

```csharp
// Construct the multiplexer once at app startup with whatever ConfigurationOptions you need.
var configurationOptions = new ConfigurationOptions
{
    EndPoints = { { "your-redis-host", 6380 } },
    Ssl = true,
    AbortOnConnectFail = false,
    ConfigCheckSeconds = 10,
    ReconnectRetryPolicy = new ExponentialRetry(deltaBackOffMilliseconds: 1000),
};

// (Azure Managed Redis with Entra ID example)
await configurationOptions.ConfigureForAzureWithTokenCredentialAsync(new ManagedIdentityCredential());

var multiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions);

// Most apps share one multiplexer across journal + snapshot store. Pass the same factory
// delegate to both options.
Func<Task<IConnectionMultiplexer>> factory = () => Task.FromResult<IConnectionMultiplexer>(multiplexer);

builder.WithRedisPersistence(
    journalOptions: new RedisJournalOptions
    {
        ConnectionMultiplexerFactory = factory,
    },
    snapshotOptions: new RedisSnapshotOptions
    {
        ConnectionMultiplexerFactory = factory,
    });
```

#### Different Redis backends per plugin

The journal and snapshot store can also point at *different* Redis instances by giving each options object its own factory delegate. This is useful when journal events live on a write-tuned cluster while snapshots live on a separate durable store, or when cluster-sharding regions need different persistence backends, or during a migration cutover where new writes go to one Redis while existing snapshots are still served from another.

```csharp
var journalMultiplexer  = await ConnectionMultiplexer.ConnectAsync(journalConfigOptions);
var snapshotMultiplexer = await ConnectionMultiplexer.ConnectAsync(snapshotConfigOptions);

builder.WithRedisPersistence(
    journalOptions: new RedisJournalOptions
    {
        ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(journalMultiplexer),
    },
    snapshotOptions: new RedisSnapshotOptions
    {
        ConnectionMultiplexerFactory = () => Task.FromResult<IConnectionMultiplexer>(snapshotMultiplexer),
    });
```

Each plugin instance is identified by its plugin id (the full HOCON path, e.g. `akka.persistence.journal.redis`), and factories are routed to the matching plugin at construction time.

#### How it works under the hood

The hosting extension carries factories through Akka.NET's typed `ActorSystemSetup` container. When `journalOptions.ConnectionMultiplexerFactory` and/or `snapshotOptions.ConnectionMultiplexerFactory` is set, `WithRedisPersistence` adds a `MultiRedisConnectionMultiplexerSetup` to the builder's `Setups` list, with one entry per plugin id. The journal and snapshot store actors read this back via `Context.System.Settings.Setup.Get<MultiRedisConnectionMultiplexerSetup>()` and look up the factory keyed by their own `Self.Path.Name` when they first need to connect. Each `ActorSystem` carries its own setup, so multiple systems in the same process can each have their own factory configurations without interfering.

A simpler `RedisConnectionMultiplexerSetup` (single-factory) is also available. When present, it applies to every Redis plugin instance in the system and takes precedence over `MultiRedisConnectionMultiplexerSetup`. The hosting extension always uses the multi-keyed variant; the single variant is intended for code that bootstraps an `ActorSystem` directly without Akka.Hosting (see below).

#### Without Akka.Hosting

If you bootstrap the `ActorSystem` directly via `ActorSystem.Create(...)`, build the setup yourself and pass it to the system. For one shared multiplexer across all Redis plugins:

```csharp
var multiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions);

var setup = ActorSystemSetup.Create(
    BootstrapSetup.Create().WithConfig(redisHocon),
    new RedisConnectionMultiplexerSetup(() => Task.FromResult<IConnectionMultiplexer>(multiplexer)));

var system = ActorSystem.Create("my-system", setup);
```

For per-plugin factories, use `MultiRedisConnectionMultiplexerSetup` and add an entry per plugin id:

```csharp
var multi = new MultiRedisConnectionMultiplexerSetup()
    .AddFactory("akka.persistence.journal.redis",        () => Task.FromResult<IConnectionMultiplexer>(journalMultiplexer))
    .AddFactory("akka.persistence.snapshot-store.redis", () => Task.FromResult<IConnectionMultiplexer>(snapshotMultiplexer));

var setup = ActorSystemSetup.Create(
    BootstrapSetup.Create().WithConfig(redisHocon),
    multi);

var system = ActorSystem.Create("my-system", setup);
```

The journal and snapshot store actors will pick up the setup the moment they need to connect.

### Health Checks

The Hosting package includes built-in connectivity health check support for verifying Redis availability and accessibility. These liveness checks proactively verify that your Redis instance is accessible and responsive by performing PING commands against the configured Redis instance.

#### Enabling Connectivity Health Checks

Enable connectivity health checks by calling `WithHealthCheck()` on the journal and/or snapshot builder:

```csharp
builder
    .WithRedisPersistence(
        journalOptions: new RedisJournalOptions
        {
            ConfigurationString = "your-redis-connection-string",
        },
        snapshotOptions: new RedisSnapshotOptions
        {
            ConfigurationString = "your-redis-connection-string",
        },
        journalBuilder: journal => journal.WithHealthCheck(HealthStatus.Degraded),
        snapshotBuilder: snapshot => snapshot.WithHealthCheck(HealthStatus.Degraded));
```

When enabled, the connectivity health checks will:
- Verify connectivity to the Redis instance
- Test the Redis PING command to ensure responsiveness
- Report `Healthy` when the Redis instance is accessible
- Report `Degraded` or `Unhealthy` (configurable) when the instance is unreachable or unresponsive

Health checks are tagged with `akka`, `persistence`, and `redis` for easy filtering and organization in your health check endpoints.

For ASP.NET Core applications, you can expose these health checks via an endpoint:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add health checks service
builder.Services.AddHealthChecks();

builder.Services.AddAkka("redisDemo", (configBuilder, provider) =>
{
    configBuilder
        .WithRedisPersistence(
            journalOptions: new RedisJournalOptions { ConfigurationString = "your-redis-connection-string" },
            snapshotOptions: new RedisSnapshotOptions { ConfigurationString = "your-redis-connection-string" },
            journalBuilder: journal => journal.WithHealthCheck(),
            snapshotBuilder: snapshot => snapshot.WithHealthCheck());
});

var app = builder.Build();

// Map health check endpoint
app.MapHealthChecks("/healthz");

app.Run();
```

#### Customizing Health Check Tags

You can customize the tags applied to health checks by providing an `IEnumerable<string>` to the `WithHealthCheck()` method:

```csharp
journalBuilder: journal => journal.WithHealthCheck(
    unHealthyStatus: HealthStatus.Degraded,
    name: "redis-journal",
    tags: new[] { "backend", "database", "redis" }),
snapshotBuilder: snapshot => snapshot.WithHealthCheck(
    unHealthyStatus: HealthStatus.Degraded,
    name: "redis-snapshot",
    tags: new[] { "backend", "database", "redis" })
```

When tags are not specified, the default tags are used: `["akka", "persistence", "redis"]` for both journals and snapshot stores.

## Serialization
Akka Persistence provided serializers wrap the user payload in an envelope containing all persistence-relevant information. Redis Journal uses provided Protobuf serializers for the wrapper types (e.g. `IPersistentRepresentation`), then the payload will be serialized using the user configured serializer. 

The payload will be serialized [using Akka.NET's serialization bindings for your events and snapshot objects](https://getakka.net/articles/networking/serialization.html). By default, all `object`s that do not have a specified serializer will use Newtonsoft.Json polymorphic serialization (your CLR types <--> JSON.)

This is fine for testing and initial phases of your development (while you’re still figuring out things and the data will not need to stay persisted forever). However, once you move to production you _should really pick a different serializer for your payloads_.

We highly recommend creating schema-based serialization definitions using MsgPack, Google.Protobuf, or something similar and configuring serialization bindings for those in your configuration: https://getakka.net/articles/networking/serialization.html#usage

Serialization of snapshots and payloads of Persistent messages is configurable with Akka’s Serialization infrastructure. For example, if an application wants to serialize

- payloads of type `MyPayload` with a custom `MyPayloadSerializer` and
- snapshots of type `MySnapshot` with a custom `MySnapshotSerializer`
it must add
```
akka.actor {
  serializers {
    redis = "Akka.Serialization.YourOwnSerializer, YourOwnSerializer"
  }
  serialization-bindings {
    "Akka.Persistence.Redis.Journal.JournalEntry, Akka.Persistence.Redis" = redis
    "Akka.Persistence.Redis.Snapshot.SnapshotEntry, Akka.Persistence.Redis" = redis
  }
}
```

## Running the tests locally

The test suites stand up Redis via [Testcontainers for .NET](https://dotnet.testcontainers.org/), so a working Docker host is required (Docker Desktop, Rancher Desktop, Colima, or Podman all work — Testcontainers auto-detects the socket).

* `Akka.Persistence.Redis.Tests` spins up two `redis:latest` containers and connects to them through a comma-separated host list, exercising the standalone-Redis code paths.
* `Akka.Persistence.Redis.Cluster.Tests` runs the cluster suite against the [`grokzen/redis-cluster:6.0.13`](https://hub.docker.com/r/grokzen/redis-cluster) image. **The image is one Docker container running six `redis-server` processes under supervisord** — three masters + three replicas, on consecutive ports starting from a randomly-chosen base port. The fixture publishes those six ports 1:1 to the host so the cluster's gossiped node addresses match what the SE.Redis client connects to, then probes `CLUSTER INFO` on every endpoint until each node reports `cluster_state:ok` with full 16384-slot coverage before any test runs.

  One implication of the single-container topology: integration tests cannot trigger a real per-node failover by stopping a Docker container, because killing the container kills the whole cluster. Anything that needs to exercise a single primary failover today has to either `docker exec` into the container and signal one of the redis processes (or `supervisorctl stop redis-N`), or use `IServer.Shutdown(...)` against a specific endpoint. A multi-container cluster fixture is the longer-term answer when richer failover coverage is required.
