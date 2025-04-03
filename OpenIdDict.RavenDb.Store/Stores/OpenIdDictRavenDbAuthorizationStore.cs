using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Index;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique.Exceptions;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Raven.Client;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Queries;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores;

/// <inheritdoc />
public class OpenIdDictRavenDbAuthorizationStore
    : OpenIdDictRavenDbAuthorizationStore<OpenIdDictRavenDbAuthorization>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenIdDictRavenDbAuthorizationStore"/> class.
    /// </summary>
    /// <param name="sessionProvider"></param>
    /// <param name="logger"></param>
    public OpenIdDictRavenDbAuthorizationStore(
        OpenIdDictDocumentSessionProvider sessionProvider,
        ILogger<OpenIdDictRavenDbAuthorizationStore> logger)
        : base(sessionProvider, logger)
    {
    }
}

/// <summary>
/// OpenIdDict RavenDb authorization store.
/// </summary>
/// <typeparam name="TAuthorization">Authorization document type.</typeparam>
public abstract class OpenIdDictRavenDbAuthorizationStore<TAuthorization>
    : OpenIdDictStoreBase, IOpenIddictAuthorizationStore<TAuthorization>
    where TAuthorization : OpenIdDictRavenDbAuthorization
{
    private readonly TimeSpan _operationWaitForResultTimeout = TimeSpan.FromSeconds(30);

    private readonly ILogger<OpenIdDictRavenDbAuthorizationStore<TAuthorization>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenIdDictRavenDbAuthorizationStore{TAuthorization}"/> class.
    /// </summary>
    /// <param name="sessionProvider"></param>
    /// <param name="logger"></param>
    protected OpenIdDictRavenDbAuthorizationStore(
        OpenIdDictDocumentSessionProvider sessionProvider,
        ILogger<OpenIdDictRavenDbAuthorizationStore<TAuthorization>> logger)
        : base(sessionProvider.Invoke())
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        return await Session.Query<TAuthorization>().CountAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TAuthorization>, IQueryable<TResult>> query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query, nameof(query));
        return await query(Session.Query<TAuthorization>()).CountAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask CreateAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization, nameof(authorization));
        ArgumentException.ThrowIfNullOrWhiteSpace(authorization.ApplicationId, nameof(authorization.ApplicationId));

        ThrowWhenClusterWideAsNotSupported();

        try
        {
            await Session.StoreAsync(
                entity: authorization,
                changeVector: string.Empty,
                id: authorization.Id,
                token: cancellationToken
            ).ConfigureAwait(false);
        }
        catch (Raven.Client.Exceptions.Documents.Session.NonUniqueObjectException nonUniqueException)
        {
            _logger.LogInformation(
                nonUniqueException,
                "Failed creating authorization for application {ApplicationId} due to a non-unique identifier",
                authorization.ApplicationId
            );
            throw new DuplicateException("An authorization with the specified identifier already exists.");
        }

        try
        {
            Debug.Assert(
                IsClusterWideTransaction() is false,
                "Cluster wide transaction is not supported when creating an authorization because atomic guards are not removed after prune/(delete by query) operation."
            );

            await Session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Raven.Client.Exceptions.ConcurrencyException concurrencyException)
        {
            _logger.LogError(
                concurrencyException,
                "Failed creating authorization for application {ApplicationId} due to a concurrency conflict",
                authorization.ApplicationId
            );
            throw new ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed creating authorization for application {ApplicationId} due to an error",
                authorization.ApplicationId
            );
            throw;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DeleteAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization, nameof(authorization));

        try
        {
            // When deleting it doesn't matter if cluster-wide. See CreateAsync for more info.
            if (IsClusterWideTransaction())
            {
                Session.Delete(authorization.Id);
            }
            else
            {
                string changeVector = Session.Advanced.GetChangeVectorFor(authorization);
                Session.Delete(authorization.Id, changeVector);
            }

            await Session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Raven.Client.Exceptions.ConcurrencyException concurrencyException)
        {
            _logger.LogError(
                concurrencyException,
                "Failed deleting authorization for application {ApplicationId} due to a concurrency conflict",
                authorization.ApplicationId
            );
            throw new ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed deleting authorization for application {ApplicationId} due to an error",
                authorization.ApplicationId
            );
            throw;
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TAuthorization> FindAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        ImmutableArray<string>? scopes,
        CancellationToken cancellationToken)
    {
        IRavenQueryable<TAuthorization>? query = Session.Query<TAuthorization>();

        if (!string.IsNullOrEmpty(subject))
        {
            query = query.Where(authorization => authorization.Subject == subject);
        }

        if (!string.IsNullOrEmpty(client))
        {
            query = query.Where(authorization => authorization.ApplicationId == client);
        }

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(authorization => authorization.Status == status);
        }

        if (!string.IsNullOrEmpty(type))
        {
            query = query.Where(authorization => authorization.Type == type);
        }

        if (scopes is not null)
        {
            // Note: Enumerable.All() is deliberately used without the extension method syntax to ensure
            // ImmutableArrayExtensions.All() (which is not supported by MongoDB) is not used instead.
            query = query.Where(
                authorization => authorization.Scopes != null && authorization.Scopes.ContainsAll(scopes));
        }

        return Session.ToRavenDbStreamAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TAuthorization> FindByApplicationIdAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        IRavenQueryable<TAuthorization> query = Session
            .Query<TAuthorization>()
            .Where(authorization => authorization.ApplicationId == identifier);

        return Session.ToRavenDbStreamAsync(query, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<TAuthorization?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier, nameof(identifier));
        return await Session.LoadAsync<TAuthorization>(identifier, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TAuthorization> FindBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject, nameof(subject));

        IRavenQueryable<TAuthorization> query = Session
            .Query<TAuthorization>()
            .Where(authorization => authorization.Subject == subject);

        return Session.ToRavenDbStreamAsync(query, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetApplicationIdAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return new ValueTask<string?>(authorization.ApplicationId);
    }

    /// <inheritdoc/>
    public async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query, nameof(query));
        return await query(Session.Query<TAuthorization>(), state).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<DateTimeOffset?> GetCreationDateAsync(
        TAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization, nameof(authorization));
        return new ValueTask<DateTimeOffset?>(authorization.CreationDate);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetIdAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization, nameof(authorization));
        return new ValueTask<string?>(authorization.Id);
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        if (authorization.Properties is null)
        {
            return new ValueTask<ImmutableDictionary<string, JsonElement>>(
                ImmutableDictionary.Create<string, JsonElement>());
        }

        using var document = JsonDocument.Parse(authorization.Properties);
        ImmutableDictionary<string, JsonElement>.Builder builder =
            ImmutableDictionary.CreateBuilder<string, JsonElement>();

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            builder[property.Name] = property.Value.Clone();
        }

        return new ValueTask<ImmutableDictionary<string, JsonElement>>(builder.ToImmutable());
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableArray<string>> GetScopesAsync(
        TAuthorization authorization,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        return authorization.Scopes is not { Count: > 0 }
            ? new ValueTask<ImmutableArray<string>>([])
            : new ValueTask<ImmutableArray<string>>([.. authorization.Scopes]);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetStatusAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return new ValueTask<string?>(authorization.Status);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetSubjectAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return new ValueTask<string?>(authorization.Subject);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetTypeAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return new ValueTask<string?>(authorization.Type);
    }

    /// <inheritdoc />
    public ValueTask<TAuthorization> InstantiateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new ValueTask<TAuthorization>(Activator.CreateInstance<TAuthorization>());
        }
        catch (MemberAccessException exception)
        {
            return new ValueTask<TAuthorization>(
                Task.FromException<TAuthorization>(
                    new InvalidOperationException(
                        $"Could not create instance of {typeof(TAuthorization)}",
                        exception
                    )
                ));
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TAuthorization> ListAsync(int? count, int? offset, CancellationToken cancellationToken)
    {
        IRavenQueryable<TAuthorization>? query = Session.Query<TAuthorization>();

        if (offset.HasValue)
        {
            query = query.Skip(offset.Value);
        }

        if (count.HasValue)
        {
            query = query.Take(count.Value);
        }

        return Session.ToRavenDbStreamAsync(query, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TAuthorization>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        return Session.ToRavenDbStreamAsync(query(Session.Query<TAuthorization>(), state), cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        const string authIndexOutputCollectionName =
            OpenIdDictRavenDbConventions.AuthorizationsIndexDocumentsCollectionName;

        Debug.Assert(
            !string.IsNullOrWhiteSpace(authIndexOutputCollectionName),
            "IndexOutputCollectionName must not be null or whitespace."
        );

        const string collectionAlias = "outputDoc";

        var query = new IndexQuery
        {
            Query = $" from {authIndexOutputCollectionName} as {collectionAlias} where " +
                    $" {collectionAlias}.{nameof(OIDct_Authorizations.IndexEntry.CreationDate)} < $threshold and " +
                    $" {collectionAlias}.{nameof(OIDct_Authorizations.IndexEntry.HasTokens)} = false and " +
                    $" ( " +
                    $"   {collectionAlias}.{nameof(OIDct_Authorizations.IndexEntry.Status)} != $statusValid or " +
                    $"   {collectionAlias}.{nameof(OIDct_Authorizations.IndexEntry.Type)} == $typeAdHoc " +
                    $" ) " +
                    $" update {{ del({collectionAlias}.{nameof(OIDct_Authorizations.IndexEntry.AuthorizationId)}) ; }}",
            QueryParameters = new Parameters
            {
                { "threshold", threshold },
                { "statusValid", OpenIddictConstants.Statuses.Valid },
                { "typeAdHoc", OpenIddictConstants.AuthorizationTypes.AdHoc },
            },
        };
        var pruneAuthByPath = new PatchByQueryOperation(query);

        Operation? pruneResult = await Session.Advanced.DocumentStore
            .Operations
            .SendAsync(pruneAuthByPath, null, cancellationToken)
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

        return 0;
    }

    /// <inheritdoc/>
    public ValueTask<long> RevokeAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        CancellationToken cancellationToken)
    {
        const string collectionAlias = "auth";

        var filters = new List<string>();
        var parameters = new Parameters();

        if (!string.IsNullOrEmpty(subject))
        {
            filters.Add($"{collectionAlias}.{nameof(OpenIdDictRavenDbAuthorization.Subject)} = $subject");
            parameters.Add("subject", subject);
        }

        if (!string.IsNullOrEmpty(client))
        {
            filters.Add($"{collectionAlias}.{nameof(OpenIdDictRavenDbAuthorization.ApplicationId)} = $client");
            parameters.Add("client", client);
        }

        if (!string.IsNullOrEmpty(status))
        {
            filters.Add($"{collectionAlias}.{nameof(OpenIdDictRavenDbAuthorization.Status)} = $status");
            parameters.Add("status", status);
        }

        if (!string.IsNullOrEmpty(type))
        {
            filters.Add($"{collectionAlias}.{nameof(OpenIdDictRavenDbAuthorization.Type)} = $type");
            parameters.Add("type", type);
        }

        return MarkAsRevokedAsync(collectionAlias, parameters, filters, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier, nameof(identifier));

        const string collectionAlias = "auth";

        var filters = new List<string>
        {
            $"{collectionAlias}.{nameof(OpenIdDictRavenDbAuthorization.ApplicationId)} = $applicationId",
        };
        var parameters = new Parameters
        {
            { "applicationId", identifier },
        };

        return MarkAsRevokedAsync(collectionAlias, parameters, filters, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject, nameof(subject));

        const string collectionAlias = "auth";

        var filters = new List<string>
        {
            $"{collectionAlias}.{nameof(OpenIdDictRavenDbAuthorization.Subject)} = $subject",
        };
        var parameters = new Parameters
        {
            { "subject", subject },
        };

        return MarkAsRevokedAsync(collectionAlias, parameters, filters, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask SetApplicationIdAsync(
        TAuthorization authorization,
        string? identifier,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.ApplicationId = identifier;
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetCreationDateAsync(
        TAuthorization authorization,
        DateTimeOffset? date,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.CreationDate = date;
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetPropertiesAsync(
        TAuthorization authorization,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization, nameof(authorization));

        if (properties is not { Count: > 0 })
        {
            authorization.Properties = null;
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

        authorization.Properties = Encoding.UTF8.GetString(stream.ToArray());

        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetScopesAsync(
        TAuthorization authorization,
        ImmutableArray<string> scopes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        if (scopes.IsDefaultOrEmpty)
        {
            authorization.Scopes = null;
            return default;
        }

        authorization.Scopes = scopes.ToList();
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetStatusAsync(TAuthorization authorization, string? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.Status = status;
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetSubjectAsync(TAuthorization authorization, string? subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.Subject = subject;
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetTypeAsync(TAuthorization authorization, string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        authorization.Type = type;
        return default;
    }

    /// <inheritdoc/>
    public async ValueTask UpdateAsync(TAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization, nameof(authorization));

        if (!Session.Advanced.IsLoaded(authorization.Id))
        {
            throw new Exception("Authorization document is expected to be already loaded in the RavenDB session.");
        }

        ThrowWhenClusterWideAsNotSupported();

        string changeVector = Session.Advanced.GetChangeVectorFor(authorization);
        await Session
            .StoreAsync(authorization, changeVector, authorization.Id, cancellationToken)
            .ConfigureAwait(false);

        try
        {
            Debug.Assert(
                IsClusterWideTransaction() is false,
                "Cluster wide transaction is not supported when updating an authorization because atomic guards are not removed after prune/(delete by query) operation."
            );
            await Session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Raven.Client.Exceptions.ConcurrencyException concurrencyException)
        {
            _logger.LogError(
                concurrencyException,
                "Failed updating authorization with Id {Id} due to a concurrency conflict",
                authorization.Id
            );
            throw new ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed updating authorization with Id {Id}  due to an error",
                authorization.Id
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
        string? authorizationsCollectionName = Session.Advanced
            .DocumentStore
            .Conventions
            .FindCollectionName(typeof(TAuthorization));

        Debug.Assert(authorizationsCollectionName is not null, "authorizationsCollectionName must not be null");

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
                Query = $" from {authorizationsCollectionName} as {collectionAlias} " +
                        $" {whereClause} " +
                        $" update {{ {collectionAlias}.{nameof(OpenIdDictRavenDbAuthorization.Status)} = ${revokedStatusParameterName} ; }}",
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
                "Authorization revoke took longer than the configured timeout of {Timeout} seconds",
                _operationWaitForResultTimeout.TotalSeconds
            );
        }

        return 0;
    }
}