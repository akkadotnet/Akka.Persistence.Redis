// -----------------------------------------------------------------------
// <copyright file="FencedRedisPlugins.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System.Threading.Tasks;
using Akka.Configuration;
using Akka.Persistence.Redis.Journal;
using Akka.Persistence.Redis.Snapshot;
using StackExchange.Redis;

namespace Akka.Persistence.Redis.Tests
{
    /// <summary>
    /// A journal that writes only while the fence key of the persistence id holds the expected
    /// token: <c>fence-key-format</c> (formatted with the persistence id) and <c>fence-token</c>
    /// are read from the plugin's HOCON.
    /// </summary>
    public sealed class FencedRedisJournal : RedisJournal
    {
        private readonly Fence _fence;

        public FencedRedisJournal(Config journalConfig) : base(journalConfig)
        {
            _fence = new Fence(journalConfig);
        }

        protected override ITransaction CreateTransaction(string persistenceId)
            => _fence.AddTo(base.CreateTransaction(persistenceId), persistenceId);
    }

    /// <summary>
    /// The snapshot-store counterpart of <see cref="FencedRedisJournal"/>.
    /// </summary>
    public sealed class FencedRedisSnapshotStore : RedisSnapshotStore
    {
        private readonly Fence _fence;

        public FencedRedisSnapshotStore(Config snapshotConfig) : base(snapshotConfig)
        {
            _fence = new Fence(snapshotConfig);
        }

        protected override ITransaction CreateTransaction(string persistenceId)
            => _fence.AddTo(base.CreateTransaction(persistenceId), persistenceId);
    }

    internal sealed class Fence
    {
        private readonly string _keyFormat;
        private readonly string _token;

        public Fence(Config config)
        {
            _keyFormat = config.GetString("fence-key-format");
            _token = config.GetString("fence-token");
        }

        public ITransaction AddTo(ITransaction transaction, string persistenceId)
        {
            transaction.AddCondition(Condition.StringEqual(string.Format(_keyFormat, persistenceId), _token));
            return transaction;
        }

        /// <summary>
        /// Sets a fence key on every server of the fixture: it has two standalone primaries, and
        /// the plugin's multiplexer writes to one of them.
        /// </summary>
        public static async Task SetAsync(RedisFixture fixture, int database, string key, string token)
        {
            foreach (var endpoint in fixture.ConnectionString.Split(','))
            {
                using var connection = await ConnectionMultiplexer.ConnectAsync(endpoint);
                await connection.GetDatabase(database).StringSetAsync(key, token);
            }
        }
    }
}
