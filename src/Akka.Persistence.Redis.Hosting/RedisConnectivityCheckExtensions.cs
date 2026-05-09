using System;
using System.Collections.Generic;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

/// <summary>
/// Extension methods for Redis persistence connectivity checks.
/// </summary>
/// <remarks>
/// At probe time the check resolves a <see cref="RedisConnectionMultiplexerSetup"/> from
/// the <see cref="Akka.Actor.ActorSystem"/> when present (so it shares the plugin's
/// multiplexer), and falls back to <see cref="RedisJournalOptions.ConfigurationString"/> /
/// <see cref="RedisSnapshotOptions.ConfigurationString"/> otherwise. Any valid path that
/// successfully configures the plugin will also successfully drive the check.
/// </remarks>
public static class RedisConnectivityCheckExtensions
{
    public static AkkaPersistenceJournalBuilder WithConnectivityCheck(
        this AkkaPersistenceJournalBuilder builder,
        HealthStatus unHealthyStatus = HealthStatus.Unhealthy,
        string? name = null,
        IEnumerable<string>? tags = null)
    {
        var journalOptions = builder.Options as RedisJournalOptions
            ?? throw new InvalidOperationException(
                $"Options must be {nameof(RedisJournalOptions)}");

        return RegisterJournalCheck(builder, journalOptions, unHealthyStatus, name, tags);
    }

    [Obsolete("Use the overload without journalOptions; options are read from builder.Options. This overload will be removed in a future version.")]
    public static AkkaPersistenceJournalBuilder WithConnectivityCheck(
        this AkkaPersistenceJournalBuilder builder,
        RedisJournalOptions journalOptions,
        HealthStatus unHealthyStatus = HealthStatus.Unhealthy,
        string? name = null,
        string[]? tags = null)
    {
        if (journalOptions is null)
            throw new ArgumentNullException(nameof(journalOptions));

        return RegisterJournalCheck(builder, journalOptions, unHealthyStatus, name, tags);
    }

    public static AkkaPersistenceSnapshotBuilder WithConnectivityCheck(
        this AkkaPersistenceSnapshotBuilder builder,
        HealthStatus unHealthyStatus = HealthStatus.Unhealthy,
        string? name = null,
        IEnumerable<string>? tags = null)
    {
        var snapshotOptions = builder.Options as RedisSnapshotOptions
            ?? throw new InvalidOperationException(
                $"Options must be {nameof(RedisSnapshotOptions)}");

        return RegisterSnapshotCheck(builder, snapshotOptions, unHealthyStatus, name, tags);
    }

    [Obsolete("Use the overload without snapshotOptions; options are read from builder.Options. This overload will be removed in a future version.")]
    public static AkkaPersistenceSnapshotBuilder WithConnectivityCheck(
        this AkkaPersistenceSnapshotBuilder builder,
        RedisSnapshotOptions snapshotOptions,
        HealthStatus unHealthyStatus = HealthStatus.Unhealthy,
        string? name = null,
        string[]? tags = null)
    {
        if (snapshotOptions is null)
            throw new ArgumentNullException(nameof(snapshotOptions));

        return RegisterSnapshotCheck(builder, snapshotOptions, unHealthyStatus, name, tags);
    }

    private static AkkaPersistenceJournalBuilder RegisterJournalCheck(
        AkkaPersistenceJournalBuilder builder,
        RedisJournalOptions options,
        HealthStatus unHealthyStatus,
        string? name,
        IEnumerable<string>? tags)
    {
        // ConfigurationString may be empty when the caller supplied a multiplexer or
        // factory via WithRedisPersistence — at probe time the check resolves the Setup
        // first and only falls back to the connection string when none is registered.
        var check = new RedisJournalConnectivityCheck(options.ConfigurationString ?? string.Empty, options.Identifier);

        var registration = new AkkaHealthCheckRegistration(
            name ?? $"Akka.Persistence.Redis.Journal.{options.Identifier}.Connectivity",
            check,
            unHealthyStatus,
            tags ?? new[] { "akka", "persistence", "redis", "journal", "connectivity" });

        return builder.WithCustomHealthCheck(registration);
    }

    private static AkkaPersistenceSnapshotBuilder RegisterSnapshotCheck(
        AkkaPersistenceSnapshotBuilder builder,
        RedisSnapshotOptions options,
        HealthStatus unHealthyStatus,
        string? name,
        IEnumerable<string>? tags)
    {
        var check = new RedisSnapshotStoreConnectivityCheck(options.ConfigurationString ?? string.Empty, options.Identifier);

        var registration = new AkkaHealthCheckRegistration(
            name ?? $"Akka.Persistence.Redis.SnapshotStore.{options.Identifier}.Connectivity",
            check,
            unHealthyStatus,
            tags ?? new[] { "akka", "persistence", "redis", "snapshot-store", "connectivity" });

        return builder.WithCustomHealthCheck(registration);
    }
}
