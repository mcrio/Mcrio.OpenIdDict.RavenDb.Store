using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique.Exceptions;
using Microsoft.Extensions.Logging;
using Moq;
using Raven.Client.Documents;
using Shouldly;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Tests.Integration;

public sealed class ApplicationStoreTest : IntegrationTestsBase
{
    private readonly IDocumentStore _documentStore;

    public ApplicationStoreTest()
    {
        _documentStore = GetDocumentStore();
    }

    [Fact]
    public async Task ShouldCreateApplication()
    {
        OpenIdDictRavenDbApplication application = ModelFactory.CreateApplicationInstance("test-client");

        OpenIdDictRavenDbApplicationStore store = CreateApplicationStore();

        await store.CreateAsync(application, CancellationToken.None);

        application.Id.ShouldNotBeNull();
        application.ClientId.ShouldBe("test-client");

        WaitForUserToContinueTheTest(_documentStore);

        await AssertReservationDocumentExistsWithValueAsync(
            session: _documentStore.OpenAsyncSession(),
            uniqueReservationType: UniqueReservationType.ApplicationClientId,
            expectedUniqueValue: application.ClientId!,
            expectedReferenceDocumentId: application.Id
        );

        OpenIdDictRavenDbApplication? applicationFromDatabase = await LoadApplicationFromDatabase(application.Id);
        applicationFromDatabase.ShouldNotBeNull();
        applicationFromDatabase.ClientId.ShouldNotBeNull();
        applicationFromDatabase.ClientId.ShouldBe("test-client");
        applicationFromDatabase.Id.ShouldBe(application.Id);
    }

    [Fact]
    public async Task ShouldNotCreateApplicationWithSameClientId()
    {
        await CreateApplicationInstanceAndStoreToDb(
            CreateApplicationStore(),
            "test-client"
        );

        await CreateWithExistingClientId().ShouldThrowAsync<DuplicateException>();
        return;

        async Task CreateWithExistingClientId()
        {
            await CreateApplicationInstanceAndStoreToDb(
                CreateApplicationStore(),
                "test-client"
            );
        }
    }

    [Fact]
    public async Task ShouldUpdateApplication()
    {
        string applicationId = (await CreateApplicationInstanceAndStoreToDb(
            CreateApplicationStore(),
            "test-client"
        )).Id!;

        OpenIdDictRavenDbApplicationStore store = CreateApplicationStore();
        OpenIdDictRavenDbApplication? application = await store.FindByIdAsync(
            applicationId,
            CancellationToken.None
        );
        application.ShouldNotBeNull();
        application.ClientId.ShouldBe("test-client");

        application.ClientId = "test-client-updated";
        await store.UpdateAsync(application, CancellationToken.None);

        OpenIdDictRavenDbApplication? applicationFromDatabase = await LoadApplicationFromDatabase(applicationId);
        applicationFromDatabase.ShouldNotBeNull();
        applicationFromDatabase.ClientId.ShouldBe("test-client-updated");

        await AssertReservationDocumentExistsWithValueAsync(
            session: _documentStore.OpenAsyncSession(),
            uniqueReservationType: UniqueReservationType.ApplicationClientId,
            expectedUniqueValue: "test-client-updated",
            expectedReferenceDocumentId: applicationId
        );

        await AssertReservationDocumentDoesNotExistAsync(
            _documentStore.OpenAsyncSession(),
            UniqueReservationType.ApplicationClientId,
            "test-client"
        );
    }

    [Fact]
    public async Task ShouldDeleteApplication()
    {
        string applicationId = (await CreateApplicationInstanceAndStoreToDb(
            CreateApplicationStore(),
            "test-client"
        )).Id!;

        OpenIdDictRavenDbApplicationStore store = CreateApplicationStore();
        OpenIdDictRavenDbApplication? application = await store.FindByIdAsync(
            applicationId,
            CancellationToken.None
        );
        application.ShouldNotBeNull();
        application.ClientId.ShouldBe("test-client");

        await store.DeleteAsync(application, CancellationToken.None);

        await AssertReservationDocumentDoesNotExistAsync(
            _documentStore.OpenAsyncSession(),
            UniqueReservationType.ApplicationClientId,
            "test-client"
        );
    }

