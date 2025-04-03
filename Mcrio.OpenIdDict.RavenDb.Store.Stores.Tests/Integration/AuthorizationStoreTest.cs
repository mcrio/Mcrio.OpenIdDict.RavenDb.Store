using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique.Exceptions;
using Microsoft.Extensions.Logging;
using Moq;
using OpenIddict.Abstractions;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Shouldly;
using Xunit;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Tests.Integration;

public sealed class AuthorizationStoreTest : IntegrationTestsBase
{
    private readonly IDocumentStore _documentStore;

    public AuthorizationStoreTest()
    {
        _documentStore = GetDocumentStore();
    }

    [Fact]
    public async Task ShouldCreateAuthorization()
    {
        DateTimeOffset creationDate = DateTimeOffset.Now;
        OpenIdDictRavenDbAuthorization authorization = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: "type",
            status: "status",
            subject: "subject");

        OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();

        await store.CreateAsync(authorization, CancellationToken.None);

        authorization.Id.ShouldNotBeNull();
        authorization.ApplicationId.ShouldBe("appid");
        authorization.CreationDate.ShouldBe(creationDate);

        WaitForUserToContinueTheTest(_documentStore);

        OpenIdDictRavenDbAuthorization? authFromDatabase = await LoadAuthorizationFromDatabase(authorization.Id);
        authFromDatabase.ShouldNotBeNull();
        authFromDatabase.ApplicationId.ShouldNotBeNull();
        authFromDatabase.ApplicationId.ShouldBe("appid");
        authFromDatabase.Id.ShouldBe(authorization.Id);
        authFromDatabase.CreationDate.ShouldNotBeNull();
        authFromDatabase.CreationDate?.ToUnixTimeSeconds().ShouldBe(creationDate.ToUnixTimeSeconds());
        authFromDatabase.Scopes.ShouldNotBeNull();
        authFromDatabase.Scopes.Count.ShouldBe(2);
        authFromDatabase.Scopes.ShouldBe(["scope1", "scope2"]);
        authFromDatabase.Type.ShouldBe("type");
        authFromDatabase.Status.ShouldBe("status");
        authFromDatabase.Subject.ShouldBe("subject");

