using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Infrastructure.Persistence;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services;

public sealed class PlanningOrgMirrorService(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<PlanningOrgMirrorService> logger) : IPlanningOrgMirrorService
{
    public async Task<int> SyncFromDirectoryOverviewAsync(string? authorizationHeader, CancellationToken ct = default)
    {
        var overview = await FetchDirectoryOverviewAsync(authorizationHeader, ct);
        if (overview is null)
            return 0;

        var orgActions = await SyncFromPrimeTreeAsync(MapOverviewToMirrorPoles(overview), ct);
        var employeeActions = await SyncEmployeeSubServicesFromOverviewAsync(overview, ct);
        return orgActions + employeeActions;
    }

    public async Task<int> SyncEmployeeSubServicesFromDirectoryOverviewAsync(
        string? authorizationHeader,
        CancellationToken ct = default)
    {
        var overview = await FetchDirectoryOverviewAsync(authorizationHeader, ct);
        return overview is null ? 0 : await SyncEmployeeSubServicesFromOverviewAsync(overview, ct);
    }

    public Task<EmployeeImportOrgOverview?> GetDirectoryOverviewAsync(
        string? authorizationHeader,
        CancellationToken ct = default) =>
        FetchDirectoryOverviewAsync(authorizationHeader, ct);

    private async Task<EmployeeImportOrgOverview?> FetchDirectoryOverviewAsync(
        string? authorizationHeader,
        CancellationToken ct)
    {
        var baseUrl = (configuration["Directory:BaseUrl"] ?? "http://employee-directory-backend:8080/").TrimEnd('/') + "/";
        using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(15) };