    [Fact]
    public async Task ShouldFindApplicationBy()
    {
        string applicationId = (await CreateApplicationInstanceAndStoreToDb(
            CreateApplicationStore(),
            clientId: "test-client",
            redirectUris: ["https://localhost/signin-oidc", "https://localhost/signin-oidc/callback"],
            postLogoutRedirectUris: ["https://localhost/signout-oidc",]
        )).Id!;

        OpenIdDictRavenDbApplicationStore store = CreateApplicationStore();
        OpenIdDictRavenDbApplication? byId = await store.FindByIdAsync(applicationId, CancellationToken.None);
        byId.ShouldNotBeNull();
        byId.Id.ShouldBe(applicationId);
        byId.ClientId.ShouldBe("test-client");

        WaitForIndexing(_documentStore);

        OpenIdDictRavenDbApplication? byClientId = await store.FindByClientIdAsync(
            "test-client",
            CancellationToken.None);
        byClientId.ShouldNotBeNull();
        byClientId.Id.ShouldBe(applicationId);
        byClientId.ClientId.ShouldBe("test-client");

        string application2Id = (await CreateApplicationInstanceAndStoreToDb(
            CreateApplicationStore(),
            clientId: "test-client-2",
            redirectUris: ["https://localhost/signin-oidc", "https://localhost/signin-oidc/callback"],
            postLogoutRedirectUris: ["https://localhost/signout-oidc",]
        )).Id!;

        string application3Id = (await CreateApplicationInstanceAndStoreToDb(
            CreateApplicationStore(),
            clientId: "test-client-3",
            redirectUris: ["https://localhost"],
            postLogoutRedirectUris: ["https://localhost",]
        )).Id!;

        WaitForIndexing(_documentStore);

        {
            var byRedirectUri = new List<OpenIdDictRavenDbApplication>();
            await foreach (OpenIdDictRavenDbApplication app in store.FindByRedirectUriAsync(
                               "https://localhost",
                               CancellationToken.None))
            {
                byRedirectUri.Add(app);
            }

            byRedirectUri.Count.ShouldBe(1);
            byRedirectUri.Select(item => item.Id).ShouldContain(application3Id);
        }

        {
            var byRedirectUri = new List<OpenIdDictRavenDbApplication>();
            await foreach (OpenIdDictRavenDbApplication app in store.FindByRedirectUriAsync(
                               "https://localhost/signin-oidc/callback",
                               CancellationToken.None))
            {
                byRedirectUri.Add(app);
            }

            byRedirectUri.Count.ShouldBe(2);
            byRedirectUri.Select(item => item.Id).ShouldContain(applicationId, application2Id);
        }

        {
            var byPostLogoutRedirectUri = new List<OpenIdDictRavenDbApplication>();
            await foreach (OpenIdDictRavenDbApplication app in store.FindByPostLogoutRedirectUriAsync(
                               "https://localhost/signout-oidc",
                               CancellationToken.None))
            {
                byPostLogoutRedirectUri.Add(app);
            }

            byPostLogoutRedirectUri.Count.ShouldBe(2);
            byPostLogoutRedirectUri.Select(item => item.Id).ShouldContain(applicationId, application2Id);
        }
    }

    private OpenIdDictRavenDbApplicationStore CreateApplicationStore()
    {
        return new OpenIdDictRavenDbApplicationStore(
            () => _documentStore.OpenAsyncSession(),
            new Mock<ILogger<OpenIdDictRavenDbApplicationStore>>().Object
        );
    }

    private async Task<OpenIdDictRavenDbApplication?> LoadApplicationFromDatabase(string id)
    {
        return await _documentStore.OpenAsyncSession().LoadAsync<OpenIdDictRavenDbApplication>(id);
    }
}