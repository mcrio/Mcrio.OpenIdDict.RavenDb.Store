using System.Threading;
using System.Threading.Tasks;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Index;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;

namespace Mcrio.OpenIdDict.RavenDb.Store;

/// <summary>
/// RavenDB create OpenIdDict related index helper.
/// </summary>
public static class RavenDbOpenIdDictIndexCreator
{
    /// <summary>
    /// Creates OpenIdDict related RavenDb static indexes.
    /// </summary>
    /// <remarks>For production environments it is advised to manage indexes manually.</remarks>
    /// <typeparam name="TAuthorizationsIndex">Authorization index type.</typeparam>
    /// <typeparam name="TTokenIndex">Token index type.</typeparam>
    /// <typeparam name="TAuthorization">Authorization document type.</typeparam>
    /// <typeparam name="TToken">Token document type.</typeparam>
    /// <param name="documentStore"></param>
    /// <param name="databaseName"></param>
    /// <param name="documentConventions"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>A <see cref="Task"/> representing the result of the asynchronous operation.</returns>
    public static async Task CreateIndexesAsync<
        TAuthorizationsIndex,
        TTokenIndex,
        TAuthorization,
        TToken>(
        IDocumentStore documentStore,
        string databaseName,
        DocumentConventions? documentConventions = null,
        CancellationToken cancellationToken = default)
        where TAuthorizationsIndex : OIDct_Authorizations<TAuthorization, TToken>, new()
        where TTokenIndex : OIDct_Tokens<TToken>, new()
        where TAuthorization : OpenIdDictRavenDbAuthorization
        where TToken : OpenIdDictRavenDbToken
    {
        await new TAuthorizationsIndex().ExecuteAsync(
            documentStore,
            documentConventions,
            databaseName,
            cancellationToken
        );
        await new TTokenIndex().ExecuteAsync(
            documentStore,
            documentConventions,
            databaseName,
            cancellationToken
        );
    }
}