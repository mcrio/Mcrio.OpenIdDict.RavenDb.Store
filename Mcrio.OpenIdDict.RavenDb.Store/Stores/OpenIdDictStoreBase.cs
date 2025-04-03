using System;
using Raven.Client.Documents.Session;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores;

/// <summary>
/// OpenIdDict stores base class.
/// </summary>
public abstract class OpenIdDictStoreBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenIdDictStoreBase"/> class.
    /// </summary>
    /// <param name="session"></param>
    protected OpenIdDictStoreBase(IAsyncDocumentSession session)
    {
        Session = session;
    }

    /// <summary>
    /// Gets the document session.
    /// </summary>
    protected IAsyncDocumentSession Session { get; }

    /// <summary>
    /// Indicates whether the current document session is in Cluster Wide transaction mode.
    /// </summary>
    /// <returns>TRUE when cluster wide transaction, FALSE otherwise.</returns>
    protected bool IsClusterWideTransaction()
    {
        return ((AsyncDocumentSession)Session).TransactionMode == TransactionMode.ClusterWide;
    }

    /// <summary>
    /// If cluster-wide transaction throw na exception as it is not supported for current operation.
    /// </summary>
    /// <exception cref="InvalidOperationException">When cluster-wide transaction.</exception>
    /// <remarks><see cref="OpenIdDictRavenDbTokenStore{TToken,TTokenIndex}.CreateAsync"/> inline comment for more details.</remarks>
    protected void ThrowWhenClusterWideAsNotSupported()
    {
        if (IsClusterWideTransaction())
        {
            throw new InvalidOperationException(
                "Cluster wide transaction is not supported for this operation due to possible issues: - Atomic Guards are not removed when deleting documents by query (https://github.com/ravendb/ravendb/issues/20404)."
            );
        }
    }
}