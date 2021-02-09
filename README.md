# Akka.Persistence.Redis 

![Akka.NET logo](docs/images/AkkaNetLogo.Normal.png)

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

  # connection string, as described here: https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Configuration.md#basic-configuration-strings
  configuration-string = ""

  # Redis journals key prefixes. Leave it for default or change it to appropriate value. WARNING: don't change it on production instances.
  key-prefix = ""
}  
```

### Configuration
- `configuration-string` - connection string, as described here: https://github.com/StackExchange/StackExchange.Redis/blob/master/docs/Configuration.md#basic-configuration-strings
- `key-prefix` - Redis journals key prefixes. Leave it for default or change it to customized value. WARNING: don't change this value after you've started persisting data in production.

## Snapshot Store
To activate the snapshot plugin, add the following line to your HOCON config:
```
akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.redis"
```
This will run the snapshot-store with its default settings. The default settings can be changed with the configuration properties defined in your HOCON config:


```
akka.persistence.snapshot-store.redis {
  # qualified type name of the Redis persistence journal actor
  class = "Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis"

  # connection string, as described here: https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Configuration.md#basic-configuration-strings
  configuration-string = ""

  # Redis journals key prefixes. Leave it for default or change it to appropriate value. WARNING: don't change it on production instances.
  key-prefix = ""
}  
```

### Configuration
- `configuration-string` - connection string, as described here: https://github.com/StackExchange/StackExchange.Redis/blob/master/docs/Configuration.md#basic-configuration-strings
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