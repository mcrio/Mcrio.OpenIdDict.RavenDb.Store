using Raven.Client.Documents.Session;

namespace Mcrio.OpenIdDict.RavenDb.Store;

/// <summary>
/// Provides the async document session.
/// </summary>
/// <returns>RavenDB async document session.</returns>
public delegate IAsyncDocumentSession OpenIdDictDocumentSessionProvider();