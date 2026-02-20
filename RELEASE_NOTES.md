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
