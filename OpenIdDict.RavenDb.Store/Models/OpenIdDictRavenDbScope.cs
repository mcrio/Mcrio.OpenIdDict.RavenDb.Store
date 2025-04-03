using System.Collections.Generic;

namespace Mcrio.OpenIdDict.RavenDb.Store.Models;

/// <summary>
/// Represents an OpenIdDict scope.
/// </summary>
public class OpenIdDictRavenDbScope : IEntity
{
    /// <summary>
    /// Gets or sets document Id.
    /// </summary>
    public virtual string? Id { get; set; }

    /// <summary>
    /// Gets or sets the public description associated with the current scope.
    /// </summary>
    public virtual string? Description { get; set; }

    /// <summary>
    /// Gets or sets the localized public descriptions associated with the current scope.
    /// </summary>
    public virtual Dictionary<string, string>? Descriptions { get; set; }

    /// <summary>
    /// Gets or sets the display name associated with the current scope.
    /// </summary>
    public virtual string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the localized display names associated with the current scope.
    /// </summary>
    public virtual Dictionary<string, string>? DisplayNames { get; set; }

    /// <summary>
    /// Gets or sets the unique name associated with the current scope.
    /// </summary>
    public virtual string? Name { get; set; }

    /// <summary>
    /// Gets or sets the additional properties serialized as a JSON object,
    /// or <see langword="null"/> if no bag was associated with the current application.
    /// </summary>
    public virtual string? Properties { get; set; }

    /// <summary>
    /// Gets or sets the resources associated with the current scope.
    /// </summary>
    public virtual List<string>? Resources { get; set; }
}