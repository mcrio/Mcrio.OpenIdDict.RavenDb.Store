using System;
using System.Linq;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Raven.Client.Documents.Indexes;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Index;

/// <summary>
/// OpenIdDict Authorizations static index required for the prune job and deletion by query.
/// </summary>
/// <typeparam name="TAuthorization">Authorization type.</typeparam>
/// <typeparam name="TToken">Token type.</typeparam>
/// ReSharper disable once InconsistentNaming
public abstract class OIDct_Authorizations<TAuthorization, TToken>
    : AbstractMultiMapIndexCreationTask<OIDct_Authorizations<TAuthorization, TToken>.IndexEntry>
    where TAuthorization : OpenIdDictRavenDbAuthorization
    where TToken : OpenIdDictRavenDbToken
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OIDct_Authorizations{TAuthorization, TToken}"/> class.
    /// </summary>
    protected OIDct_Authorizations()
    {
        AddMap<TAuthorization>(
            authorizations => from auth in authorizations
                let authId = auth.Id ?? string.Empty
                select new IndexEntry
                {
                    AuthorizationId = authId,
                    CreationDate = auth.CreationDate,
                    Status = auth.Status,
                    Type = auth.Type,
                    HasTokens = false,
                }
        );

        // This map is needed so we can compute the `HasTokens` property
        AddMap<TToken>(
            tokens => from token in tokens
                let authId = token.AuthorizationId ?? string.Empty
                select new IndexEntry
                {
                    AuthorizationId = authId,
                    CreationDate = null,
                    Status = null,
                    Type = null,
                    HasTokens = true,
                }
        );

        Reduce = results => from result in results
            group result by result.AuthorizationId
            into g
            where g.Key != null // check if authorization id is present
            where g.Any(
                item => item.CreationDate != null
            ) // check if authorization document is present in the database
            let creationDateItem = g.FirstOrDefault(f => f.CreationDate != null)
            let statusItem = g.FirstOrDefault(f => f.Status != null)
            let typeItem = g.FirstOrDefault(f => f.Type != null)
            select new IndexEntry
            {
                AuthorizationId = g.Key,
                CreationDate = creationDateItem != null ? creationDateItem.CreationDate : null,
                Status = statusItem != null ? statusItem.Status : null,
                Type = typeItem != null ? typeItem.Type : null,
                HasTokens = g.Any(x => x.HasTokens),
            };

        // Outputting to collection required as RavenDB currently does not support using delete by query on a map-reduce index.
        // See `OpenIdDictRavenDbConventions.AuthorizationsIndexDocumentsCollectionName` comment for more info
        OutputReduceToCollection = OpenIdDictRavenDbConventions.AuthorizationsIndexDocumentsCollectionName;
    }

    /// <summary>
    /// Index entry.
    /// </summary>
    public class IndexEntry
    {
        /// <summary>
        /// Gets or sets the authorization id.
        /// </summary>
        public required string AuthorizationId { get; set; }

        /// <summary>
        /// Gets or sets the UTC creation date of the current authorization.
        /// </summary>
        public DateTimeOffset? CreationDate { get; set; }

        /// <summary>
        /// Gets or sets the status of the current authorization.
        /// </summary>
        public string? Status { get; set; }

        /// <summary>
        /// Gets or sets the type of the current authorization.
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// Indicates whether the authorization has related tokens.
        /// </summary>
        public bool HasTokens { get; set; }
    }
}

/// <inheritdoc />
/// ReSharper disable once InconsistentNaming
internal class OIDct_Authorizations : OIDct_Authorizations<OpenIdDictRavenDbAuthorization, OpenIdDictRavenDbToken>;