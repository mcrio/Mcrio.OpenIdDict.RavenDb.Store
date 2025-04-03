// <copyright file="OpenIdDictRavenDbScopeStore.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

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
using OpenIddict.Abstractions;
using Raven.Client.Documents;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Session;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores;

/// <inheritdoc />
public class OpenIdDictRavenDbScopeStore : OpenIdDictRavenDbScopeStore<OpenIdDictRavenDbScope, UniqueReservation>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenIdDictRavenDbScopeStore"/> class.
    /// </summary>
    /// <param name="sessionProvider"></param>
    /// <param name="logger"></param>
    public OpenIdDictRavenDbScopeStore(
        OpenIdDictDocumentSessionProvider sessionProvider,
        ILogger<OpenIdDictRavenDbScopeStore> logger)
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
/// OpenIdDict RavenDB scope store.
/// </summary>
/// <typeparam name="TScope">Scope type.</typeparam>
/// <typeparam name="TUniqueReservation">Unique reservation document type.</typeparam>
public abstract class OpenIdDictRavenDbScopeStore<TScope, TUniqueReservation>
    : OpenIdDictStoreBase, IOpenIddictScopeStore<TScope>
    where TScope : OpenIdDictRavenDbScope
    where TUniqueReservation : UniqueReservation
{
    private readonly ILogger<OpenIdDictRavenDbScopeStore<TScope, TUniqueReservation>> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenIdDictRavenDbScopeStore{TScope, TUniqueReservation}"/> class.
    /// </summary>
    /// <param name="sessionProvider"></param>
    /// <param name="logger"></param>
    protected OpenIdDictRavenDbScopeStore(
        OpenIdDictDocumentSessionProvider sessionProvider,
        ILogger<OpenIdDictRavenDbScopeStore<TScope, TUniqueReservation>> logger)
        : base(sessionProvider.Invoke())
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        return await Session.Query<TScope>().CountAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<TScope>, IQueryable<TResult>> query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await query(Session.Query<TScope>()).CountAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask CreateAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope, nameof(scope));
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Name, nameof(scope.Name));

        // cluster wide as we will deal with compare exchange values either directly or as atomic guards
        // for unique value reservations
        if (IsClusterWideTransaction() is false)
        {
            _logger.LogDebug("Setting cluster wide transaction mode for scope creation");
        }

        Session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);
        Session.Advanced.UseOptimisticConcurrency = false; // cluster wide tx doesn't support opt. concurrency

        try
        {
            // no change vector specified as we rely on cluster wide optimistic concurrency and atomic guards
            Debug.Assert(
                IsClusterWideTransaction(),
                "Cluster wide transaction required in order to create related atomic guards"
            );
            await Session.StoreAsync(scope, cancellationToken).ConfigureAwait(false);
        }
        catch (Raven.Client.Exceptions.Documents.Session.NonUniqueObjectException nonUniqueException)
        {
            _logger.LogInformation(
                nonUniqueException,
                "Failed creating scope with name {ScopeName} due to a non-unique value",
                scope.Name
            );
            throw new DuplicateException($"The scope name '{scope.Name}' is already registered.");
        }

        Debug.Assert(scope.Id is not null, "Scope id must not be null");

        // Unique reservation for scope name
        UniqueReservationDocumentUtility<TUniqueReservation> uniqueReservationUtil =
            CreateUniqueReservationDocumentsUtility(
                UniqueReservationType.ScopeName,
                scope.Name);
        bool uniqueExists = await uniqueReservationUtil.CheckIfUniqueIsTakenAsync().ConfigureAwait(false);
        if (uniqueExists)
        {
            throw new DuplicateException($"The scope name '{scope.Name}' is already registered.");
        }

        await uniqueReservationUtil
            .CreateReservationDocumentAddToUnitOfWorkAsync(scope.Id)
            .ConfigureAwait(false);

        try
        {
            await Session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Raven.Client.Exceptions.ConcurrencyException concurrencyException)
        {
            _logger.LogError(
                concurrencyException,
                "Failed creating scope with name {ScopeName} due to a concurrency conflict",
                scope.Name);
            throw new ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed creating application with scope name {ScopeName} due to an error",
                scope.Name);
            throw;
        }
    }

    /// <inheritdoc/>
    public async ValueTask DeleteAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope, nameof(scope));

        // cluster wide as we will deal with compare exchange values either directly or as atomic guards
        if (IsClusterWideTransaction() is false)
        {
            _logger.LogDebug("Setting cluster wide transaction mode for scope deletion");
        }

        Session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);

        // cluster wide tx doesn't support opt. concurrency
        Session.Advanced.UseOptimisticConcurrency = false;

        if (string.IsNullOrEmpty(scope.Name))
        {
            _logger.LogWarning(
                "Unexpected empty scope name when deleting scope {ScopeID}. Deleting unique value reservation document was not handled",
                scope.Id);
        }
        else
        {
            UniqueReservationDocumentUtility<TUniqueReservation> usernameReservationUtil =
                CreateUniqueReservationDocumentsUtility(
                    UniqueReservationType.ScopeName,
                    scope.Name);
            await usernameReservationUtil.MarkReservationForDeletionAsync().ConfigureAwait(false);
        }

        try
        {
            Session.Delete(scope.Id);

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
                "Failed deleting scope with name {ScopeName} due to a concurrency conflict",
                scope.Name);
            throw new ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed deleting scope with name {ScopeName} due to an error",
                scope.Name);
            throw;
        }
    }

    /// <inheritdoc />
    public async ValueTask<TScope?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        return await Session.LoadAsync<TScope>(identifier, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<TScope?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        return await Session
            .Query<TScope>()
            .FirstOrDefaultAsync(x => x.Name == name, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TScope> FindByNamesAsync(ImmutableArray<string> names, CancellationToken cancellationToken)
    {
        if (names.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("Names item cannot be empty string", nameof(names));
        }

        return ExecuteAsync(cancellationToken);

        // ReSharper disable once VariableHidesOuterVariable
        IAsyncEnumerable<TScope> ExecuteAsync(CancellationToken cancellationToken)
        {
            IQueryable<TScope> query = Queryable.Where(
                Session
                    .Query<TScope>(),
                item => item.Name.In(names));

            return Session.ToRavenDbStreamAsync(query, cancellationToken);
        }
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TScope> FindByResourceAsync(string resource, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource, nameof(resource));
        IQueryable<TScope> query = Queryable.Where(
            Session
                .Query<TScope>(),
            s => s.Resources != null && s.Resources.Contains(resource));
        return Session.ToRavenDbStreamAsync(query, cancellationToken);
    }

    /// <inheritdoc/>
    public async ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<TScope>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await query(Session.Query<TScope>(), state).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetDescriptionAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope, nameof(scope));
        return new ValueTask<string?>(scope.Description);
    }

    /// <inheritdoc />
    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(
        TScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.Descriptions is not { Count: > 0 })
        {
            return new ValueTask<ImmutableDictionary<CultureInfo, string>>(
                ImmutableDictionary.Create<CultureInfo, string>());
        }

        return new ValueTask<ImmutableDictionary<CultureInfo, string>>(
            scope.Descriptions.ToImmutableDictionary(
                pair => CultureInfo.GetCultureInfo(pair.Key),
                pair => pair.Value));
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetDisplayNameAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new ValueTask<string?>(scope.DisplayName);
    }

    /// <inheritdoc />
    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
        TScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.DisplayNames is not { Count: > 0 })
        {
            return new ValueTask<ImmutableDictionary<CultureInfo, string>>(
                ImmutableDictionary.Create<CultureInfo, string>());
        }

        return new ValueTask<ImmutableDictionary<CultureInfo, string>>(
            scope.DisplayNames.ToImmutableDictionary(
                pair => CultureInfo.GetCultureInfo(pair.Key),
                pair => pair.Value));
    }

    /// <inheritdoc />
    public ValueTask<string?> GetIdAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new ValueTask<string?>(scope.Id);
    }

    /// <inheritdoc/>
    public ValueTask<string?> GetNameAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return new ValueTask<string?>(scope.Name);
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        TScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (scope.Properties is null)
        {
            return new ValueTask<ImmutableDictionary<string, JsonElement>>(
                ImmutableDictionary.Create<string, JsonElement>());
        }

        using var document = JsonDocument.Parse(scope.Properties);
        ImmutableDictionary<string, JsonElement>.Builder builder =
            ImmutableDictionary.CreateBuilder<string, JsonElement>();

        foreach (JsonProperty property in document.RootElement.EnumerateObject())
        {
            builder[property.Name] = property.Value.Clone();
        }

        return new ValueTask<ImmutableDictionary<string, JsonElement>>(builder.ToImmutable());
    }

    /// <inheritdoc/>
    public ValueTask<ImmutableArray<string>> GetResourcesAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        return scope.Resources is not { Count: > 0 }
            ? new ValueTask<ImmutableArray<string>>([])
            : new ValueTask<ImmutableArray<string>>([.. scope.Resources]);
    }

    /// <inheritdoc />
    public ValueTask<TScope> InstantiateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return new ValueTask<TScope>(Activator.CreateInstance<TScope>());
        }
        catch (MemberAccessException exception)
        {
            return new ValueTask<TScope>(
                Task.FromException<TScope>(
                    new InvalidOperationException($"Could not create instance of {typeof(TScope)}", exception)
                )
            );
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TScope> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IRavenQueryable<TScope> query = Session.Query<TScope>();

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

        List<TScope> scopes = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (TScope scope in scopes)
        {
            yield return scope;
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<TScope>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query, nameof(query));
        return Session.ToRavenDbStreamAsync(query(Session.Query<TScope>(), state), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask SetDescriptionAsync(TScope scope, string? description, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.Description = description;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetDescriptionsAsync(
        TScope scope,
        ImmutableDictionary<CultureInfo, string> descriptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (descriptions is not { Count: > 0 })
        {
            scope.Descriptions = null;
            return default;
        }

        scope.Descriptions = descriptions.ToDictionary(
            pair => pair.Key.Name,
            pair => pair.Value);

        return default;
    }

    /// <inheritdoc />
    public ValueTask SetDisplayNameAsync(TScope scope, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.DisplayName = name;
        return default;
    }

    /// <inheritdoc />
    public ValueTask SetDisplayNamesAsync(
        TScope scope,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (names is not { Count: > 0 })
        {
            scope.DisplayNames = null;
            return default;
        }

        scope.DisplayNames = names.ToDictionary(
            pair => pair.Key.Name,
            pair => pair.Value);

        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetNameAsync(TScope scope, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        scope.Name = name;
        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetPropertiesAsync(
        TScope scope,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (properties is not { Count: > 0 })
        {
            scope.Properties = null;

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

        scope.Properties = Encoding.UTF8.GetString(stream.ToArray());

        return default;
    }

    /// <inheritdoc/>
    public ValueTask SetResourcesAsync(
        TScope scope,
        ImmutableArray<string> resources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        if (resources.IsDefaultOrEmpty)
        {
            scope.Resources = null;
            return default;
        }

        scope.Resources = resources.ToList();
        return default;
    }

    /// <inheritdoc/>
    public async ValueTask UpdateAsync(TScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope, nameof(scope));
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Name, nameof(scope.Name));

        if (!Session.Advanced.IsLoaded(scope.Id))
        {
            throw new Exception("Scope is expected to be already loaded in the RavenDB session.");
        }

        // if scope name has changed make sure it's unique by reserving it
        if (Session.IfPropertyChanged(
                scope,
                changedPropertyName: nameof(scope.Name),
                newPropertyValue: scope.Name,
                out PropertyChange<string?>? scopeNamePropertyChange))
        {
            Debug.Assert(
                scopeNamePropertyChange != null,
                $"Unexpected NULL value for {nameof(scopeNamePropertyChange)}");

            Debug.Assert(
                !string.IsNullOrWhiteSpace(scopeNamePropertyChange.OldPropertyValue),
                "Scope name old value must never be empty or NULL.");

            Debug.Assert(
                !string.IsNullOrWhiteSpace(scope.Name),
                "Scope name must never be empty or NULL.");

            Debug.Assert(scope.Id is not null, "Scope ID must not be NULL.");

            // cluster wide as we will deal with compare exchange values either directly or as atomic guards
            if (IsClusterWideTransaction() is false)
            {
                _logger.LogDebug("Setting cluster wide transaction mode for scope update");
            }

            Session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);
            Session.Advanced.UseOptimisticConcurrency = false; // cluster wide tx doesn't support opt. concurrency

            UniqueReservationDocumentUtility<TUniqueReservation> uniqueReservationUtil =
                CreateUniqueReservationDocumentsUtility(
                    reservationType: UniqueReservationType.ScopeName,
                    uniqueValue: scope.Name);
            bool uniqueExists = await uniqueReservationUtil.CheckIfUniqueIsTakenAsync().ConfigureAwait(false);
            if (uniqueExists)
            {
                throw new DuplicateException($"Scope Name {scope.Name} is not unique.");
            }

            await uniqueReservationUtil.UpdateReservationAndAddToUnitOfWork(
                scopeNamePropertyChange.OldPropertyValue,
                scope.Id).ConfigureAwait(false);

            Debug.Assert(
                IsClusterWideTransaction(),
                "Cluster wide transaction required in order to update related atomic guards"
            );
        }

        // in cluster wide mode relying on atomic guards for optimistic concurrency
        if (IsClusterWideTransaction() is false)
        {
            string changeVector = Session.Advanced.GetChangeVectorFor(scope);
            await Session
                .StoreAsync(scope, changeVector, scope.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await Session.StoreAsync(scope, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await Session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Raven.Client.Exceptions.ConcurrencyException concurrencyException)
        {
            _logger.LogError(
                concurrencyException,
                "Failed updating scope with name {Name} due to a concurrency conflict",
                scope.Name);
            throw new ConcurrencyException();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed updating scope with name {Name}  due to an error",
                scope.Name);
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