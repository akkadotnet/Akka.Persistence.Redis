using System;
using System.Collections.Generic;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

/// <summary>
/// Extension methods for Redis persistence connectivity checks
/// </summary>
public static class RedisConnectivityCheckExtensions
{
    /// <summary>
    /// Adds a connectivity check for the Redis journal using the simplified Akka.Hosting 1.5.55.1+ API.
    /// This is a liveness check that proactively verifies database connectivity.
    /// Options are automatically accessed from the builder.
    /// </summary>
    /// <param name="builder">The journal builder</param>
    /// <param name="unHealthyStatus">The status to return when check fails. Defaults to Unhealthy.</param>
    /// <param name="name">Optional name for the health check. Defaults to "Akka.Persistence.Redis.Journal.{id}.Connectivity"</param>
    /// <param name="tags">Optional tags for the health check. Defaults to ["akka", "persistence", "redis", "journal", "connectivity"]</param>
    /// <returns>The journal builder for chaining</returns>
    public static AkkaPersistenceJournalBuilder WithConnectivityCheck(
        this AkkaPersistenceJournalBuilder builder,
        HealthStatus unHealthyStatus = HealthStatus.Unhealthy,
        string? name = null,
        IEnumerable<string>? tags = null)
    {
        // Get options from builder - this is the Akka.Hosting 1.5.55.1 simplified API
        var journalOptions = builder.Options as RedisJournalOptions
            ?? throw new InvalidOperationException(
                $"Options must be {nameof(RedisJournalOptions)}");

        if (string.IsNullOrWhiteSpace(journalOptions.ConfigurationString))
            throw new ArgumentException("ConfigurationString must be set on RedisJournalOptions");

        var registration = new AkkaHealthCheckRegistration(
            name ?? $"Akka.Persistence.Redis.Journal.{journalOptions.Identifier}.Connectivity",
            new RedisJournalConnectivityCheck(journalOptions.ConfigurationString!, journalOptions.Identifier),
            unHealthyStatus,
            tags ?? new[] { "akka", "persistence", "redis", "journal", "connectivity" });

        return builder.WithCustomHealthCheck(registration);
    }

    /// <summary>
    /// Adds a connectivity check for the Redis journal (legacy API for backward compatibility).
    /// This is a liveness check that proactively verifies database connectivity.
    /// </summary>
    /// <param name="builder">The journal builder</param>
    /// <param name="journalOptions">The journal options containing connection details</param>
    /// <param name="unHealthyStatus">The status to return when check fails. Defaults to Unhealthy.</param>
    /// <param name="name">Optional name for the health check. Defaults to "Akka.Persistence.Redis.Journal.{id}.Connectivity"</param>
    /// <param name="tags">Optional tags for the health check. Defaults to ["akka", "persistence", "redis", "journal", "connectivity"]</param>
    /// <returns>The journal builder for chaining</returns>
    [Obsolete("Use the simplified API without passing journalOptions. Options are now automatically accessed from builder.Options. This overload will be removed in a future version.")]
    public static AkkaPersistenceJournalBuilder WithConnectivityCheck(
        this AkkaPersistenceJournalBuilder builder,
        RedisJournalOptions journalOptions,
        HealthStatus unHealthyStatus = HealthStatus.Unhealthy,
        string? name = null,
        string[]? tags = null)
    {
        if (journalOptions is null)
            throw new ArgumentNullException(nameof(journalOptions));

        if (string.IsNullOrWhiteSpace(journalOptions.ConfigurationString))
            throw new ArgumentException("ConfigurationString must be set on RedisJournalOptions", nameof(journalOptions));

        var registration = new AkkaHealthCheckRegistration(
            name ?? $"Akka.Persistence.Redis.Journal.{journalOptions.Identifier}.Connectivity",
            new RedisJournalConnectivityCheck(journalOptions.ConfigurationString, journalOptions.Identifier),
            unHealthyStatus,
            tags ?? new[] { "akka", "persistence", "redis", "journal", "connectivity" });

        return builder.WithCustomHealthCheck(registration);
    }

