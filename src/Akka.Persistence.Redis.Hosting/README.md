# Akka.Persistence.Redis.Hosting

Akka.Hosting extension methods to add Akka.Persistence.Redis to an ActorSystem

# Akka.Persistence.Redis Extension Methods

## WithRedisPersistence() Method

```csharp
public static AkkaConfigurationBuilder WithRedisPersistence(
    this AkkaConfigurationBuilder builder,
    string configurationString,
    PersistenceMode mode = PersistenceMode.Both,
    bool autoInitialize = true,
    Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
    Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null,
    string pluginIdentifier = "Redis",
    bool isDefaultPlugin = true);
```

```csharp
public static AkkaConfigurationBuilder WithRedisPersistence(
    this AkkaConfigurationBuilder builder,
    Action<RedisJournalOptions>? journalOptionConfigurator = null,
    Action<RedisSnapshotOptions>? snapshotOptionConfigurator = null,
    bool isDefaultPlugin = true)
```

```csharp
public static AkkaConfigurationBuilder WithRedisPersistence(
    this AkkaConfigurationBuilder builder,
    RedisJournalOptions? journalOptions = null,
    RedisSnapshotOptions? snapshotOptions = null
    Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
    Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null)
```

### Parameters

* `configurationString` __string__

  Connection string used for database access. Connection string as described here: https://stackexchange.github.io/StackExchange.Redis/Configuration#basic-configuration-strings.

* `mode` __PersistenceMode__

  Determines which settings should be added by this method call. __Default__: `PersistenceMode.Both`

    * `PersistenceMode.Journal`: Only add the journal settings
    * `PersistenceMode.SnapshotStore`: Only add the snapshot store settings
    * `PersistenceMode.Both`: Add both journal and snapshot store settings

* `autoInitialize` __bool__

  Should the Redis store collection be initialized automatically. __Default__: `false`

* `journalBuilder` __Action\<AkkaPersistenceJournalBuilder\>__

  An Action delegate used to configure an `AkkaPersistenceJournalBuilder` instance. Used to configure health check.

* `snapshotBuilder` __Action\<AkkaPersistenceSnapshotBuilder\>__

  An Action delegate used to configure an `AkkaPersistenceSnapshotBuilder` instance. Used to configure health check.

* `journalConfigurator` __Action\<RedisJournalOptions\>__

  An Action delegate to configure a `RedisJournalOptions` instance.

* `snapshotConfigurator` __Action\<RedisSnapshotOptions\>__

  An Action delegate to configure a `RedisSnapshotOptions` instance.

* `journalOptions` __RedisJournalOptions__

  An `RedisJournalOptions` instance to configure the Redis journal store.

* `snapshotOptions` __RedisSnapshotOptions__

  An `RedisSnapshotOptions` instance to configure the Redis snapshot store.

## Microsoft.Extensions.Diagnostics.HealthChecks Integration

Akka.Persistence.Redis.Hosting includes built-in health check support for Redis persistence plugins through the `WithHealthCheck()` extension methods. These health checks integrate with [Microsoft.Extensions.Diagnostics.HealthChecks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks) to monitor the health of your Redis journal and snapshot stores.

### Built-in Health Checks

All health checks are tagged with `akka`, `persistence`, and `mongodb` for easy filtering.

### Configuring Health Checks

You can add health checks when configuring Redis persistence using the `WithHealthCheck()` method:

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
        snapshotBuilder: snapshot.WithHealthCheck(HealthStatus.Degraded));
```
