// -----------------------------------------------------------------------
//  <copyright file="RedisSnapshotStoreConnectivityCheck.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

/// <summary>
/// Health check that verifies connectivity to the Redis instance used by the snapshot store.
/// </summary>
public sealed class RedisSnapshotStoreConnectivityCheck : RedisConnectivityCheckBase
{
    public RedisSnapshotStoreConnectivityCheck(string connectionString, string snapshotStoreId)
        : base(connectionString, "snapshot store", snapshotStoreId ?? throw new ArgumentNullException(nameof(snapshotStoreId)))
    {
    }
}
