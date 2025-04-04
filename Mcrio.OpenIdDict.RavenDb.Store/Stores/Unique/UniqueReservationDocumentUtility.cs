using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique.Exceptions;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique;

/// <summary>
/// Delegate for Unicode normalization of the unique value.
/// </summary>
/// <param name="input">Input string.</param>
/// <returns>Normalized value.</returns>
public delegate string NormalizationCallback(string input);

/// <inheritdoc />
public class UniqueReservationDocumentUtility : UniqueReservationDocumentUtility<OpenIdDictUniqueReservation>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UniqueReservationDocumentUtility"/> class.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="reservationType"></param>
    /// <param name="uniqueValue"></param>
    public UniqueReservationDocumentUtility(
        IAsyncDocumentSession session,
        UniqueReservationType reservationType,
        string uniqueValue)
        : base(session, reservationType, uniqueValue)
    {
    }

    /// <inheritdoc />
    protected override OpenIdDictUniqueReservation CreateReservationDocument(string documentId, string ownerDocumentId)
    {
        return new OpenIdDictUniqueReservation(documentId, ownerDocumentId);
    }
}

/// <summary>
/// Utility for Unique value reservations using reservation documents, cluster wide transactions
/// RavenDB and atomic guards.
/// Unique values can be usernames, email addresses, etc.
/// </summary>
/// <typeparam name="TReservation">Unique reservation document type.</typeparam>
public abstract class UniqueReservationDocumentUtility<TReservation>
    where TReservation : OpenIdDictUniqueReservation
{
    private readonly NormalizationCallback _defaultNormalizeCallback =
        input => input.Normalize(NormalizationForm.FormKD).ToLowerInvariant();

    private readonly IAsyncDocumentSession _session;
    private readonly UniqueReservationType _reservationType;
    private readonly string _uniqueValue;
    private readonly NormalizationCallback? _normalizationOverride;

    // ReSharper disable once RedundantDefaultMemberInitializer
    private bool _checkedIfUniqueExists = false;

    // ReSharper disable once RedundantDefaultMemberInitializer
    private bool _reservationAddedToUow = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="UniqueReservationDocumentUtility{TReservation}"/> class.
    /// </summary>
    /// <param name="session">Document session.</param>
    /// <param name="reservationType">Unique reservation prefix used as part of the reservation document id.</param>
    /// <param name="uniqueValue">New unique value.</param>
    protected UniqueReservationDocumentUtility(
        IAsyncDocumentSession session,
        UniqueReservationType reservationType,
        string uniqueValue)
        : this(session, reservationType, uniqueValue, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="UniqueReservationDocumentUtility{TReservation}"/> class.
    /// </summary>
    /// <param name="session">Document session.</param>
    /// <param name="reservationType">Unique reservation prefix used as part of the reservation document id.</param>
    /// <param name="uniqueValue">New unique value.</param>
    /// <param name="normalizationOverride">When not NULL overrides the default Unicode normalization functionality.</param>
    protected UniqueReservationDocumentUtility(
        IAsyncDocumentSession session,
        UniqueReservationType reservationType,
        string uniqueValue,
        NormalizationCallback? normalizationOverride)
    {
        if (string.IsNullOrWhiteSpace(uniqueValue))
        {
            throw new ArgumentException($"{nameof(uniqueValue)} must not be empty");
        }

        _session = session;
        _reservationType = reservationType;
        _uniqueValue = uniqueValue;
        _normalizationOverride = normalizationOverride;
    }

    /// <summary>
    /// Indicates whether the unique value is already taken.
    /// </summary>
    /// <returns>True if unique value is already taken, False otherwise.</returns>
    public async Task<bool> CheckIfUniqueIsTakenAsync()
    {
        _checkedIfUniqueExists = true;
        string reservationDocumentId = GetReservationDocumentId(_uniqueValue);
        return await _session.Advanced.ExistsAsync(reservationDocumentId).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a new reservation document and adds it to the document session unit of work.
    /// </summary>
    /// <param name="ownerDocumentId">Id of the document the unique value is related to.</param>
    /// <returns>Reservation document.</returns>
    /// <exception cref="DuplicateException">When the unique value reservation already exists.</exception>
    /// <exception cref="ReservationDocumentAlreadyAddedToUnitOfWorkException">When the reservation document was already added to unit of work.</exception>
    public Task<TReservation> CreateReservationDocumentAddToUnitOfWorkAsync(string ownerDocumentId)
    {
        if (string.IsNullOrEmpty(ownerDocumentId))
        {
            throw new ArgumentException($"{nameof(ownerDocumentId)} must not be empty");
        }

        if (_reservationAddedToUow)
        {
            throw new ReservationDocumentAlreadyAddedToUnitOfWorkException();
        }

        return NewReservationCreateAndAddToUow(ownerDocumentId);
    }

    /// <summary>
    /// If reservation exists marks it for deletion in the unit of work.
    /// </summary>
    /// <returns>Task.</returns>
    /// <exception cref="ClusterWideTransactionModeRequiredException">If not a cluster wide transaction.</exception>
    public async Task MarkReservationForDeletionAsync()
    {
        if (IsClusterWideTransaction() is false)
        {
            throw new ClusterWideTransactionModeRequiredException();
        }

        string oldReservationDocumentId = GetReservationDocumentId(_uniqueValue);

        TReservation? oldReservation = await _session
            .LoadAsync<TReservation>(oldReservationDocumentId)
            .ConfigureAwait(false);

        if (oldReservation != null)
        {
            _session.Delete(oldReservation);
        }
    }

    /// <summary>
    /// Load reservation document.
    /// </summary>
    /// <returns>Reservation document.</returns>
    public async Task<TReservation?> LoadReservationAsync()
    {
        string reservationDocumentId = GetReservationDocumentId(_uniqueValue);
        TReservation? reservation = await _session
            .LoadAsync<TReservation>(reservationDocumentId)
            .ConfigureAwait(false);
        return reservation;
    }

    /// <summary>
    /// Update reservation by marking the old reservation document for deletion and adding a new one to
    /// the unit of work.
    /// </summary>
    /// <param name="oldUniqueValue">Old unique value. When NULL we assume there was no reservation for old value.</param>
    /// <param name="ownerDocumentId">Id of the document the reservation belongs to.</param>
    /// <returns>Task.</returns>
    /// <exception cref="DuplicateException">When the unique value reservation already exists.</exception>
    /// <exception cref="ReservationDocumentAlreadyAddedToUnitOfWorkException">When the reservation document was already added to unit of work.</exception>
    public async Task<TReservation> UpdateReservationAndAddToUnitOfWork(
        string? oldUniqueValue,
        string ownerDocumentId)
    {
        if (string.IsNullOrWhiteSpace(ownerDocumentId))
        {
            throw new ArgumentException(
                $"Unexpected empty value for {nameof(ownerDocumentId)} in {nameof(UpdateReservationAndAddToUnitOfWork)}");
        }

        if (_reservationAddedToUow)
        {
            throw new ReservationDocumentAlreadyAddedToUnitOfWorkException();
        }

        _reservationAddedToUow = true;

        if (oldUniqueValue != null)
        {
            // Get old reservation and mark it for deletion
            string oldReservationDocumentId = GetReservationDocumentId(oldUniqueValue);
            OpenIdDictUniqueReservation? oldReservation = await _session
                .LoadAsync<OpenIdDictUniqueReservation>(oldReservationDocumentId)
                .ConfigureAwait(false);

            if (oldReservation != null)
            {
                _session.Delete(oldReservation);
            }
        }

        return await NewReservationCreateAndAddToUow(ownerDocumentId).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the document id type prefix for the given reservation type.
    /// </summary>
    /// <param name="reservationType"></param>
    /// <returns>The document id type prefix for the given reservation type.</returns>
    protected virtual string GetKeyPrefix(UniqueReservationType reservationType)
    {
        return reservationType switch
        {
            UniqueReservationType.ApplicationClientId => "appclientid",
            UniqueReservationType.ScopeName => "scopename",
            _ => throw new Exception($"Unhandled reservation type {reservationType}"),
        };
    }

    /// <summary>
    /// Get semantic document id for provided unique value.
    /// </summary>
    /// <param name="uniqueValue">Unique value.</param>
    /// <returns>Semantic, unique, reservation document id for provided unique value.</returns>
    protected virtual string GetReservationDocumentId(string uniqueValue)
    {
        Debug.Assert(
            !string.IsNullOrWhiteSpace(uniqueValue),
            "Unique value must not be empty"
        );

        IDocumentStore store = _session.Advanced.DocumentStore;
        string reservationsCollectionPrefix = store.Conventions.TransformTypeCollectionNameToDocumentIdPrefix(
            store.Conventions.FindCollectionName(typeof(TReservation))
        );
        char separator = store.Conventions.IdentityPartsSeparator;
        string reservationTypePrefix = GetKeyPrefix(_reservationType);
        string normalizedUniqueValue = _normalizationOverride is not null
            ? _normalizationOverride(uniqueValue)
            : _defaultNormalizeCallback(uniqueValue);

        if (string.IsNullOrWhiteSpace(normalizedUniqueValue))
        {
            throw new ArgumentException(
                $"Unexpected empty value for {nameof(normalizedUniqueValue)} in {nameof(GetReservationDocumentId)} after unicode normalization of {uniqueValue}"
            );
        }

        return
            $"{reservationsCollectionPrefix}{separator}{reservationTypePrefix}{separator}{normalizedUniqueValue}";
    }

    /// <summary>
    /// Creates an instance of <typeparamref name="TReservation"/>.
    /// </summary>
    /// <param name="documentId">Unique reservation document id.</param>
    /// <param name="ownerDocumentId">Owner, reservation document id.</param>
    /// <returns>Instance of <typeparamref name="TReservation"/>.</returns>
    protected abstract TReservation CreateReservationDocument(
        string documentId,
        string ownerDocumentId
    );

    /// <summary>
    /// Create new reservation document and add it to the unit of work.
    /// </summary>
    /// <param name="ownerDocumentId">Id of the document the unique value belongs to.</param>
    /// <returns>Unique value reservation document.</returns>
    /// <exception cref="DuplicateException">When the unique value reservation already exists.</exception>
    protected virtual async Task<TReservation> NewReservationCreateAndAddToUow(string ownerDocumentId)
    {
        if (string.IsNullOrWhiteSpace(ownerDocumentId))
        {
            throw new ArgumentException(
                $"Unexpected empty value for {nameof(ownerDocumentId)} in {nameof(NewReservationCreateAndAddToUow)}");
        }

        if (IsClusterWideTransaction() is false)
        {
            throw new ClusterWideTransactionModeRequiredException();
        }

        _reservationAddedToUow = true;

        if (!_checkedIfUniqueExists)
        {
            bool exists = await CheckIfUniqueIsTakenAsync().ConfigureAwait(false);
            if (exists)
            {
                throw new DuplicateException();
            }
        }

        string reservationDocumentId = GetReservationDocumentId(_uniqueValue);
        TReservation reservationDocument = CreateReservationDocument(reservationDocumentId, ownerDocumentId);

        await _session.StoreAsync(reservationDocument).ConfigureAwait(false);

        return reservationDocument;
    }

    /// <summary>
    /// Indicates whether the current document session is in Cluster Wide transaction mode.
    /// </summary>
    /// <returns>TRUE when cluster wide transaction, FALSE otherwise.</returns>
    protected bool IsClusterWideTransaction()
    {
        return ((AsyncDocumentSession)_session).TransactionMode == TransactionMode.ClusterWide;
    }
}