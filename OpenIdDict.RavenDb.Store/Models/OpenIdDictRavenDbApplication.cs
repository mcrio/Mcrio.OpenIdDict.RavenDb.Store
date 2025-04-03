using System.Collections.Generic;

namespace Mcrio.OpenIdDict.RavenDb.Store.Models;

/// <summary>
/// Represents an OpenIdDict application.
/// </summary>
public class OpenIdDictRavenDbApplication : IEntity
{
    /// <summary>
    /// Gets or sets document Id.
    /// </summary>
#pragma warning disable CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).
    public virtual string? Id { get; set; }
#pragma warning restore CS8766 // Nullability of reference types in return type doesn't match implicitly implemented member (possibly because of nullability attributes).

    /// <summary>
    /// Gets or sets the application type associated with the current application.
    /// </summary>
    public virtual string? ApplicationType { get; set; }

    /// <summary>
    /// Gets or sets the client identifier associated with the current application.
    /// </summary>
    public virtual string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret associated with the current application.
    /// Note: depending on the application manager used to create this instance,
    /// this property may be hashed or encrypted for security reasons.
    /// </summary>
    public virtual string? ClientSecret { get; set; }

    /// <summary>
    /// Gets or sets the client type associated with the current application.
    /// </summary>
    public virtual string? ClientType { get; set; }

    /// <summary>
    /// Gets or sets the consent type associated with the current application.
    /// </summary>
    public virtual string? ConsentType { get; set; }

    /// <summary>
    /// Gets or sets the display name associated with the current application.
    /// </summary>
    public virtual string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the localized display names
    /// associated with the current application.
    /// </summary>
    public virtual Dictionary<string, string>? DisplayNames { get; set; }

    /// <summary>
    /// Gets or sets the JSON Web Key Set associated with
    /// the application, serialized as a JSON object.
    /// </summary>
    public virtual string? JsonWebKeySet { get; set; }

    /// <summary>
    /// Gets or sets the permissions associated with the
    /// current application.
    /// </summary>
    public virtual List<string>? Permissions { get; set; }

    /// <summary>
    /// Gets or sets the post-logout redirect URIs associated with
    /// the current application.
    /// </summary>
    public virtual List<string>? PostLogoutRedirectUris { get; set; }

    /// <summary>
    /// Gets or sets the additional properties serialized as a JSON object,
    /// or <see langword="null"/> if no bag was associated with the current application.
    /// </summary>
    public virtual string? Properties { get; set; }

    /// <summary>
    /// Gets or sets the redirect URIs associated with the
    /// current application, serialized as a JSON array.
    /// </summary>
    public virtual List<string>? RedirectUris { get; set; }

    /// <summary>
    /// Gets or sets the requirements associated with the
    /// current application, serialized as a JSON array.
    /// </summary>
    public virtual List<string>? Requirements { get; set; }

    /// <summary>
    /// Gets or sets the settings serialized as a JSON object.
    /// </summary>
    public virtual Dictionary<string, string>? Settings { get; set; }
}