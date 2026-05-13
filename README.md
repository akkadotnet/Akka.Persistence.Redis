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


## Running against Redis Cluster

Redis Cluster failovers shift slot ownership between nodes. During the brief window between a node being demoted to replica and the SE.Redis client refreshing its slot-map cache, writes can be routed to a node that now refuses them (`Command cannot be issued to a replica`). Production deployments need three layers of mitigation, two of which are user-configured and the third of which the plugin does for you automatically.

### 1. Tune the StackExchange.Redis connection string

```
{addresses},abortConnect=false,connectRetry=5,configCheckSeconds=10,syncTimeout=5000,asyncTimeout=5000
```

| Setting | Why |
|---------|-----|
| `abortConnect=false` | Without it, initial connection failures can leave the multiplexer permanently unhealthy. |
| `connectRetry=5` | Number of times SE.Redis retries the initial connection attempt. Helps during transient failover. |
| `configCheckSeconds=10` | How often SE.Redis re-queries cluster topology (`CLUSTER NODES`/`CLUSTER SLOTS`). Default is 60s — cutting to 10s shrinks the stale-topology window after a failover. |
| `syncTimeout` / `asyncTimeout` (`5000` ms) | Per-operation timeouts at the SE.Redis layer. Should be **lower than** the Akka circuit-breaker `call-timeout` so the breaker trips on real hangs rather than waiting on SE.Redis's own timeout. |

> Do **not** add `allowAdmin=true` unless you genuinely need admin commands (`FLUSHDB`, `CONFIG`, etc.). It enables dangerous operations and is not needed for persistence.

### 2. Tune the Akka.Persistence circuit breaker and recovery concurrency

```hocon
akka.persistence {
    max-concurrent-recoveries = 64    # or 128 for large sharded deployments — NOT higher
    journal.redis {
        circuit-breaker {
            call-timeout  = 10s       # do NOT set to 120s
            reset-timeout = 30s
            max-failures  = 10
        }
    }
    snapshot-store.redis {
        circuit-breaker {
            call-timeout  = 10s
            reset-timeout = 30s
            max-failures  = 10
        }
    }
}
```

- **`max-concurrent-recoveries = 64`–`128`** — higher values turn a cluster blip into a recovery storm.
- **`call-timeout = 10s`** — values like 120s are an **amplifier**, not a safety margin. They turn a 30-second cluster failover into a 30-minute outage by holding hundreds of in-flight recoveries open for two minutes each.
- **App-level `Ask` timeouts must be _larger_ than `call-timeout`**, never smaller. Otherwise the `Ask` fails before the journal has any chance to fail fast its own way, and upstream consumers (RabbitMQ requeues, HTTP retries, etc.) hammer the system while persistence is still on its first attempt. Rule of thumb: `Ask timeout ≥ call-timeout × max-failures × 1.5`.

### 3. What the plugin does for you automatically

Two failure modes need different handling:

- **`MOVED` redirects** — handled by StackExchange.Redis itself (since 2.6.86). It proactively refreshes its slot map on a 5-second debounce when it sees a `MOVED` response. No plugin involvement.
- **`Command cannot be issued to a replica`** — runtime refusal from a node that thinks it's a replica. SE.Redis does NOT auto-handle this. The plugin catches it inside the journal and snapshot-store write paths, fires `IConnectionMultiplexer.ConfigureAsync()` in the background to force a topology refresh, and rethrows so the journal's circuit breaker handles retry timing. By the time `reset-timeout` (e.g. 30s) elapses, the refresh has completed and the next attempt sees fresh topology.

No manual restart or topology poke is required.

### 4. Full `ConfigurationOptions` control via `WithRedisPersistence`

