using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique;

namespace Mcrio.OpenIdDict.RavenDb.Store;

/// <summary>
/// Method to produce predefined collection names for existing document types.
/// </summary>
public static class OpenIdDictRavenDbConventions
{
    /// <summary>
    /// Pruning authorizations requires making a join on the related tokens but RavenDb does not support
    /// running DeleteByQuery operations on a map/reduce index. To work around this we need to output the index
    /// entries to a collection and work on that to prune the data.
    /// <see href="https://github.com/ravendb/ravendb/discussions/14496"/>.
    /// </summary>
    public const string AuthorizationsIndexDocumentsCollectionName = "_OIDDctAuthIndex";

    /// <summary>
    /// If type is a RavenDB OpenIdDict document type, returns the default collection name.
    /// </summary>
    /// <param name="type">Object type to get the collection for.</param>
    /// <param name="collectionInfo">Collection info of matching type.</param>
    /// <returns>Default collection name if type is known, otherwise NULL.</returns>
    public static bool TryGetCollectionName(
        Type type,
        [NotNullWhen(true)] out string? collectionInfo)
    {
        collectionInfo = GetOpenIdDictCollectionInfos()
            .FirstOrDefault(info => info.DocumentType.IsAssignableFrom(type))?
            .Name;
        return collectionInfo is not null;
    }

    /// <summary>
    /// Attempts to retrieve the collection prefix associated with the specified collection name.
    /// </summary>
    /// <param name="collectionName">The name of the collection for which the prefix is requested.</param>
    /// <param name="collectionPrefix">
    /// When the method returns, contains the prefix of the collection if found; otherwise, null.
    /// </param>
    /// <returns>True if the collection prefix was found; otherwise, false.</returns>
    public static bool TryGetCollectionPrefix(
        string collectionName,
        [NotNullWhen(true)] out string? collectionPrefix)
    {
        collectionPrefix = GetOpenIdDictCollectionInfos()
            .FirstOrDefault(info => info.Name == collectionName)?
            .Prefix;
        return collectionPrefix != null;
    }

    /// <summary>
    /// Gets the OpenIdDict related collection names and prefixes.
    /// </summary>
    /// <returns>List of <see cref="CollectionInfo"/>.</returns>
    private static List<CollectionInfo> GetOpenIdDictCollectionInfos() =>
    [
        new (typeof(OpenIdDictRavenDbApplication), "OIDDctApplications", "oidctapplication"),
        new (typeof(OpenIdDictRavenDbScope), "OIDDctScopes", "oidctscope"),
        new (typeof(OpenIdDictRavenDbAuthorization), "OIDDctAuthorizations", "oiddctauth"),
        new (typeof(OpenIdDictRavenDbToken), "OIDDctTokens", "oiddcttoken"),
        new (typeof(OpenIdDictUniqueReservation), "OIDDctUniques", "oiddctunique"),
    ];

    /// <summary>
    /// Collection info.
    /// </summary>
    /// <param name="DocumentType">Document type.</param>
    /// <param name="Name">Name of the collection.</param>
    /// <param name="Prefix">Document identifier prefix for collection.</param>
    private record CollectionInfo(Type DocumentType, string Name, string Prefix);
}