using System;
using System.Text;
using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using StackExchange.Redis;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

public class RedisJournalOptions : JournalOptions
{
    private static readonly Config Default = RedisPersistence.DefaultConfig().GetConfig(RedisPersistence.JournalConfigPath);

    public RedisJournalOptions() : this(true)
    {
    }

    public RedisJournalOptions(bool isDefault, string identifier = "redis") : base(isDefault)
    {
        Identifier = identifier;
    }

    /// <summary>
    /// Connection string, as described here: https://stackexchange.github.io/StackExchange.Redis/Configuration#basic-configuration-strings
    /// </summary>
    public string ConfigurationString { get; set; } = string.Empty;

    /// <summary>
    /// Redis journals key prefixes. Leave it for default or change it to appropriate value. WARNING: don't change it on production instances.
    /// </summary>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// Set the Redis default database to use. If you added defaultDatabase to the connection-strings, you have to set database to the value of defaultDatabase.
    /// </summary>
    public int? Database { get; set; }

    /// <summary>
    /// Determines redis database precedence when a user adds defaultDatabase to the connection-strings. For Redis Cluster, the defaultDatabase is 0
    /// </summary>
    public bool? UseDatabaseFromConnectionString { get; set; }

    /// <summary>
    /// Optional factory that returns a pre-configured <see cref="IConnectionMultiplexer"/>.
    /// When set, the journal will use this multiplexer instead of opening one from
    /// <see cref="ConfigurationString"/>. Required for scenarios that need a programmatically
    /// authored <see cref="ConfigurationOptions"/> (Azure Managed Redis with Entra ID,
    /// Redis Sentinel, custom <see cref="ConfigurationOptions.ReconnectRetryPolicy"/>,
    /// shorter <see cref="ConfigurationOptions.ConfigCheckSeconds"/> for clustered Redis, etc.).
    /// </summary>
    /// <remarks>
    /// The multiplexer is treated as caller-owned: the plugin will <i>not</i> dispose it on
    /// actor shutdown. The factory should cache and return the same instance on every call.
    /// If <see cref="WithRedisSnapshot"/>-style configuration also sets a factory on
    /// <see cref="RedisSnapshotOptions.ConnectionMultiplexerFactory"/>, the two delegates
    /// must be the same reference; otherwise <see cref="AkkaPersistenceRedisHostingExtensions"/>
    /// will throw because the underlying registry is process-global.
    /// </remarks>
    public Func<Task<IConnectionMultiplexer>>? ConnectionMultiplexerFactory { get; set; }

    public override string Identifier { get; set; }
    protected override Config InternalDefaultConfig { get; } = Default;

    protected override StringBuilder Build(StringBuilder sb)
    {
        sb.AppendLine($"configuration-string = {ConfigurationString.ToHocon()}");

        if (KeyPrefix is not null)
            sb.AppendLine($"key-prefix = {KeyPrefix.ToHocon()}");

        if (Database is not null)
            sb.AppendLine($"database = {Database.ToHocon()}");

        if (UseDatabaseFromConnectionString is not null)
            sb.AppendLine($"use-database-number-from-connection-string = {UseDatabaseFromConnectionString.Value.ToHocon()}");

        return base.Build(sb);
    }
}