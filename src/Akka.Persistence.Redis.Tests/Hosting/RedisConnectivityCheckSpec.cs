// -----------------------------------------------------------------------
//  <copyright file="RedisConnectivityCheckSpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Actor.Setup;
using Akka.Hosting;
using Akka.Persistence.Redis;
using Akka.Persistence.Redis.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Hosting;

[Collection("RedisSpec")]
public class RedisConnectivityCheckSpec : IClassFixture<RedisFixture>
{
    private const string InvalidConnectionString = "invalid-host:6379";
    private readonly ITestOutputHelper _output;
    private readonly RedisFixture _fixture;
    private readonly string _validConnectionString;

    public RedisConnectivityCheckSpec(ITestOutputHelper output, RedisFixture fixture)
    {
        _output = output;
        _fixture = fixture;
        _validConnectionString = fixture.ConnectionString;
    }

    // Happy path tests - verify health checks work with real Redis
    [Fact]
    public async Task Journal_Connectivity_Check_Should_Return_Healthy_When_Connected()
    {
        // Arrange
        var check = new RedisJournalConnectivityCheck(_validConnectionString, "redis");
        var context = new AkkaHealthCheckContext(null!);

        // Act
        var result = await check.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Contains("successful", result.Description);
    }

    [Fact]
    public async Task Snapshot_Connectivity_Check_Should_Return_Healthy_When_Connected()
    {
        // Arrange
        var check = new RedisSnapshotStoreConnectivityCheck(_validConnectionString, "redis");
        var context = new AkkaHealthCheckContext(null!);

        // Act
        var result = await check.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Null(result.Exception);
        Assert.Contains("successful", result.Description);
    }

    // Unhappy path tests - verify health checks detect connection failures
    [Fact]
    public async Task Journal_Connectivity_Check_Should_Return_Unhealthy_When_Disconnected()
    {
        // Arrange
        var check = new RedisJournalConnectivityCheck(InvalidConnectionString, "redis");
        var context = new AkkaHealthCheckContext(null!);

        // Act
        var result = await check.CheckHealthAsync(context, CancellationToken.None);

        // Assert
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
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
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }

    [Fact]
    public void Journal_Connectivity_Check_Should_Require_ConnectionString()
    {
        // Act & Assert
        var action = () => new RedisJournalConnectivityCheck((string)null!, "redis");
        Assert.Throws<ArgumentNullException>(action).Where(ex => ex.ParamName == "connectionString");
    }

    [Fact]
    public void Journal_Connectivity_Check_Should_Require_JournalId()
    {
        // Act & Assert
        var action = () => new RedisJournalConnectivityCheck("localhost:6379", null!);
        Assert.Throws<ArgumentNullException>(action).Where(ex => ex.ParamName == "journalId");
    }

    [Fact]
    public void Snapshot_Connectivity_Check_Should_Require_ConnectionString()
    {
        // Act & Assert
        var action = () => new RedisSnapshotStoreConnectivityCheck((string)null!, "redis");
        Assert.Throws<ArgumentNullException>(action).Where(ex => ex.ParamName == "connectionString");
    }

    [Fact]
    public void Snapshot_Connectivity_Check_Should_Require_SnapshotStoreId()
    {
        // Act & Assert
        var action = () => new RedisSnapshotStoreConnectivityCheck("localhost:6379", null!);
        Assert.Throws<ArgumentNullException>(action).Where(ex => ex.ParamName == "snapshotStoreId");
    }

    [Fact]
    public async Task Journal_Connectivity_Check_Should_Use_Plugin_Scoped_Setup_When_Present()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(_validConnectionString);
        var setup = ActorSystemSetup.Create(
            new RedisConnectionMultiplexerSetup()
                .Add(
                    "akka.persistence.journal.redis",
                    () => Task.FromResult<IConnectionMultiplexer>(multiplexer),
                    RedisConnectionOwnership.CallerOwned));
        var system = ActorSystem.Create($"redis-health-{Guid.NewGuid():N}", setup);
        try
        {
            var check = new RedisJournalConnectivityCheck(InvalidConnectionString, "redis");
            var context = new AkkaHealthCheckContext(system);

            var result = await check.CheckHealthAsync(context, CancellationToken.None);

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact]
    public async Task Snapshot_Connectivity_Check_Should_Use_Plugin_Scoped_Setup_When_Present()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(_validConnectionString);
        var setup = ActorSystemSetup.Create(
            new RedisConnectionMultiplexerSetup()
                .Add(
                    "akka.persistence.snapshot-store.redis",
                    () => Task.FromResult<IConnectionMultiplexer>(multiplexer),
                    RedisConnectionOwnership.CallerOwned));
        var system = ActorSystem.Create($"redis-snapshot-health-{Guid.NewGuid():N}", setup);
        try
        {
            var check = new RedisSnapshotStoreConnectivityCheck(InvalidConnectionString, "redis");
            var context = new AkkaHealthCheckContext(system);

            var result = await check.CheckHealthAsync(context, CancellationToken.None);

            Assert.Equal(HealthStatus.Healthy, result.Status);
        }
        finally
        {
            await system.Terminate();
        }
    }

    [Fact]
    public async Task Connectivity_Check_Should_Not_Use_Setup_For_Different_Plugin_Id()
    {
        using var multiplexer = ConnectionMultiplexer.Connect(_validConnectionString);
        var setup = ActorSystemSetup.Create(
            new RedisConnectionMultiplexerSetup()
                .Add(
                    "akka.persistence.journal.redis",
                    () => Task.FromResult<IConnectionMultiplexer>(multiplexer),
                    RedisConnectionOwnership.CallerOwned));
        var system = ActorSystem.Create($"redis-health-mismatch-{Guid.NewGuid():N}", setup);
        try
        {
            var check = new RedisJournalConnectivityCheck(InvalidConnectionString, "custom");
            var context = new AkkaHealthCheckContext(system);

            var result = await check.CheckHealthAsync(context, CancellationToken.None);

            Assert.Equal(HealthStatus.Unhealthy, result.Status);
            Assert.NotNull(result.Exception);
        }
        finally
        {
            await system.Terminate();
        }
    }
}
