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
    RedisSnapshotOptions? snapshotOptions = null)
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

  An Action delegate used to configure an `AkkaPersistenceJournalBuilder` instance. Used to configure [Event Adapters](https://getakka.net/articles/persistence/event-adapters.html)

* `journalConfigurator` __Action\<RedisJournalOptions\>__

  An Action delegate to configure a `RedisJournalOptions` instance.

* `snapshotConfigurator` __Action\<RedisSnapshotOptions\>__

  An Action delegate to configure a `RedisSnapshotOptions` instance.

* `journalOptions` __RedisJournalOptions__

  An `RedisJournalOptions` instance to configure the SqlServer journal.

* `snapshotOptions` __RedisSnapshotOptions__

  An `RedisSnapshotOptions` instance to configure the SqlServer snapshot store.

## Example

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