using System;
using System.Collections.Generic;

namespace Mcrio.OpenIdDict.RavenDb.Store.Models;

/// <summary>
/// Represents an OpenIdDict authorization.
/// </summary>
public class OpenIdDictRavenDbAuthorization : IEntity
{
    /// <summary>
    /// Gets or sets document Id.
    /// </summary>
#pragma warning disable CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).
    public virtual string? Id { get; set; }
#pragma warning restore CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).

    /// <summary>
    /// Gets or sets the identifier of the application associated with the current authorization.
    /// </summary>
    public virtual string? ApplicationId { get; set; }

    /// <summary>
    /// Gets or sets the UTC creation date of the current authorization.
    /// </summary>
    public virtual DateTimeOffset? CreationDate { get; set; }

    /// <summary>
    /// Gets or sets the additional properties serialized as a JSON object,
    /// or <see langword="null"/> if no bag was associated with the current application.
    /// </summary>
    public virtual string? Properties { get; set; }

    /// <summary>
    /// Gets or sets the scopes associated with the current authorization.
    /// </summary>
    public virtual List<string>? Scopes { get; set; }

    /// <summary>
    /// Gets or sets the status of the current authorization.
    /// </summary>
    public virtual string? Status { get; set; }

    /// <summary>
    /// Gets or sets the subject associated with the current authorization.
    /// </summary>
    public virtual string? Subject { get; set; }

    /// <summary>
    /// Gets or sets the type of the current authorization.
    /// </summary>
    public virtual string? Type { get; set; }
}