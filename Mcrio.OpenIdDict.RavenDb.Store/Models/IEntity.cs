namespace Mcrio.OpenIdDict.RavenDb.Store.Models;

/// <summary>
/// Defines a generalized entity that must define an Id property.
/// </summary>
internal interface IEntity
{
    /// <summary>
    /// Gets the entity Id value.
    /// </summary>
    string Id { get; }
}