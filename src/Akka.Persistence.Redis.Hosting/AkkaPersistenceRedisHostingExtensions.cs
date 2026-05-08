using System;
using System.Linq;
using System.Text;
using Akka.Actor;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Akka.Persistence.Redis;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

public static class AkkaPersistenceRedisHostingExtensions
{
    /// <summary>
    ///     Adds Akka.Persistence.Redis support to this <see cref="ActorSystem"/> with optional support
    ///     for health checks on both journal and snapshot store.
    /// </summary>
    /// <param name="builder">
    ///     The builder instance being configured.
    /// </param>
    /// <param name="configurationString">
    ///     Connection string as described here: https://stackexchange.github.io/StackExchange.Redis/Configuration#basic-configuration-strings.
    /// </param>
    /// <param name="mode">
    ///     <para>
    ///         Determines which settings should be added by this method call.
    ///     </para>
    ///     <i>Default</i>: <see cref="PersistenceMode.Both"/>
    /// </param>
    /// <param name="autoInitialize">
    ///     <para>
    ///         Should the redis store table be initialized automatically.
    ///     </para>
    ///     <i>Default</i>: <c>false</c>
    /// </param>
    /// <param name="journalBuilder">
    ///     <para>
    ///         An <see cref="Action"/> used to configure an <see cref="AkkaPersistenceJournalBuilder"/> instance.
    ///     </para>
    ///     <i>Default</i>: <c>null</c>
    /// </param>
    /// <param name="snapshotBuilder">
    ///     <para>
    ///         An <see cref="Action{T}"/> used to configure an <see cref="AkkaPersistenceSnapshotBuilder"/> instance.
    ///     </para>
    ///     <i>Default</i>: <c>null</c>
    /// </param>
    /// <param name="pluginIdentifier">
    ///     <para>
    ///         The configuration identifier for the plugins
    ///     </para>
    ///     <i>Default</i>: <c>"redis"</c>
    /// </param>
    /// <param name="isDefaultPlugin">
    ///     <para>
    ///         A <c>bool</c> flag to set the plugin as the default persistence plugin for the <see cref="ActorSystem"/>
    ///     </para>
    ///     <b>Default</b>: <c>true</c>
    /// </param>
    /// <returns>
    ///     The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     Thrown when <see cref="journalBuilder"/> is set and <see cref="mode"/> is set to
    ///     <see cref="PersistenceMode.SnapshotStore"/>
    /// </exception>
    /// <example>
    /// <code>
    /// builder.WithRedisPersistence(
    ///     configurationString: "...",
    ///     journalBuilder: journal => journal
    ///         .WithHealthCheck(HealthStatus.Degraded),
    ///     snapshotBuilder: snapshot => snapshot
    ///         .WithHealthCheck(HealthStatus.Degraded));
    /// </code>
    /// </example>
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
        if (mode == PersistenceMode.SnapshotStore && journalBuilder is { })
            throw new Exception(
                $"{nameof(journalBuilder)} can only be set when {nameof(mode)} is set to either {PersistenceMode.Both} or {PersistenceMode.Journal}");

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
    ///     Adds Akka.Persistence.Redis support to this <see cref="ActorSystem"/>. At least one of the
    ///     configurator delegate needs to be populated else this method will throw an exception.
    /// </summary>
    /// <param name="builder">
    ///     The builder instance being configured.
    /// </param>
    /// <param name="journalOptionConfigurator">
    ///     <para>
    ///         An <see cref="Action{T}"/> that modifies an instance of <see cref="RedisJournalOptions"/>,
    ///         used to configure the journal plugin
    ///     </para>
    ///     <i>Default</i>: <c>null</c>
    /// </param>
    /// <param name="snapshotOptionConfigurator">
    ///     <para>
    ///         An <see cref="Action{T}"/> that modifies an instance of <see cref="RedisSnapshotOptions"/>,
    ///         used to configure the snapshot store plugin
    ///     </para>
    ///     <i>Default</i>: <c>null</c>
    /// </param>
    /// <param name="isDefaultPlugin">
    ///     <para>
    ///         A <c>bool</c> flag to set the plugin as the default persistence plugin for the <see cref="ActorSystem"/>
    ///     </para>
    ///     <b>Default</b>: <c>true</c>
    /// </param>
    /// <returns>
    ///     The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when both <paramref name="journalOptionConfigurator"/> and <paramref name="snapshotOptionConfigurator"/> are null.
    /// </exception>
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
    ///     Adds Akka.Persistence.Redis support to this <see cref="ActorSystem"/>. At least one of the options
    ///     have to be populated else this method will throw an exception.
    /// </summary>
    /// <param name="builder">
    ///     The builder instance being configured.
    /// </param>
    /// <param name="journalOptions">
    ///     <para>
    ///         An instance of <see cref="RedisJournalOptions"/>, used to configure the journal plugin
    ///     </para>
    ///     <i>Default</i>: <c>null</c>
    /// </param>
    /// <param name="snapshotOptions">
    ///     <para>
    ///         An instance of <see cref="RedisSnapshotOptions"/>, used to configure the snapshot store plugin
    ///     </para>
    ///     <i>Default</i>: <c>null</c>
    /// </param>
    /// <param name="journalBuilder">
    ///     <para>
    ///         An <see cref="Action{T}" /> used to configure an <see cref="AkkaPersistenceJournalBuilder" /> instance for event adapters and health checks.
    ///     </para>
    ///     <i>Default</i>: <c>null</c>
    /// </param>
    /// <param name="snapshotBuilder">
    ///     <para>
    ///         An <see cref="Action{T}" /> used to configure an <see cref="AkkaPersistenceSnapshotBuilder" /> instance for health checks.
    ///     </para>
    ///     <i>Default</i>: <c>null</c>
    /// </param>
    /// <returns>
    ///     The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.
    /// </returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when both <paramref name="journalOptions"/> and <paramref name="snapshotOptions"/> are null.
    /// </exception>
    public static AkkaConfigurationBuilder WithRedisPersistence(
        this AkkaConfigurationBuilder builder,
        RedisJournalOptions? journalOptions = null,
        RedisSnapshotOptions? snapshotOptions = null,
        Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
        Action<AkkaPersistenceSnapshotBuilder>? snapshotBuilder = null)
    {
        // Carry per-plugin factories through ActorSystemSetup, mirroring how Akka.Persistence.Sql
        // ships DataOptions via MultiDataOptionsSetup. Each plugin's options can supply its own
        // factory (the common case is one shared factory across journal+snapshot, but multiple
        // plugin instances against different Redis backends — e.g. Cluster.Sharding regions —
        // are supported by setting different delegates per options object). The journal and
        // snapshot store actors read this back via
        // Context.System.Settings.Setup.Get<MultiRedisConnectionMultiplexerSetup>(), keyed by
        // their own plugin path.
        var journalFactory = journalOptions?.ConnectionMultiplexerFactory;
        var snapshotFactory = snapshotOptions?.ConnectionMultiplexerFactory;
        if (journalFactory is not null || snapshotFactory is not null)
        {
            var multi = builder.Setups.OfType<MultiRedisConnectionMultiplexerSetup>().FirstOrDefault();
            if (multi is null)
            {
                multi = new MultiRedisConnectionMultiplexerSetup();
                builder.Setups.Add(multi);
            }

            if (journalFactory is not null && journalOptions is not null)
                multi.AddFactory(journalOptions.PluginId, journalFactory);

            if (snapshotFactory is not null && snapshotOptions is not null)
                multi.AddFactory(snapshotOptions.PluginId, snapshotFactory);
        }

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
}