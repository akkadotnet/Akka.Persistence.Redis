//-----------------------------------------------------------------------
// <copyright file="RedisJournal.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2017 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2017 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;

namespace Akka.Persistence.Redis.Query
{
    public class RedisReadJournalProvider : IReadJournalProvider
    {
        private readonly ExtendedActorSystem _system;
        private readonly Config _config;

        public RedisReadJournalProvider(ExtendedActorSystem system, Config config)
        {
            _system = system;
            _config = config;
        }

        public IReadJournal GetReadJournal()
        {
            return new RedisReadJournal(_system, _config);
        }
    }
}
