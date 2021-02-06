#### 1.4.16 February 6th 2021 ####
This is a major update to the Akka.Persistence.Redis plugin.

**Enabled Redis Cluster Support**
Akka.Persistence.Redis will now automatically detect whether or not you are running in clustered mode via your Redis connection string and will distribute journal entries and snapshots accordingly.

All journal entries and all snapshots for a single entity will all reside inside the same Redis host cost - [using Redis' consistent hash distribution tagging](https://redis.io/topics/cluster-tutorial) scheme.

**Significant Performance Improvements**
Akka.Persistence.Redis' write throughput was improved significantly in Akka.Persistence.Redis v1.4.16:

| Test            | Akka.Persistence.Redis v1.4.4 (msg/s) | current PR (msg/s) |
|-----------------|---------------------------------------|--------------------|
| Persist         | 782                                   | 772                |
| PersistAll      | 15019                                 | 20275              |
| PersistAsync    | 9496                                  | 13131              |
| PersistAllAsync | 32765                                 | 44776              |
| PersistGroup10  | 611                                   | 6523               |
| PersistGroup100 | 8878                                  | 12533              |
| PersistGroup200 | 9598                                  | 12214              |
| PersistGroup25  | 9209                                  | 10819              |
| PersistGroup400 | 9209                                  | 11824              |
| PersistGroup50  | 9506                                  | 9704               |
| Recovering      | 17374                                 | 20119              |
| Recovering8     | 36915                                 | 37290              |
| RecoveringFour  | 22432                                 | 20884              |
| RecoveringTwo   | 22209                                 | 21222              |

These numbers were generated running a single Redis instance inside a Docker container on Docker for Windows - real-world values generated in cloud environments will likely be much higher.

**Removed Akka.Persistence.Query Support**
In order to achieve support for clustering and improved write performance, we made the descision to drop Akka.Persistence.Query support from Akka.Persistence.Redis at this time - if you wish to learn more about our decision-making process or if you are affected by this change, please comment on this thread here: https://github.com/akkadotnet/Akka.Persistence.Redis/issues/126

**Other Changes**

- Bump [Akka.NET to version 1.4.16](https://github.com/akkadotnet/akka.net/releases/tag/1.4.16)
- Modernized Akka.NET Serialization calls
- [Added benchmarks](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/118)
- Upgraded to [StackExchange.Redis 2.2.11](https://github.com/StackExchange/StackExchange.Redis/blob/main/docs/ReleaseNotes.md)
- Improved documentation