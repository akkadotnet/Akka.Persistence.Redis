// -----------------------------------------------------------------------
//  <copyright file="SimplifiedConnectivityCheckApiSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Hosting;
using Akka.Persistence.Redis.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Hosting;

/// <summary>
/// Tests for the simplified connectivity check API (Akka.Hosting 1.5.55.1+)
/// that automatically accesses options from builder.Options
/// </summary>
[Collection("RedisSpec")]
public class SimplifiedConnectivityCheckApiSpec : Akka.Hosting.TestKit.TestKit, IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;

    public SimplifiedConnectivityCheckApiSpec(ITestOutputHelper output, RedisFixture fixture)
        : base(nameof(SimplifiedConnectivityCheckApiSpec), output)
    {
        _fixture = fixture;
    }

    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        base.ConfigureServices(context, services);
        services.AddHealthChecks();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, System.IServiceProvider provider)
    {
        builder.WithRedisPersistence(
            configurationString: _fixture.ConnectionString,
            journalBuilder: journal =>
            {
                // This is the simplified API - no parameters needed
                journal.WithConnectivityCheck();
            },
            snapshotBuilder: snapshot =>
            {
                // This is the simplified API - no parameters needed
                snapshot.WithConnectivityCheck();
            });
    }

    [Fact]
    public async Task Connectivity_checks_should_be_registered()
    {
        // Arrange
        var healthCheckService = Host.Services.GetRequiredService<HealthCheckService>();

        // Act - run all health checks
        var healthReport = await healthCheckService.CheckHealthAsync(CancellationToken.None);

        // Assert
        Assert.NotEmpty(healthReport.Entries);

        // Look for connectivity checks
        var journalConnectivityCheck = healthReport.Entries
            .FirstOrDefault(e => e.Key.Contains("Redis.Journal", StringComparison.OrdinalIgnoreCase) &&
                                e.Key.Contains("Connectivity", StringComparison.OrdinalIgnoreCase));

        var snapshotConnectivityCheck = healthReport.Entries
            .FirstOrDefault(e => e.Key.Contains("Redis.SnapshotStore", StringComparison.OrdinalIgnoreCase) &&
                                e.Key.Contains("Connectivity", StringComparison.OrdinalIgnoreCase));

        Assert.NotNull(journalConnectivityCheck.Key);
        Assert.NotNull(snapshotConnectivityCheck.Key);
    }
}

/// <summary>
/// Tests for customizable options in the simplified connectivity check API
/// </summary>
[Collection("RedisSpec")]
public class CustomConnectivityCheckConfigSpec : Akka.Hosting.TestKit.TestKit, IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;

    public CustomConnectivityCheckConfigSpec(ITestOutputHelper output, RedisFixture fixture)
        : base(nameof(CustomConnectivityCheckConfigSpec), output)
    {
        _fixture = fixture;
    }

    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        base.ConfigureServices(context, services);
        services.AddHealthChecks();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, System.IServiceProvider provider)
    {
        var customTags = new[] { "custom", "redis", "test" };
        const string customName = "MyCustomRedisJournalCheck";

        builder.WithRedisPersistence(
            configurationString: _fixture.ConnectionString,
            journalBuilder: journal =>
            {
                journal.WithConnectivityCheck(
                    unHealthyStatus: HealthStatus.Degraded,
                    name: customName,
                    tags: customTags);
            });
    }

    [Fact]
    public async Task Simplified_API_should_support_custom_configuration()
    {
        // Arrange
        var healthCheckService = Host.Services.GetRequiredService<HealthCheckService>();

        // Act - run all health checks
        var healthReport = await healthCheckService.CheckHealthAsync(CancellationToken.None);

        // Assert
        Assert.NotEmpty(healthReport.Entries);

        // Look for the custom named check
        var customCheck = healthReport.Entries
            .FirstOrDefault(e => e.Key == "MyCustomRedisJournalCheck");

        Assert.NotNull(customCheck.Key);
    }
}