        // create another one
        OpenIdDictRavenDbAuthorization authorization2 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid2",
            creationDate: DateTimeOffset.Now);
        await store.CreateAsync(authorization2, CancellationToken.None);

        authorization2.Id.ShouldNotBeNull();

        OpenIdDictRavenDbAuthorization? auth2FromDatabase = await LoadAuthorizationFromDatabase(authorization2.Id);
        auth2FromDatabase.ShouldNotBeNull();
        auth2FromDatabase.ApplicationId.ShouldNotBeNull();
        auth2FromDatabase.ApplicationId.ShouldBe("appid2");
        auth2FromDatabase.Id.ShouldBe(authorization2.Id);
        auth2FromDatabase.CreationDate.ShouldNotBeNull();
    }

    [Fact]
    public async Task ShouldDeleteAuthorization()
    {
        string authorizationId;
        {
            OpenIdDictRavenDbAuthorization authorization = ModelFactory.CreateAuthorizationInstance(
                applicationId: "appid",
                creationDate: DateTimeOffset.Now);
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.CreateAsync(authorization, CancellationToken.None);

            authorization.Id.ShouldNotBeNull();
            authorizationId = authorization.Id;
        }

        {
            authorizationId.ShouldNotBeNull();
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            OpenIdDictRavenDbAuthorization? authorization = await store.FindByIdAsync(
                authorizationId,
                CancellationToken.None);
            authorization.ShouldNotBeNull();
            authorization.Id.ShouldBe(authorizationId);

            await store.DeleteAsync(authorization, CancellationToken.None);
        }

        {
            authorizationId.ShouldNotBeNull();
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            OpenIdDictRavenDbAuthorization? authorization = await store.FindByIdAsync(
                authorizationId,
                CancellationToken.None);
            authorization.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ShouldFindAuthorization()
    {
        DateTimeOffset creationDate = DateTimeOffset.Now;
        OpenIdDictRavenDbAuthorization authorization1 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: "type",
            status: "status",
            subject: "subject");
        OpenIdDictRavenDbAuthorization authorization2 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: "type",
            status: "status",
            subject: "subject");
        OpenIdDictRavenDbAuthorization authorization3 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid3",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: "type",
            status: "status",
            subject: "subject3");

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.CreateAsync(authorization1, CancellationToken.None);
            await store.CreateAsync(authorization2, CancellationToken.None);
            await store.CreateAsync(authorization3, CancellationToken.None);
        }

        WaitForIndexing(_documentStore);

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();

            List<OpenIdDictRavenDbAuthorization> all = await FromAsyncEnumerableAsync(
                store.FindAsync(
                    subject: null,
                    client: null,
                    status: null,
                    type: null,
                    scopes: null,
                    cancellationToken: CancellationToken.None));
            all.Count.ShouldBe(3);
            all.Select(item => item.Id)
                .ShouldBe([authorization1.Id, authorization2.Id, authorization3.Id]);

            List<OpenIdDictRavenDbAuthorization> bySubject = await FromAsyncEnumerableAsync(
                store.FindAsync(
                    subject: "subject3",
                    client: null,
                    status: null,
                    type: null,
                    scopes: null,
                    cancellationToken: CancellationToken.None));
            bySubject.Count.ShouldBe(1);
            bySubject.Single().Id.ShouldBe(authorization3.Id);

            List<OpenIdDictRavenDbAuthorization> byApplicationId = await FromAsyncEnumerableAsync(
                store.FindByApplicationIdAsync("appid", CancellationToken.None));
            byApplicationId.Count.ShouldBe(2);
            byApplicationId.Select(item => item.Id)
                .ShouldBe([authorization1.Id, authorization2.Id]);

            List<OpenIdDictRavenDbAuthorization> bySubjectUsingFindBySubject = await FromAsyncEnumerableAsync(
                store.FindBySubjectAsync("subject3", CancellationToken.None));
            bySubjectUsingFindBySubject.Count.ShouldBe(1);
            bySubjectUsingFindBySubject.Single().Id.ShouldBe(authorization3.Id);
        }
    }

    [Fact]
    public async Task ShouldUpdateAuthorization()
    {
        string authorizationId;

        {
            DateTimeOffset creationDate = DateTimeOffset.Now;
            OpenIdDictRavenDbAuthorization authorization = ModelFactory.CreateAuthorizationInstance(
                applicationId: "appid",
                creationDate: creationDate,
                scopes: ["scope1", "scope2"],
                type: "type",
                status: "status",
                subject: "subject");

            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.CreateAsync(authorization, CancellationToken.None);
            authorization.Id.ShouldNotBeNull();
            authorizationId = authorization.Id;
        }

        authorizationId.ShouldNotBeNull();

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            OpenIdDictRavenDbAuthorization? authorization = await store.FindByIdAsync(
                authorizationId,
                CancellationToken.None);
            authorization.ShouldNotBeNull();
            authorization.Id.ShouldBe(authorizationId);

            authorization.ApplicationId = "new-appid";
            await store.UpdateAsync(authorization, CancellationToken.None);
        }

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            OpenIdDictRavenDbAuthorization? authorization = await store.FindByIdAsync(
                authorizationId,
                CancellationToken.None);
            authorization.ShouldNotBeNull();
            authorization.Id.ShouldBe(authorizationId);
            authorization.ApplicationId.ShouldBe("new-appid");
        }
    }

    [Fact]
    public async Task ShouldPruneWhenNoTokensAndStatusNotValid()
    {
        const int expiredAfterDays = 30;

        List<(string Id, string Message)> expectedToBePruned = [];
        List<(string Id, string Message)> expectedNotToBePruned = [];

        {
            OpenIdDictRavenDbAuthorizationStore authStore = CreateAuthorizationStore();
            OpenIdDictRavenDbTokenStore tokenStore = CreateTokenStore();

            foreach ((bool pruneExpected, OpenIdDictRavenDbAuthorization auth, string description) in CreateDataSet()
                         .ToList())
            {
                // store authorization to db
                await authStore.CreateAsync(auth, cancellationToken: CancellationToken.None);

                Debug.Assert(auth.Id is not null, "Auth.Id should not be null");

                if (pruneExpected)
                {
                    expectedToBePruned.Add((auth.Id, description));
                }
                else
                {
                    expectedNotToBePruned.Add((auth.Id, description));
                }

                // When prune is expected:
                // create new authorization with same parameters but with tokens so it should not be pruned
                // because of tokens existence
                if (pruneExpected)
                {
                    OpenIdDictRavenDbAuthorization authWithTokens = ModelFactory.CreateAuthorizationInstance(
                        applicationId: auth.ApplicationId ??
                                       throw new ArgumentException("Unexpected null application id"),
                        creationDate: auth.CreationDate ?? throw new ArgumentException("Unexpected null creation data"),
                        scopes: auth.Scopes,
                        type: auth.Type,
                        status: auth.Status,
                        subject: auth.Subject);

                    await authStore.CreateAsync(authWithTokens, cancellationToken: CancellationToken.None);
                    Debug.Assert(authWithTokens.Id is not null, "Auth.Id should not be null");

                    await tokenStore.CreateAsync(
                        ModelFactory.CreateTokenInstance(
                            authWithTokens.ApplicationId!,
                            authWithTokens.Id,
                            DateTimeOffset.Now,
                            DateTimeOffset.Now.AddDays(10)),
                        CancellationToken.None);

                    expectedNotToBePruned.Add((authWithTokens.Id, "No pruned as has tokens"));
                }
            }
        }

        WaitForIndexing(_documentStore);
        WaitForUserToContinueTheTest(_documentStore);

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.PruneAsync(
                threshold: DateTimeOffset.Now.Subtract(TimeSpan.FromDays(expiredAfterDays)),
                CancellationToken.None);
        }

        WaitForIndexing(_documentStore);
        WaitForUserToContinueTheTest(_documentStore);

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            List<OpenIdDictRavenDbAuthorization> all = await FromAsyncEnumerableAsync(
                store.FindAsync(null, null, null, null, null, CancellationToken.None));
            all.Count.ShouldBe(expectedNotToBePruned.Count);
            foreach ((string authId, string description) in expectedNotToBePruned)
            {
                if (all.All(item => item.Id != authId))
                {
                    Assert.Fail(
                        $"Expected authorization with id {authId} to be present, but it was not. Expectation description: {description}");
                }
            }

            foreach ((string authId, string description) in expectedToBePruned)
            {
                if (all.Any(item => item.Id == authId))
                {
                    Assert.Fail(
                        $"Expected authorization with id {authId} to not be present, but it was. Expectation description: {description}");
                }
            }
        }

        return;

        IEnumerable<(bool PruneExpected, OpenIdDictRavenDbAuthorization Auth, string Description)> CreateDataSet()
        {
            DateTimeOffset creationDateStillValid = DateTimeOffset.Now;
            DateTimeOffset creationDateExpired = DateTimeOffset.Now.Subtract(TimeSpan.FromDays(expiredAfterDays + 10));

            yield return (
                PruneExpected: true,
                Auth: ModelFactory.CreateAuthorizationInstance(
                    applicationId: "appid",
                    creationDate: creationDateExpired,
                    scopes: ["scope1", "scope2"],
                    type: OpenIddictConstants.AuthorizationTypes.Permanent,
                    status: OpenIddictConstants.Statuses.Revoked,
                    subject: "subject"),
                Description: "Prune expected: no tokens, expired creation, revoked status"
            );

            yield return (
                PruneExpected: false,
                Auth: ModelFactory.CreateAuthorizationInstance(
                    applicationId: "appid",
                    creationDate: creationDateStillValid,
                    scopes: ["scope1", "scope2"],
                    type: OpenIddictConstants.AuthorizationTypes.AdHoc,
                    status: OpenIddictConstants.Statuses.Revoked,
                    subject: "subject"),
                Description: "Prune not expected: still valid creation date"
            );

            yield return (
                PruneExpected: false,
                Auth: ModelFactory.CreateAuthorizationInstance(
                    applicationId: "appid",
                    creationDate: creationDateExpired,
                    scopes: ["scope1", "scope2"],
                    type: OpenIddictConstants.AuthorizationTypes.Permanent,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "subject"),
                Description: "Prune not expected: status valid"
            );

            yield return (
                PruneExpected: true,
                Auth: ModelFactory.CreateAuthorizationInstance(
                    applicationId: "appid",
                    creationDate: creationDateExpired,
                    scopes: ["scope1", "scope2"],
                    type: OpenIddictConstants.AuthorizationTypes.Permanent,
                    status: OpenIddictConstants.Statuses.Inactive,
                    subject: "subject"),
                Description: "Prune expected: no tokens, status inactive"
            );

            yield return (
                PruneExpected: true,
                Auth: ModelFactory.CreateAuthorizationInstance(
                    applicationId: "appid",
                    creationDate: creationDateExpired,
                    scopes: ["scope1", "scope2"],
                    type: OpenIddictConstants.AuthorizationTypes.Permanent,
                    status: OpenIddictConstants.Statuses.Redeemed,
                    subject: "subject"),
                Description: "Prune expected: no tokens, status redeemed"
            );

            yield return (
                PruneExpected: true,
                Auth: ModelFactory.CreateAuthorizationInstance(
                    applicationId: "appid",
                    creationDate: creationDateExpired,
                    scopes: ["scope1", "scope2"],
                    type: OpenIddictConstants.AuthorizationTypes.Permanent,
                    status: OpenIddictConstants.Statuses.Rejected,
                    subject: "subject"),
                Description: "Prune expected: no tokens, status rejected"
            );

            yield return (
                PruneExpected: true,
                Auth: ModelFactory.CreateAuthorizationInstance(
                    applicationId: "appid",
                    creationDate: creationDateExpired,
                    scopes: ["scope1", "scope2"],
                    type: OpenIddictConstants.AuthorizationTypes.AdHoc,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "subject"),
                Description: "Prune expected: no tokens, type adhoc"
            );

            yield return (
                PruneExpected: false,
                Auth: ModelFactory.CreateAuthorizationInstance(
                    applicationId: "appid",
                    creationDate: creationDateExpired,
                    scopes: ["scope1", "scope2"],
                    type: OpenIddictConstants.AuthorizationTypes.Permanent,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "subject"),
                Description: "Prune not expected: type permanent"
            );

            yield return (
                PruneExpected: false,
                Auth: ModelFactory.CreateAuthorizationInstance(
                    applicationId: "appid",
                    creationDate: creationDateExpired,
                    scopes: ["scope1", "scope2"],
                    type: OpenIddictConstants.AuthorizationTypes.Permanent,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "subject"),
                Description: "Prune no expected: type permanent but has tokens"
            );
        }
    }

    [Fact]
    public async Task ShouldRevokeAllAuthorizations()
    {
        DateTimeOffset creationDate = DateTimeOffset.Now;
        OpenIdDictRavenDbAuthorization authorization1 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject");
        OpenIdDictRavenDbAuthorization authorization2 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject");
        OpenIdDictRavenDbAuthorization authorization3 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid3",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject3");
        OpenIdDictRavenDbAuthorization authorization4 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid3",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.AdHoc,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject3");

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.CreateAsync(authorization1, CancellationToken.None);
            await store.CreateAsync(authorization2, CancellationToken.None);
            await store.CreateAsync(authorization3, CancellationToken.None);
            await store.CreateAsync(authorization4, CancellationToken.None);
        }

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.RevokeAsync(
                null,
                null,
                null,
                null,
                CancellationToken.None);
        }

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            List<OpenIdDictRavenDbAuthorization> all = await FromAsyncEnumerableAsync(
                store.FindAsync(
                    subject: null,
                    client: null,
                    status: OpenIddictConstants.Statuses.Revoked,
                    type: null,
                    scopes: null,
                    cancellationToken: CancellationToken.None));
            all.Count.ShouldBe(4);
            all.Select(item => item.Id)
                .ShouldBe([authorization1.Id, authorization2.Id, authorization3.Id, authorization4.Id]);
        }

        WaitForUserToContinueTheTest(_documentStore);
    }

    [Fact]
    public async Task ShouldRevokeUsingFilters()
    {
        DateTimeOffset creationDate = DateTimeOffset.Now;
        OpenIdDictRavenDbAuthorization authorization1 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject");
        OpenIdDictRavenDbAuthorization authorization2 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject");
        OpenIdDictRavenDbAuthorization authorization3 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid3",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject3");
        OpenIdDictRavenDbAuthorization authorization4 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "appid3",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.AdHoc,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject3");

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.CreateAsync(authorization1, CancellationToken.None);
            await store.CreateAsync(authorization2, CancellationToken.None);
            await store.CreateAsync(authorization3, CancellationToken.None);
            await store.CreateAsync(authorization4, CancellationToken.None);
        }

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.RevokeAsync(
                null,
                "appid",
                null,
                null,
                CancellationToken.None);
        }

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            List<OpenIdDictRavenDbAuthorization> all = await FromAsyncEnumerableAsync(
                store.FindAsync(
                    subject: null,
                    client: null,
                    status: OpenIddictConstants.Statuses.Revoked,
                    type: null,
                    scopes: null,
                    cancellationToken: CancellationToken.None));
            all.Count.ShouldBe(2);
            all.Select(item => item.Id)
                .ShouldBe([authorization1.Id, authorization2.Id]);
        }
    }

    [Fact]
    public async Task ShouldRevokeBySubject()
    {
        DateTimeOffset creationDate = DateTimeOffset.Now;
        OpenIdDictRavenDbAuthorization authorization1 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "1",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject1");
        OpenIdDictRavenDbAuthorization authorization2 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "1",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject1");
        OpenIdDictRavenDbAuthorization authorization3 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "99",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject1");
        OpenIdDictRavenDbAuthorization authorization4 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "99",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.AdHoc,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject3");

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.CreateAsync(authorization1, CancellationToken.None);
            await store.CreateAsync(authorization2, CancellationToken.None);
            await store.CreateAsync(authorization3, CancellationToken.None);
            await store.CreateAsync(authorization4, CancellationToken.None);
        }

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.RevokeBySubjectAsync(
                "subject1",
                CancellationToken.None);
        }

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            List<OpenIdDictRavenDbAuthorization> all = await FromAsyncEnumerableAsync(
                store.FindAsync(
                    subject: null,
                    client: null,
                    status: OpenIddictConstants.Statuses.Revoked,
                    type: null,
                    scopes: null,
                    cancellationToken: CancellationToken.None));
            all.Count.ShouldBe(3);
            all.Select(item => item.Id)
                .ShouldBe([authorization1.Id, authorization2.Id, authorization3.Id]);
        }
    }

    [Fact]
    public async Task ShouldRevokeByApplicationId()
    {
        DateTimeOffset creationDate = DateTimeOffset.Now;
        OpenIdDictRavenDbAuthorization authorization1 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "1",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject");
        OpenIdDictRavenDbAuthorization authorization2 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "1",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject");
        OpenIdDictRavenDbAuthorization authorization3 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "99",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.Permanent,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject3");
        OpenIdDictRavenDbAuthorization authorization4 = ModelFactory.CreateAuthorizationInstance(
            applicationId: "99",
            creationDate: creationDate,
            scopes: ["scope1", "scope2"],
            type: OpenIddictConstants.AuthorizationTypes.AdHoc,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "subject3");

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.CreateAsync(authorization1, CancellationToken.None);
            await store.CreateAsync(authorization2, CancellationToken.None);
            await store.CreateAsync(authorization3, CancellationToken.None);
            await store.CreateAsync(authorization4, CancellationToken.None);
        }

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.RevokeByApplicationIdAsync(
                "1",
                CancellationToken.None);
        }

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            List<OpenIdDictRavenDbAuthorization> all = await FromAsyncEnumerableAsync(
                store.FindAsync(
                    subject: null,
                    client: null,
                    status: OpenIddictConstants.Statuses.Revoked,
                    type: null,
                    scopes: null,
                    cancellationToken: CancellationToken.None));
            all.Count.ShouldBe(2);
            all.Select(item => item.Id)
                .ShouldBe([authorization1.Id, authorization2.Id]);
        }
    }

    [Fact]
    public async Task ShouldNotUpdateAuthorizationIfUpdatedByAnotherSession()
    {
        string authorizationId;

        {
            DateTimeOffset creationDate = DateTimeOffset.Now;
            OpenIdDictRavenDbAuthorization authorization = ModelFactory.CreateAuthorizationInstance(
                applicationId: "appid",
                creationDate: creationDate,
                scopes: ["scope1", "scope2"],
                type: "type",
                status: "status",
                subject: "subject");

            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            await store.CreateAsync(authorization, CancellationToken.None);
            authorization.Id.ShouldNotBeNull();
            authorizationId = authorization.Id;
        }

        authorizationId.ShouldNotBeNull();

        OpenIdDictRavenDbAuthorizationStore storeToFailUpdate = CreateAuthorizationStore();
        OpenIdDictRavenDbAuthorization? authorizationToFailUpdate = await storeToFailUpdate.FindByIdAsync(
            authorizationId,
            CancellationToken.None);
        authorizationToFailUpdate.ShouldNotBeNull();
        authorizationToFailUpdate.ApplicationId = Guid.NewGuid().ToString();

        {
            OpenIdDictRavenDbAuthorizationStore store = CreateAuthorizationStore();
            OpenIdDictRavenDbAuthorization? authorization = await store.FindByIdAsync(
                authorizationId,
                CancellationToken.None);
            authorization.ShouldNotBeNull();
            authorization.ApplicationId = Guid.NewGuid().ToString();
            await store.UpdateAsync(authorization, CancellationToken.None);
        }

        await ShouldFail().ShouldThrowAsync<ConcurrencyException>();
        return;

        async Task ShouldFail() =>
            await storeToFailUpdate.UpdateAsync(authorizationToFailUpdate, CancellationToken.None);
    }

    private OpenIdDictRavenDbAuthorizationStore CreateAuthorizationStore(bool useSessionOptimisticConcurrency = false)
    {
        return new OpenIdDictRavenDbAuthorizationStore(
            () =>
            {
                IAsyncDocumentSession? session = _documentStore.OpenAsyncSession();
                if (useSessionOptimisticConcurrency)
                {
                    session.Advanced.UseOptimisticConcurrency = true;
                }

                return session;
            },
            new Mock<ILogger<OpenIdDictRavenDbAuthorizationStore>>().Object);
    }

    private OpenIdDictRavenDbTokenStore CreateTokenStore(bool useSessionOptimisticConcurrency = false)
    {
        return new OpenIdDictRavenDbTokenStore(
            () =>
            {
                IAsyncDocumentSession? session = _documentStore.OpenAsyncSession();
                if (useSessionOptimisticConcurrency)
                {
                    session.Advanced.UseOptimisticConcurrency = true;
                }

                return session;
            },
            new Mock<ILogger<OpenIdDictRavenDbTokenStore>>().Object);
    }

    private async Task<OpenIdDictRavenDbAuthorization?> LoadAuthorizationFromDatabase(string id)
    {
        return await _documentStore.OpenAsyncSession().LoadAsync<OpenIdDictRavenDbAuthorization>(id);
    }
}