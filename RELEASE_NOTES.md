#### 1.5.70 July 29th 2026 ####

* Upgraded to [Akka.NET 1.5.70](https://github.com/akkadotnet/akka.net/releases/tag/1.5.70)
* Upgraded to [Akka.Hosting 1.5.70](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.70)
* Event queries by persistence ID remain supported. Persistence-ID enumeration, tag, and all-events queries, including `Offset.FromEnd`, remain unavailable because Redis Cluster does not provide the required global ordering.

#### 1.5.68 June 1st 2026 ####

This is the stable release of the post-1.5.67 Redis hardening work.

**Behavior Changes / Compatibility Notes**

* Removed the legacy query subscriber notification protocol (`ISubscriptionCommand`, `SubscribeNewEvents`, and `NewEventAppended`). Live persistence queries now rely exclusively on polling via `akka.persistence.query.journal.redis.refresh-interval`. This removes a dead actor-local optimization that could leak subscribers and race with journal writes. Users that need lower live-query latency can reduce the Redis query `refresh-interval`.
* Redis journal and snapshot-store connections are now created during plugin actor construction instead of lazily on first database access. This fixes a `ConnectionMultiplexer` lifecycle leak and allows plugin-owned connections to be disposed when the actor stops, but connection failures may now surface earlier during plugin startup.
* Akka.Hosting Redis configuration now validates missing connection sources earlier. Each configured Redis journal or snapshot store must have either a HOCON connection string or a matching `RedisConnectionMultiplexerSetup` entry.

**Improvements**

* Added `WithAzureRedisPersistence(...)` for Azure Managed Redis and Azure Cache for Redis using Entra ID / Managed Identity authentication. The helper configures TLS, RESP3, `SslHost`, and token authentication via `Microsoft.Azure.StackExchangeRedis`.
* Added plugin-scoped `RedisConnectionMultiplexerSetup` support for caller-supplied multiplexers and multiplexer factories. Setup entries are keyed by Redis plugin id, so one Redis plugin's injected connection source does not override another plugin's HOCON connection string.
* Added explicit Redis connection ownership semantics via `RedisConnectionOwnership`: caller-owned, plugin-owned, and actor-system-owned.
* Updated Redis health checks so setup-backed plugins reuse the configured connection source, while HOCON-only plugins open a temporary multiplexer for the probe and dispose it after `PING`.
* Fixed snapshot-only Akka.Hosting configuration so Redis default HOCON is still loaded when only the snapshot store is configured.
* Fixed `DeferAsync` with an empty write batch, which previously could throw from `ContinueWhenAll`.
* Added `CommandFlags.DemandMaster` to Redis write operations as defense-in-depth for primary-only writes.
* Removed the `ConnectionMultiplexer` leak in journal and snapshot actors by tracking connection ownership and disposing plugin-owned connections from `PostStop`.
* Reworked Redis test fixtures to use Testcontainers and hardened cluster readiness / CI failure behavior.
* Added automatic Redis Cluster topology refresh on the "Command cannot be issued to a replica" error that surfaces after a failover demotes a primary. The journal and snapshot store catch this case, fire `IConnectionMultiplexer.ConfigureAsync()` in the background, and rethrow so the existing circuit breaker handles retry timing. Detection uses `IServer.IsReplica` against the endpoint in `Exception.Data["redis-server"]`, with the literal message as a fallback when `ConfigurationOptions.IncludeDetailInExceptions = false`.
* Added a new "Running against Redis Cluster" section to `README.md` covering the recommended SE.Redis connection string, Akka.Persistence circuit-breaker tuning, the `Ask` timeout vs `call-timeout` rule, and which cluster-failover failure modes are handled by StackExchange.Redis vs the plugin.
* Added commented circuit-breaker tuning guidance under the journal and snapshot-store sections of `reference.conf`. No HOCON defaults change.

**Dependencies**

* Upgraded `StackExchange.Redis` to 2.12.14.
* Added `Microsoft.Azure.StackExchangeRedis` 3.3.1 and `Azure.Identity` 1.17.1 to `Akka.Persistence.Redis.Hosting`.
* Updated test/build dependencies including `Microsoft.NET.Test.Sdk`, `coverlet.collector`, `Testcontainers`, and `Microsoft.SourceLink.GitHub`.

#### 1.5.67 April 28th 2026 ####

* Upgraded to [Akka.NET 1.5.67](https://github.com/akkadotnet/akka.net/releases/tag/1.5.67)
* Upgraded to [Akka.Hosting 1.5.67](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.67)

#### 1.5.60 February 10th 2026 ####

* Upgraded to [Akka.NET 1.5.60](https://github.com/akkadotnet/akka.net/releases/tag/1.5.60)
* Upgraded to [Akka.Hosting 1.5.60](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.60)
* Upgraded `Microsoft.Extensions.Hosting` from 6.0.1 to 8.0.0 to align with Akka.Hosting 1.5.60 requirements

#### 1.5.59 December 18th 2025 ####

* Upgraded to [Akka.NET 1.5.59](https://github.com/akkadotnet/akka.net/releases/tag/1.5.59)
* Upgraded to [Akka.Hosting 1.5.59](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.59)

#### 1.5.55.1 October 29th 2025 ####

**Improved API**

This release introduces the simplified Akka.Hosting 1.5.55.1 API for connectivity health checks, eliminating redundant parameter passing:

**New Simplified API (Recommended):**
```csharp
journalBuilder: journal =>
{
    journal.WithConnectivityCheck(); // Options automatically accessed from builder
}
```

**Previous API (Still Supported):**
```csharp
journalBuilder: journal =>
{
    journal.WithConnectivityCheck(journalOptions); // Explicit parameter passing
}
```

The new API automatically accesses options from `builder.Options`, making the code cleaner and less error-prone. The previous API is marked as `[Obsolete]` but remains functional for backward compatibility.

* Update `WithConnectivityCheck()` extension methods to use simplified Akka.Hosting 1.5.55.1 API pattern
* Upgraded to [Akka.Hosting 1.5.55.1](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.55.1)

#### 1.5.55 October 26th 2025 ####

* Upgraded to [Akka.NET 1.5.55](https://github.com/akkadotnet/akka.net/releases/tag/1.5.55)
* Upgraded to [Akka.Hosting 1.5.55](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.55)
* [Add Redis connectivity health checks](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/447)

Adds new `WithConnectivityCheck()` methods for proactive Redis connectivity verification with customizable tags.

#### 1.5.53 October 16th 2025 ####

* Upgraded to [Akka.NET 1.5.53](https://github.com/akkadotnet/akka.net/releases/tag/1.5.53)
* Upgraded to [Akka.Hosting 1.5.53](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.53)
* [Added Microsoft.Extensions.Diagnostics.HealthChecks integration](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/445)
* [Bump Akka.Cluster.Sharding to 1.5.51](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/440)
* [Bump StackExchange.Redis to 2.8.31](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/406)

#### 1.5.42 May 22nd 2025 ####

* Upgraded to [Akka.NET 1.5.42](https://github.com/akkadotnet/akka.net/releases/tag/1.5.37)
* Upgraded to [Akka.Hosting 1.5.42](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.37)

#### 1.5.37 January 23rd 2025 ####

* Upgraded to [Akka.NET 1.5.37](https://github.com/akkadotnet/akka.net/releases/tag/1.5.37)
* Upgraded to [Akka.Hosting 1.5.37](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.37)
* [Bump StackExchange.Redis to 2.8.16](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/359)

#### 1.5.30 October 3rd 2024 ####

* Upgraded to [Akka.NET 1.5.30](https://github.com/akkadotnet/akka.net/releases/tag/1.5.30)
* Upgraded to [Akka.Hosting 1.5.30](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.30)
* [Bump StackExchange.Redis to 2.8.0](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/345)

#### 1.5.29 October 1st 2024 ####

> [!NOTE]
>
> **Deprecated**
>
> Deprecated due to Akka.NET 1.5.29 deprecation. Please use 1.5.30 instead.

* Upgraded to [Akka.NET 1.5.29](https://github.com/akkadotnet/akka.net/releases/tag/1.5.29)
* Upgraded to [Akka.Hosting 1.5.29](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.29)
* [Bump StackExchange.Redis to 2.8.0](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/345)

#### 1.5.24 June 11 2024 ####

* Upgraded to [Akka.NET 1.5.24](https://github.com/akkadotnet/akka.net/releases/tag/1.5.24)
* Upgraded to [Akka.Hosting 1.5.24](https://github.com/akkadotnet/Akka.Hosting/releases/tag/1.5.24)
* [Bump StackExchange.Redis to 2.7.33](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/310) 

#### 1.5.13 October 6 2023 ####

* Upgraded to [Akka.NET 1.5.13](https://github.com/akkadotnet/akka.net/releases/tag/1.5.13)
* [First release of Akka.Persistence.Redis.Hosting v1.5.13](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/283)
* [Bump StackExchange.Redis to 2.6.122](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/269)

#### 1.5.0 March 3 2023 ####
* Upgraded to [Akka.NET 1.5.0](https://github.com/akkadotnet/akka.net/releases/tag/1.5.0)
* Upgraded [StackExchange.Redis 2.6.86](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/232)

#### 1.4.35 March 23 2022 ####
* Fix default database 0 overriding StackExchange.Redis' defaultDatabase in configuration-strings [#194](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/194)
* Upgraded to [Akka.NET 1.4.35](https://github.com/akkadotnet/akka.net/releases/tag/1.4.35)

#### 1.4.31 December 21 2021 ####
* Upgraded to [Akka.NET 1.4.31](https://github.com/akkadotnet/akka.net/releases/tag/1.4.31)
* [Upgraded StackExchange.Redis to 2.2.88](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/179)

#### 1.4.25 September 9 2021 ####
* Upgraded to [Akka.NET 1.4.25](https://github.com/akkadotnet/akka.net/releases/tag/1.4.25)
* [Upgraded StackExchange.Redis to 2.2.62](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/154)

#### 1.4.20 May 14 2021 ####

* Upgraded to [Akka.NET 1.4.20](https://github.com/akkadotnet/akka.net/releases/tag/1.4.20)
* [Journal and snapshot store should obtain their configuration from Persistence](https://github.com/akkadotnet/Akka.Persistence.Redis/pull/147)
