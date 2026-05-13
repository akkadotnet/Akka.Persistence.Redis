// -----------------------------------------------------------------------
// <copyright file="RedisTopologyRefresherSpecs.cs" company="Petabridge, LLC">
//      Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace Akka.Persistence.Redis.Tests
{
    public class RedisTopologyRefresherSpecs : Akka.TestKit.Xunit.TestKit
    {
        [Fact]
        public void IsReplicaRefusal_returns_true_for_canonical_replica_message()
        {
            var ex = new RedisCommandException(
                "Command cannot be issued to a replica: SET {_EventId-1}.Catalog.MarketSnapshot");

            RedisTopologyRefresher.IsReplicaRefusal(ex).Should().BeTrue();
        }

        [Fact]
        public void IsReplicaRefusal_returns_false_for_MOVED_redirect()
        {
            // MOVED is auto-handled by SE.Redis 2.6.86+; the refresher must NOT
            // react to it (would cause double-refresh storms).
            var ex = new RedisCommandException("MOVED 1234 192.168.0.5:6379");

            RedisTopologyRefresher.IsReplicaRefusal(ex).Should().BeFalse();
        }

        [Fact]
        public void IsReplicaRefusal_returns_false_for_unrelated_command_exception()
        {
            var ex = new RedisCommandException("WRONGTYPE Operation against a key holding the wrong kind of value");

            RedisTopologyRefresher.IsReplicaRefusal(ex).Should().BeFalse();
        }

        [Fact]
        public void TriggerBackgroundRefresh_invokes_refresh_callable_once()
        {
            var invocations = 0;
            var gate = new TaskCompletionSource<int>();
            var refresher = new RedisTopologyRefresher(
                () =>
                {
                    Interlocked.Increment(ref invocations);
                    return gate.Task;
                },
                Log);

            refresher.TriggerBackgroundRefresh(
                "pid-1",
                "WriteBatch",
                new RedisCommandException("Command cannot be issued to a replica: SET foo"));

            invocations.Should().Be(1);

            gate.SetResult(0);
        }

        [Fact]
        public void TriggerBackgroundRefresh_deduplicates_concurrent_calls()
        {
            // Two writes hitting the same demoted node in rapid succession should
            // result in exactly one refresh dispatch, not N.
            var invocations = 0;
            var gate = new TaskCompletionSource<int>();
            var refresher = new RedisTopologyRefresher(
                () =>
                {
                    Interlocked.Increment(ref invocations);
                    return gate.Task;
                },
                Log);

            var ex = new RedisCommandException("Command cannot be issued to a replica");

            refresher.TriggerBackgroundRefresh("pid-1", "WriteBatch", ex);
            refresher.TriggerBackgroundRefresh("pid-2", "WriteBatch", ex);
            refresher.TriggerBackgroundRefresh("pid-3", "WriteBatch", ex);

            invocations.Should().Be(1);

            gate.SetResult(0);
        }

        [Fact]
        public async Task TriggerBackgroundRefresh_resets_flag_after_refresh_completes()
        {
            // After the refresh task finishes, a fresh trigger must dispatch a new
            // refresh — otherwise a one-time topology blip would permanently disable
            // subsequent refreshes.
            var invocations = 0;
            TaskCompletionSource<int> gate = new();
            var refresher = new RedisTopologyRefresher(
                () =>
                {
                    Interlocked.Increment(ref invocations);
                    return gate.Task;
                },
                Log);

            var ex = new RedisCommandException("Command cannot be issued to a replica");

            refresher.TriggerBackgroundRefresh("pid-1", "WriteBatch", ex);
            gate.SetResult(0);

            await AwaitConditionAsync(() => Task.FromResult(invocations == 1), TimeSpan.FromSeconds(2));

            gate = new TaskCompletionSource<int>();
            refresher.TriggerBackgroundRefresh("pid-2", "WriteBatch", ex);

            await AwaitConditionAsync(() => Task.FromResult(invocations == 2), TimeSpan.FromSeconds(2));

            gate.SetResult(0);
        }

        [Fact]
        public async Task TriggerBackgroundRefresh_logs_warning_when_refresh_faults()
        {
            // When the background refresh task itself faults, we must log the failure
            // (the operator's only signal that auto-refresh isn't working) and reset
            // the flag so future triggers can try again.
            var refresher = new RedisTopologyRefresher(
                () => Task.FromException(new InvalidOperationException("simulated async failure")),
                Log);

            var ex = new RedisCommandException("Command cannot be issued to a replica");

            await EventFilter.Warning(contains: "Background Redis topology refresh failed").ExpectAsync(1, async () =>
            {
                refresher.TriggerBackgroundRefresh("pid-1", "WriteBatch", ex);
                await Task.CompletedTask;
            });
        }

        [Fact]
        public void TriggerBackgroundRefresh_swallows_synchronous_refresh_exception()
        {
            // If the refresh callable throws synchronously (before returning a Task),
            // the refresher must not propagate (it's fire-and-forget by contract) and
            // must reset the in-flight flag so future writes can trigger a fresh refresh.
            var invocations = 0;
            var refresher = new RedisTopologyRefresher(
                () =>
                {
                    Interlocked.Increment(ref invocations);
                    throw new InvalidOperationException("simulated synchronous failure");
                },
                Log);

            var ex = new RedisCommandException("Command cannot be issued to a replica");

            Action act = () => refresher.TriggerBackgroundRefresh("pid-1", "WriteBatch", ex);
            act.Should().NotThrow();

            AwaitAssert(() => invocations.Should().BeGreaterOrEqualTo(1), TimeSpan.FromSeconds(2));
        }
    }
}
