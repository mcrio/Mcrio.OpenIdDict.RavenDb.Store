using System;
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

public sealed class ScopeStoreTest : IntegrationTestsBase
{
    private readonly IDocumentStore _documentStore;

    public ScopeStoreTest()
    {
        _documentStore = GetDocumentStore();
    }

    [Fact]
    public async Task ShouldCreateScope()
    {
        OpenIdDictRavenDbScope scope = ModelFactory.CreateScopeInstance("scope-1");

        OpenIdDictRavenDbScopeStore store = CreateScopeStore();

        await store.CreateAsync(scope, CancellationToken.None);

        scope.Id.ShouldNotBeNull();
        scope.Name.ShouldBe("scope-1");

        WaitForUserToContinueTheTest(_documentStore);

        await AssertReservationDocumentExistsWithValueAsync(
            session: _documentStore.OpenAsyncSession(),
            uniqueReservationType: UniqueReservationType.ScopeName,
            expectedUniqueValue: scope.Name!,
            expectedReferenceDocumentId: scope.Id
        );

        OpenIdDictRavenDbScope? scopeFromDatabase = await LoadScopeFromDatabase(scope.Id);
        scopeFromDatabase.ShouldNotBeNull();
        scopeFromDatabase.Name.ShouldNotBeNull();
        scopeFromDatabase.Name.ShouldBe("scope-1");
        scopeFromDatabase.Id.ShouldBe(scope.Id);
    }

    [Fact]
    public async Task ShouldNotCreateScopeWithSameName()
    {
        const string scopeName = "scope-1";
        await CreateScopeInstanceAndStoreToDb(
            CreateScopeStore(),
            scopeName
        );

        await CreateWithExistingScopeName().ShouldThrowAsync<DuplicateException>();
        return;

        async Task CreateWithExistingScopeName()
        {
            await CreateScopeInstanceAndStoreToDb(
                CreateScopeStore(),
                scopeName
            );
        }
    }

    [Fact]
    public async Task ShouldUpdateScope()
    {
        string scopeId = (await CreateScopeInstanceAndStoreToDb(
            CreateScopeStore(),
            "scope-1"
        )).Id!;

        OpenIdDictRavenDbScopeStore store = CreateScopeStore();
        OpenIdDictRavenDbScope? scope = await store.FindByIdAsync(
            scopeId,
            CancellationToken.None
        );
        scope.ShouldNotBeNull();
        scope.Name.ShouldBe("scope-1");

        scope.Name = "scope-1-updated";
        await store.UpdateAsync(scope, CancellationToken.None);

        OpenIdDictRavenDbScope? scopeFromDatabase = await LoadScopeFromDatabase(scopeId);
        scopeFromDatabase.ShouldNotBeNull();
        scopeFromDatabase.Name.ShouldBe("scope-1-updated");

        await AssertReservationDocumentExistsWithValueAsync(
            session: _documentStore.OpenAsyncSession(),
            uniqueReservationType: UniqueReservationType.ScopeName,
            expectedUniqueValue: "scope-1-updated",
            expectedReferenceDocumentId: scopeId
        );

        await AssertReservationDocumentDoesNotExistAsync(
            _documentStore.OpenAsyncSession(),
            UniqueReservationType.ScopeName,
            "scope-1"
        );
    }

    [Fact]
    public async Task ShouldDeleteScope()
    {
        string scopeId = (await CreateScopeInstanceAndStoreToDb(
            CreateScopeStore(),
            "scope-1"
        )).Id!;

        OpenIdDictRavenDbScopeStore store = CreateScopeStore();
        OpenIdDictRavenDbScope? scope = await store.FindByIdAsync(
            scopeId,
            CancellationToken.None
        );
        scope.ShouldNotBeNull();
        scope.Name.ShouldBe("scope-1");

        await store.DeleteAsync(scope, CancellationToken.None);

        await AssertReservationDocumentDoesNotExistAsync(
            _documentStore.OpenAsyncSession(),
            UniqueReservationType.ScopeName,
            "scope-1"
        );
    }

    [Fact]
    public async Task ShouldFindScopeByName()
    {
        string scope1Id = (await CreateScopeInstanceAndStoreToDb(
            CreateScopeStore(),
            "scope-1"
        )).Id!;
        string scope2Id = (await CreateScopeInstanceAndStoreToDb(
            CreateScopeStore(),
            "scope-2"
        )).Id!;
        string scope3Id = (await CreateScopeInstanceAndStoreToDb(
            CreateScopeStore(),
            "scope-3"
        )).Id!;

        WaitForIndexing(_documentStore);

        OpenIdDictRavenDbScopeStore store = CreateScopeStore();

        OpenIdDictRavenDbScope? byExistingName = await store.FindByNameAsync("scope-2", CancellationToken.None);
        byExistingName.ShouldNotBeNull();
        byExistingName.Id.ShouldBe(scope2Id);

        OpenIdDictRavenDbScope? byNonExistingName = await store.FindByNameAsync(
            Guid.NewGuid().ToString(),
            CancellationToken.None
        );
        byNonExistingName.ShouldBeNull();

        var byExistingAndNonExistingNames = new List<OpenIdDictRavenDbScope>();
        await foreach (OpenIdDictRavenDbScope scope in store.FindByNamesAsync(
                           ["scope-1", "scope-2", Guid.NewGuid().ToString()],
                           CancellationToken.None
                       ))
        {
            byExistingAndNonExistingNames.Add(scope);
        }

        byExistingAndNonExistingNames.Count.ShouldBe(2);
        byExistingAndNonExistingNames.Select(item => item.Id).ShouldContain(scope1Id, scope2Id);
    }

    private OpenIdDictRavenDbScopeStore CreateScopeStore()
    {
        return new OpenIdDictRavenDbScopeStore(
            () => _documentStore.OpenAsyncSession(),
            new Mock<ILogger<OpenIdDictRavenDbScopeStore>>().Object
        );
    }

    private async Task<OpenIdDictRavenDbScope?> LoadScopeFromDatabase(string id)
    {
        return await _documentStore.OpenAsyncSession().LoadAsync<OpenIdDictRavenDbScope>(id);
    }
}