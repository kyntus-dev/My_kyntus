using System.Security.Claims;
using Documentation.Domain.Entities;
using Documentation.Infrastructure.Persistence;
using Kyntus.Caching;
using Kyntus.Identity.Jwt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Documentation.Infrastructure.Services;

/// <summary>Résout l'utilisateur annuaire documentation avec cache partagé (évite N lookups DB par page).</summary>
public sealed class DocumentationDirectoryUserLookup(
    DocumentationDbContext db,
    DocumentationJwtDirectoryProvisioner jwtProvisioner,
    ICacheService cache,
    IConfiguration configuration)
{
    private static readonly TimeSpan CacheTtl = KyntusCacheTtl.Entity;
    private readonly bool _autoProvisionDirectoryFromJwt =
        configuration.GetValue("Documentation:AutoProvisionDirectoryFromJwt", true)
        || configuration.GetValue("Documentation:AllowHeaderContextFallback", false);

    public async Task<DirectoryUser?> ResolveAsync(
        ClaimsPrincipal principal,
        string tenantId,
        CancellationToken ct)
    {
        var email = principal.GetEmail()?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var cacheKey = KyntusCacheKeys.DocUserLookup($"{tenantId}:{email}");
        var cached = await cache.GetAsync<DirectoryUser>(cacheKey, ct);
        if (cached is not null)
            return cached;

        var directoryUser = await db.DirectoryUsers
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.TenantId == tenantId && u.Email.ToLower() == email,
                ct);

        if (directoryUser is null && _autoProvisionDirectoryFromJwt)
            directoryUser = await jwtProvisioner.TryProvisionAsync(principal, tenantId, ct);

        if (directoryUser is not null)
            await cache.SetAsync(cacheKey, directoryUser, CacheTtl, ct);

        return directoryUser;
    }
}
