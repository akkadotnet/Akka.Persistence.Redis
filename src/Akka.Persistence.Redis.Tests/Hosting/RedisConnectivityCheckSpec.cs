// -----------------------------------------------------------------------
//  <copyright file="RedisConnectivityCheckSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Hosting;
using Akka.Persistence.Redis.Hosting;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Persistence.Redis.Tests.Hosting;

public class RedisConnectivityCheckSpec
{
    private const string ValidConnectionString = "localhost:6379";
    private const string InvalidConnectionString = "invalid-host:6379";
    private readonly ITestOutputHelper _output;

    public RedisConnectivityCheckSpec(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Journal_Connectivity_Check_Should_Return_Unhealthy_When_Disconnected()
    {
        // Arrange
        var check = new RedisJournalConnectivityCheck(InvalidConnectionString, "redis");
        var context = new AkkaHealthCheckContext(null!);

        // Act
        var result = await check.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public async Task Snapshot_Connectivity_Check_Should_Return_Unhealthy_When_Disconnected()
    {
        // Arrange
        var check = new RedisSnapshotStoreConnectivityCheck(InvalidConnectionString, "redis");
        var context = new AkkaHealthCheckContext(null!);

        // Act
        var result = await check.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public void Journal_Connectivity_Check_Should_Require_ConnectionString()
    {
        // Act & Assert
        var action = () => new RedisJournalConnectivityCheck(null!, "redis");
        action.Should().Throw<ArgumentNullException>().Where(ex => ex.ParamName == "connectionString");
    }

    [Fact]
    public void Journal_Connectivity_Check_Should_Require_JournalId()
    {
        // Act & Assert
        var action = () => new RedisJournalConnectivityCheck("localhost:6379", null!);
        action.Should().Throw<ArgumentNullException>().Where(ex => ex.ParamName == "journalId");
    }

    [Fact]
    public void Snapshot_Connectivity_Check_Should_Require_ConnectionString()
    {
        // Act & Assert
        var action = () => new RedisSnapshotStoreConnectivityCheck(null!, "redis");
        action.Should().Throw<ArgumentNullException>().Where(ex => ex.ParamName == "connectionString");
    }

    [Fact]
    public void Snapshot_Connectivity_Check_Should_Require_SnapshotStoreId()
    {
        // Act & Assert
        var action = () => new RedisSnapshotStoreConnectivityCheck("localhost:6379", null!);
        action.Should().Throw<ArgumentNullException>().Where(ex => ex.ParamName == "snapshotStoreId");
    }
}
