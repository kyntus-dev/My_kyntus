using Kyntus.Caching;
using Kyntus.Iam;
using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Résout le périmètre Planning (sub-services) d'un manager depuis UserManagedServices / UserSubServices.
/// Pas de repli sur SubServiceId (appartenance ≠ responsabilité).
/// Réorganisation 2026 : un Chef de projet couvre en plus tous les sous-services des pôles
/// (Floor.PrimePoleId) qu'il détient dans Employee Directory — la projection ne créait
/// aucun lien Planning pour ce rôle, d'où un périmètre vide.
/// Interrupteur : Planning__ChefDeProjetPerimeterFromPoles=false.
/// </summary>
public interface IPlanningPerimeterResolver
{
    Task<HashSet<int>> GetManagedSubServiceIdsAsync(User manager, CancellationToken ct = default);
    Task<HashSet<int>> GetManagedSubServiceIdsAsync(int userId, CancellationToken ct = default);
}

public sealed class PlanningPerimeterResolver(
    AppDbContext db,
    ICacheService? cache = null,
    IRebacClient? rebac = null,
    IConfiguration? configuration = null,
    ILogger<PlanningPerimeterResolver>? logger = null) : IPlanningPerimeterResolver
{
    private bool ChefDeProjetPerimeterEnabled =>
        !bool.TryParse(configuration?["Planning:ChefDeProjetPerimeterFromPoles"], out var enabled) || enabled;

    public async Task<HashSet<int>> GetManagedSubServiceIdsAsync(User manager, CancellationToken ct = default)
    {
        if (cache is null)
            return await WithChefDeProjetPolesAsync(manager, (await LoadManagedIdsAsync(manager, ct)).ToHashSet(), ct);

        var cached = await cache.GetOrCreateAsync(
            KyntusCacheKeys.PlanningPerimeter(manager.Id),
            token => LoadManagedIdsAsync(manager, token),
            KyntusCacheTtl.Entity,
            ct);
        return await WithChefDeProjetPolesAsync(manager, cached.ToHashSet(), ct);
    }

    public async Task<HashSet<int>> GetManagedSubServiceIdsAsync(int userId, CancellationToken ct = default)
    {
        if (cache is null)
        {
            var user = await db.Users.AsNoTracking()
                .Include(u => u.ManagedSubServices)
                .Include(u => u.ManagedServices)
                .FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null) return [];
            return await WithChefDeProjetPolesAsync(user, (await LoadManagedIdsAsync(user, ct)).ToHashSet(), ct);
        }

        var cached = await cache.GetOrCreateAsync(
            KyntusCacheKeys.PlanningPerimeter(userId),
            async token =>
            {
                var user = await db.Users.AsNoTracking()
                    .Include(u => u.ManagedSubServices)
                    .Include(u => u.ManagedServices)
                    .FirstOrDefaultAsync(u => u.Id == userId, token);
                if (user is null) return [];
                return await LoadManagedIdsAsync(user, token);
            },
            KyntusCacheTtl.Entity,
            ct);
        var set = cached.ToHashSet();
        if (rebac is null || !ChefDeProjetPerimeterEnabled)
            return set;

        var owner = await db.Users.AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        return owner is null ? set : await WithChefDeProjetPolesAsync(owner, set, ct);
    }

    /// <summary>
    /// Ajoute au périmètre d'un Chef de projet les sous-services de ses pôles Directory.
    /// En cas d'échec de l'appel Directory (ex. tâche de fond sans JWT), renvoie le périmètre
    /// local inchangé : jamais d'élargissement non vérifié, jamais d'exception.
    /// </summary>
    private async Task<HashSet<int>> WithChefDeProjetPolesAsync(User user, HashSet<int> ids, CancellationToken ct)
    {
        if (rebac is null || !ChefDeProjetPerimeterEnabled || user.Guid == Guid.Empty)
            return ids;

        var roleName = user.Role?.Name
            ?? await db.Roles.AsNoTracking()
                .Where(r => r.Id == user.RoleId)
                .Select(r => r.Name)
                .FirstOrDefaultAsync(ct);
        if (!KyntusRoleNames.IsChefDeProjet(roleName))
            return ids;

        var cacheKey = $"{KyntusCacheKeys.PlanningPerimeter(user.Id)}:cp-poles";
        List<int>? fromPoles = null;
        if (cache is not null)
            fromPoles = await cache.GetAsync<List<int>>(cacheKey, ct);

        if (fromPoles is null)
        {
            try
            {
                var poleIds = (await rebac.GetManagedNodeIdsAsync(user.Guid, "ChefDeProjet", ct))
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Select(p => p.Trim())
                    .ToList();
                fromPoles = poleIds.Count == 0
                    ? []
                    : await db.SubServices.AsNoTracking()
                        .Where(ss => ss.Service != null
                                     && ss.Service.Floor != null
                                     && ss.Service.Floor.PrimePoleId != null
                                     && poleIds.Contains(ss.Service.Floor.PrimePoleId))
                        .Select(ss => ss.Id)
                        .ToListAsync(ct);
                if (cache is not null)
                    await cache.SetAsync(cacheKey, fromPoles, KyntusCacheTtl.Entity, ct);
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Périmètre Chef de projet (pôles Directory) indisponible pour l'utilisateur {UserId}", user.Id);
                return ids;
            }
        }

        ids.UnionWith(fromPoles);
        return ids;
    }

    private async Task<List<int>> LoadManagedIdsAsync(User manager, CancellationToken ct)
    {
        var subServiceIds = manager.ManagedSubServices?
            .Select(s => s.SubServiceId)
            .ToList() ?? [];

        var serviceIds = manager.ManagedServices?
            .Select(s => s.ServiceId)
            .ToList() ?? [];

        if (subServiceIds.Count == 0 && serviceIds.Count == 0)
        {
            subServiceIds = await db.UserSubServices.AsNoTracking()
                .Where(x => x.UserId == manager.Id)
                .Select(x => x.SubServiceId)
                .ToListAsync(ct);
            serviceIds = await db.UserManagedServices.AsNoTracking()
                .Where(x => x.UserId == manager.Id)
                .Select(x => x.ServiceId)
                .ToListAsync(ct);
        }

        if (serviceIds.Count > 0)
        {
            var fromServices = await db.SubServices.AsNoTracking()
                .Where(ss => serviceIds.Contains(ss.ServiceId))
                .Select(ss => ss.Id)
                .ToListAsync(ct);
            subServiceIds = subServiceIds.Union(fromServices).ToList();
        }

        return subServiceIds;
    }
}
