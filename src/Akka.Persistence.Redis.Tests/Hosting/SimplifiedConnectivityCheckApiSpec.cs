// -----------------------------------------------------------------------
//  <copyright file="SimplifiedConnectivityCheckApiSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Hosting;
using Akka.Persistence.Redis.Hosting;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Hosting;

/// <summary>
/// Tests for the simplified connectivity check API (Akka.Hosting 1.5.55.1+)
/// that automatically accesses options from builder.Options
/// </summary>
public class SimplifiedConnectivityCheckApiSpec
{
    [Fact]
    public void Journal_Should_Support_Simplified_WithConnectivityCheck_API()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHealthChecks();

        // Act
        services.AddAkka("TestSystem", (builder, provider) =>
        {
            builder.WithRedisPersistence(
                configurationString: "localhost:6379",
                journalBuilder: journal =>
                {
                    // This is the simplified API - no parameters needed
                    journal.WithConnectivityCheck();
                });
        });

        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Assert
        var registration = healthCheckService.GetType()
            .GetProperty("_registrations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(healthCheckService) as System.Collections.Generic.IEnumerable<HealthCheckRegistration>;

        registration.Should().NotBeNull();
        var checkRegistration = registration!.FirstOrDefault(r => r.Name.Contains("Redis.Journal") && r.Name.Contains("Connectivity"));
        checkRegistration.Should().NotBeNull("connectivity check should be registered");
    }

    [Fact]
    public void SnapshotStore_Should_Support_Simplified_WithConnectivityCheck_API()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHealthChecks();

        // Act
        services.AddAkka("TestSystem", (builder, provider) =>
        {
            builder.WithRedisPersistence(
                configurationString: "localhost:6379",
                snapshotBuilder: snapshot =>
                {
                    // This is the simplified API - no parameters needed
                    snapshot.WithConnectivityCheck();
                });
        });

        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Assert
        var registration = healthCheckService.GetType()
            .GetProperty("_registrations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(healthCheckService) as System.Collections.Generic.IEnumerable<HealthCheckRegistration>;

        registration.Should().NotBeNull();
        var checkRegistration = registration!.FirstOrDefault(r => r.Name.Contains("Redis.SnapshotStore") && r.Name.Contains("Connectivity"));
        checkRegistration.Should().NotBeNull("connectivity check should be registered");
    }

    [Fact]
    public void Simplified_API_Should_Throw_When_ConnectionString_Not_Set()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHealthChecks();

        // Act & Assert
        var action = () => services.AddAkka("TestSystem", (builder, provider) =>
        {
            builder.WithRedisPersistence(
                configurationString: "",  // Empty connection string
                journalBuilder: journal =>
                {
                    journal.WithConnectivityCheck();
                });
        });

        action.Should().Throw<ArgumentException>()
            .WithMessage("*ConnectionString*");
    }
}

/// <summary>
/// Tests for customizable options in the simplified connectivity check API
/// </summary>
public class CustomConnectivityCheckConfigSpec
{
    [Fact]
    public void Simplified_API_Should_Support_Custom_HealthStatus()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHealthChecks();

        // Act
        services.AddAkka("TestSystem", (builder, provider) =>
        {
            builder.WithRedisPersistence(
                configurationString: "localhost:6379",
                journalBuilder: journal =>
                {
                    journal.WithConnectivityCheck(unHealthyStatus: HealthStatus.Degraded);
                });
        });

        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Assert - verify the health check was registered
        var registration = healthCheckService.GetType()
            .GetProperty("_registrations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(healthCheckService) as System.Collections.Generic.IEnumerable<HealthCheckRegistration>;

        registration.Should().NotBeNull();
        var checkRegistration = registration!.FirstOrDefault(r => r.Name.Contains("Redis.Journal") && r.Name.Contains("Connectivity"));
        checkRegistration.Should().NotBeNull();
        checkRegistration!.FailureStatus.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public void Simplified_API_Should_Support_Custom_Name()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHealthChecks();
        const string customName = "MyCustomRedisJournalCheck";

        // Act
        services.AddAkka("TestSystem", (builder, provider) =>
        {
            builder.WithRedisPersistence(
                configurationString: "localhost:6379",
                journalBuilder: journal =>
                {
                    journal.WithConnectivityCheck(name: customName);
                });
        });

        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Assert
        var registration = healthCheckService.GetType()
            .GetProperty("_registrations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(healthCheckService) as System.Collections.Generic.IEnumerable<HealthCheckRegistration>;

        registration.Should().NotBeNull();
        var checkRegistration = registration!.FirstOrDefault(r => r.Name == customName);
        checkRegistration.Should().NotBeNull("custom named health check should be registered");
    }

    [Fact]
    public void Simplified_API_Should_Support_Custom_Tags()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddHealthChecks();
        var customTags = new[] { "custom", "redis", "test" };

        // Act
        services.AddAkka("TestSystem", (builder, provider) =>
        {
            builder.WithRedisPersistence(
                configurationString: "localhost:6379",
                journalBuilder: journal =>
                {
                    journal.WithConnectivityCheck(tags: customTags);
                });
        });

        var serviceProvider = services.BuildServiceProvider();
        var healthCheckService = serviceProvider.GetRequiredService<HealthCheckService>();

        // Assert
        var registration = healthCheckService.GetType()
            .GetProperty("_registrations", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(healthCheckService) as System.Collections.Generic.IEnumerable<HealthCheckRegistration>;

        registration.Should().NotBeNull();
        var checkRegistration = registration!.FirstOrDefault(r => r.Name.Contains("Redis.Journal") && r.Name.Contains("Connectivity"));
        checkRegistration.Should().NotBeNull();
        checkRegistration!.Tags.Should().BeEquivalentTo(customTags);
    }
}
