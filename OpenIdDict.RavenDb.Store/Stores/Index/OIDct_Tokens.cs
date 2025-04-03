using System;
using System.Linq;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Raven.Client.Documents.Indexes;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Index;

/// <summary>
/// OpenIdDict static index required for token pruning operation.
/// </summary>
/// <typeparam name="TToken">Token document type.</typeparam>
/// ReSharper disable once InconsistentNaming
public abstract class OIDct_Tokens<TToken> : AbstractIndexCreationTask<TToken, OIDct_Tokens<TToken>.IndexEntry>
    where TToken : OpenIdDictRavenDbToken
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OIDct_Tokens{TToken}"/> class.
    /// </summary>
    protected OIDct_Tokens()
    {
        Map = tokens => from token in tokens
            let authorization = token.AuthorizationId == null
                ? null
                : LoadDocument<OpenIdDictRavenDbAuthorization>(token.AuthorizationId)
            let authorizationStatus = authorization == null ? null : authorization.Status
            select new IndexEntry
            {
                AuthorizationStatus = authorizationStatus,
                CreationDate = token.CreationDate,
                Status = token.Status,
                ExpirationDate = token.ExpirationDate,
            };
    }

    /// <summary>
    /// Index entry.
    /// </summary>
    public class IndexEntry
    {
        /// <summary>
        /// Gets the related authorization status.
        /// </summary>
        public string? AuthorizationStatus { get; set; }

        /// <summary>
        /// Gets the token creation date.
        /// </summary>
        public virtual DateTimeOffset? CreationDate { get; set; }

        /// <summary>
        /// Gets the token status.
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Gets the token expiration date.
        /// </summary>
        public virtual DateTimeOffset? ExpirationDate { get; set; }
    }
}

/// <inheritdoc />
/// ReSharper disable once InconsistentNaming
internal class OIDct_Tokens : OIDct_Tokens<OpenIdDictRavenDbToken>;