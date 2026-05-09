# ADR-0001: Redis Connection Injection and Azure Authentication

## Status

Proposed

## Scope

This is an internal engineering design record, not product documentation.

All connection-injection APIs discussed here were introduced after the latest released tag, `1.5.67`, and have not shipped. They may be renamed, reshaped, or removed before the next release. API cleanliness and correct plugin behavior take precedence over preserving draft API shapes from PRs stacked after `1.5.67`.

## Context

Akka.Persistence.Redis needs connection injection for scenarios that cannot be represented safely or conveniently as a plain Redis connection string:

- Azure Managed Redis and Azure Cache for Redis with Entra ID authentication.
- Programmatically authored `ConfigurationOptions`, including custom retry policies and shorter topology refresh intervals.
- Redis Sentinel or other deployments that need custom multiplexer construction.
- Pre-built `IConnectionMultiplexer` instances owned by application infrastructure.
- Health checks that must validate the same Redis backend and connection source used by the journal or snapshot store.

Akka.Persistence also supports multiple Redis plugin instances in one `ActorSystem`, using distinct plugin identifiers. A connection source registered for one plugin must not silently override another plugin's HOCON connection string.

## Decision

Redis connection injection is plugin-scoped, not `ActorSystem`-global.

Connection resolution order for each Redis journal or snapshot store is:

1. Use the connection source registered for this plugin id, if one exists.
2. Otherwise, open a multiplexer from this plugin's HOCON connection string.

A setup entry registered for `akka.persistence.journal.redis` must not affect `akka.persistence.journal.events`, `akka.persistence.snapshot-store.redis`, or any other Redis plugin unless those plugin ids are explicitly registered to the same connection source.

## Ownership Rules

Connection ownership must be explicit:

- HOCON connection string: plugin-owned; the journal or snapshot actor opens the multiplexer and disposes it from `PostStop`.
- Caller-supplied `IConnectionMultiplexer`: caller-owned; the plugin never disposes it.
- Caller-supplied factory: caller-owned by default unless explicitly marked plugin-owned.
- Azure helper-created multiplexer: actor-system-owned; shared plugin instances may use it, and it must be disposed exactly once during `ActorSystem` shutdown.

## Azure Authentication Rules

The Azure helper is a convenience layer over plugin-scoped connection injection.

- Detect Azure token-auth candidates by host suffix: `.redis.azure.net` and `.redis.cache.windows.net`.
- If the connection string contains a password, treat it as access-key authentication and do not configure token auth.
- Token-auth connections force TLS and RESP3.
- If `SslHost` is absent, default it from the Redis host in the connection string.
- Accept any `Azure.Core.TokenCredential`; default to managed identity for the one-line Azure hosting path.
- The Azure helper may intentionally register journal and snapshot plugin ids to the same shared connection source, but that sharing must be explicit in the setup entries.

## Rejected Alternatives

- A static or process-global connection registry. This leaks across actor systems and tests.
- A single `RedisConnectionMultiplexerSetup` that applies to every Redis plugin in an `ActorSystem`. This can silently route unrelated plugins to the wrong Redis backend.
- Treating Azure helper-created multiplexers as caller-owned. The caller does not receive the created multiplexer and cannot dispose it.
- Preserving draft post-`1.5.67` API shapes solely for compatibility. They have not shipped.
- Per-options factory properties without clear consistency and ownership rules.

## Consequences

- Setup registration is slightly more explicit because journal and snapshot plugin ids are registered independently.
- Multi-plugin deployments remain safe: HOCON remains the fallback for plugins without a registered connection source.
- Health checks can resolve the same connection source as the plugin they monitor.
- Azure token-auth connections can be shared intentionally and disposed deterministically.

## Acceptance Criteria

Before release:

- Mixed Azure and non-Azure Redis plugins in one `ActorSystem` do not misroute.
- A factory registered for one plugin id does not override another plugin's HOCON connection string.
- Journal and snapshot stores can intentionally share an Azure-created multiplexer.
- Azure helper-created multiplexers are disposed exactly once during `ActorSystem` shutdown.
- Health checks use the same connection source as their corresponding plugin.
- Azure connection strings with a password skip token-auth setup.
- Dependency choices do not introduce unsupported target-framework warnings unless explicitly justified.
