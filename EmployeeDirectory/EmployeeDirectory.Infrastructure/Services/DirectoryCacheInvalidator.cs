using Kyntus.Caching;

namespace EmployeeDirectory.Infrastructure.Services;

public interface IDirectoryCacheInvalidator
{
    Task InvalidateOrgAsync(CancellationToken ct = default);
    Task InvalidateEmployeeAsync(Guid employeeId, CancellationToken ct = default);
}

public sealed class DirectoryCacheInvalidator(ICacheService cache) : IDirectoryCacheInvalidator
{
    /// <summary>Tailles de page utilisées par DirectoryReadService (défaut / max).</summary>
    private static readonly int[] EmployeePageSizes = [200, 500];

    public async Task InvalidateOrgAsync(CancellationToken ct = default)
    {
        await cache.RemoveAsync(KyntusCacheKeys.DirectoryOrgOverview(), ct);
        await cache.RemoveByPrefixAsync("directory:employees:", ct);

        // Clés Redis exactes (RemoveByPrefix ne scanne pas Redis) — pages employés courantes.
        foreach (var take in EmployeePageSizes)
        {
            await cache.RemoveAsync($"{KyntusCacheKeys.DirectoryEmployees(null, null)}:p:0:{take}", ct);
        }
    }

    public async Task InvalidateEmployeeAsync(Guid employeeId, CancellationToken ct = default)
    {
        await cache.RemoveAsync(KyntusCacheKeys.DirectoryEmployee(employeeId), ct);
        await InvalidateOrgAsync(ct);
    }
}
