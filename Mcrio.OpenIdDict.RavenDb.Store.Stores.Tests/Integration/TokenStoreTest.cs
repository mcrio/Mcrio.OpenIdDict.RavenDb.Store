using System;
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

public sealed class TokenStoreTest : IntegrationTestsBase
{
    private readonly IDocumentStore _documentStore;

    public TokenStoreTest()
    {
        _documentStore = GetDocumentStore();
    }

    [Fact]
    public async Task ShouldCreateToken()
    {
        DateTimeOffset creationDate = DateTimeOffset.Now;
        DateTimeOffset expirationDate = DateTimeOffset.Now.AddMinutes(60);
        OpenIdDictRavenDbToken token = ModelFactory.CreateTokenInstance(
            "appid",
            "auth1",
            creationDate,
            expirationDate,
            OpenIddictConstants.TokenTypes.Bearer,
            OpenIddictConstants.Statuses.Valid,
            "sub",
            "reference1"
        );

        OpenIdDictRavenDbTokenStore store = CreateTokenStore();

        await store.CreateAsync(token, CancellationToken.None);

        token.Id.ShouldNotBeNull();

        WaitForUserToContinueTheTest(_documentStore);

        OpenIdDictRavenDbToken? tokenFromDatabase = await LoadTokenFromDatabase(token.Id);
        tokenFromDatabase.ShouldNotBeNull();
        tokenFromDatabase.ApplicationId.ShouldNotBeNull();
        tokenFromDatabase.ApplicationId.ShouldBe("appid");
        tokenFromDatabase.Id.ShouldBe(token.Id);
        tokenFromDatabase.CreationDate.ShouldNotBeNull();
        tokenFromDatabase.CreationDate?.ToUnixTimeSeconds().ShouldBe(creationDate.ToUnixTimeSeconds());
        tokenFromDatabase.ExpirationDate.ShouldNotBeNull();
        tokenFromDatabase.ExpirationDate?.ToUnixTimeSeconds().ShouldBe(expirationDate.ToUnixTimeSeconds());
        tokenFromDatabase.Type.ShouldBe(OpenIddictConstants.TokenTypes.Bearer);
        tokenFromDatabase.Status.ShouldBe(OpenIddictConstants.Statuses.Valid);
        tokenFromDatabase.Subject.ShouldBe("sub");
        tokenFromDatabase.ReferenceId.ShouldBe("reference1");
        tokenFromDatabase.Properties.ShouldBeNull();
        tokenFromDatabase.AuthorizationId.ShouldBe("auth1");

        // create another one
        OpenIdDictRavenDbToken token2 = ModelFactory.CreateTokenInstance(
            applicationId: "appid2",
            authorizationId: "auth1",
            creationDate: DateTimeOffset.Now,
            expirationDate: DateTimeOffset.Now.Subtract(TimeSpan.FromMinutes(55))
        );
        await store.CreateAsync(token2, CancellationToken.None);

        token2.Id.ShouldNotBeNull();

        OpenIdDictRavenDbToken? token2FromDatabase = await LoadTokenFromDatabase(token2.Id);
        token2FromDatabase.ShouldNotBeNull();
        token2FromDatabase.ApplicationId.ShouldNotBeNull();
        token2FromDatabase.ApplicationId.ShouldBe("appid2");
        token2FromDatabase.Id.ShouldBe(token2.Id);
        token2FromDatabase.CreationDate.ShouldNotBeNull();
        token2FromDatabase.ReferenceId.ShouldBeNull();
        token2FromDatabase.Type.ShouldBeNull();
    }

