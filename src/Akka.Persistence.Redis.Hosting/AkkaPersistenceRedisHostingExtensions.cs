using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Akka.Persistence.Redis;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

public static class AkkaPersistenceRedisHostingExtensions
{
    /// <summary>
    /// Adds Akka.Persistence.Redis using a HOCON connection string. The plugin opens its
    /// own <see cref="IConnectionMultiplexer"/> and disposes it when the actor stops.
    /// </summary>
    public static AkkaConfigurationBuilder WithRedisPersistence(
        this AkkaConfigurationBuilder builder,
        string configurationString,
        PersistenceMode mode = PersistenceMode.Both,
        bool autoInitialize = true,
        Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
        Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null,
        string pluginIdentifier = "redis",
        bool isDefaultPlugin = true)
    {
        if (string.IsNullOrWhiteSpace(configurationString))
            throw new ArgumentException("Connection string must not be empty.", nameof(configurationString));

        var (journalOpt, snapshotOpt) = BuildOptions(configurationString, autoInitialize, pluginIdentifier, isDefaultPlugin);
        return ApplyMode(builder, mode, journalOpt, snapshotOpt, journalBuilder, snapshotBuilder);
    }

    /// <summary>
    /// Adds Akka.Persistence.Redis using a pre-built <see cref="IConnectionMultiplexer"/>.
    /// Caller-owned by default — set <paramref name="ownedByPlugin"/> to <see langword="true"/>
    /// to hand off disposal to the plugin.
    /// </summary>
    public static AkkaConfigurationBuilder WithRedisPersistence(
        this AkkaConfigurationBuilder builder,
        IConnectionMultiplexer multiplexer,
        bool ownedByPlugin = false,
        int database = 0,
        string? keyPrefix = null,
        PersistenceMode mode = PersistenceMode.Both,
        bool autoInitialize = true,
        Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
        Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null,
        string pluginIdentifier = "redis",
        bool isDefaultPlugin = true)
    {
        if (multiplexer is null) throw new ArgumentNullException(nameof(multiplexer));

        RegisterMultiplexerSetup(builder, () => Task.FromResult(multiplexer), ownedByPlugin);

        var (journalOpt, snapshotOpt) = BuildOptions(connectionString: string.Empty, autoInitialize, pluginIdentifier, isDefaultPlugin, database, keyPrefix);
        return ApplyMode(builder, mode, journalOpt, snapshotOpt, journalBuilder, snapshotBuilder);
    }

    /// <summary>
    /// Adds Akka.Persistence.Redis using an async factory for the
    /// <see cref="IConnectionMultiplexer"/>. Caller-owned by default. The plugin invokes
    /// the factory once at construction; whether multiple plugins share a multiplexer is
    /// determined entirely by what the factory returns.
    /// </summary>
    public static AkkaConfigurationBuilder WithRedisPersistence(
        this AkkaConfigurationBuilder builder,
        Func<Task<IConnectionMultiplexer>> multiplexerFactory,
        bool ownedByPlugin = false,
        int database = 0,
        string? keyPrefix = null,
        PersistenceMode mode = PersistenceMode.Both,
        bool autoInitialize = true,
        Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
        Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null,
        string pluginIdentifier = "redis",
        bool isDefaultPlugin = true)
    {
        if (multiplexerFactory is null) throw new ArgumentNullException(nameof(multiplexerFactory));

        RegisterMultiplexerSetup(builder, multiplexerFactory, ownedByPlugin);

        var (journalOpt, snapshotOpt) = BuildOptions(connectionString: string.Empty, autoInitialize, pluginIdentifier, isDefaultPlugin, database, keyPrefix);
        return ApplyMode(builder, mode, journalOpt, snapshotOpt, journalBuilder, snapshotBuilder);
    }

    /// <summary>
    /// Adds Akka.Persistence.Redis using configurator delegates. Useful when callers need
    /// to set options that aren't surfaced as named parameters on the other overloads
    /// (e.g. <see cref="RedisJournalOptions.UseDatabaseFromConnectionString"/>). To inject a
    /// multiplexer, register a <see cref="RedisConnectionMultiplexerSetup"/> on the builder
    /// before calling this overload.
    /// </summary>
    public static AkkaConfigurationBuilder WithRedisPersistence(
        this AkkaConfigurationBuilder builder,
        Action<RedisJournalOptions>? journalOptionConfigurator = null,
        Action<RedisSnapshotOptions>? snapshotOptionConfigurator = null,
        bool isDefaultPlugin = true)
    {
        if (journalOptionConfigurator is null && snapshotOptionConfigurator is null)
            throw new ArgumentException(
                $"{nameof(journalOptionConfigurator)} and {nameof(snapshotOptionConfigurator)} could not both be null");

        RedisJournalOptions? journalOptions = null;
        if (journalOptionConfigurator is { })
        {
            journalOptions = new RedisJournalOptions(isDefaultPlugin);
            journalOptionConfigurator(journalOptions);
        }

        RedisSnapshotOptions? snapshotOptions = null;
        if (snapshotOptionConfigurator is { })
        {
            snapshotOptions = new RedisSnapshotOptions(isDefaultPlugin);
            snapshotOptionConfigurator(snapshotOptions);
        }

        return builder.WithRedisPersistence(journalOptions, snapshotOptions);
    }

