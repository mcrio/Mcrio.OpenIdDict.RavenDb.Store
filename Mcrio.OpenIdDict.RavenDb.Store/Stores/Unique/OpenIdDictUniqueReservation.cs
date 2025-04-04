namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique;

/// <summary>
/// Unique reservation document.
/// </summary>
public class OpenIdDictUniqueReservation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OpenIdDictUniqueReservation"/> class.
    /// </summary>
    /// <param name="id">Unique reservation ID.</param>
    /// <param name="referenceId">Reference document id.</param>
    public OpenIdDictUniqueReservation(string id, string referenceId)
    {
        Id = id;
        ReferenceId = referenceId;
    }

    /// <summary>
    /// Required for object mapping.
    /// </summary>
#pragma warning disable CS8618, CS9264
    protected OpenIdDictUniqueReservation()
#pragma warning restore CS8618, CS9264
    {
    }

    /// <summary>
    /// Gets the reservation id.
    /// </summary>
    public string Id { get; private set; }

    /// <summary>
    /// Gets the reference document id.
    /// </summary>
    public string ReferenceId { get; private set; }
}