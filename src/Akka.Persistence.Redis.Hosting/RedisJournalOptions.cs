using System.Text;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Hosting;

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