The connection string above expresses most of what you need, but some tunings (e.g. an `ExponentialRetry` `ReconnectRetryPolicy`, custom `SocketManager`, explicit `EndPoints`) can only be set programmatically. For those, supply a pre-configured `IConnectionMultiplexer` — see [_Supplying a Pre-Configured IConnectionMultiplexer (Non-Azure)_](#supplying-a-pre-configured-iconnectionmultiplexer-non-azure) below. The plugin's auto-topology-refresh works identically whether you let the plugin own the connection or supply your own.


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

### Azure Managed Redis with Entra ID / Managed Identity

For Azure Managed Redis (`*.redis.azure.net`) or Azure Cache for Redis (`*.redis.cache.windows.net`) deployments, use `WithAzureRedisPersistence(...)`. It auto-detects the Azure host suffix, configures TLS + RESP3, and authenticates via [`Microsoft.Azure.StackExchangeRedis`](https://github.com/Azure/Microsoft.Azure.StackExchangeRedis)'s `ConfigureForAzureWithTokenCredentialAsync` using a `TokenCredential` from `Azure.Identity` (defaulting to `ManagedIdentityCredential`). For non-Azure hosts (e.g. local-dev `localhost:6379`) it falls through to the plain HOCON connection-string path.

```csharp
// One-liner — system-assigned managed identity is detected automatically.
builder.WithAzureRedisPersistence("your-redis.swedencentral.redis.azure.net:10000");

// Pin a user-assigned managed identity by client id.
builder.WithAzureRedisPersistence(
    "your-redis.swedencentral.redis.azure.net:10000",
    credential: new ManagedIdentityCredential("your-client-id"));

// Local development — no Azure suffix, plain HOCON connection-string auth.
builder.WithAzureRedisPersistence("localhost:6379");
```

Add the `Akka.Persistence.Redis.Hosting` package and `using Azure.Identity;` (the package transitively pulls in `Microsoft.Azure.StackExchangeRedis` and `Azure.Identity` for you).

### Supplying a Pre-Configured `IConnectionMultiplexer` (Non-Azure)

For deployments that aren't Azure-managed but still need an `IConnectionMultiplexer` authored programmatically — Redis Sentinel, a custom `ReconnectRetryPolicy` (e.g. `ExponentialRetry`), a tighter `ConfigCheckSeconds` for clustered Redis, or any other `ConfigurationOptions` knob that does not have a connection-string equivalent — pass the multiplexer or factory directly to `WithRedisPersistence`.

`WithRedisPersistence` has three connection-source overloads. Each takes its connection source as a non-optional positional argument, so the compiler enforces that you always provide one:

```csharp
// 1) HOCON connection string. The plugin opens, owns, and disposes the multiplexer.
builder.WithRedisPersistence("your-redis-connection-string");

// 2) Pre-built IConnectionMultiplexer. Caller-owned by default — the plugin will not
//    dispose it on actor shutdown. Use PluginOwned to hand off disposal.
var multiplexer = await ConnectionMultiplexer.ConnectAsync(new ConfigurationOptions
{
    EndPoints = { { "your-redis-host", 6380 } },
    Ssl = true,
    AbortOnConnectFail = false,
    ConfigCheckSeconds = 10,
    ReconnectRetryPolicy = new ExponentialRetry(deltaBackOffMilliseconds: 1000),
});
builder.WithRedisPersistence(multiplexer);

// 3) Async factory. Useful when construction itself is async (custom auth handshakes,
//    deferred bootstrap, etc.). Cache the result in your closure if you want sharing
//    across plugins; an uncached factory gives every plugin its own multiplexer.
var shared = new Lazy<Task<IConnectionMultiplexer>>(
    () => ConnectionMultiplexer.ConnectAsync(configurationOptions),
    LazyThreadSafetyMode.ExecutionAndPublication);
builder.WithRedisPersistence(multiplexerFactory: () => shared.Value);
```

#### Different Redis backends per plugin

When the journal and snapshot store should point at *different* Redis instances — journal events on a write-tuned cluster, snapshots on a separate durable store, or cluster-sharding regions backed by different persistence — give each plugin its own HOCON connection string with a distinct `pluginIdentifier`. Each plugin opens its own multiplexer:

```csharp
builder
    .WithRedisPersistence("redis-events.example.com:6379",
        pluginIdentifier: "events", isDefaultPlugin: false, mode: PersistenceMode.Journal)
    .WithRedisPersistence("redis-snapshots.example.com:6379",
        pluginIdentifier: "snapshots", isDefaultPlugin: false, mode: PersistenceMode.SnapshotStore);
```

Connection injection is plugin-scoped. A source registered for `akka.persistence.journal.events` does not affect `akka.persistence.journal.redis` or any other Redis plugin unless that plugin id is explicitly registered to the same source. Plugins without a registered source fall back to their own HOCON connection string.

#### How it works under the hood

The hosting extension carries multiplexer factories through Akka.NET's typed `ActorSystemSetup` container. The `multiplexer:` and `multiplexerFactory:` overloads register entries in `RedisConnectionMultiplexerSetup` for the journal and/or snapshot plugin ids configured by the call. The journal and snapshot store actors read their plugin-specific entry once at construction. When no entry exists for that plugin id, the plugin opens its own multiplexer from the HOCON connection string and disposes it on `PostStop`. The connectivity health check resolves the same plugin-specific setup entry at probe time, so a single source of configuration drives both the plugin and its liveness probe.

#### Without Akka.Hosting

If you bootstrap the `ActorSystem` directly via `ActorSystem.Create(...)`, build the setup yourself and pass it to the system:

```csharp
var multiplexer = await ConnectionMultiplexer.ConnectAsync(configurationOptions);

var setup = ActorSystemSetup.Create(
    BootstrapSetup.Create().WithConfig(redisHocon),
    new RedisConnectionMultiplexerSetup()
        .Add(
            "akka.persistence.journal.redis",
            () => Task.FromResult<IConnectionMultiplexer>(multiplexer),
            RedisConnectionOwnership.CallerOwned)
        .Add(
            "akka.persistence.snapshot-store.redis",
            () => Task.FromResult<IConnectionMultiplexer>(multiplexer),
            RedisConnectionOwnership.CallerOwned));

var system = ActorSystem.Create("my-system", setup);
```

`RedisConnectionOwnership.CallerOwned` means the plugin will not dispose the multiplexer. Use `RedisConnectionOwnership.PluginOwned` when the factory is purely a construction hook and each plugin should dispose the connection it receives.

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
