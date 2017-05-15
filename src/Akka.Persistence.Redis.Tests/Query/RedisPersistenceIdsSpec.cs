using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.Redis.Query;
using Akka.Streams;
using Akka.Streams.TestKit;
using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace Akka.Persistence.Redis.Tests.Query
{
    public class RedisPersistenceIdsSpec : Akka.TestKit.Xunit2.TestKit
    {
        public const int Database = 1;

        public static Config Config(int id) => ConfigurationFactory.ParseString($@"
            akka.loglevel = INFO
            akka.persistence.journal.plugin = ""akka.persistence.journal.redis""
            akka.persistence.journal.redis {{
                class = ""Akka.Persistence.Redis.Journal.RedisJournal, Akka.Persistence.Redis""
                plugin-dispatcher = ""akka.actor.default-dispatcher""
                auto-initialize = on
                configuration-string = ""127.0.0.1:6379,allowAdmin:true""
                database = {id}
                key-prefix = ""akka:persistence:journal""
            }}
            akka.test.single-expect-default = 3s")
            .WithFallback(RedisReadJournal.DefaultConfiguration());


        private readonly ActorMaterializer _materializer;

        public RedisPersistenceIdsSpec() : base(Config(Database))
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public void Redis_query_AllPersistenceIds_should_implement_standard_AllPersistenceIdsQuery()
        {
            var queries = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);
            queries.Should().BeAssignableTo<IAllPersistenceIdsQuery>();
        }

        [Fact]
        public void Redis_query_CurrentPersistenceIds_should_implement_standard_AllPersistenceIdsQuery()
        {
            var queries = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);
            queries.Should().BeAssignableTo<ICurrentPersistenceIdsQuery>();
        }

        [Fact]
        public void Redis_query_CurrentPersistenceIds_should_find_existing_persistence_ids()
        {
            var queries = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);
            Sys.ActorOf(Query.TestActor.Props("a")).Tell("a1");
            ExpectMsg("a1-done");
            Sys.ActorOf(Query.TestActor.Props("b")).Tell("b1");
            ExpectMsg("b1-done");
            Sys.ActorOf(Query.TestActor.Props("c")).Tell("c1");
            ExpectMsg("c1-done");

            var source = queries.CurrentPersistenceIds();
            var probe = source.RunWith(this.SinkProbe<string>(), _materializer);
            probe.Within(TimeSpan.FromSeconds(10), () =>
                probe.Request(5)
                    .ExpectNextUnordered("a", "b", "c")
                    .ExpectComplete());
        }

        [Fact]
        public void Redis_query_AllPersistenceIds_should_find_new_persistence_ids()
        {
            var queries = Sys.ReadJournalFor<RedisReadJournal>(RedisReadJournal.Identifier);
            Redis_query_CurrentPersistenceIds_should_find_existing_persistence_ids();
            // a, b, c created by previous step

            Sys.ActorOf(Query.TestActor.Props("d")).Tell("d1");
            ExpectMsg("d1-done");

            var source = queries.AllPersistenceIds();
            var newprobe = source.RunWith(this.SinkProbe<string>(), _materializer);
            newprobe.Within(TimeSpan.FromSeconds(10), () =>
            {
                newprobe.Request(5).ExpectNextUnordered("a", "b", "c", "d");

                Sys.ActorOf(Query.TestActor.Props("e")).Tell("e1");
                newprobe.ExpectNext("e");

                var more = Enumerable.Range(1, 100).Select(i => "f" + i).ToArray();
                foreach (var x in more)
                    Sys.ActorOf(Query.TestActor.Props(x)).Tell(x);

                newprobe.Request(100);
                return newprobe.ExpectNextUnorderedN(more);
            });
        }

        protected override void Dispose(bool disposing)
        {
            _materializer.Dispose();
            DbUtils.Clean(Database);
            base.Dispose(disposing);
        }
    }
}
