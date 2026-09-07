using Auth.Domain.Entities;
using Auth.Domain.Interfaces;
using Auth.Infrastructure.Persistence;
using Kyntus.Caching;
using Microsoft.EntityFrameworkCore;

namespace Auth.Infrastructure.Repositories;

public class RoleRepository : IRoleRepository
{
    private readonly AuthDbContext _context;
    private readonly ICacheService _cache;

    public RoleRepository(AuthDbContext context, ICacheService cache)
    {
        _context = context;
        _cache = cache;
    }

    public async Task<Role?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var catalog = await GetCatalogAsync(ct);
        return catalog.FirstOrDefault(r => r.Id == id);
    }

    public async Task<Role?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        var catalog = await GetCatalogAsync(ct);
        return catalog.FirstOrDefault(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private Task<List<Role>> GetCatalogAsync(CancellationToken ct) =>
        _cache.GetOrCreateAsync(
            KyntusCacheKeys.AuthRoleCatalog(),
            async token => await _context.Roles
                .AsNoTracking()
                .Select(r => new Role
                {
                    Id = r.Id,
                    Name = r.Name,
                    Description = r.Description,
                    CreatedAt = r.CreatedAt,
                })
                .ToListAsync(token),
            KyntusCacheTtl.Referential,
            ct);
}
