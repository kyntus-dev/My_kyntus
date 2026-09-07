using Kyntus.Caching;
using Microsoft.EntityFrameworkCore;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Résout le périmètre Planning (sub-services) d'un manager depuis UserManagedServices / UserSubServices.
/// Pas de repli sur SubServiceId (appartenance ≠ responsabilité).
/// </summary>
public interface IPlanningPerimeterResolver
{
    Task<HashSet<int>> GetManagedSubServiceIdsAsync(User manager, CancellationToken ct = default);
    Task<HashSet<int>> GetManagedSubServiceIdsAsync(int userId, CancellationToken ct = default);
}

public sealed class PlanningPerimeterResolver(AppDbContext db, ICacheService? cache = null) : IPlanningPerimeterResolver
{
    public async Task<HashSet<int>> GetManagedSubServiceIdsAsync(User manager, CancellationToken ct = default)
    {
        if (cache is null)
            return (await LoadManagedIdsAsync(manager, ct)).ToHashSet();

        var cached = await cache.GetOrCreateAsync(
            KyntusCacheKeys.PlanningPerimeter(manager.Id),
            token => LoadManagedIdsAsync(manager, token),
            KyntusCacheTtl.Entity,
            ct);
        return cached.ToHashSet();
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
            return (await LoadManagedIdsAsync(user, ct)).ToHashSet();
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
        return cached.ToHashSet();
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
