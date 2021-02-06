# Akka.Persistence.Redis 

[![NuGet Version](http://img.shields.io/nuget/v/Akka.Persistence.Redis.svg?style=flat)](https://www.nuget.org/packages/Akka.Persistence.Redis)

Akka Persistence Redis Plugin is a plugin for `Akka persistence` that provides several components:
 - a journal store ;
 - a snapshot store ;
 - a journal query interface implementation.

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

## Journal plugin
To activate the journal plugin, add the following line to your HOCON config:
```
akka.persistence.journal.plugin = "akka.persistence.journal.redis"
```
This will run the journal with its default settings. The default settings can be changed with the configuration properties defined in your HOCON config:

### Configuration
- `configuration-string` - connection string, as described here: https://github.com/StackExchange/StackExchange.Redis/blob/master/docs/Configuration.md#basic-configuration-strings
- `key-prefix` - Redis journals key prefixes. Leave it for default or change it to appropriate value. WARNING: don't change it on production instances.

## Snapshot config
To activate the snapshot plugin, add the following line to your HOCON config:
```
akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.redis"
```
This will run the snapshot-store with its default settings. The default settings can be changed with the configuration properties defined in your HOCON config:

### Configuration
- `configuration-string` - connection string, as described here: https://github.com/StackExchange/StackExchange.Redis/blob/master/docs/Configuration.md#basic-configuration-strings
- `key-prefix` - Redis journals key prefixes. Leave it for default or change it to appropriate value. WARNING: don't change it on production instances.

## Persistence Query

The plugin supports the following queries:

### PersistenceIdsQuery and CurrentPersistenceIdsQuery

`PersistenceIds` and `CurrentPersistenceIds` are used for retrieving all persistenceIds of all persistent actors.
```C#
var readJournal = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);

Source<string, NotUsed> willNotCompleteTheStream = readJournal.PersistenceIds();
Source<string, NotUsed> willCompleteTheStream = readJournal.CurrentPersistenceIds();
```
The returned event stream is unordered and you can expect different order for multiple executions of the query.

When using the `PersistenceIds` query, the stream is not completed when it reaches the end of the currently used `persistenceIds`, but it continues to push new `persistenceIds` when new persistent actors are created.

When using the `CurrentPersistenceIds` query, the stream is completed when the end of the current list of `persistenceIds` is reached, thus it is not a live query.

The stream is completed with failure if there is a failure in executing the query in the backend journal.

### EventsByPersistenceIdQuery and CurrentEventsByPersistenceIdQuery

`EventsByPersistenceId` and `CurrentEventsByPersistenceId` is used for retrieving events for a specific `PersistentActor` identified by `persistenceId`.
```C#
import akka.actor.ActorSystem
import akka.stream.{Materializer, ActorMaterializer}
import akka.stream.scaladsl.Source
import akka.persistence.query.{ PersistenceQuery, EventEnvelope }
import akka.persistence.jdbc.query.scaladsl.JdbcReadJournal

implicit val system: ActorSystem = ActorSystem()
implicit val mat: Materializer = ActorMaterializer()(system)
val readJournal: JdbcReadJournal = PersistenceQuery(system).readJournalFor[JdbcReadJournal](JdbcReadJournal.Identifier)

val willNotCompleteTheStream: Source[EventEnvelope, NotUsed] = readJournal.eventsByPersistenceId("some-persistence-id", 0L, Long.MaxValue)

val willCompleteTheStream: Source[EventEnvelope, NotUsed] = readJournal.currentEventsByPersistenceId("some-persistence-id", 0L, Long.MaxValue)


var readJournal = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);

Source<EventEnvelope, NotUsed> willNotCompleteTheStream = queries.EventsByPersistenceId("some-persistence-id", 0L, long.MaxValue);
Source<EventEnvelope, NotUsed> willCompleteTheStream = queries.CurrentEventsByPersistenceId("some-persistence-id", 0L, long.MaxValue);
```
You can retrieve a subset of all events by specifying `fromSequenceNr` and `toSequenceNr` or use `0L` and `long.MaxValue` respectively to retrieve all events. Note that the corresponding sequence number of each event is provided in the `EventEnvelope`, which makes it possible to resume the stream at a later point from a given sequence number.

The returned event stream is ordered by sequence number, i.e. the same order as the `PersistentActor` persisted the events. The same prefix of stream elements (in same order) are returned for multiple executions of the query, except for when events have been deleted.

The stream is completed with failure if there is a failure in executing the query in the backend journal.

### EventsByTag and CurrentEventsByTag

`EventsByTag` and `CurrentEventsByTag` are used for retrieving events that were marked with a given tag, e.g. all domain events of an Aggregate Root type.
```C#
var readJournal = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);

Source<EventEnvelope, NotUsed> willNotCompleteTheStream = queries.EventsByTag("apple", 0L);
Source<EventEnvelope, NotUsed> willCompleteTheStream = queries.CurrentEventsByTag("apple", 0L);
```

### Tagging Events
To tag events you'll need to create an Event Adapter that will wrap the event in a akka.persistence.journal.Tagged class with the given tags. The Tagged class will instruct akka-persistence-jdbc to tag the event with the given set of tags.

The persistence plugin will not store the Tagged class in the journal. It will strip the tags and payload from the Tagged class, and use the class only as an instruction to tag the event with the given tags and store the payload in the message field of the journal table.
```
public class ColorTagger : IWriteEventAdapter
{
    public string Manifest(object evt) => string.Empty;
    internal Tagged WithTag(object evt, string tag) => new Tagged(evt, ImmutableHashSet.Create(tag));

    public object ToJournal(object evt)
    {
        switch (evt)
        {
            case string s when s.Contains("green"):
                return WithTag(evt, "green");
            case string s when s.Contains("black"):
                return WithTag(evt, "black");
            case string s when s.Contains("blue"):
                return WithTag(evt, "blue");
            default:
                return evt;
        }
    }
}
```
The EventAdapter must be registered by adding the following to the root of `application.conf` Please see the demo-akka-persistence-jdbc project for more information.
```
akka.persistence.journal.redis {
    event-adapters {
        color-tagger  = "Akka.Persistence.Redis.Tests.Query.ColorTagger, Akka.Persistence.Redis.Tests"
    }
    event-adapter-bindings = {
        "System.String" = color-tagger
    }
}
```
You can retrieve a subset of all events by specifying `offset`, or use `0L` to retrieve all events with a given tag. The `offset` corresponds to an ordered sequence number for the specific tag. Note that the corresponding offset of each event is provided in the `EventEnvelope`, which makes it possible to resume the stream at a later point from a given `offset`.

In addition to the `offset` the `EventEnvelope` also provides `persistenceId` and `sequenceNr` for each event. The `sequenceNr` is the sequence number for the persistent actor with the `persistenceId` that persisted the event. The `persistenceId` + `sequenceNr` is an unique identifier for the event.

The returned event stream contains only events that correspond to the given tag, and is ordered by the creation time of the events. The same stream elements (in same order) are returned for multiple executions of the same query. Deleted events are not deleted from the tagged event stream.

## Serialization
Akka Persistence provided serializers wrap the user payload in an envelope containing all persistence-relevant information. Redis Journal uses provided Protobuf serializers for the wrapper types (e.g. `IPersistentRepresentation`), then the payload will be serialized using the user configured serializer. By default, the payload will be serialized using JSON.NET serializer. This is fine for testing and initial phases of your development (while you’re still figuring out things and the data will not need to stay persisted forever). However, once you move to production you should really pick a different serializer for your payloads.

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

## Securing your Redis server
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

### Minimum command set
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

## Maintainer
- [alexvaluyskiy](https://github.com/alexvaluyskiy)
