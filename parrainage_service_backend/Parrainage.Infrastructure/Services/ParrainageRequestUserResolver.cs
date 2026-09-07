using System.Security.Claims;
using Kyntus.Identity.Jwt;
using Kyntus.Messaging.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Parrainage.Application.Abstractions;
using Parrainage.Infrastructure.Persistence;

namespace Parrainage.Infrastructure.Services;

/// <summary>
/// Identité Parrainage = JWT uniquement (pas d'en-têtes / query spoofables).
/// </summary>
public sealed class ParrainageRequestUserResolver(
    IHttpContextAccessor httpContextAccessor,
    IServiceProvider serviceProvider,
    ILogger<ParrainageRequestUserResolver> logger) : IParrainageRequestUserResolver
{
    public ParrainageResolvedUser Resolve(
        string? headerUserId,
        string? headerRole,
        string? headerProjectId,
        string? queryRole = null,
        string? queryUserId = null,
        string? queryProjectId = null) =>
        Resolve(httpContextAccessor.HttpContext?.Request, headerUserId, headerRole, headerProjectId, queryRole, queryUserId, queryProjectId);

    public ParrainageResolvedUser Resolve(
        HttpRequest? request,
        string? headerUserId = null,
        string? headerRole = null,
        string? headerProjectId = null,
        string? queryRole = null,
        string? queryUserId = null,
        string? queryProjectId = null)
    {
        // Les paramètres header*/query* sont ignorés volontairement (anti-spoofing).
        _ = (request, headerUserId, headerRole, headerProjectId, queryRole, queryUserId);

        var fromJwt = TryResolveFromJwt(queryProjectId);
        if (fromJwt is null)
        {
            logger.LogWarning("PARRAINAGE : JWT absent ou incomplet — accès refusé.");
            throw new UnauthorizedAccessException("Authentification JWT requise pour le module Parrainage.");
        }

        return fromJwt;
    }

    private ParrainageResolvedUser? TryResolveFromJwt(string? queryProjectId)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true)
            return null;

        var email = principal.GetEmail();
        if (string.IsNullOrWhiteSpace(email))
            return ResolveFromJwtClaims(principal, queryProjectId);

        using var scope = serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetService<ParrainageDbContext>();
        if (db is null)
            return ResolveFromJwtClaims(principal, queryProjectId);

        var needle = email.Trim().ToLowerInvariant();
        var row = db.PortalUsers.AsNoTracking()
            .FirstOrDefault(u => u.Email.ToLower() == needle);
        if (row is null)
        {
            logger.LogDebug(
                "PARRAINAGE : aucun utilisateur portail pour {Email} — claims JWT.",
                email);
            return ResolveFromJwtClaims(principal, queryProjectId);
        }

        var portalRole = IParrainageRequestUserResolver.NormalizeRole(row.Role);
        var jwtRole = ResolveJwtParrainageRole(principal);
        var role = ResolveEffectiveRole(jwtRole, portalRole);
        var projectId = queryProjectId ?? row.ProjectId;
        return new ParrainageResolvedUser(row.Id, role, projectId, false);
    }

    private static ParrainageResolvedUser? ResolveFromJwtClaims(
        ClaimsPrincipal principal,
        string? queryProjectId)
    {
        var jwtRole = ResolveJwtParrainageRole(principal);
        if (string.IsNullOrWhiteSpace(jwtRole))
            return null;

        var userId = FirstNonEmpty(
            principal.GetSubjectId()?.ToString(),
            principal.GetAuthUserId()?.ToString(),
            principal.FindFirst("sub")?.Value,
            principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        if (string.IsNullOrWhiteSpace(userId))
            return null;

        return new ParrainageResolvedUser(userId, jwtRole, queryProjectId, false);
    }

    internal static string ResolveEffectiveRole(string? primaryRole, string? secondaryRole)
    {
        if (!string.IsNullOrWhiteSpace(primaryRole) && IsPrivilegedParrainageRole(primaryRole))
            return primaryRole;
        if (!string.IsNullOrWhiteSpace(secondaryRole) && IsPrivilegedParrainageRole(secondaryRole))
            return secondaryRole;
        if (string.Equals(primaryRole, "PILOTE", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(secondaryRole)
            && !string.Equals(secondaryRole, "PILOTE", StringComparison.Ordinal))
            return secondaryRole;
        if (!string.IsNullOrWhiteSpace(primaryRole))
            return primaryRole;
        if (!string.IsNullOrWhiteSpace(secondaryRole))
            return secondaryRole;
        return "PILOTE";
    }

    private static string? ResolveJwtParrainageRole(ClaimsPrincipal principal)
    {
        var raw = principal.GetAuthRole();
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        return IParrainageRequestUserResolver.NormalizeRole(KyntusPortalRoleMapping.ToParrainageRole(raw));
    }

    private static bool IsPrivilegedParrainageRole(string role) =>
        role is "RH" or "ADMIN" or "COMPTA" or "COMPTABILITE" or "AUDIT";

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}
