// -----------------------------------------------------------------------
//  <copyright file="RedisJournalConnectivityCheck.cs" company="Akka.NET Project">
//      Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
//  </copyright>
// -----------------------------------------------------------------------

using System;

#nullable enable
namespace Akka.Persistence.Redis.Hosting;

/// <summary>
/// Health check that verifies connectivity to the Redis instance used by the journal.
/// </summary>
public sealed class RedisJournalConnectivityCheck : RedisConnectivityCheckBase
{
    public RedisJournalConnectivityCheck(string connectionString, string journalId)
        : base(
            connectionString,
            "journal",
            journalId ?? throw new ArgumentNullException(nameof(journalId)),
            $"akka.persistence.journal.{journalId}")
    {
    }
}
