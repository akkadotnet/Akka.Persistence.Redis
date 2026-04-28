// -----------------------------------------------------------------------
//  <copyright file="HealthCheckSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Hosting;
using Akka.Persistence.Redis.Hosting;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Hosting;

/// <summary>
/// Validates that health checks are properly registered after the refactoring.
/// </summary>
[Collection("RedisSpec")]
public class HealthCheckSpec : Akka.Hosting.TestKit.TestKit, IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;

    public HealthCheckSpec(ITestOutputHelper output, RedisFixture fixture)
        : base(nameof(HealthCheckSpec), output)
    {
        _fixture = fixture;
    }

    protected override void ConfigureServices(HostBuilderContext context, IServiceCollection services)
    {
        base.ConfigureServices(context, services);
        services.AddHealthChecks();
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        // Use the refactored WithSqlPersistence with health check registration
        builder.WithRedisPersistence(
            configurationString: _fixture.ConnectionString,
            journalBuilder: journal =>
            {
                journal.WithHealthCheck(HealthStatus.Degraded);
            },
            snapshotBuilder: snapshot =>
            {
                snapshot.WithHealthCheck(HealthStatus.Degraded);
            });
    }

    [Fact]
    public async Task Health_checks_should_be_registered_and_healthy()
    {
        // Arrange
        var healthCheckService = Host.Services.GetRequiredService<HealthCheckService>();

        // Act - run all health checks
        var healthReport = await healthCheckService.CheckHealthAsync(CancellationToken.None);

        // Assert - verify that health checks are registered and healthy
        healthReport.Entries.Should().NotBeEmpty("health checks should be registered");

        // Debug: print all registered health checks (ALL of them, not just SQL)
        Output?.WriteLine($"Total health checks registered: {healthReport.Entries.Count}");
        foreach (var entry in healthReport.Entries)
        {
            Output?.WriteLine($"  - {entry.Key}: {entry.Value.Status}");
        }

        // We should have exactly 2 health checks: journal and snapshot
        // Look for any Akka.Persistence-related health checks
        var persistenceHealthChecks = healthReport.Entries
            .Where(e => e.Key.Contains("Akka.Persistence", StringComparison.OrdinalIgnoreCase))
            .ToList();

        persistenceHealthChecks.Should().HaveCount(2, "because we registered health checks for both journal and snapshot store");

        // Verify journal health check exists and is healthy
        var journalHealthCheck = persistenceHealthChecks
            .FirstOrDefault(e => e.Key.Contains("journal", StringComparison.OrdinalIgnoreCase));

        journalHealthCheck.Should().NotBeNull("journal health check should be registered");
        journalHealthCheck.Value.Status.Should().Be(HealthStatus.Healthy, "SQL journal should be properly initialized");

        // Verify snapshot health check exists and is healthy
        var snapshotHealthCheck = persistenceHealthChecks
            .FirstOrDefault(e => e.Key.Contains("snapshot", StringComparison.OrdinalIgnoreCase));

        snapshotHealthCheck.Should().NotBeNull("snapshot health check should be registered");
        snapshotHealthCheck.Value.Status.Should().Be(HealthStatus.Healthy, "SQL snapshot store should be properly initialized");

        // Verify overall health status
        healthReport.Status.Should().Be(HealthStatus.Healthy, "because all health checks should pass");
    }
}