    /// <summary>
    /// Adds a connectivity check for the Redis snapshot store using the simplified Akka.Hosting 1.5.55.1+ API.
    /// This is a liveness check that proactively verifies database connectivity.
    /// Options are automatically accessed from the builder.
    /// </summary>
    /// <param name="builder">The snapshot builder</param>
    /// <param name="unHealthyStatus">The status to return when check fails. Defaults to Unhealthy.</param>
    /// <param name="name">Optional name for the health check. Defaults to "Akka.Persistence.Redis.SnapshotStore.{id}.Connectivity"</param>
    /// <param name="tags">Optional tags for the health check. Defaults to ["akka", "persistence", "redis", "snapshot-store", "connectivity"]</param>
    /// <returns>The snapshot builder for chaining</returns>
    public static AkkaPersistenceSnapshotBuilder WithConnectivityCheck(
        this AkkaPersistenceSnapshotBuilder builder,
        HealthStatus unHealthyStatus = HealthStatus.Unhealthy,
        string? name = null,
        IEnumerable<string>? tags = null)
    {
        // Get options from builder - this is the Akka.Hosting 1.5.55.1 simplified API
        var snapshotOptions = builder.Options as RedisSnapshotOptions
            ?? throw new InvalidOperationException(
                $"Options must be {nameof(RedisSnapshotOptions)}");

        if (string.IsNullOrWhiteSpace(snapshotOptions.ConfigurationString))
            throw new ArgumentException("ConfigurationString must be set on RedisSnapshotOptions");

        var registration = new AkkaHealthCheckRegistration(
            name ?? $"Akka.Persistence.Redis.SnapshotStore.{snapshotOptions.Identifier}.Connectivity",
            new RedisSnapshotStoreConnectivityCheck(snapshotOptions.ConfigurationString!, snapshotOptions.Identifier),
            unHealthyStatus,
            tags ?? new[] { "akka", "persistence", "redis", "snapshot-store", "connectivity" });

        return builder.WithCustomHealthCheck(registration);
    }

    /// <summary>
    /// Adds a connectivity check for the Redis snapshot store (legacy API for backward compatibility).
    /// This is a liveness check that proactively verifies database connectivity.
    /// </summary>
    /// <param name="builder">The snapshot builder</param>
    /// <param name="snapshotOptions">The snapshot options containing connection details</param>
    /// <param name="unHealthyStatus">The status to return when check fails. Defaults to Unhealthy.</param>
    /// <param name="name">Optional name for the health check. Defaults to "Akka.Persistence.Redis.SnapshotStore.{id}.Connectivity"</param>
    /// <param name="tags">Optional tags for the health check. Defaults to ["akka", "persistence", "redis", "snapshot-store", "connectivity"]</param>
    /// <returns>The snapshot builder for chaining</returns>
    [Obsolete("Use the simplified API without passing snapshotOptions. Options are now automatically accessed from builder.Options. This overload will be removed in a future version.")]
    public static AkkaPersistenceSnapshotBuilder WithConnectivityCheck(
        this AkkaPersistenceSnapshotBuilder builder,
        RedisSnapshotOptions snapshotOptions,
        HealthStatus unHealthyStatus = HealthStatus.Unhealthy,
        string? name = null,
        string[]? tags = null)
    {
        if (snapshotOptions is null)
            throw new ArgumentNullException(nameof(snapshotOptions));

        if (string.IsNullOrWhiteSpace(snapshotOptions.ConfigurationString))
            throw new ArgumentException("ConfigurationString must be set on RedisSnapshotOptions", nameof(snapshotOptions));

        var registration = new AkkaHealthCheckRegistration(
            name ?? $"Akka.Persistence.Redis.SnapshotStore.{snapshotOptions.Identifier}.Connectivity",
            new RedisSnapshotStoreConnectivityCheck(snapshotOptions.ConfigurationString, snapshotOptions.Identifier),
            unHealthyStatus,
            tags ?? new[] { "akka", "persistence", "redis", "snapshot-store", "connectivity" });

        return builder.WithCustomHealthCheck(registration);
    }
}