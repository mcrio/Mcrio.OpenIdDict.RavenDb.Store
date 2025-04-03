using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Index;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique.Exceptions;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Raven.Client;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Queries;
using Raven.Client.Exceptions.Documents.Session;
using ConcurrencyException = Raven.Client.Exceptions.ConcurrencyException;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores;

/// <summary>
/// OpenIdDict token store.
/// </summary>
/// <typeparam name="TToken">Token document type.</typeparam>
/// <typeparam name="TTokenIndex">Token index type.</typeparam>
public abstract class OpenIdDictRavenDbTokenStore<TToken, TTokenIndex>
    : OpenIdDictStoreBase, IOpenIddictTokenStore<TToken>
    where TToken : OpenIdDictRavenDbToken
    where TTokenIndex : OIDct_Tokens<TToken>, new()
{
    private readonly TimeSpan _operationWaitForResultTimeout = TimeSpan.FromSeconds(30);
    private readonly ILogger<OpenIdDictRavenDbTokenStore<TToken, TTokenIndex>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenIdDictRavenDbTokenStore{TToken, TTokenIndex}"/> class.
    /// </summary>
    /// <param name="sessionProvider"></param>
    /// <param name="logger"></param>
    protected OpenIdDictRavenDbTokenStore(
        OpenIdDictDocumentSessionProvider sessionProvider,
        ILogger<OpenIdDictRavenDbTokenStore<TToken, TTokenIndex>> logger)
        : base(sessionProvider.Invoke())
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        return await Session.Query<TToken>().CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TToken>, IQueryable<TResult>> query,
        CancellationToken cancellationToken)
    {
        return await query(Session.Query<TToken>()).CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask CreateAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token, nameof(token));

        /*
         * NOTE: ReferenceId has a Unique requirement ONLY when it is set.
         * There can be many tokens with ReferenceId equal to NULL.
         * Using reference documents and atomic guards to fulfill the unique requirement
         * introduces complexity when pruning old tokens as the reference documents must be pruned as well.
         *
         * Expectation: We are expecting the referenceId will not change for a token, and that will be checked in code.
         *
         * Solution: When reference token is set make it part of the semantic id, otherwise use HiLo.
         * NOTE!!!
         * ~~Use cluster wide transaction to enforce atomic guard usage for token id to fulfill unique requirement.~~
         * We cannot rely on cluster wide transactions and atomic guards as RavenDB deletion by query is not
         * a cluster wide transaction and WILL NOT remove the atomic guards.
         * We can just use optimistic concurrency and make sure the transaction is not cluster wide for handling tokens,
         * to prevent pile up of atomic guards!
         *
         * We will use optimistic concurrency and rely on the library not to assign duplicate reference tokens, and in
         * case it should happen then the new one would overwrite the existing one.
         */

        ThrowWhenClusterWideAsNotSupported();

        bool hasReferenceId = !string.IsNullOrWhiteSpace(token.ReferenceId);
        if (hasReferenceId)
        {
            // make it part of the ID
            string? tokenCollectionName =
                Session.Advanced.DocumentStore.Conventions.FindCollectionName(typeof(TToken));
            Debug.Assert(tokenCollectionName is not null, "Token collection name must not be null");

            string? tokenCollectionPrefix =
                Session.Advanced.DocumentStore.Conventions.TransformTypeCollectionNameToDocumentIdPrefix(
                    tokenCollectionName);
            Debug.Assert(
                !string.IsNullOrWhiteSpace(tokenCollectionPrefix),
                "Token collection prefix must not be empty");

            char idSeparator = Session.Advanced.DocumentStore.Conventions.IdentityPartsSeparator;

            Debug.Assert(
                !string.IsNullOrWhiteSpace(token.ReferenceId),
                "Token reference id must not be null or whitespace"
            );
            token.Id = $"{tokenCollectionPrefix}{idSeparator}{token.ReferenceId}";

            Debug.Assert(token.Id.EndsWith(token.ReferenceId), "Token id expected to end with reference id");

            // as we are relying on optimistic concurrency, and the library not generating duplicate reference ids
            // let's also check if there's already a document with the same identifier
            TToken? existingWithSameId = await Session
                .LoadAsync<TToken>(token.Id, cancellationToken)
                .ConfigureAwait(false);
            if (existingWithSameId is not null)
            {
                throw new DuplicateException(
                    $"Failed creating token as document with the same id {token.Id} already exists"
                );
            }
        }

        try
        {
            await Session.StoreAsync(
                entity: token,
                changeVector: string.Empty,
                id: token.Id,
                token: cancellationToken
            ).ConfigureAwait(false);
        }
        catch (NonUniqueObjectException nonUniqueException)
        {
            _logger.LogInformation(
                nonUniqueException,
                "Failed creating token with authorization {AuthorizationId} and reference id {ReferenceId} due to a non-unique token id",
                token.AuthorizationId,
                token.ReferenceId
            );
            throw new DuplicateException("Failed creating token as document with the same id already exists");
        }

        Debug.Assert(token.Id is not null, "Token id must not be null");

        // Adding @expire tag supported by RavenDb automatic cleanup.
        Session.Advanced.GetMetadataFor(
            token
        )[Constants.Documents.Metadata.Expires] = token.ExpirationDate;

        try
        {
            Debug.Assert(
                IsClusterWideTransaction() is false,
                "Cluster wide transaction is not supported as atomic guards would not be cleaned up after the prune/(delete by query) operation"
            );

            await Session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyException concurrencyException)
        {
            _logger.LogError(
                concurrencyException,
                "Failed creating token with authorization {AuthorizationId} and reference id {ReferenceId} due to a concurrency conflict",
                token.AuthorizationId,
                token.ReferenceId
            );
            throw new Unique.Exceptions.ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed creating token with authorization {AuthorizationId} and reference id {ReferenceId} due to an error",
                token.AuthorizationId,
                token.ReferenceId
            );
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token, nameof(token));

        // it's a deletion operation so it doesn't matter if it's cluster-wide or not. See CreateAsync for more info.
        if (IsClusterWideTransaction())
        {
            Session.Delete(token.Id);
        }
        else
        {
            string changeVector = Session.Advanced.GetChangeVectorFor(token);
            Session.Delete(token.Id, changeVector);
        }

        try
        {
            await Session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyException concurrencyException)
        {
            _logger.LogError(
                concurrencyException,
                "Failed deleting token {Id} due to a concurrency conflict",
                token.Id
            );
            throw new Unique.Exceptions.ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed deleting token {Id} due to an error",
                token.Id
            );
            throw;
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TToken> FindAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        CancellationToken cancellationToken)
    {
        IRavenQueryable<TToken>? query = Session.Query<TToken>();

        if (!string.IsNullOrEmpty(subject))
        {
            query = query.Where(token => token.Subject == subject);
        }

        if (!string.IsNullOrEmpty(client))
        {
            query = query.Where(token => token.ApplicationId == client);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(token => token.Status == status);
        }

        if (!string.IsNullOrEmpty(type))
        {
            query = query.Where(token => token.Type == type);
        }

        return Session.ToRavenDbStreamAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TToken> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        IRavenQueryable<TToken> query = Session.Query<TToken>().Where(a => a.ApplicationId == identifier);
        return Session.ToRavenDbStreamAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TToken> FindByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        IRavenQueryable<TToken>? query = Session.Query<TToken>().Where(a => a.AuthorizationId == identifier);
        return Session.ToRavenDbStreamAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<TToken?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        return await Session.LoadAsync<TToken>(identifier, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<TToken?> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken)
    {
        return await Session
            .Query<TToken>()
            .FirstOrDefaultAsync(a => a.ReferenceId == identifier, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TToken> FindBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        IRavenQueryable<TToken>? query = Session.Query<TToken>().Where(a => a.Subject == subject);
        return Session.ToRavenDbStreamAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetApplicationIdAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return string.IsNullOrWhiteSpace(token.ApplicationId)
            ? new ValueTask<string?>(result: null)
            : new ValueTask<string?>(token.ApplicationId);
    }

    /// <inheritdoc />
    public async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TToken>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        return await query(Session.Query<TToken>(), state).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetAuthorizationIdAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return string.IsNullOrWhiteSpace(token.AuthorizationId)
            ? new ValueTask<string?>(result: null)
            : new ValueTask<string?>(token.AuthorizationId);
    }

    /// <inheritdoc />
    public ValueTask<DateTimeOffset?> GetCreationDateAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return token.CreationDate is null
            ? new ValueTask<DateTimeOffset?>(result: null)
            : new ValueTask<DateTimeOffset?>(token.CreationDate.Value);
    }

    /// <inheritdoc />
    public ValueTask<DateTimeOffset?> GetExpirationDateAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token, nameof(token));
        return token.ExpirationDate is null
            ? new ValueTask<DateTimeOffset?>(result: null)
            : new ValueTask<DateTimeOffset?>(token.ExpirationDate.Value);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetIdAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new ValueTask<string?>(token.Id);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetPayloadAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new ValueTask<string?>(token.Payload);
    }

    /// <inheritdoc />
    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TToken token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token, nameof(token));

        if (token.Properties is null)
        {
            return new ValueTask<ImmutableDictionary<string, JsonElement>>(
                ImmutableDictionary.Create<string, JsonElement>());
        }

        using var document = JsonDocument.Parse(token.Properties);
        ImmutableDictionary<string, JsonElement>.Builder builder =
            ImmutableDictionary.CreateBuilder<string, JsonElement>();

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            builder[property.Name] = property.Value.Clone();
        }

        return new ValueTask<ImmutableDictionary<string, JsonElement>>(builder.ToImmutable());
    }

    /// <inheritdoc />
    public ValueTask<DateTimeOffset?> GetRedemptionDateAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        return token.RedemptionDate is null
            ? new ValueTask<DateTimeOffset?>(result: null)
            : new ValueTask<DateTimeOffset?>(token.RedemptionDate.Value);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetReferenceIdAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new ValueTask<string?>(token.ReferenceId);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetStatusAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new ValueTask<string?>(token.Status);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetSubjectAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new ValueTask<string?>(token.Subject);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetTypeAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return new ValueTask<string?>(token.Type);
    }

    /// <inheritdoc />
    public ValueTask<TToken> InstantiateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new ValueTask<TToken>(Activator.CreateInstance<TToken>());
        }
        catch (MemberAccessException exception)
        {
            return new ValueTask<TToken>(
                Task.FromException<TToken>(
                    new InvalidOperationException($"Could not create instance of {typeof(TToken)}", exception)
                ));
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TToken> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IRavenQueryable<TToken> query = Session.Query<TToken>();

        if (offset.HasValue)
        {
            Debug.Assert(offset.Value >= 0, "Offset value must be greater than or equal to 0");
            query = query.Skip(offset.Value);
        }

        if (count.HasValue)
        {
            Debug.Assert(count.Value >= 0, "Count value must be greater than or equal to 0");
            query = query.Take(count.Value);
        }

        List<TToken> tokens = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (TToken token in tokens)
        {
            yield return token;
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TToken>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        return Session.ToRavenDbStreamAsync(
            (IRavenQueryable<TResult>)query(Session.Query<TToken>(), state),
            cancellationToken
        );
    }

    /// <inheritdoc />
    public async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.Now;

        var deleteOperation = new DeleteByQueryOperation<OIDct_Tokens<TToken>.IndexEntry, TTokenIndex>(
            indexEntry => indexEntry.CreationDate < threshold &&
                          (
                              indexEntry.ExpirationDate < now ||
                              indexEntry.AuthorizationStatus != OpenIddictConstants.Statuses.Valid ||
                              (
                                  indexEntry.Status != OpenIddictConstants.Statuses.Inactive &&
                                  indexEntry.Status != OpenIddictConstants.Statuses.Valid
                              )
                          )
        );

        Operation? pruneResult = await Session.Advanced.DocumentStore
            .Operations
            .SendAsync(deleteOperation, null, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            BulkOperationResult result = await pruneResult.WaitForCompletionAsync<BulkOperationResult>(
                _operationWaitForResultTimeout
            );
            return result.Total;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Authorization pruning took longer than the configured timeout of {Timeout} seconds",
                _operationWaitForResultTimeout.TotalSeconds
            );
        }

        return 1;
    }

    /// <inheritdoc />
    public ValueTask<long> RevokeAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        CancellationToken cancellationToken)
    {
        const string collectionAlias = "token";

        var filters = new List<string>();
        var parameters = new Parameters();

        if (!string.IsNullOrEmpty(subject))
        {
            filters.Add($"{collectionAlias}.{nameof(OpenIdDictRavenDbToken.Subject)} = $subject");
            parameters.Add("subject", subject);
        }

        if (!string.IsNullOrEmpty(client))
        {
            filters.Add($"{collectionAlias}.{nameof(OpenIdDictRavenDbToken.ApplicationId)} = $client");
            parameters.Add("client", client);
        }

        if (!string.IsNullOrEmpty(status))
        {
            filters.Add($"{collectionAlias}.{nameof(OpenIdDictRavenDbToken.Status)} = $status");
            parameters.Add("status", status);
        }

        if (!string.IsNullOrEmpty(type))
        {
            filters.Add($"{collectionAlias}.{nameof(OpenIdDictRavenDbToken.Type)} = $type");
            parameters.Add("type", type);
        }

        return MarkAsRevokedAsync(collectionAlias, parameters, filters, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<long> RevokeByApplicationIdAsync(
        string identifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier, nameof(identifier));

        const string collectionAlias = "token";

        var filters = new List<string>
        {
            $"{collectionAlias}.{nameof(OpenIdDictRavenDbToken.ApplicationId)} = $applicationId",
        };
        var parameters = new Parameters
        {
            { "applicationId", identifier },
        };

        return MarkAsRevokedAsync(collectionAlias, parameters, filters, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier, nameof(identifier));

        const string collectionAlias = "token";

        var filters = new List<string>
        {
            $"{collectionAlias}.{nameof(OpenIdDictRavenDbToken.AuthorizationId)} = $authId",
        };
        var parameters = new Parameters
        {
            { "authId", identifier },
        };

        return MarkAsRevokedAsync(collectionAlias, parameters, filters, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<long> RevokeBySubjectAsync(
        string subject,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject, nameof(subject));

        const string collectionAlias = "token";

        var filters = new List<string>
        {
            $"{collectionAlias}.{nameof(OpenIdDictRavenDbToken.Subject)} = $subject",
        };
        var parameters = new Parameters
        {
            { "subject", subject },
        };

        return MarkAsRevokedAsync(collectionAlias, parameters, filters, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SetApplicationIdAsync(TToken token, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.ApplicationId = identifier;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetAuthorizationIdAsync(TToken token, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.AuthorizationId = identifier;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetCreationDateAsync(TToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.CreationDate = date;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetExpirationDateAsync(TToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.ExpirationDate = date;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetPayloadAsync(TToken token, string? payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.Payload = payload;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetPropertiesAsync(
        TToken token,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token, nameof(token));

        if (properties is not { Count: > 0 })
        {
            token.Properties = null;
            return default;
        }

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = false,
            }
        );

        writer.WriteStartObject();

        foreach (KeyValuePair<string, JsonElement> property in properties)
        {
            writer.WritePropertyName(property.Key);
            property.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();

        token.Properties = Encoding.UTF8.GetString(stream.ToArray());

        return default;
    }

    /// <inheritdoc />
    public ValueTask SetRedemptionDateAsync(TToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.RedemptionDate = date;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetReferenceIdAsync(TToken token, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.ReferenceId = identifier;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetStatusAsync(TToken token, string? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.Status = status;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetSubjectAsync(TToken token, string? subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.Subject = subject;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetTypeAsync(TToken token, string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        token.Type = type;
        return default;
    }

    /// <inheritdoc />
    public async ValueTask UpdateAsync(TToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token, nameof(token));

        if (!Session.Advanced.IsLoaded(token.Id))
        {
            throw new Exception("Token is expected to be already loaded in the RavenDB session.");
        }

        ThrowWhenClusterWideAsNotSupported();

        /*
         * NOTE: When ReferenceId is set it is part of the ID to endure document uniqueness.
         * We do not support changing the reference ID for an existing token.
         */

        if (Session.IfPropertyChanged(
                token,
                changedPropertyName: nameof(token.ReferenceId),
                newPropertyValue: token.ReferenceId,
                out PropertyChange<string?>? tokenReferencePropertyChange))
        {
            Debug.Assert(tokenReferencePropertyChange is not null, "tokenReferencePropertyChange must not be null");
            throw new Exception(
                $"OpenIdDict RavenDb Store does not support changing the reference ID for an existing token as that reference id is part of the document identifier. " +
                $"Token ID: {token.Id} Old reference: {tokenReferencePropertyChange.OldPropertyValue} " +
                $"New reference: {tokenReferencePropertyChange.NewPropertyValue}"
            );
        }

        string changeVector = Session.Advanced.GetChangeVectorFor(token);
        await Session
            .StoreAsync(token, changeVector, token.Id, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            Debug.Assert(
                IsClusterWideTransaction() is false,
                "Cluster-wide transaction is not supported for updating tokens as atomic guards are not removed after prune/(delete bu query) operation."
            );
            await Session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ConcurrencyException concurrencyException)
        {
            _logger.LogError(
                concurrencyException,
                "Failed updating token with id {TokenID} due to a concurrency conflict",
                token.Id
            );
            throw new Unique.Exceptions.ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed updating token with id {TokenId}  due to an error",
                token.Id
            );
            throw;
        }
    }

    private async ValueTask<long> MarkAsRevokedAsync(
        string collectionAlias,
        Parameters parameters,
        List<string> filters,
        CancellationToken cancellationToken)
    {
        string? tokensCollectionName = Session.Advanced
            .DocumentStore
            .Conventions
            .FindCollectionName(typeof(TToken));

        Debug.Assert(tokensCollectionName is not null, "tokensCollectionName must not be null");

        const string revokedStatusParameterName = "revokedStatusValue";
        Debug.Assert(
            !parameters.ContainsKey(revokedStatusParameterName),
            $"{revokedStatusParameterName} must not be set in parameters"
        );
        parameters.Add(revokedStatusParameterName, OpenIddictConstants.Statuses.Revoked);

        string whereClause = filters.Count > 0 ? $" where {string.Join(" and ", filters)}" : string.Empty;
        var patchOperation = new PatchByQueryOperation(
            new IndexQuery
            {
                Query = $" from {tokensCollectionName} as {collectionAlias} " +
                        $" {whereClause} " +
                        $" update {{ {collectionAlias}.{nameof(OpenIdDictRavenDbToken.Status)} = ${revokedStatusParameterName} ; }}",
                QueryParameters = parameters,
            }
        );

        Operation? operation = await Session.Advanced.DocumentStore
            .Operations
            .SendAsync(patchOperation, null, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            BulkOperationResult result = await operation.WaitForCompletionAsync<BulkOperationResult>(
                _operationWaitForResultTimeout
            );
            return result.Total;
        }
        catch (TimeoutException)
        {
            _logger.LogWarning(
                "Token revoke took longer than the configured timeout of {Timeout} seconds",
                _operationWaitForResultTimeout.TotalSeconds
            );
        }

        return 0;
    }
}

/// <inheritdoc />
internal class OpenIdDictRavenDbTokenStore
    : OpenIdDictRavenDbTokenStore<OpenIdDictRavenDbToken, OIDct_Tokens>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenIdDictRavenDbTokenStore"/> class.
    /// </summary>
    /// <param name="sessionProvider"></param>
    /// <param name="logger"></param>
    public OpenIdDictRavenDbTokenStore(
        OpenIdDictDocumentSessionProvider sessionProvider,
        ILogger<OpenIdDictRavenDbTokenStore> logger)
        : base(sessionProvider, logger)
    {
    }
}