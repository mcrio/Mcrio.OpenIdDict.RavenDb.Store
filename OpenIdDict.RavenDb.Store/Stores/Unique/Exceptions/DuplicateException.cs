using System;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique.Exceptions;

/// <summary>
/// Duplicate document exception.
/// </summary>
public sealed class DuplicateException : ConcurrencyException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateException"/> class.
    /// </summary>
    /// <param name="message"></param>
    public DuplicateException(string? message = null)
        : base(message ?? "Item already exists.")
    {
    }
}