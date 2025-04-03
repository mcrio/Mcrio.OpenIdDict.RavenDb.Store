using System;
using System.Collections.Generic;
using Mcrio.OpenIdDict.RavenDb.Store.Models;

namespace Mcrio.OpenIdDict.RavenDb.Store.Stores.Tests.Integration;

internal static class ModelFactory
{
    internal static OpenIdDictRavenDbApplication CreateApplicationInstance(
        string? clientId,
        List<string>? redirectUris = null,
        List<string>? postLogoutRedirectUris = null)
    {
        return new OpenIdDictRavenDbApplication
        {
            Id = null,
            ClientId = clientId,
            RedirectUris = redirectUris,
            PostLogoutRedirectUris = postLogoutRedirectUris,
        };
    }

    internal static OpenIdDictRavenDbScope CreateScopeInstance(
        string? name)
    {
        return new OpenIdDictRavenDbScope
        {
            Id = null,
            Name = name,
        };
    }

    internal static OpenIdDictRavenDbAuthorization CreateAuthorizationInstance(
        string applicationId,
        DateTimeOffset creationDate,
        List<string>? scopes = null,
        string? type = null,
        string? status = null,
        string? subject = null,
        string? id = null)
    {
        return new OpenIdDictRavenDbAuthorization
        {
            Id = id,
            ApplicationId = applicationId,
            CreationDate = creationDate,
            Scopes = scopes,
            Type = type,
            Status = status,
            Subject = subject,
        };
    }

    internal static OpenIdDictRavenDbToken CreateTokenInstance(
        string applicationId,
        string authorizationId,
        DateTimeOffset creationDate,
        DateTimeOffset expirationDate,
        string? type = null,
        string? status = null,
        string? subject = null,
        string? referenceId = null)
    {
        return new OpenIdDictRavenDbToken
        {
            Id = null,
            ApplicationId = applicationId,
            AuthorizationId = authorizationId,
            CreationDate = creationDate,
            Type = type,
            Status = status,
            Subject = subject,
            ExpirationDate = expirationDate,
            ReferenceId = referenceId,
        };
    }
}