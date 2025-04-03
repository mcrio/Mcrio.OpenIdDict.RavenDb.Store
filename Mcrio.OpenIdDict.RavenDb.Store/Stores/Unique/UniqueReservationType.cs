namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique;

/// <summary>
/// Unique value reservation types.
/// </summary>
public enum UniqueReservationType
{
    /// <summary>
    /// OpenIdDict Application Client ID reservation.
    /// </summary>
    ApplicationClientId,

    /// <summary>
    /// OpenIdDict Scope Name reservation.
    /// </summary>
    ScopeName,
}