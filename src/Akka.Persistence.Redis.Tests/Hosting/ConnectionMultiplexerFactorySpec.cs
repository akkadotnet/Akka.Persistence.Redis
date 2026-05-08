// -----------------------------------------------------------------------
//  <copyright file="ConnectionMultiplexerFactorySpec.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence;
using Akka.Persistence.Redis;
using Akka.Persistence.Redis.Hosting;
using Akka.Persistence.Redis.Tests;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Hosting;

[Collection("RedisSpec")]
public class ConnectionMultiplexerFactorySpec : Akka.Hosting.TestKit.TestKit, IClassFixture<RedisFixture>
{
    private readonly RedisFixture _fixture;
    private IConnectionMultiplexer? _suppliedMultiplexer;

    public ConnectionMultiplexerFactorySpec(ITestOutputHelper output, RedisFixture fixture)
        : base(nameof(ConnectionMultiplexerFactorySpec), output: output)
    {
        _fixture = fixture;
    }

    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        _suppliedMultiplexer = ConnectionMultiplexer.Connect(_fixture.ConnectionString);

        builder.WithRedisPersistence(
            multiplexer: _suppliedMultiplexer,
            ownedByPlugin: false,
            autoInitialize: true);
    }

    protected override async Task AfterAllAsync()
    {
        // Caller-owned multiplexer: the plugin must NOT have disposed it.
        _suppliedMultiplexer.Should().NotBeNull();
        _suppliedMultiplexer!.IsConnected.Should().BeTrue(
            "the plugin must not dispose multiplexers supplied via RedisConnectionMultiplexerSetup when ownedByPlugin is false");

        _suppliedMultiplexer.Dispose();

        await base.AfterAllAsync();
    }

    [Fact]
    public async Task Caller_supplied_multiplexer_should_round_trip_through_the_journal()
    {
        var actor = Sys.ActorOf(Props.Create(() => new FactoryProbeActor("factory-probe-1", TestActor)));

        actor.Tell("save");
        ExpectMsg("saved", TimeSpan.FromSeconds(10));

        // Recover via a fresh actor to confirm the write actually landed in Redis through
        // the supplied multiplexer (not just acknowledged).
        var recovered = Sys.ActorOf(Props.Create(() => new FactoryProbeActor("factory-probe-1", TestActor)));
        ExpectMsg("recovered:save", TimeSpan.FromSeconds(10));

        await Task.CompletedTask;
    }

    [Fact]
    public void Setup_should_be_registered_when_multiplexer_is_supplied()
    {
        var setup = Sys.Settings.Setup.Get<RedisConnectionMultiplexerSetup>();
        setup.HasValue.Should().BeTrue(
            "WithRedisPersistence(multiplexer:) must register a RedisConnectionMultiplexerSetup");
        setup.Value.OwnedByPlugin.Should().BeFalse();
    }

    private sealed class FactoryProbeActor : Akka.Persistence.ReceivePersistentActor
    {
        private readonly string _persistenceId;

        public FactoryProbeActor(string persistenceId, IActorRef probe)
        {
            _persistenceId = persistenceId;

            Recover<string>(payload => probe.Tell($"recovered:{payload}"));

            Command<string>(msg =>
            {
                if (msg == "save")
                {
                    Persist(msg, _ => probe.Tell("saved"));
                }
            });
        }

        public override string PersistenceId => _persistenceId;
    }
}
