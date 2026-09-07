using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Conge.Application.Abstractions;
using Conge.Domain.Entities;
using Conge.Domain.Interfaces;
using Kyntus.Caching;
using Microsoft.Extensions.Logging;

namespace Conge.Infrastructure.Services;

/// <summary>
/// Catalogue org : overview Directory (HTTP + cache) enrichi par le miroir local <see cref="OrgNodeConge"/>.
/// </summary>
public sealed class DirectoryOrgCatalog(
    HttpClient http,
    IOrgNodeCongeRepository orgNodes,
    ICacheService cache,
    ILogger<DirectoryOrgCatalog> logger) : IDirectoryOrgCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task<DirectoryOrgCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default) =>
        cache.GetOrCreateAsync(
            KyntusCacheKeys.CongeOrgCatalog(),
            BuildSnapshotAsync,
            KyntusCacheTtl.Entity,
            ct);

    public async Task<string?> ResolveNodeNameAsync(string nodeId, CancellationToken ct = default)
    {
        var id = QuotaCongeService.NormalizeNodeId(nodeId);
        if (id is null) return null;
        var snap = await GetSnapshotAsync(ct);
        return snap.GetName(id);
    }

    public void InvalidateCache() =>
        _ = cache.RemoveAsync(KyntusCacheKeys.CongeOrgCatalog());

    private async Task<DirectoryOrgCatalogSnapshot> BuildSnapshotAsync(CancellationToken ct)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var celluleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var serviceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var serviceParents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Miroir local d’abord (offline / rename events).
        try
        {
            foreach (var node in await orgNodes.GetAllActiveAsync(ct))
            {
                if (!string.IsNullOrWhiteSpace(node.Name))
                    names[node.Id] = node.Name;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Conge org mirror read skipped");
        }

        // Overview Directory (noms + effectifs).
        try
        {
            using var resp = await http.GetAsync("api/directory/org/overview", ct);
            if (resp.IsSuccessStatusCode)
            {
                var overview = await resp.Content.ReadFromJsonAsync<DirectoryOrgOverviewDto>(JsonOptions, ct);
                if (overview is not null)
                    MergeOverview(overview, names, celluleCounts, serviceCounts, serviceParents);
            }
            else
            {
                logger.LogWarning("Directory org overview HTTP {Status}", (int)resp.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Directory org overview unavailable — using mirror names only");
        }

        return new DirectoryOrgCatalogSnapshot(names, celluleCounts, serviceCounts, serviceParents);
    }

    private static void MergeOverview(
        DirectoryOrgOverviewDto overview,
        Dictionary<string, string> names,
        Dictionary<string, int> celluleCounts,
        Dictionary<string, int> serviceCounts,
        Dictionary<string, string> serviceParents)
    {
        // Flat lists (legacy shape).
        foreach (var c in overview.Services ?? [])
        {
            if (!string.IsNullOrWhiteSpace(c.Id) && !string.IsNullOrWhiteSpace(c.Name))
                names[c.Id.Trim()] = c.Name.Trim();
        }

        foreach (var s in overview.SousServices ?? [])
        {
            if (!string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Name))
                names[s.Id.Trim()] = s.Name.Trim();
        }

        // Nested operational tree (preferred).
        foreach (var dept in overview.OperationalDepartments ?? [])
        {
            foreach (var pole in dept.Poles ?? [])
            {
                if (!string.IsNullOrWhiteSpace(pole.Id) && !string.IsNullOrWhiteSpace(pole.Name))
                    names[pole.Id.Trim()] = pole.Name.Trim();
                foreach (var cell in pole.Cellules ?? [])
                    RegisterCelluleServices(cell, names, serviceParents);
            }
        }

        foreach (var pole in overview.UnassignedPoles ?? [])
        {
            if (!string.IsNullOrWhiteSpace(pole.Id) && !string.IsNullOrWhiteSpace(pole.Name))
                names[pole.Id.Trim()] = pole.Name.Trim();
            foreach (var cell in pole.Cellules ?? [])
                RegisterCelluleServices(cell, names, serviceParents);
        }

        foreach (var emp in overview.Employees ?? [])
        {
            var celluleId = emp.CelluleId?.Trim();
            if (!string.IsNullOrWhiteSpace(celluleId))
            {
                celluleCounts.TryGetValue(celluleId, out var cc);
                celluleCounts[celluleId] = cc + 1;
            }

            var serviceId = emp.ServiceId?.Trim();
            if (!string.IsNullOrWhiteSpace(serviceId))
            {
                serviceCounts.TryGetValue(serviceId, out var sc);
                serviceCounts[serviceId] = sc + 1;
            }
        }
    }

    private static void RegisterCelluleServices(
        CelluleDto cell,
        Dictionary<string, string> names,
        Dictionary<string, string> serviceParents)
    {
        var cellId = cell.Id?.Trim();
        if (!string.IsNullOrWhiteSpace(cellId) && !string.IsNullOrWhiteSpace(cell.Name))
            names[cellId] = cell.Name.Trim();

        foreach (var svc in cell.Services ?? [])
        {
            var svcId = svc.Id?.Trim();
            if (string.IsNullOrWhiteSpace(svcId)) continue;
            if (!string.IsNullOrWhiteSpace(svc.Name))
                names[svcId] = svc.Name.Trim();
            if (!string.IsNullOrWhiteSpace(cellId))
                serviceParents[svcId] = cellId;
        }
    }

    // DTOs JSON Directory (subset).
    private sealed class DirectoryOrgOverviewDto
    {
        public List<FlatNodeDto>? Services { get; set; }
        public List<SousServiceDto>? SousServices { get; set; }
        public List<EmployeeDto>? Employees { get; set; }
        public List<OperationalDeptDto>? OperationalDepartments { get; set; }
        public List<PoleDto>? UnassignedPoles { get; set; }
    }

    private sealed class FlatNodeDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class SousServiceDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? ServiceId { get; set; }
    }

    private sealed class EmployeeDto
    {
        public string? Id { get; set; }
        public string? ServiceId { get; set; }
        public string? CelluleId { get; set; }
    }

    private sealed class OperationalDeptDto
    {
        public List<PoleDto>? Poles { get; set; }
    }

    private sealed class PoleDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public List<CelluleDto>? Cellules { get; set; }
    }

    private sealed class CelluleDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public List<FlatNodeDto>? Services { get; set; }
    }
}