    [Fact]
    public async Task ShouldNotCreateTokenWithSameReferenceIdWhenNotNull()
    {
        OpenIdDictRavenDbTokenStore store = CreateTokenStore();

        await store.CreateAsync(
            ModelFactory.CreateTokenInstance(
                "appId",
                "auth1",
                DateTimeOffset.Now,
                DateTimeOffset.Now.AddMinutes(60),
                OpenIddictConstants.TokenTypes.Bearer,
                OpenIddictConstants.Statuses.Valid,
                "sub",
                "reference1"
            ),
            CancellationToken.None
        );

        await store.CreateAsync(
            ModelFactory.CreateTokenInstance(
                "appId",
                "auth1",
                DateTimeOffset.Now,
                DateTimeOffset.Now.AddMinutes(60),
                OpenIddictConstants.TokenTypes.Bearer,
                OpenIddictConstants.Statuses.Valid,
                "sub",
                "reference2"
            ),
            CancellationToken.None
        );

        await CreateWithExistingReferenceId().ShouldThrowAsync<DuplicateException>();

        WaitForUserToContinueTheTest(_documentStore);
        return;

        async Task CreateWithExistingReferenceId()
        {
            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    "appId",
                    "auth1",
                    DateTimeOffset.Now,
                    DateTimeOffset.Now.AddMinutes(60),
                    OpenIddictConstants.TokenTypes.Bearer,
                    OpenIddictConstants.Statuses.Valid,
                    "sub",
                    "reference1"
                ),
                CancellationToken.None
            );
        }
    }

    [Fact]
    public async Task ShouldAllowCreatingTokensWithNullReferenceId()
    {
        OpenIdDictRavenDbTokenStore store = CreateTokenStore();

        await store.CreateAsync(
            ModelFactory.CreateTokenInstance(
                "appId",
                "auth1",
                DateTimeOffset.Now,
                DateTimeOffset.Now.AddMinutes(60),
                OpenIddictConstants.TokenTypes.Bearer,
                OpenIddictConstants.Statuses.Valid,
                "sub",
                "reference1"
            ),
            CancellationToken.None
        );

        await store.CreateAsync(
            ModelFactory.CreateTokenInstance(
                applicationId: "appId",
                authorizationId: "auth1",
                creationDate: DateTimeOffset.Now,
                expirationDate: DateTimeOffset.Now.AddMinutes(60),
                type: OpenIddictConstants.TokenTypes.Bearer,
                status: OpenIddictConstants.Statuses.Valid,
                subject: "sub",
                referenceId: null
            ),
            CancellationToken.None
        );

        await store.CreateAsync(
            ModelFactory.CreateTokenInstance(
                applicationId: "appId",
                authorizationId: "auth1",
                creationDate: DateTimeOffset.Now,
                expirationDate: DateTimeOffset.Now.AddMinutes(60),
                type: OpenIddictConstants.TokenTypes.Bearer,
                status: OpenIddictConstants.Statuses.Valid,
                subject: "sub",
                referenceId: null
            ),
            CancellationToken.None
        );

        await store.CreateAsync(
            ModelFactory.CreateTokenInstance(
                applicationId: "appId",
                authorizationId: "auth1",
                creationDate: DateTimeOffset.Now,
                expirationDate: DateTimeOffset.Now.AddMinutes(60),
                type: OpenIddictConstants.TokenTypes.Bearer,
                status: OpenIddictConstants.Statuses.Valid,
                subject: "sub",
                referenceId: null
            ),
            CancellationToken.None
        );

        await CreateWithExistingReferenceId().ShouldThrowAsync<DuplicateException>();

        WaitForIndexing(_documentStore);

        long totalCount = await store.CountAsync(CancellationToken.None);
        totalCount.ShouldBe(4);
        return;

        async Task CreateWithExistingReferenceId()
        {
            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    "appId",
                    "auth1",
                    DateTimeOffset.Now,
                    DateTimeOffset.Now.AddMinutes(60),
                    OpenIddictConstants.TokenTypes.Bearer,
                    OpenIddictConstants.Statuses.Valid,
                    "sub",
                    "reference1"
                ),
                CancellationToken.None
            );
        }
    }

    [Fact]
    public async Task ShouldPruneTokensAndCreateNewWithPrunedReferenceId()
    {
        const string reference1Id = "reference1";

        /*
         * Note: do not set the token expiry date in the past as we are using the RavenDb automatic expire/cleanup
         * feature, so RavenDb will remove that token automatically together with the atomic guard, and will make
         * the test fail as there will be no concurrency exception thrown.
         */

        const int createdThresholdValidMinutes = 30;

        {
            // token that can be prunes based on creation date and non valid status
            await CreateTokenStore(true).CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: "appId",
                    authorizationId: "auth1",
                    creationDate: DateTimeOffset.Now.Subtract(TimeSpan.FromMinutes(createdThresholdValidMinutes + 10)),
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Revoked,
                    subject: "sub",
                    referenceId: reference1Id
                ),
                CancellationToken.None
            );

            await CreateTokenStore(true).CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: "appId",
                    authorizationId: "auth1",
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(60),
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "sub",
                    referenceId: null
                ),
                CancellationToken.None
            );
        }

        WaitForIndexing(_documentStore);

        await CreateAnotherWithReference1().ShouldThrowAsync<ConcurrencyException>();

        WaitForUserToContinueTheTest(_documentStore);

        await CreateTokenStore().PruneAsync(
            DateTimeOffset.Now.Subtract(TimeSpan.FromMinutes(createdThresholdValidMinutes)),
            CancellationToken.None
        );

        WaitForIndexing(_documentStore);

        await CreateAnotherWithReference1();
        await CreateAnotherWithReference1().ShouldThrowAsync<ConcurrencyException>();

        return;

        async Task CreateAnotherWithReference1()
        {
            await CreateTokenStore().CreateAsync(
                ModelFactory.CreateTokenInstance(
                    "appId",
                    "auth-another",
                    DateTimeOffset.Now,
                    DateTimeOffset.Now.AddMinutes(60),
                    OpenIddictConstants.TokenTypes.Bearer,
                    OpenIddictConstants.Statuses.Valid,
                    "sub-another",
                    reference1Id
                ),
                CancellationToken.None
            );
        }
    }

    [Fact]
    public async Task ShouldDeleteToken()
    {
        const string reference1Id = "reference1";

        OpenIdDictRavenDbToken tokenWithReference = ModelFactory.CreateTokenInstance(
            applicationId: "appId",
            authorizationId: "auth1",
            creationDate: DateTimeOffset.Now,
            expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
            type: OpenIddictConstants.TokenTypes.Bearer,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "sub",
            referenceId: reference1Id
        );

        OpenIdDictRavenDbToken tokenWithoutReference = ModelFactory.CreateTokenInstance(
            applicationId: "appId",
            authorizationId: "auth1",
            creationDate: DateTimeOffset.Now,
            expirationDate: DateTimeOffset.Now.AddMinutes(60),
            type: OpenIddictConstants.TokenTypes.Bearer,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "sub",
            referenceId: null
        );

        await CreateTokenStore(true).CreateAsync(
            tokenWithReference,
            CancellationToken.None
        );

        await CreateTokenStore(true).CreateAsync(
            tokenWithoutReference,
            CancellationToken.None
        );

        await CreateAnotherWithReference1().ShouldThrowAsync<ConcurrencyException>();

        {
            OpenIdDictRavenDbTokenStore store = CreateTokenStore();
            Debug.Assert(tokenWithReference.Id != null, nameof(tokenWithReference.Id) + " != null");
            OpenIdDictRavenDbToken? tokenWithReferenceFromDb =
                await store.FindByIdAsync(tokenWithReference.Id, CancellationToken.None);
            tokenWithReferenceFromDb.ShouldNotBeNull();
            tokenWithReferenceFromDb.Id.ShouldBe(tokenWithReference.Id);

            await store.DeleteAsync(tokenWithReferenceFromDb, CancellationToken.None);
        }

        await CreateAnotherWithReference1();
        await CreateAnotherWithReference1().ShouldThrowAsync<ConcurrencyException>();

        return;

        async Task CreateAnotherWithReference1()
        {
            await CreateTokenStore().CreateAsync(
                ModelFactory.CreateTokenInstance(
                    "appId",
                    "auth-another",
                    DateTimeOffset.Now,
                    DateTimeOffset.Now.AddMinutes(60),
                    OpenIddictConstants.TokenTypes.Bearer,
                    OpenIddictConstants.Statuses.Valid,
                    "sub-another",
                    reference1Id
                ),
                CancellationToken.None
            );
        }
    }

    [Fact]
    public async Task ShouldUpdateToken()
    {
        OpenIdDictRavenDbToken tokenWithReference = ModelFactory.CreateTokenInstance(
            applicationId: "appId",
            authorizationId: "auth1",
            creationDate: DateTimeOffset.Now,
            expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
            type: OpenIddictConstants.TokenTypes.Bearer,
            status: OpenIddictConstants.Statuses.Valid,
            subject: "sub",
            referenceId: "reference1"
        );

        OpenIdDictRavenDbToken tokenWithoutReference = ModelFactory.CreateTokenInstance(
            "appId",
            "auth1",
            DateTimeOffset.Now,
            DateTimeOffset.Now.AddMinutes(60),
            OpenIddictConstants.TokenTypes.Bearer,
            OpenIddictConstants.Statuses.Valid,
            "sub"
        );

        await CreateTokenStore(true).CreateAsync(
            tokenWithReference,
            CancellationToken.None
        );
        await CreateTokenStore(true).CreateAsync(
            tokenWithoutReference,
            CancellationToken.None
        );

        {
            OpenIdDictRavenDbTokenStore store = CreateTokenStore();
            Debug.Assert(tokenWithReference.Id != null, nameof(tokenWithReference.Id) + " != null");
            OpenIdDictRavenDbToken? tokenWithReferenceFromDb = await store.FindByIdAsync(
                tokenWithReference.Id,
                CancellationToken.None
            );
            tokenWithReferenceFromDb.ShouldNotBeNull();

            tokenWithReferenceFromDb.Status.ShouldNotBe(OpenIddictConstants.Statuses.Revoked);
            tokenWithReferenceFromDb.Status = OpenIddictConstants.Statuses.Revoked;

            await store.UpdateAsync(tokenWithReferenceFromDb, CancellationToken.None);
        }

        {
            OpenIdDictRavenDbTokenStore store = CreateTokenStore();
            Debug.Assert(tokenWithReference.Id != null, nameof(tokenWithReference.Id) + " != null");
            OpenIdDictRavenDbToken? tokenWithReferenceFromDb = await store.FindByIdAsync(
                tokenWithReference.Id,
                CancellationToken.None
            );
            tokenWithReferenceFromDb.ShouldNotBeNull();
            tokenWithReferenceFromDb.Status.ShouldBe(OpenIddictConstants.Statuses.Revoked);
        }

        {
            OpenIdDictRavenDbTokenStore store = CreateTokenStore();
            Debug.Assert(tokenWithReference.Id != null, nameof(tokenWithReference.Id) + " != null");
            OpenIdDictRavenDbToken? tokenWithReferenceFromDb = await store.FindByIdAsync(
                tokenWithReference.Id,
                CancellationToken.None
            );
            tokenWithReferenceFromDb.ShouldNotBeNull();

            tokenWithReferenceFromDb.ReferenceId.ShouldBe("reference1");
            tokenWithReferenceFromDb.ReferenceId = Guid.NewGuid().ToString();

            // changing reference id is not expected as the reference id is part of the document identifier
            await Assert.ThrowsAsync<Exception>(
                async () => await store.UpdateAsync(tokenWithReferenceFromDb, CancellationToken.None)
            );
        }
    }

    [Fact]
    public async Task ShouldNotUpdateTokenIfUpdatedByAnotherSession()
    {
        string tokenId;
        {
            OpenIdDictRavenDbToken token = ModelFactory.CreateTokenInstance(
                applicationId: "app1",
                authorizationId: "auth1",
                creationDate: DateTimeOffset.Now,
                expirationDate: DateTimeOffset.Now.AddMinutes(60),
                OpenIddictConstants.TokenTypes.Bearer,
                OpenIddictConstants.Statuses.Valid,
                subject: "sub",
                referenceId: "reference1"
            );
            await CreateTokenStore().CreateAsync(token, CancellationToken.None);
            token.Id.ShouldNotBeNull();
            tokenId = token.Id;
        }

        OpenIdDictRavenDbTokenStore store1 = CreateTokenStore();
        OpenIdDictRavenDbTokenStore store2 = CreateTokenStore();

        OpenIdDictRavenDbToken? tokenLoadedFromStore1 = await store1.FindByIdAsync(tokenId, CancellationToken.None);
        tokenLoadedFromStore1.ShouldNotBeNull();

        OpenIdDictRavenDbToken? sameTokenLoadedFromStore2 = await store2.FindByIdAsync(tokenId, CancellationToken.None);
        sameTokenLoadedFromStore2.ShouldNotBeNull();

        tokenLoadedFromStore1.Status = OpenIddictConstants.Statuses.Revoked;
        await store1.UpdateAsync(tokenLoadedFromStore1, CancellationToken.None);

        sameTokenLoadedFromStore2.Status = OpenIddictConstants.Statuses.Revoked;

        await Should.ThrowAsync<ConcurrencyException>(
            async () => { await store2.UpdateAsync(sameTokenLoadedFromStore2, CancellationToken.None); }
        );
    }

    [Fact]
    public async Task ShouldRevokeAllTokens()
    {
        {
            OpenIdDictRavenDbTokenStore store = CreateTokenStore();

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: "appId",
                    authorizationId: "auth1",
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "sub",
                    referenceId: Guid.NewGuid().ToString()
                ),
                CancellationToken.None
            );

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: "appId",
                    authorizationId: "auth1",
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "sub",
                    referenceId: null
                ),
                CancellationToken.None
            );

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: "appId",
                    authorizationId: "auth1",
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "sub",
                    referenceId: Guid.NewGuid().ToString()
                ),
                CancellationToken.None
            );
        }

        int validTokensBeforeRevoke = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Valid);
        validTokensBeforeRevoke.ShouldBe(3);

        await CreateTokenStore().RevokeAsync(
            subject: null,
            client: null,
            status: null,
            type: null,
            cancellationToken: CancellationToken.None
        );

        int validTokensAfterRevoke = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Valid);
        validTokensAfterRevoke.ShouldBe(0);

        int revokedTokens = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Revoked);
        revokedTokens.ShouldBe(3);
    }

    [Fact]
    public async Task ShouldRevokeTokensByApplicationId()
    {
        const string application1Id = "app1Id";
        const string application2Id = "app2Id";

        {
            OpenIdDictRavenDbTokenStore store = CreateTokenStore();

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: application1Id,
                    authorizationId: "auth1",
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "sub",
                    referenceId: Guid.NewGuid().ToString()
                ),
                CancellationToken.None
            );

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: application1Id,
                    authorizationId: "auth1",
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "sub",
                    referenceId: null
                ),
                CancellationToken.None
            );

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: application2Id,
                    authorizationId: "auth1",
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "sub",
                    referenceId: Guid.NewGuid().ToString()
                ),
                CancellationToken.None
            );
        }

        int validTokensBeforeRevoke = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Valid);
        validTokensBeforeRevoke.ShouldBe(3);

        await CreateTokenStore().RevokeByApplicationIdAsync(application1Id, CancellationToken.None);

        int validTokensAfterRevoke = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Valid);
        validTokensAfterRevoke.ShouldBe(1);

        int revokedTokens = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Revoked);
        revokedTokens.ShouldBe(2);

        WaitForUserToContinueTheTest(_documentStore);
    }

    [Fact]
    public async Task ShouldRevokeTokensByAuthorizationId()
    {
        const string authorization1Id = "auth1Id";
        const string authorization2Id = "auth2Id";

        {
            OpenIdDictRavenDbTokenStore store = CreateTokenStore();

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: "app",
                    authorizationId: authorization1Id,
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "sub",
                    referenceId: Guid.NewGuid().ToString()
                ),
                CancellationToken.None
            );

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: "app",
                    authorizationId: authorization1Id,
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "sub",
                    referenceId: null
                ),
                CancellationToken.None
            );

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: "app",
                    authorizationId: authorization2Id,
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: "sub",
                    referenceId: Guid.NewGuid().ToString()
                ),
                CancellationToken.None
            );
        }

        int validTokensBeforeRevoke = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Valid);
        validTokensBeforeRevoke.ShouldBe(3);

        await CreateTokenStore().RevokeByAuthorizationIdAsync(authorization1Id, CancellationToken.None);

        int validTokensAfterRevoke = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Valid);
        validTokensAfterRevoke.ShouldBe(1);

        int revokedTokens = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Revoked);
        revokedTokens.ShouldBe(2);
    }

    [Fact]
    public async Task ShouldRevokeTokensBySubject()
    {
        const string subject1 = "sub1";
        const string subject2 = "sub2";

        {
            OpenIdDictRavenDbTokenStore store = CreateTokenStore();

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: "app",
                    authorizationId: Guid.NewGuid().ToString(),
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: subject1,
                    referenceId: Guid.NewGuid().ToString()
                ),
                CancellationToken.None
            );

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: "app",
                    authorizationId: Guid.NewGuid().ToString(),
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: subject1,
                    referenceId: null
                ),
                CancellationToken.None
            );

            await store.CreateAsync(
                ModelFactory.CreateTokenInstance(
                    applicationId: "app",
                    authorizationId: Guid.NewGuid().ToString(),
                    creationDate: DateTimeOffset.Now,
                    expirationDate: DateTimeOffset.Now.AddMinutes(20), // see note above
                    type: OpenIddictConstants.TokenTypes.Bearer,
                    status: OpenIddictConstants.Statuses.Valid,
                    subject: subject2,
                    referenceId: Guid.NewGuid().ToString()
                ),
                CancellationToken.None
            );
        }

        int validTokensBeforeRevoke = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Valid);
        validTokensBeforeRevoke.ShouldBe(3);

        await CreateTokenStore().RevokeBySubjectAsync(subject1, CancellationToken.None);

        int validTokensAfterRevoke = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Valid);
        validTokensAfterRevoke.ShouldBe(1);

        int revokedTokens = (await CreateTokenStore().ListAsync(100, 0, CancellationToken.None).ToListAsync())
            .Count(t => t.Status == OpenIddictConstants.Statuses.Revoked);
        revokedTokens.ShouldBe(2);
    }

    private OpenIdDictRavenDbTokenStore CreateTokenStore(bool useSessionOptimisticConcurrency = false)
    {
        return new OpenIdDictRavenDbTokenStore(
            () =>
            {
                IAsyncDocumentSession session = _documentStore.OpenAsyncSession();
                if (useSessionOptimisticConcurrency)
                {
                    session.Advanced.UseOptimisticConcurrency = true;
                }

                return session;
            },
            new Mock<ILogger<OpenIdDictRavenDbTokenStore>>().Object
        );
    }

    private async Task<OpenIdDictRavenDbToken?> LoadTokenFromDatabase(string id)
    {
        return await _documentStore.OpenAsyncSession().LoadAsync<OpenIdDictRavenDbToken>(id);
    }
}