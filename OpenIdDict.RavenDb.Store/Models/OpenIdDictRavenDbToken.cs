using System;

namespace Mcrio.OpenIdDict.RavenDb.Store.Models;

/// <summary>
/// Represents an OpenIdDict token.
/// </summary>
public class OpenIdDictRavenDbToken : IEntity
{
    /// <summary>
    /// Gets or sets document Id.
    /// </summary>
    public virtual string? Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the application associated with the current token.
    /// </summary>
    public virtual string? ApplicationId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the authorization associated with the current token.
    /// </summary>
    public virtual string? AuthorizationId { get; set; }

    /// <summary>
    /// Gets or sets the creation date of the current token.
    /// </summary>
    public virtual DateTimeOffset? CreationDate { get; set; }

    /// <summary>
    /// Gets or sets the expiration date of the current token.
    /// </summary>
    public virtual DateTimeOffset? ExpirationDate { get; set; }

    /// <summary>
    /// Gets or sets the payload of the current token, if applicable.
    /// Note: this property is only used for reference tokens
    /// and may be encrypted for security reasons.
    /// </summary>
    public virtual string? Payload { get; set; }

    /// <summary>
    /// Gets or sets the additional properties serialized as a JSON object,
    /// or <see langword="null"/> if no bag was associated with the current application.
    /// </summary>
    public virtual string? Properties { get; set; }

    /// <summary>
    /// Gets or sets the redemption date of the current token.
    /// </summary>
    public virtual DateTimeOffset? RedemptionDate { get; set; }

    /// <summary>
    /// Gets or sets the reference identifier associated
    /// with the current token, if applicable.
    /// Note: this property is only used for reference tokens
    /// and may be hashed or encrypted for security reasons.
    /// </summary>
    public virtual string? ReferenceId { get; set; }

    /// <summary>
    /// Gets or sets the status of the current token.
    /// </summary>
    public virtual string? Status { get; set; }

    /// <summary>
    /// Gets or sets the subject associated with the current token.
    /// </summary>
    public virtual string? Subject { get; set; }

    /// <summary>
    /// Gets or sets the type of the current token.
    /// </summary>
    public virtual string? Type { get; set; }
}