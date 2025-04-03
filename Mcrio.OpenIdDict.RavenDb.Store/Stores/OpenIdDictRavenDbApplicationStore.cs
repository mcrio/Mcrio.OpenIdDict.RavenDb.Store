using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;
using MemberAccessException = System.MemberAccessException;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores;

/// <inheritdoc />
public class OpenIdDictRavenDbApplicationStore
    : OpenIdDictRavenDbApplicationStore<OpenIdDictRavenDbApplication, UniqueReservation>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenIdDictRavenDbApplicationStore"/> class.
    /// </summary>
    /// <param name="sessionProvider"></param>
    /// <param name="logger"></param>
    public OpenIdDictRavenDbApplicationStore(
        OpenIdDictDocumentSessionProvider sessionProvider,
        ILogger<OpenIdDictRavenDbApplicationStore> logger)
        : base(sessionProvider, logger)
    {
    }

    /// <inheritdoc />
    protected override UniqueReservationDocumentUtility<UniqueReservation> CreateUniqueReservationDocumentsUtility(
        UniqueReservationType reservationType,
        string uniqueValue)
    {
        return new UniqueReservationDocumentUtility(
            Session,
            reservationType,
            uniqueValue);
    }
}

/// <summary>
/// RavenDB OpenIdDict application store implementation.
/// </summary>
/// <typeparam name="TApplication">RavenDb Application type.</typeparam>
/// <typeparam name="TUniqueReservation">Unique reservation document type.</typeparam>
public abstract class OpenIdDictRavenDbApplicationStore<TApplication, TUniqueReservation>
    : OpenIdDictStoreBase, IOpenIddictApplicationStore<TApplication>
    where TApplication : OpenIdDictRavenDbApplication
    where TUniqueReservation : UniqueReservation
{
    private readonly ILogger<OpenIdDictRavenDbApplicationStore<TApplication, TUniqueReservation>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenIdDictRavenDbApplicationStore{TApplication,TUniqueReservation}"/> class.
    /// </summary>
    /// <param name="sessionProvider"></param>
    /// <param name="logger"></param>
    protected OpenIdDictRavenDbApplicationStore(
        OpenIdDictDocumentSessionProvider sessionProvider,
        ILogger<OpenIdDictRavenDbApplicationStore<TApplication, TUniqueReservation>> logger)
        : base(sessionProvider.Invoke())
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        return await Session.Query<TApplication>().CountAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TApplication>, IQueryable<TResult>> query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await query(Session.Query<TApplication>()).CountAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Create new application.
    /// </summary>
    /// <param name="application"></param>
    /// <param name="cancellationToken"></param>
    /// <exception cref="DuplicateException">When ClientId already exists.</exception>
    /// <exception cref="ConcurrencyException">When document was already updated.</exception>
    /// <returns>Instance of <see cref="ValueTask"/>.</returns>
    public async ValueTask CreateAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application, nameof(application));
        ArgumentException.ThrowIfNullOrWhiteSpace(application.ClientId, nameof(application.ClientId));

        // cluster wide as we will deal with compare exchange values either directly or as atomic guards
        // for unique value reservations
        if (IsClusterWideTransaction() is false)
        {
            _logger.LogDebug("Setting cluster wide transaction mode for application creation");
        }

        Session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);
        Session.Advanced.UseOptimisticConcurrency = false; // cluster wide tx doesn't support opt. concurrency

        try
        {
            // no change vector specified as we rely on cluster wide optimistic concurrency and atomic guards
            await Session.StoreAsync(application, cancellationToken).ConfigureAwait(false);
        }
        catch (Raven.Client.Exceptions.Documents.Session.NonUniqueObjectException nonUniqueException)
        {
            _logger.LogInformation(
                nonUniqueException,
                "Failed creating application with client identifier {ClientId} due to a non-unique identifier",
                application.ClientId);
            throw new DuplicateException($"The client identifier '{application.ClientId}' is already registered.");
        }

        Debug.Assert(application.Id is not null, "Application id must not be null");

        // Unique reservation for client id
        UniqueReservationDocumentUtility<TUniqueReservation> uniqueReservationUtil =
            CreateUniqueReservationDocumentsUtility(
                reservationType: UniqueReservationType.ApplicationClientId,
                uniqueValue: application.ClientId
            );
        bool uniqueExists = await uniqueReservationUtil.CheckIfUniqueIsTakenAsync().ConfigureAwait(false);
        if (uniqueExists)
        {
            throw new DuplicateException($"The client identifier '{application.ClientId}' is already registered.");
        }

        await uniqueReservationUtil
            .CreateReservationDocumentAddToUnitOfWorkAsync(
                ownerDocumentId: application.Id
            ).ConfigureAwait(false);

        try
        {
            Debug.Assert(
                IsClusterWideTransaction(),
                "Cluster wide transaction required in order to create related atomic guards"
            );
            await Session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Raven.Client.Exceptions.ConcurrencyException concurrencyException)
        {
            _logger.LogError(
                concurrencyException,
                "Failed creating application with client identifier {ClientId} due to a concurrency conflict",
                application.ClientId);
            throw new ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed creating application with client identifier {ClientId} due to an error",
                application.ClientId);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application, nameof(application));

        // cluster wide as we will deal with compare exchange values either directly or as atomic guards
        if (IsClusterWideTransaction() is false)
        {
            _logger.LogDebug("Setting cluster wide transaction mode for application deletion");
        }

        Session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);

        // cluster wide tx doesn't support opt. concurrency
        Session.Advanced.UseOptimisticConcurrency = false;

        if (string.IsNullOrEmpty(application.ClientId))
        {
            _logger.LogWarning(
                "Unexpected empty application clientId when deleting application {ApplicationId}. Deleting unique value reservation document was not handled",
                application.Id);
        }
        else
        {
            UniqueReservationDocumentUtility<TUniqueReservation> usernameReservationUtil =
                CreateUniqueReservationDocumentsUtility(
                    UniqueReservationType.ApplicationClientId,
                    application.ClientId);
            await usernameReservationUtil.MarkReservationForDeletionAsync().ConfigureAwait(false);
        }

        try
        {
            Session.Delete(application.Id);

            Debug.Assert(
                IsClusterWideTransaction(),
                "Cluster wide transaction required in order to remove related atomic guards"
            );
            await Session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Raven.Client.Exceptions.ConcurrencyException concurrencyException)
        {
            _logger.LogError(
                concurrencyException,
                "Failed deleting application with client identifier {ClientId} due to a concurrency conflict",
                application.ClientId);
            throw new ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed deleting application with client identifier {ClientId} due to an error",
                application.ClientId);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<TApplication?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier, nameof(identifier));

        return await Session.LoadAsync<TApplication>(identifier, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<TApplication?> FindByClientIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier, nameof(identifier));

        return await Session
            .Query<TApplication>()
            .FirstOrDefaultAsync(item => item.ClientId == identifier, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TApplication> FindByPostLogoutRedirectUriAsync(
        string uri,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri, nameof(uri));

        IQueryable<TApplication> query = Session
            .Query<TApplication>()
            .Where(item => item.PostLogoutRedirectUris != null && item.PostLogoutRedirectUris.Contains(uri));

        return Session.ToRavenDbStreamAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TApplication> FindByRedirectUriAsync(string uri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uri, nameof(uri));

        IQueryable<TApplication> query = Queryable.Where(
            Session
                .Query<TApplication>(),
            item => item.RedirectUris != null && item.RedirectUris.Contains(uri));

        return Session.ToRavenDbStreamAsync(query, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetApplicationTypeAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return new ValueTask<string?>(application.ApplicationType);
    }

    /// <inheritdoc/>
    public async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await query(Session.Query<TApplication>(), state)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetClientIdAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return new ValueTask<string?>(application.ClientId);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetClientSecretAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return new ValueTask<string?>(application.ClientSecret);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetClientTypeAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return new ValueTask<string?>(application.ClientType);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetConsentTypeAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return new ValueTask<string?>(application.ConsentType);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetDisplayNameAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return new ValueTask<string?>(application.DisplayName);
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
        TApplication application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application.DisplayNames is not { Count: > 0 })
        {
            return new ValueTask<ImmutableDictionary<CultureInfo, string>>(
                ImmutableDictionary.Create<CultureInfo, string>());
        }

        return new ValueTask<ImmutableDictionary<CultureInfo, string>>(
            application.DisplayNames.ToImmutableDictionary(
                pair => CultureInfo.GetCultureInfo(pair.Key),
                pair => pair.Value));
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetIdAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return new ValueTask<string?>(application.Id);
    }

    /// <inheritdoc/>
    public ValueTask<JsonWebKeySet?> GetJsonWebKeySetAsync(
        TApplication application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return string.IsNullOrWhiteSpace(application.JsonWebKeySet)
            ? new ValueTask<JsonWebKeySet?>(result: null)
            : new ValueTask<JsonWebKeySet?>(JsonWebKeySet.Create(application.JsonWebKeySet));
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableArray<string>> GetPermissionsAsync(
        TApplication application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application.Permissions is null || application.Permissions.Count <= 0)
        {
            return new ValueTask<ImmutableArray<string>>([]);
        }

        return new ValueTask<ImmutableArray<string>>([.. application.Permissions]);
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(
        TApplication application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application.PostLogoutRedirectUris is null || application.PostLogoutRedirectUris.Count <= 0)
        {
            return new ValueTask<ImmutableArray<string>>([]);
        }

        return new ValueTask<ImmutableArray<string>>([.. application.PostLogoutRedirectUris]);
    }

    /// <inheritdoc />
    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TApplication application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (application.Properties is null)
        {
            return new ValueTask<ImmutableDictionary<string, JsonElement>>(
                ImmutableDictionary.Create<string, JsonElement>());
        }

        using var document = JsonDocument.Parse(application.Properties);
        ImmutableDictionary<string, JsonElement>.Builder builder =
            ImmutableDictionary.CreateBuilder<string, JsonElement>();

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            builder[property.Name] = property.Value.Clone();
        }

        return new ValueTask<ImmutableDictionary<string, JsonElement>>(builder.ToImmutable());
    }

    /// <inheritdoc />
    public ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(
        TApplication application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.RedirectUris is not { Count: > 0 }
            ? new ValueTask<ImmutableArray<string>>([])
            : new ValueTask<ImmutableArray<string>>([.. application.RedirectUris]);
    }

    /// <inheritdoc />
    public ValueTask<ImmutableArray<string>> GetRequirementsAsync(
        TApplication application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.Requirements is not { Count: > 0 }
            ? new ValueTask<ImmutableArray<string>>([])
            : new ValueTask<ImmutableArray<string>>([.. application.Requirements]);
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(
        TApplication application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        return application.Settings is not { Count: > 0 }
            ? new ValueTask<ImmutableDictionary<string, string>>(ImmutableDictionary.Create<string, string>())
            : new ValueTask<ImmutableDictionary<string, string>>(application.Settings.ToImmutableDictionary());
    }

    /// <inheritdoc/>
    public ValueTask<TApplication> InstantiateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new ValueTask<TApplication>(Activator.CreateInstance<TApplication>());
        }
        catch (MemberAccessException exception)
        {
            return new ValueTask<TApplication>(
                Task.FromException<TApplication>(
                    new InvalidOperationException($"Could not create instance of {typeof(TApplication)}", exception)
                )
            );
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TApplication> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IRavenQueryable<TApplication> query = Session.Query<TApplication>();

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

        List<TApplication> applications = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (TApplication application in applications)
        {
            yield return application;
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TApplication>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query, nameof(query));
        return Session.ToRavenDbStreamAsync(query(Session.Query<TApplication>(), state), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask SetApplicationTypeAsync(
        TApplication application,
        string? type,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.ApplicationType = type;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetClientIdAsync(TApplication application, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.ClientId = identifier;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetClientSecretAsync(TApplication application, string? secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.ClientSecret = secret;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetClientTypeAsync(TApplication application, string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.ClientType = type;
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetConsentTypeAsync(TApplication application, string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.ConsentType = type;
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetDisplayNameAsync(TApplication application, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.DisplayName = name;
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetDisplayNamesAsync(
        TApplication application,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (names is not { Count: > 0 })
        {
            application.DisplayNames = null;
            return default;
        }

        application.DisplayNames = names.ToDictionary(
            pair => pair.Key.Name,
            pair => pair.Value);

        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetJsonWebKeySetAsync(
        TApplication application,
        JsonWebKeySet? set,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        application.JsonWebKeySet = set is not null
            ? JsonSerializer.Serialize(set, OpenIddictSerializer.Default.JsonWebKeySet)
            : null;

        return default;
    }

    /// <inheritdoc />
    public ValueTask SetPermissionsAsync(
        TApplication application,
        ImmutableArray<string> permissions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (permissions.IsDefaultOrEmpty)
        {
            application.Permissions = null;
            return default;
        }

        application.Permissions = permissions.ToList();

        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetPostLogoutRedirectUrisAsync(
        TApplication application,
        ImmutableArray<string> uris,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (uris.IsDefaultOrEmpty)
        {
            application.PostLogoutRedirectUris = null;
            return default;
        }

        application.PostLogoutRedirectUris = uris.ToList();
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetPropertiesAsync(
        TApplication application,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (properties is not { Count: > 0 })
        {
            application.Properties = null;
            return default;
        }

        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(
            stream,
            new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                Indented = false,
            });

        writer.WriteStartObject();

        foreach (KeyValuePair<string, JsonElement> property in properties)
        {
            writer.WritePropertyName(property.Key);
            property.Value.WriteTo(writer);
        }

        writer.WriteEndObject();
        writer.Flush();

        application.Properties = Encoding.UTF8.GetString(stream.ToArray());

        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetRedirectUrisAsync(
        TApplication application,
        ImmutableArray<string> uris,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (uris.IsDefaultOrEmpty)
        {
            application.RedirectUris = null;
            return default;
        }

        application.RedirectUris = uris.ToList();
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetRequirementsAsync(
        TApplication application,
        ImmutableArray<string> requirements,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (requirements.IsDefaultOrEmpty)
        {
            application.Requirements = null;
            return default;
        }

        application.Requirements = requirements.ToList();
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetSettingsAsync(
        TApplication application,
        ImmutableDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        if (settings is not { Count: > 0 })
        {
            application.Settings = null;
            return default;
        }

        application.Settings = settings.ToDictionary();
        return default;
    }

    /// <inheritdoc/>
    public async ValueTask UpdateAsync(TApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application, nameof(application));
        ArgumentException.ThrowIfNullOrWhiteSpace(application.ClientId, nameof(application.ClientId));

        if (!Session.Advanced.IsLoaded(application.Id))
        {
            throw new Exception("Application is expected to be already loaded in the RavenDB session.");
        }

        // if application client id has changed make sure it's unique by reserving it
        if (Session.IfPropertyChanged(
                application,
                changedPropertyName: nameof(application.ClientId),
                newPropertyValue: application.ClientId,
                out PropertyChange<string?>? clientIdPropertyChange))
        {
            Debug.Assert(
                clientIdPropertyChange != null,
                $"Unexpected NULL value for {nameof(clientIdPropertyChange)}");

            Debug.Assert(
                !string.IsNullOrWhiteSpace(clientIdPropertyChange.OldPropertyValue),
                "Application client id old value must never be empty or NULL.");

            Debug.Assert(
                !string.IsNullOrWhiteSpace(application.ClientId),
                "Application client id must never be empty or NULL.");

            Debug.Assert(application.Id is not null, "Application identifier must not be NULL.");

            // cluster wide as we will deal with compare exchange values either directly or as atomic guards
            if (IsClusterWideTransaction() is false)
            {
                _logger.LogDebug("Setting cluster wide transaction mode for application update");
            }

            Session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);
            Session.Advanced.UseOptimisticConcurrency = false; // cluster wide tx doesn't support opt. concurrency

            UniqueReservationDocumentUtility<TUniqueReservation> uniqueReservationUtil =
                CreateUniqueReservationDocumentsUtility(
                    UniqueReservationType.ApplicationClientId,
                    application.ClientId);
            bool uniqueExists = await uniqueReservationUtil.CheckIfUniqueIsTakenAsync().ConfigureAwait(false);
            if (uniqueExists)
            {
                throw new DuplicateException($"Application Client ID {application.ClientId} is not unique.");
            }

            await uniqueReservationUtil.UpdateReservationAndAddToUnitOfWork(
                clientIdPropertyChange.OldPropertyValue,
                application.Id).ConfigureAwait(false);

            Debug.Assert(
                IsClusterWideTransaction(),
                "Cluster wide transaction required in order to update related atomic guards"
            );
        }

        // in cluster wide mode relying on atomic guards for optimistic concurrency
        if (IsClusterWideTransaction() is false)
        {
            string changeVector = Session.Advanced.GetChangeVectorFor(application);
            await Session
                .StoreAsync(application, changeVector, application.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await Session.StoreAsync(application, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await Session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Raven.Client.Exceptions.ConcurrencyException concurrencyException)
        {
            _logger.LogError(
                concurrencyException,
                "Failed updating application with client identifier {ClientId} due to a concurrency conflict",
                application.ClientId);
            throw new ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed updating application with client identifier {ClientId} due to an error",
                application.ClientId);
            throw;
        }
    }

    /// <summary>
    /// Create an instance of <see cref="UniqueReservationDocumentUtility"/>.
    /// </summary>
    /// <param name="reservationType"></param>
    /// <param name="uniqueValue"></param>
    /// <returns>Instance of <see cref="UniqueReservationDocumentUtility"/>.</returns>
    protected abstract UniqueReservationDocumentUtility<TUniqueReservation> CreateUniqueReservationDocumentsUtility(
        UniqueReservationType reservationType,
        string uniqueValue);
}