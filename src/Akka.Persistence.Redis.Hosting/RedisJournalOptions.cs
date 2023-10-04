using System.Text;
using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Hosting;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

public class RedisJournalOptions : JournalOptions
{
    private static readonly Config Default = RedisPersistence.DefaultConfig()
        .GetConfig(RedisPersistence.JournalConfigPath);

    public RedisJournalOptions() : this(true)
    {
    }

    public RedisJournalOptions(bool isDefault, string identifier = "redis") : base(isDefault)
    {
        Identifier = identifier;
    }

    public string ConfigurationString { get; set; } = string.Empty;
    public string? KeyPrefix { get; set; }
    public int? Database { get; set; }
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