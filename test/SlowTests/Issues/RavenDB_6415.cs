﻿using System.Threading.Tasks;
using FastTests.Server.Replication;
using Raven.Client.Documents.Exceptions;
using Raven.Client.Documents.Replication.Messages;
using Raven.Client.Exceptions;
using Raven.Client.Extensions;
using Xunit;
using Raven.Server.Documents.Replication;

namespace SlowTests.Issues
{
    public class RavenDB_6415 : ReplicationTestsBase
    {
        private class User
        {
            public string Name { get; set; }
        }

        [Fact]
        public async Task PUT_of_conflicted_document_with_outdated_etag_throws_concurrency_exception()
        {
            using (var storeA = GetDocumentStore())
            using (var storeB = GetDocumentStore())
            {
                using (var session = storeA.OpenSession())
                {
                    session.Store(new User { Name = "John Doe" }, "users/1");
                    session.SaveChanges();
                }

                using (var session = storeB.OpenSession())
                {
                    session.Store(new User { Name = "Jane Doe" }, "users/1");
                    session.SaveChanges();
                }

                await SetupReplicationAsync(storeA, storeB);
                await SetupReplicationAsync(storeB, storeA);

                WaitUntilHasConflict(storeA, "users/1");

                long maxConflictEtag;
                using (var session = storeA.OpenSession())
                {
                    var ex = Assert.Throws<DocumentConflictException>(() => session.Load<User>("users/1"));
                    maxConflictEtag = ex.LargestEtag;
                }
                //should throw concurrency exception because we use lower etag then max etag of existing conflicts
                using (var session = storeA.OpenSession())
                {
                    var db = GetDocumentDatabaseInstanceFor(storeA).Result;
                    var cv = new ChangeVectorEntry[1];
                    cv[0] = new ChangeVectorEntry
                    {
                        DbId = db.DbId,
                        Etag = maxConflictEtag - 1
                    };
                    session.Store(new User { Name = "James Doe" }, cv.SerializeVector(), "users/1");
                    Assert.Throws<ConcurrencyException>(() => session.SaveChanges());
                }

                //now this should _not_ throw, since we do not specify expected conflict etag, so...
                using (var session = storeA.OpenSession())
                {
                    session.Store(new User { Name = "James Doe" }, "users/1");
                    session.SaveChanges();
                }
            }
        }

        [Fact]
        public async Task DELETE_of_conflicted_document_with_outdated_etag_throws_concurrency_exception()
        {
            using (var storeA = GetDocumentStore())
            using (var storeB = GetDocumentStore())
            {
                using (var session = storeA.OpenSession())
                {
                    session.Store(new User { Name = "John Doe" }, "users/1");
                    session.SaveChanges();
                }

                using (var session = storeB.OpenSession())
                {
                    session.Store(new User { Name = "Jane Doe" }, "users/1");
                    session.SaveChanges();
                }

                await SetupReplicationAsync(storeA, storeB);
                await SetupReplicationAsync(storeB, storeA);

                WaitUntilHasConflict(storeA, "users/1");

                long maxConflictEtag;
                using (var session = storeA.OpenSession())
                {
                    var ex = Assert.Throws<DocumentConflictException>(() => session.Load<User>("users/1"));
                    maxConflictEtag = ex.LargestEtag;
                }

                //should throw concurrency exception because we use lower etag then max etag of existing conflicts
                using (var session = storeA.OpenSession())
                {
                    var db = GetDocumentDatabaseInstanceFor(storeA).Result;
                    var cv = new ChangeVectorEntry[1];
                    cv[0] = new ChangeVectorEntry
                    {
                        DbId = db.DbId,
                        Etag = maxConflictEtag - 1
                    };
                    session.Delete("users/1", cv.SerializeVector());
                    Assert.Throws<ConcurrencyException>(() => session.SaveChanges());
                }

                //now this should _not_ throw, since we do not specify expected conflict etag, so...
                using (var session = storeA.OpenSession())
                {
                    session.Delete("users/1");
                    session.SaveChanges();
                }
            }
        }

    }
}
