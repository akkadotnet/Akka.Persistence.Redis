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
public static class RedisConnectivityCheckExtensions
{
    /// <summary>
    /// Adds a connectivity check for the Redis journal. The check uses the
    /// <see cref="RedisConnectionMultiplexerSetup"/> registered on the
    /// <see cref="Akka.Actor.ActorSystem"/> when present, otherwise opens a fresh
    /// connection per probe from <see cref="RedisJournalOptions.ConfigurationString"/>.
    /// </summary>
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

    /// <summary>
    /// Legacy overload retained for source compatibility with pre-1.5.55 Akka.Hosting.
    /// </summary>
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

    /// <summary>
    /// Adds a connectivity check for the Redis snapshot store. Same Setup-aware behavior
    /// as the journal check.
    /// </summary>
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

    /// <summary>
    /// Legacy overload retained for source compatibility with pre-1.5.55 Akka.Hosting.
    /// </summary>
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
        if (string.IsNullOrWhiteSpace(options.ConfigurationString))
            throw new ArgumentException(
                $"{nameof(RedisJournalOptions.ConfigurationString)} must be set on {nameof(RedisJournalOptions)}.");

        var check = new RedisJournalConnectivityCheck(options.ConfigurationString, options.Identifier);

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
        if (string.IsNullOrWhiteSpace(options.ConfigurationString))
            throw new ArgumentException(
                $"{nameof(RedisSnapshotOptions.ConfigurationString)} must be set on {nameof(RedisSnapshotOptions)}.");

        var check = new RedisSnapshotStoreConnectivityCheck(options.ConfigurationString, options.Identifier);

        var registration = new AkkaHealthCheckRegistration(
            name ?? $"Akka.Persistence.Redis.SnapshotStore.{options.Identifier}.Connectivity",
            check,
            unHealthyStatus,
            tags ?? new[] { "akka", "persistence", "redis", "snapshot-store", "connectivity" });

        return builder.WithCustomHealthCheck(registration);
    }
}
