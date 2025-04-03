using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Session;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores;

/// <summary>
/// Store extension methods.
/// </summary>
internal static class StoreExtensions
{
    /// <summary>
    /// Streams documents from a RavenDB query as an asynchronous enumerable sequence.
    /// </summary>
    /// <typeparam name="TDocument">The type of the documents to stream.</typeparam>
    /// <param name="session">The RavenDB asynchronous document session.</param>
    /// <param name="query">The query to be executed against the database.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the operation if required.</param>
    /// <returns>An <see cref="IAsyncEnumerable{T}"/> that streams the query results as instances of <typeparamref name="TDocument"/>.</returns>
    internal static async IAsyncEnumerable<TDocument> ToRavenDbStreamAsync<TDocument>(
        this IAsyncDocumentSession session,
        IQueryable<TDocument> query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(query);

        await using IAsyncEnumerator<StreamResult<TDocument>> streamResults =
            await session.Advanced.StreamAsync(query, cancellationToken).ConfigureAwait(false);

        while (await streamResults.MoveNextAsync().ConfigureAwait(false))
        {
            yield return streamResults.Current.Document;
        }
    }
}