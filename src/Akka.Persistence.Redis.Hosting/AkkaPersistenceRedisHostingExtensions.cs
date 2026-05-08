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
    /// Adds Akka.Persistence.Redis support to this <see cref="ActorSystem"/>.
    /// </summary>
    /// <param name="builder">The builder instance being configured.</param>
    /// <param name="configurationString">
    /// Connection string as described here: https://stackexchange.github.io/StackExchange.Redis/Configuration#basic-configuration-strings.
    /// </param>
    /// <param name="multiplexer">
    /// Optional pre-built <see cref="IConnectionMultiplexer"/>. When supplied, every Redis
    /// journal and snapshot store in this <see cref="ActorSystem"/> uses this instance
    /// instead of opening one from <paramref name="configurationString"/>. Treated as
    /// caller-owned by default — see <paramref name="ownedByPlugin"/>. Mutually exclusive
    /// with <paramref name="multiplexerFactory"/>.
    /// </param>
    /// <param name="multiplexerFactory">
    /// Optional async factory for the <see cref="IConnectionMultiplexer"/>. Each plugin
    /// instance invokes the factory once during construction; whether multiple plugins
    /// receive the same multiplexer is determined entirely by the factory (a closure-cached
    /// delegate shares; an uncached delegate gives every plugin its own). Mutually exclusive
    /// with <paramref name="multiplexer"/>.
    /// </param>
    /// <param name="ownedByPlugin">
    /// When <see langword="false"/> (the default) and a <paramref name="multiplexer"/> or
    /// <paramref name="multiplexerFactory"/> is supplied, the plugin treats the multiplexer
    /// as caller-owned and never disposes it. Set to <see langword="true"/> to hand off
    /// disposal to the plugin (useful when the factory is purely a construction hook).
    /// Ignored when neither <paramref name="multiplexer"/> nor <paramref name="multiplexerFactory"/>
    /// is supplied — the HOCON-only path is always plugin-owned.
    /// </param>
    /// <param name="mode">Determines which settings should be added by this method call. Default <see cref="PersistenceMode.Both"/>.</param>
    /// <param name="autoInitialize">Should the redis store table be initialized automatically. Default <c>true</c>.</param>
    /// <param name="journalBuilder">Optional configurator for an <see cref="AkkaPersistenceJournalBuilder"/>.</param>
    /// <param name="snapshotBuilder">Optional configurator for an <see cref="AkkaPersistenceSnapshotBuilder"/>.</param>
    /// <param name="pluginIdentifier">The configuration identifier for the plugins. Default <c>"redis"</c>.</param>
    /// <param name="isDefaultPlugin">Whether this plugin is the default for the <see cref="ActorSystem"/>. Default <c>true</c>.</param>
    public static AkkaConfigurationBuilder WithRedisPersistence(
        this AkkaConfigurationBuilder builder,
        string configurationString,
        IConnectionMultiplexer? multiplexer = null,
        Func<Task<IConnectionMultiplexer>>? multiplexerFactory = null,
        bool ownedByPlugin = false,
        PersistenceMode mode = PersistenceMode.Both,
        bool autoInitialize = true,
        Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
        Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null,
        string pluginIdentifier = "redis",
        bool isDefaultPlugin = true)
    {
        if (mode == PersistenceMode.SnapshotStore && journalBuilder is { })
            throw new Exception(
                $"{nameof(journalBuilder)} can only be set when {nameof(mode)} is set to either {PersistenceMode.Both} or {PersistenceMode.Journal}");

        if (multiplexer is not null && multiplexerFactory is not null)
            throw new ArgumentException(
                $"Set at most one of {nameof(multiplexer)} or {nameof(multiplexerFactory)}, not both.");

        RegisterMultiplexerSetup(builder, multiplexer, multiplexerFactory, ownedByPlugin);

        var journalOpt = new RedisJournalOptions(isDefaultPlugin, pluginIdentifier)
        {
            ConfigurationString = configurationString,
            AutoInitialize = autoInitialize,
        };

        var snapshotOpt = new RedisSnapshotOptions(isDefaultPlugin, pluginIdentifier)
        {
            ConfigurationString = configurationString,
            AutoInitialize = autoInitialize,
        };

        return mode switch
        {
            PersistenceMode.Journal => builder.WithRedisPersistence(journalOpt, null, journalBuilder, snapshotBuilder),
            PersistenceMode.SnapshotStore => builder.WithRedisPersistence(null, snapshotOpt, journalBuilder, snapshotBuilder),
            PersistenceMode.Both => builder.WithRedisPersistence(journalOpt, snapshotOpt, journalBuilder, snapshotBuilder),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid PersistenceMode defined.")
        };
    }

    /// <summary>
    /// Adds Akka.Persistence.Redis support to this <see cref="ActorSystem"/>. At least one of
    /// the configurator delegates needs to be populated.
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
    /// Adds Akka.Persistence.Redis support using pre-built option objects. To inject a
    /// custom <see cref="IConnectionMultiplexer"/>, register a
    /// <see cref="RedisConnectionMultiplexerSetup"/> on the builder before calling this
    /// overload, or use the connection-string overload with the <c>multiplexer</c> /
    /// <c>multiplexerFactory</c> parameter.
    /// </summary>
    public static AkkaConfigurationBuilder WithRedisPersistence(
        this AkkaConfigurationBuilder builder,
        RedisJournalOptions? journalOptions = null,
        RedisSnapshotOptions? snapshotOptions = null,
        Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
        Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null)
    {
        return (journalOptions, snapshotOptions) switch
        {
            (null, null) =>
                throw new ArgumentException(
                    $"{nameof(journalOptions)} and {nameof(snapshotOptions)} could not both be null"),

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

    internal static void RegisterMultiplexerSetup(
        AkkaConfigurationBuilder builder,
        IConnectionMultiplexer? multiplexer,
        Func<Task<IConnectionMultiplexer>>? multiplexerFactory,
        bool ownedByPlugin)
    {
        Func<Task<IConnectionMultiplexer>>? factory = multiplexerFactory;
        if (factory is null && multiplexer is not null)
        {
            var instance = multiplexer;
            factory = () => Task.FromResult(instance);
        }

        if (factory is null)
            return;

        // First-write-wins: don't clobber a Setup the caller already registered explicitly.
        if (builder.Setups.OfType<RedisConnectionMultiplexerSetup>().Any())
            return;

        builder.Setups.Add(new RedisConnectionMultiplexerSetup(factory, ownedByPlugin));
    }
}