    /// <summary>
    /// Adds Akka.Persistence.Redis using pre-built option objects. To inject a multiplexer,
    /// register a <see cref="RedisConnectionMultiplexerSetup"/> on the builder before calling
    /// this overload.
    /// </summary>
    public static AkkaConfigurationBuilder WithRedisPersistence(
        this AkkaConfigurationBuilder builder,
        RedisJournalOptions? journalOptions = null,
        RedisSnapshotOptions? snapshotOptions = null,
        Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
        Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null)
    {
        if (journalOptions is null && snapshotOptions is null)
            throw new ArgumentException(
                $"{nameof(journalOptions)} and {nameof(snapshotOptions)} could not both be null");

        // Builder-stage guard: fail before the user moves on if no connection source is
        // available. Either the options carry a HOCON connection string, or the caller
        // already registered a RedisConnectionMultiplexerSetup with builder.AddSetup(...).
        // Without one or the other the plugin has no way to talk to Redis.
        var hasConnectionString =
            !string.IsNullOrWhiteSpace(journalOptions?.ConfigurationString) ||
            !string.IsNullOrWhiteSpace(snapshotOptions?.ConfigurationString);
        var hasSetup = builder.Setups.OfType<RedisConnectionMultiplexerSetup>().Any();
        if (!hasConnectionString && !hasSetup)
            throw new ArgumentException(
                "WithRedisPersistence requires a connection source: set ConfigurationString " +
                "on the options object, or register a RedisConnectionMultiplexerSetup on the " +
                "builder via builder.AddSetup(...) before calling this overload. The " +
                "WithRedisPersistence(connectionString:) / WithRedisPersistence(multiplexer:) / " +
                "WithRedisPersistence(multiplexerFactory:) overloads enforce this at the type level.");

        return (journalOptions, snapshotOptions) switch
        {
            (_, null) =>
                builder
                    .WithJournal(journalOptions, journalBuilder)
                    .AddHocon(RedisPersistence.DefaultConfig(), HoconAddMode.Append),

            (null, _) =>
                builder
                    .WithSnapshot(snapshotOptions, snapshotBuilder)
                    .AddHocon(RedisPersistence.DefaultConfig(), HoconAddMode.Append),

            (_, _) =>
                builder
                    .WithJournalAndSnapshot(journalOptions, snapshotOptions, journalBuilder, snapshotBuilder)
                    .AddHocon(RedisPersistence.DefaultConfig(), HoconAddMode.Append),
        };
    }

    private static (RedisJournalOptions Journal, RedisSnapshotOptions Snapshot) BuildOptions(
        string connectionString,
        bool autoInitialize,
        string pluginIdentifier,
        bool isDefaultPlugin,
        int? database = null,
        string? keyPrefix = null)
    {
        var journal = new RedisJournalOptions(isDefaultPlugin, pluginIdentifier)
        {
            ConfigurationString = connectionString,
            AutoInitialize = autoInitialize,
            Database = database,
            KeyPrefix = keyPrefix,
        };

        var snapshot = new RedisSnapshotOptions(isDefaultPlugin, pluginIdentifier)
        {
            ConfigurationString = connectionString,
            AutoInitialize = autoInitialize,
            Database = database,
            KeyPrefix = keyPrefix,
        };

        return (journal, snapshot);
    }

    private static AkkaConfigurationBuilder ApplyMode(
        AkkaConfigurationBuilder builder,
        PersistenceMode mode,
        RedisJournalOptions journalOpt,
        RedisSnapshotOptions snapshotOpt,
        Action<AkkaPersistenceJournalBuilder>? journalBuilder,
        Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder)
    {
        if (mode == PersistenceMode.SnapshotStore && journalBuilder is { })
            throw new ArgumentException(
                $"{nameof(journalBuilder)} can only be set when {nameof(mode)} is set to either {PersistenceMode.Both} or {PersistenceMode.Journal}");

        return mode switch
        {
            PersistenceMode.Journal => builder.WithRedisPersistence(journalOpt, null, journalBuilder, snapshotBuilder),
            PersistenceMode.SnapshotStore => builder.WithRedisPersistence(null, snapshotOpt, journalBuilder, snapshotBuilder),
            PersistenceMode.Both => builder.WithRedisPersistence(journalOpt, snapshotOpt, journalBuilder, snapshotBuilder),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid PersistenceMode defined.")
        };
    }

    private static void RegisterMultiplexerSetup(
        AkkaConfigurationBuilder builder,
        Func<Task<IConnectionMultiplexer>> factory,
        bool ownedByPlugin)
    {
        if (builder.Setups.OfType<RedisConnectionMultiplexerSetup>().Any())
            throw new InvalidOperationException(
                "A RedisConnectionMultiplexerSetup is already registered on the builder. " +
                "Pass at most one source of multiplexer configuration: either register the " +
                "Setup directly via builder.AddSetup(...), or use one of the WithRedisPersistence " +
                "overloads that takes a multiplexer / multiplexerFactory — not both.");

        builder.Setups.Add(new RedisConnectionMultiplexerSetup(factory, ownedByPlugin));
    }
}
