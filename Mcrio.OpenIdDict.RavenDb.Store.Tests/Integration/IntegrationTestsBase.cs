using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Index;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique;
using OpenIddict.Abstractions;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Session;
using Raven.TestDriver;
using Shouldly;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Tests.Integration;

public abstract class IntegrationTestsBase : RavenTestDriver
{
    static IntegrationTestsBase()
    {
        ConfigureServer(
            new TestServerOptions
            {
                Licensing =
                {
                    EulaAccepted = true,
                    LicensePath = RavenDbTestLicenseGetter.GetRavenDbDeveloperLicensePath(),
                },
            });
    }

    protected static async Task AssertReservationDocumentExistsWithValueAsync(
        IAsyncDocumentSession session,
        UniqueReservationType uniqueReservationType,
        string expectedUniqueValue,
        string expectedReferenceDocumentId,
        string because = "")
    {
        var uniqueUtility = new UniqueReservationDocumentUtility(
            session,
            uniqueReservationType,
            expectedUniqueValue
        );
        bool exists = await uniqueUtility.CheckIfUniqueIsTakenAsync();
        exists.ShouldBeTrue(because);

        UniqueReservation? reservation = await uniqueUtility.LoadReservationAsync();
        reservation.ShouldNotBeNull();
        reservation.ReferenceId.ShouldBe(expectedReferenceDocumentId);
    }

    protected static async Task AssertReservationDocumentDoesNotExistAsync(
        IAsyncDocumentSession session,
        UniqueReservationType uniqueReservationType,
        string expectedUniqueValue,
        string because = "")
    {
        var uniqueUtility = new UniqueReservationDocumentUtility(
            session,
            uniqueReservationType,
            expectedUniqueValue
        );
        bool exists = await uniqueUtility.CheckIfUniqueIsTakenAsync();
        exists.ShouldBeFalse(because);
    }

    protected static async Task<OpenIdDictRavenDbApplication> CreateApplicationInstanceAndStoreToDb(
        OpenIdDictRavenDbApplicationStore store,
        string clientId,
        List<string>? redirectUris = null,
        List<string>? postLogoutRedirectUris = null
    )
    {
        OpenIdDictRavenDbApplication application = ModelFactory.CreateApplicationInstance(
            clientId: clientId,
            redirectUris: redirectUris,
            postLogoutRedirectUris: postLogoutRedirectUris);

        await store.CreateAsync(
            application,
            CancellationToken.None
        );

        application.Id.ShouldNotBeNull();
        return application;
    }

    protected static async Task<OpenIdDictRavenDbScope> CreateScopeInstanceAndStoreToDb(
        IOpenIddictScopeStore<OpenIdDictRavenDbScope> store,
        string name)
    {
        OpenIdDictRavenDbScope scope = ModelFactory.CreateScopeInstance(
            name: name
        );

        await store.CreateAsync(
            scope,
            CancellationToken.None
        );

        scope.Id.ShouldNotBeNull();
        return scope;
    }

    protected static async Task<List<T>> FromAsyncEnumerableAsync<T>(IAsyncEnumerable<T> asyncEnumerable)
    {
        var list = new List<T>();
        await foreach (T item in asyncEnumerable)
        {
            list.Add(item);
        }

        return list;
    }

    protected override void SetupDatabase(IDocumentStore documentStore)
    {
        base.SetupDatabase(documentStore);

        RavenDbOpenIdDictIndexCreator
            .CreateIndexesAsync<
                OIDct_Authorizations,
                OIDct_Tokens,
                OpenIdDictRavenDbAuthorization,
                OpenIdDictRavenDbToken
            >(
                documentStore,
                documentStore.Database
            )
            .GetAwaiter()
            .GetResult();
    }

    protected override void PreInitialize(IDocumentStore documentStore)
    {
        documentStore.Conventions.FindCollectionName = type =>
        {
            if (OpenIdDictRavenDbConventions.TryGetCollectionName(
                    type,
                    out string? collectionName
                ))
            {
                Debug.Assert(collectionName is not null, "collectionName must not be null");
                return collectionName;
            }

            return DocumentConventions.DefaultGetCollectionName(type);
        };

        documentStore.Conventions.TransformTypeCollectionNameToDocumentIdPrefix = collectionName =>
        {
            if (OpenIdDictRavenDbConventions.TryGetCollectionPrefix(collectionName, out string? prefix))
            {
                Debug.Assert(prefix is not null, "prefix must not be null");
                return prefix;
            }

            return DocumentConventions.DefaultTransformCollectionNameToDocumentIdPrefix(collectionName);
        };
    }
}