using System;
using Mcrio.OpenIdDict.RavenDb.Store.Models;
using Mcrio.OpenIdDict.RavenDb.Store.Stores;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Index;
using Mcrio.OpenIdDict.RavenDb.Store.Stores.Unique;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Core;

namespace Mcrio.OpenIdDict.RavenDb.Store;

/// <summary>
/// Exposes the necessary methods required to configure the OpenIdDict RavenDb services.
/// </summary>
public sealed class OpenIdDictRavenDbBuilder
{
    private readonly IServiceCollection _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenIdDictRavenDbBuilder"/> class.
    /// </summary>
    /// <param name="services"></param>
    public OpenIdDictRavenDbBuilder(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services, nameof(services));
        _services = services;
    }

    /// <summary>
    /// Configures OpenIdDict to use the specified entity as the default application entity.
    /// </summary>
    /// <returns>The <see cref="OpenIdDictRavenDbBuilder"/> instance.</returns>
    /// <typeparam name="TApplication">Application type.</typeparam>
    /// <typeparam name="TApplicationStore">Application store type.</typeparam>
    /// <typeparam name="TUniqueReservation">Unique reservations document.</typeparam>
    public OpenIdDictRavenDbBuilder ReplaceDefaultApplicationEntity<
        TApplication,
        TApplicationStore,
        TUniqueReservation>()
        where TApplication : OpenIdDictRavenDbApplication
        where TApplicationStore : OpenIdDictRavenDbApplicationStore<TApplication, TUniqueReservation>
        where TUniqueReservation : OpenIdDictUniqueReservation
    {
        _services.Replace(
            ServiceDescriptor.Scoped<IOpenIddictApplicationManager>(
                static provider =>
                    provider.GetRequiredService<OpenIddictApplicationManager<TApplication>>()));

        _services.Replace(
            ServiceDescriptor.Scoped<
                IOpenIddictApplicationStore<TApplication>,
                TApplicationStore
            >());

        return this;
    }

    /// <summary>
    /// Configures OpenIdDict to use the specified entity as the default application entity.
    /// </summary>
    /// <returns>The <see cref="OpenIdDictRavenDbBuilder"/> instance.</returns>
    /// <typeparam name="TApplication">Application type.</typeparam>
    /// <typeparam name="TApplicationStore">Application store type.</typeparam>
    public OpenIdDictRavenDbBuilder ReplaceDefaultApplicationEntity<
        TApplication, TApplicationStore>()
        where TApplication : OpenIdDictRavenDbApplication
        where TApplicationStore : OpenIdDictRavenDbApplicationStore<TApplication, OpenIdDictUniqueReservation>
    {
        return ReplaceDefaultApplicationEntity<TApplication, TApplicationStore, OpenIdDictUniqueReservation>();
    }

    /// <summary>
    /// Configures OpenIdDict to use the specified entity as the default authorization entity.
    /// </summary>
    /// <returns>The <see cref="OpenIdDictRavenDbBuilder"/> instance.</returns>
    /// <typeparam name="TAuthorization">Authorization document type.</typeparam>
    /// <typeparam name="TAuthorizationStore">Authorization store type.</typeparam>
    public OpenIdDictRavenDbBuilder ReplaceDefaultAuthorizationEntity<
        TAuthorization,
        TAuthorizationStore>()
        where TAuthorization : OpenIdDictRavenDbAuthorization
        where TAuthorizationStore : OpenIdDictRavenDbAuthorizationStore<TAuthorization>
    {
        _services.Replace(
            ServiceDescriptor.Scoped<IOpenIddictAuthorizationManager>(
                static provider =>
                    provider.GetRequiredService<OpenIddictAuthorizationManager<TAuthorization>>()));

        _services.Replace(
            ServiceDescriptor.Scoped<
                IOpenIddictAuthorizationStore<TAuthorization>,
                TAuthorizationStore
            >());

        return this;
    }

    /// <summary>
    /// Configures OpenIdDict to use the specified entity as the default scope entity.
    /// </summary>
    /// <returns>The <see cref="OpenIdDictRavenDbBuilder"/> instance.</returns>
    /// <typeparam name="TScope">Scope document type.</typeparam>
    /// <typeparam name="TScopeStore">Scope store type.</typeparam>
    /// <typeparam name="TUniqueReservation">Unique reservations document.</typeparam>
    public OpenIdDictRavenDbBuilder ReplaceDefaultScopeEntity<
        TScope,
        TScopeStore,
        TUniqueReservation>()
        where TScope : OpenIdDictRavenDbScope
        where TScopeStore : OpenIdDictRavenDbScopeStore<TScope, TUniqueReservation>
        where TUniqueReservation : OpenIdDictUniqueReservation
    {
        _services.Replace(
            ServiceDescriptor.Scoped<IOpenIddictScopeManager>(
                static provider =>
                    provider.GetRequiredService<OpenIddictScopeManager<TScope>>()));

        _services.Replace(
            ServiceDescriptor.Scoped<
                IOpenIddictScopeStore<TScope>,
                TScopeStore
            >());

        return this;
    }

    /// <summary>
    /// Configures OpenIdDict to use the specified entity as the default scope entity.
    /// </summary>
    /// <returns>The <see cref="OpenIdDictRavenDbBuilder"/> instance.</returns>
    /// <typeparam name="TScope">Scope document type.</typeparam>
    /// <typeparam name="TScopeStore">Scope store type.</typeparam>
    public OpenIdDictRavenDbBuilder ReplaceDefaultScopeEntity<TScope, TScopeStore>()
        where TScope : OpenIdDictRavenDbScope
        where TScopeStore : OpenIdDictRavenDbScopeStore<TScope, OpenIdDictUniqueReservation>
    {
        return ReplaceDefaultScopeEntity<TScope, TScopeStore, OpenIdDictUniqueReservation>();
    }

    /// <summary>
    /// Configures OpenIdDict to use the specified entity as the default token entity.
    /// </summary>
    /// <returns>The <see cref="OpenIdDictRavenDbBuilder"/> instance.</returns>
    /// <typeparam name="TToken">Token document type.</typeparam>
    /// <typeparam name="TTokenStore">Token store document type.</typeparam>
    /// <typeparam name="TTokenIndex">Token index type.</typeparam>
    public OpenIdDictRavenDbBuilder ReplaceDefaultTokenEntity<
        TToken,
        TTokenStore,
        TTokenIndex>()
        where TToken : OpenIdDictRavenDbToken
        where TTokenIndex : OIDct_Tokens<TToken>, new()
        where TTokenStore : OpenIdDictRavenDbTokenStore<TToken, TTokenIndex>
    {
        _services.Replace(
            ServiceDescriptor.Scoped<IOpenIddictTokenManager>(
                static provider =>
                    provider.GetRequiredService<OpenIddictTokenManager<TToken>>()));

        _services.Replace(
            ServiceDescriptor.Scoped<
                IOpenIddictTokenStore<TToken>,
                TTokenStore
            >());

        return this;
    }
}