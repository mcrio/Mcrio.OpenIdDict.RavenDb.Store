using System;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Mcrio.OpenIdDict.RavenDb.Store.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raven.Client.Documents.Session;

namespace Mcrio.OpenIdDict.RavenDb.Store;

/// <summary>
/// Locates the RavenDb document session.
/// </summary>
/// <param name="provider">Service provider.</param>
/// <returns>Instance of an RavenDB async document session.</returns>
public delegate IAsyncDocumentSession DocumentSessionServiceLocator(IServiceProvider provider);

/// <summary>
/// Exposes extensions allowing to register the OpenIdDict RavenDB services.
/// </summary>
public static class OpenIdDictRavenDbExtension
{
    /// <summary>
    /// Configures the specified OpenIdDict core builder to use RavenDB as the underlying store
    /// for OpenIdDict entities.
    /// </summary>
    /// <param name="builder">
    /// The OpenIdDict core builder that is being configured.
    /// </param>
    /// <param name="documentSessionServiceLocator">RavenDB document session service locator.</param>
    /// <returns>
    /// An instance of <see cref="OpenIdDictRavenDbBuilder"/> to allow chaining additional configuration.
    /// </returns>
    public static OpenIdDictRavenDbBuilder UseRavenDb(
        this OpenIddictCoreBuilder builder,
        DocumentSessionServiceLocator documentSessionServiceLocator)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(documentSessionServiceLocator);

        builder.Services.TryAddScoped<OpenIdDictDocumentSessionProvider>(
            provider => () => documentSessionServiceLocator(provider)
        );

        // Since RavenDB does case-insensitive comparisons by default, ensure the additional internal
        // filtering logic is enforced to check for casing
        builder.Configure(options => options.DisableAdditionalFiltering = false);

        builder
            .SetDefaultApplicationEntity<OpenIdDictRavenDbApplication>()
            .SetDefaultAuthorizationEntity<OpenIdDictRavenDbAuthorization>()
            .SetDefaultScopeEntity<OpenIdDictRavenDbScope>()
            .SetDefaultTokenEntity<OpenIdDictRavenDbToken>();

        builder
            .ReplaceApplicationStore<OpenIdDictRavenDbApplication, OpenIdDictRavenDbApplicationStore>()
            .ReplaceAuthorizationStore<OpenIdDictRavenDbAuthorization, OpenIdDictRavenDbAuthorizationStore>()
            .ReplaceScopeStore<OpenIdDictRavenDbScope, OpenIdDictRavenDbScopeStore>()
            .ReplaceTokenStore<OpenIdDictRavenDbToken, OpenIdDictRavenDbTokenStore>();

        return new OpenIdDictRavenDbBuilder(builder.Services);
    }

    /// <summary>
    /// Configures the specified OpenIdDict core builder to use RavenDB as the underlying store
    /// for OpenIdDict entities.
    /// </summary>
    /// <param name="builder">
    /// The OpenIdDict core builder that is being configured.
    /// </param>
    /// <param name="documentSessionServiceLocator">RavenDb document session service locator.</param>
    /// <param name="configuration">
    /// A delegate that allows additional configuration of the OpenIdDictRavenDbBuilder.
    /// </param>
    /// <returns>
    /// The updated OpenIdDict core builder instance.
    /// </returns>
    public static OpenIddictCoreBuilder UseRavenDb(
        this OpenIddictCoreBuilder builder,
        DocumentSessionServiceLocator documentSessionServiceLocator,
        Action<OpenIdDictRavenDbBuilder> configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);

        configuration(builder.UseRavenDb(documentSessionServiceLocator));

        return builder;
    }
}