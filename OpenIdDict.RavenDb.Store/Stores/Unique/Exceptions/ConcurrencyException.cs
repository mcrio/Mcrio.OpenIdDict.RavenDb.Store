using System;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique.Exceptions;

/// <summary>
/// Concurrency exception.
/// </summary>
public class ConcurrencyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrencyException"/> class.
    /// </summary>
    /// <param name="message">Optional exception message.</param>
    public ConcurrencyException(string? message = null)
        : base(message ?? "Concurrency exception.")
    {
    }
}