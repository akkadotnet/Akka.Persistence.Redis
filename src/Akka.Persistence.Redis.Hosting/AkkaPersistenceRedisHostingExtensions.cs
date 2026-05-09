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
    /// Caller-owned by default — set <paramref name="ownership"/> to
    /// <see cref="RedisConnectionOwnership.PluginOwned"/> to hand off disposal to the plugin.
    /// </summary>
    public static AkkaConfigurationBuilder WithRedisPersistence(
        this AkkaConfigurationBuilder builder,
        IConnectionMultiplexer multiplexer,
        RedisConnectionOwnership ownership = RedisConnectionOwnership.CallerOwned,
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

        var (journalOpt, snapshotOpt) = BuildOptions(connectionString: string.Empty, autoInitialize, pluginIdentifier, isDefaultPlugin, database, keyPrefix);
        RegisterMultiplexerSetup(builder, mode, journalOpt, snapshotOpt, () => Task.FromResult(multiplexer), ownership);
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
        RedisConnectionOwnership ownership = RedisConnectionOwnership.CallerOwned,
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

        var (journalOpt, snapshotOpt) = BuildOptions(connectionString: string.Empty, autoInitialize, pluginIdentifier, isDefaultPlugin, database, keyPrefix);
        RegisterMultiplexerSetup(builder, mode, journalOpt, snapshotOpt, multiplexerFactory, ownership);
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

        if (!HasConnectionSource(builder, journalOptions) || !HasConnectionSource(builder, snapshotOptions))
            throw new ArgumentException(
                "Each Redis persistence plugin requires a connection source: set ConfigurationString " +
                "on the options object, or register a RedisConnectionMultiplexerSetup entry for " +
                "that plugin id before calling this overload.");

        if (snapshotOptions is null)
            return builder
                .WithJournal(journalOptions!, journalBuilder)
                .AddHocon(RedisPersistence.DefaultConfig(), HoconAddMode.Append);

        if (journalOptions is null)
            return builder
                .WithSnapshot(snapshotOptions, snapshotBuilder)
                .AddHocon(RedisPersistence.DefaultConfig(), HoconAddMode.Append);

        return builder
            .WithJournalAndSnapshot(journalOptions, snapshotOptions, journalBuilder, snapshotBuilder)
            .AddHocon(RedisPersistence.DefaultConfig(), HoconAddMode.Append);
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

    internal static void RegisterMultiplexerSetup(
        AkkaConfigurationBuilder builder,
        PersistenceMode mode,
        RedisJournalOptions journalOptions,
        RedisSnapshotOptions snapshotOptions,
        Func<Task<IConnectionMultiplexer>> factory,
        RedisConnectionOwnership ownership)
    {
        var setup = GetOrCreateSetup(builder);

        if (mode is PersistenceMode.Journal or PersistenceMode.Both)
            setup.Add(journalOptions.PluginId, factory, ownership);

        if (mode is PersistenceMode.SnapshotStore or PersistenceMode.Both)
            setup.Add(snapshotOptions.PluginId, factory, ownership);
    }

    private static RedisConnectionMultiplexerSetup GetOrCreateSetup(AkkaConfigurationBuilder builder)
    {
        var setup = builder.Setups.OfType<RedisConnectionMultiplexerSetup>().FirstOrDefault();
        if (setup is not null)
            return setup;

        setup = new RedisConnectionMultiplexerSetup();
        builder.Setups.Add(setup);
        return setup;
    }

    private static bool HasConnectionSource(AkkaConfigurationBuilder builder, RedisJournalOptions? options)
    {
        if (options is null)
            return true;

        if (!string.IsNullOrWhiteSpace(options.ConfigurationString))
            return true;

        return builder.Setups.OfType<RedisConnectionMultiplexerSetup>()
            .Any(setup => setup.TryGetSource(options.PluginId, out _));
    }

    private static bool HasConnectionSource(AkkaConfigurationBuilder builder, RedisSnapshotOptions? options)
    {
        if (options is null)
            return true;

        if (!string.IsNullOrWhiteSpace(options.ConfigurationString))
            return true;

        return builder.Setups.OfType<RedisConnectionMultiplexerSetup>()
            .Any(setup => setup.TryGetSource(options.PluginId, out _));
    }
}
