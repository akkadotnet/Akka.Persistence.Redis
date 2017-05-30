# Akka.Persistence.Redis [![Build status](https://ci.appveyor.com/api/projects/status/ihgbe6g07hdvxaya/branch/dev?svg=true)](https://ci.appveyor.com/project/ravengerUA/akka-persistence-redis/branch/dev)

Akka Persistence journal and snapshot store backed by Redis database.
Based on https://github.com/StackExchange/StackExchange.Redis library.

## Installation
From `Nuget Package Manager`
```
Install-Package Akka.Persistence.Redis
```
From `.NET CLI`
```
dotnet add package Akka.Persistence.Redis
```

## Configuration

Both journal and snapshot store share the same configuration keys (however they resides in separate scopes, so they are defined distinctly for either journal or snapshot store):

Remember that connection string must be provided separately to Journal and Snapshot Store.

```hocon
akka.persistence {
    journal {
        plugin = "akka.persistence.journal.redis"
        redis {
            # qualified type name of the Redis persistence journal actor
            class = "Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis"

            # connection string, as described here: https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Configuration.md#basic-configuration-strings
            configuration-string = ""

            # dispatcher used to drive journal actor
            plugin-dispatcher = "akka.actor.default-dispatcher"

            #Redis journals key prefixes. Leave it for default or change it to appropriate value. WARNING: don't change it on production instances.
            key-prefix = "akka:persistence:journal"
        }
    }    

    snapshot-store {
        plugin = "akka.persistence.snapshot-store.redis"
        redis {
            # qualified type name of the Redis persistence snapshot storage actor
            class = "Akka.Persistence.Redis.Snapshot.RedisSnapshotStore, Akka.Persistence.Redis"

            # connection string, as described here: https://github.com/StackExchange/StackExchange.Redis/blob/master/Docs/Configuration.md#basic-configuration-strings
            configuration-string = ""

            # dispatcher used to drive snapshot storage actor
            plugin-dispatcher = "akka.actor.default-dispatcher"

            #Redis storage key prefixes. Leave it for default or change it to appropriate value. WARNING: don't change it on production instances.
            key-prefix = "akka:persistence:snapshots"
        }
    }
}
```

## Persistence Query

The plugin supports the following queries:

### AllPersistenceIdsQuery and CurrentPersistenceIdsQuery

`AllPersistenceIds` and `CurrentPersistenceIds` are used for retrieving all persistenceIds of all persistent actors.
```C#
var readJournal = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);

Source<string, NotUsed> willNotCompleteTheStream = readJournal.AllPersistenceIds();
Source<string, NotUsed> willCompleteTheStream = readJournal.CurrentPersistenceIds();
```
The returned event stream is unordered and you can expect different order for multiple executions of the query.

When using the `AllPersistenceIds` query, the stream is not completed when it reaches the end of the currently used `persistenceIds`, but it continues to push new `persistenceIds` when new persistent actors are created.

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
    public static readonly IImmutableSet<string> Colors = ImmutableHashSet.CreateRange(new[] { "green", "black", "blue" });
    public string Manifest(object evt) => string.Empty;

    public object ToJournal(object evt)
    {
        var s = evt as string;
        if (s != null)
        {
            var tags = Colors.Aggregate(ImmutableHashSet<string>.Empty, (acc, color) => s.Contains(color) ? acc.Add(color) : acc);
            return tags.IsEmpty
                ? evt
                : new Tagged(evt, tags);
        }
        else return evt;
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
The events and snapshots are stored as Json documents via default NewtonsoftJsonSerializer. If you want to change the serialization format, you should change HOCON settings
```hocon
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