        using var request = new HttpRequestMessage(HttpMethod.Get, "api/directory/org/overview");
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);

        var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            logger.LogWarning("Directory overview HTTP {Status}: {Body}", response.StatusCode, body);
            return null;
        }

        return await response.Content.ReadFromJsonAsync<EmployeeImportOrgOverview>(cancellationToken: ct);
    }

    /// <summary>
    /// Aligne User.SubServiceId sur l'affectation feuille Directory (PrimeServiceId).
    /// </summary>
    private async Task<int> SyncEmployeeSubServicesFromOverviewAsync(
        EmployeeImportOrgOverview overview,
        CancellationToken ct)
    {
        var primeToSub = await db.SubServices
            .AsNoTracking()
            .Where(s => s.PrimeServiceId != null && s.PrimeServiceId != "")
            .ToDictionaryAsync(s => s.PrimeServiceId!, s => s.Id, StringComparer.OrdinalIgnoreCase, ct);

        if (primeToSub.Count == 0)
            return 0;

        var guidToPrimeService = new Dictionary<Guid, string>();

        foreach (var emp in overview.Employees)
        {
            var primeId = emp.ServiceId?.Trim();
            if (string.IsNullOrEmpty(primeId) || !Guid.TryParse(emp.Id, out var guid))
                continue;
            guidToPrimeService[guid] = primeId;
        }

        foreach (var coach in overview.CoachSousService)
        {
            var primeId = (coach.SousServiceId ?? coach.ServiceId)?.Trim();
            if (string.IsNullOrEmpty(primeId) || !Guid.TryParse(coach.UserId, out var guid))
                continue;
            guidToPrimeService[guid] = primeId;
        }

        var updated = 0;
        foreach (var (guid, primeId) in guidToPrimeService)
        {
            if (!primeToSub.TryGetValue(primeId, out var subId))
                continue;

            var user = await db.Users.FirstOrDefaultAsync(u => u.Guid == guid, ct);
            if (user is null || user.SubServiceId == subId)
                continue;

            user.SubServiceId = subId;
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("SubServiceId Directory backfill : {Count} employé(s).", updated);
        }

        return updated;
    }

    private static List<PrimeOrgPoleMirrorDto> MapOverviewToMirrorPoles(EmployeeImportOrgOverview overview)
    {
        var poles = new List<PrimeOrgPoleMirrorDto>();
        foreach (var etage in overview.Etages)
        {
            if (string.IsNullOrWhiteSpace(etage.Id)) continue;

            var cellules = overview.Services
                .Where(s => string.Equals(s.EtageId, etage.Id, StringComparison.OrdinalIgnoreCase))
                .Select(cellule => new PrimeOrgCelluleMirrorDto
                {
                    Id = cellule.Id,
                    Name = cellule.Name,
                    Services = overview.SousServices
                        .Where(ss => string.Equals(ss.ServiceId, cellule.Id, StringComparison.OrdinalIgnoreCase))
                        .Select(ss => new PrimeOrgLeafServiceMirrorDto { Id = ss.Id, Name = ss.Name })
                        .ToList(),
                })
                .ToList();

            poles.Add(new PrimeOrgPoleMirrorDto
            {
                Id = etage.Id,
                Name = etage.Name,
                Cellules = cellules,
            });
        }

        return poles;
    }

    public async Task<int> SyncFromPrimeTreeAsync(IReadOnlyList<PrimeOrgPoleMirrorDto> poles, CancellationToken ct = default)
    {
        var actions = 0;

        foreach (var pole in poles)
        {
            if (string.IsNullOrWhiteSpace(pole.Id)) continue;

            // Correspondance par identifiant d'abord ; par nom seulement parmi les étages non archivés.
            var floor = await db.Floors.FirstOrDefaultAsync(f => f.PrimePoleId == pole.Id, ct)
                ?? await db.Floors.FirstOrDefaultAsync(f => !f.IsArchived && f.Name == pole.Name, ct);
            if (floor is null)
            {
                floor = new Floor
                {
                    Name = pole.Name,
                    FloorNumber = await db.Floors.CountAsync(ct) + 1,
                    PrimePoleId = pole.Id,
                };
                db.Floors.Add(floor);
                actions++;
            }
            else if (floor.PrimePoleId != pole.Id)
            {
                floor.PrimePoleId = pole.Id;
                actions++;
            }

            await db.SaveChangesAsync(ct);

            foreach (var cellule in pole.Cellules)
            {
                if (string.IsNullOrWhiteSpace(cellule.Id)) continue;

                var service = await db.Services.FirstOrDefaultAsync(s => s.PrimeCelluleId == cellule.Id, ct)
                    ?? await db.Services.FirstOrDefaultAsync(
                        s => !s.IsArchived && s.FloorId == floor.Id && s.Name == cellule.Name, ct);
                if (service is null)
                {
                    service = new Service
                    {
                        FloorId = floor.Id,
                        Name = cellule.Name,
                        Code = PlanningOrgMirrorCodes.ForCellule(cellule.Id),
                        PrimeCelluleId = cellule.Id,
                    };
                    db.Services.Add(service);
                    actions++;
                }
                else
                {
                    if (service.PrimeCelluleId != cellule.Id)
                    {
                        service.PrimeCelluleId = cellule.Id;
                        actions++;
                    }
                    if (service.FloorId != floor.Id)
                    {
                        service.FloorId = floor.Id;
                        actions++;
                    }
                }

                await db.SaveChangesAsync(ct);

                foreach (var leaf in cellule.Services)
                {
                    if (string.IsNullOrWhiteSpace(leaf.Id)) continue;

                    var sub = await db.SubServices.FirstOrDefaultAsync(s => s.PrimeServiceId == leaf.Id, ct)
                        ?? await db.SubServices.FirstOrDefaultAsync(
                            s => !s.IsArchived && s.ServiceId == service.Id && s.Name == leaf.Name, ct);
                    if (sub is null)
                    {
                        sub = new SubService
                        {
                            ServiceId = service.Id,
                            Name = leaf.Name,
                            Code = PlanningOrgMirrorCodes.ForLeafService(leaf.Id),
                            PrimeServiceId = leaf.Id,
                        };
                        db.SubServices.Add(sub);
                        actions++;
                    }
                    else
                    {
                        var changed = false;
                        if (sub.PrimeServiceId != leaf.Id)
                        {
                            sub.PrimeServiceId = leaf.Id;
                            changed = true;
                        }
                        if (sub.ServiceId != service.Id)
                        {
                            sub.ServiceId = service.Id;
                            changed = true;
                        }
                        if (changed) actions++;
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
        if (actions > 0)
            logger.LogInformation("Miroir org Planning : {Count} mise(s) à jour depuis Organisation RH.", actions);
        return actions;
    }
}
