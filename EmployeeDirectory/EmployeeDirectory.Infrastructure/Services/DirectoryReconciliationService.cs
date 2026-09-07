using System.Net.Http.Json;
using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using Kyntus.Messaging.Contracts;
using Kyntus.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EmployeeDirectory.Infrastructure.Services;

public sealed class DirectoryReconciliationService(
    DirectoryDbContext db,
    IOutboxWriter outbox,
    IDirectoryWriteService write,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<DirectoryReconciliationService> logger) : IDirectoryReconciliationService
{
    public async Task<DirectoryReconcileVerifyDto> VerifyAsync(CancellationToken ct = default)
    {
        var local = await BuildLocalVerifyAsync(ct);
        var (planningUsers, primeCount, orphansPlanning, orphansPrime) = await FetchExternalCountsAsync(ct);
        var ok = local.EmployeesWithUnmappedOrgRefs == 0
                 && (orphansPlanning is null or 0)
                 && (orphansPrime is null or 0);

        return local with
        {
            PlanningActiveUsers = planningUsers,
            PrimeEmployeeCount = primeCount,
            OrphansInPlanningNotDirectory = orphansPlanning,
            OrphansInPrimeNotDirectory = orphansPrime,
            Ok = ok,
        };
    }

    public async Task<DirectoryReconcileReportDto> ReconcileAsync(CancellationToken ct = default)
    {
        var nodeDupesClosed = await write.DeduplicateActiveNodeIncumbentsAsync(null, ct);
        var merged = await DedupeEmployeesByEmailAsync(ct);
        var (orgBackfilled, employeesImported) = await SyncOrgAndEmployeesFromPrimeAsync(ct);
        var orgGapsFixed = await FixUnmappedOrgReferencesAsync(ct);
        var republished = await RepublishActiveEmployeesAsync(ct);

        var verify = await VerifyAsync(ct);
        var orphansPlanning = verify.OrphansInPlanningNotDirectory ?? 0;
        var orphansPrime = verify.OrphansInPrimeNotDirectory ?? 0;

        logger.LogInformation(
            "Directory reconcile: nodeDupesClosed={NodeDupes}, merged={Merged}, orgBackfilled={Org}, imported={Import}, republished={Republish}",
            nodeDupesClosed, merged, orgBackfilled, employeesImported, republished);

        return new DirectoryReconcileReportDto(
            merged,
            orgBackfilled,
            employeesImported,
            republished,
            orphansPlanning,
            orphansPrime,
            orgGapsFixed,
            verify,
            nodeDupesClosed);
    }

    private async Task<DirectoryReconcileVerifyDto> BuildLocalVerifyAsync(CancellationToken ct)
    {
        var active = await db.Employees.CountAsync(e => e.IsActive, ct);
        var inactive = await db.Employees.CountAsync(e => !e.IsActive, ct);
        var poles = await db.OrgPoles.CountAsync(ct);
        var cellules = await db.OrgCellules.CountAsync(ct);
        var services = await db.OrgServices.CountAsync(ct);
        var unmapped = await CountUnmappedOrgRefsAsync(ct);

        return new DirectoryReconcileVerifyDto(
            active,
            inactive,
            poles,
            cellules,
            services,
            unmapped,
            null,
            null,
            null,
            null,
            unmapped == 0);
    }

    private async Task<int> CountUnmappedOrgRefsAsync(CancellationToken ct)
    {
        var poleIds = await db.OrgPoles.AsNoTracking().Select(p => p.Id).ToListAsync(ct);
        var celluleIds = await db.OrgCellules.AsNoTracking().Select(c => c.Id).ToListAsync(ct);
        var serviceIds = await db.OrgServices.AsNoTracking().Select(s => s.Id).ToListAsync(ct);
        var poleSet = poleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var celluleSet = celluleIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var serviceSet = serviceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var employees = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive)
            .Select(e => new { e.PoleId, e.CelluleId, e.ServiceId })
            .ToListAsync(ct);

        var count = 0;
        foreach (var e in employees)
        {
            if (!string.IsNullOrWhiteSpace(e.PoleId) && !poleSet.Contains(e.PoleId)) count++;
            else if (!string.IsNullOrWhiteSpace(e.CelluleId) && !celluleSet.Contains(e.CelluleId)) count++;
            else if (!string.IsNullOrWhiteSpace(e.ServiceId) && !serviceSet.Contains(e.ServiceId)) count++;
        }

        return count;
    }

    private async Task<(int? planningUsers, int? primeCount, int? orphansPlanning, int? orphansPrime)> FetchExternalCountsAsync(
        CancellationToken ct)
    {
        var directoryEmails = await db.Employees.AsNoTracking()
            .Where(e => e.IsActive)
            .Select(e => e.Email.ToLower())
            .ToListAsync(ct);
        var directorySet = directoryEmails.ToHashSet(StringComparer.OrdinalIgnoreCase);

        int? planningUsers = null;
        int? orphansPlanning = null;
        try
        {
            var client = CreateClient("Planning");
            var verify = await client.GetFromJsonAsync<PlanningVerifyJson>(
                "api/admin/org-reconciliation/verify", ct);
            planningUsers = verify?.ActiveUsers;

            var users = await client.GetFromJsonAsync<List<PlanningUserJson>>("api/users", ct) ?? [];
            orphansPlanning = users.Count(u => u.IsActive && !directorySet.Contains(u.Email.Trim().ToLower()));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Directory verify: Planning unreachable");
        }

        int? primeCount = null;
        int? orphansPrime = null;
        try
        {
            var client = CreateClient("Prime");
            var employees = await client.GetFromJsonAsync<List<PrimeEmployeeJson>>("api/prime/employees", ct) ?? [];
            primeCount = employees.Count;
            orphansPrime = employees.Count(e =>
                !string.IsNullOrWhiteSpace(e.Email)
                && !directorySet.Contains(e.Email.Trim().ToLower()));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Directory verify: Prime unreachable");
        }

        return (planningUsers, primeCount, orphansPlanning, orphansPrime);
    }

    private async Task<int> DedupeEmployeesByEmailAsync(CancellationToken ct)
    {
        var groups = await db.Employees
            .GroupBy(e => e.Email.ToLower())
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToListAsync(ct);

        var merged = 0;
        foreach (var emailKey in groups)
        {
            var duplicates = await db.Employees
                .Where(e => e.Email.ToLower() == emailKey)
                .OrderByDescending(e => e.UpdatedAt ?? e.CreatedAt)
                .ToListAsync(ct);

            foreach (var dup in duplicates.Skip(1))
            {
                dup.IsActive = false;
                dup.UpdatedAt = DateTime.UtcNow;
                await EnqueueEmployeeChangedAsync(dup, isDeleted: true, ct);
                merged++;
            }
        }

        if (merged > 0)
            await db.SaveChangesAsync(ct);

        return merged;
    }

    private async Task<(int orgBackfilled, int employeesImported)> SyncOrgAndEmployeesFromPrimeAsync(CancellationToken ct)
    {
        var orgBackfilled = 0;
        var employeesImported = 0;

        try
        {
            var client = CreateClient("Prime");
            var departments = await client.GetFromJsonAsync<List<PrimeDepartmentJson>>("api/prime/departments", ct) ?? [];
            foreach (var dept in departments)
            {
                if (await UpsertPoleAsync(dept.Id, dept.Name, ct)) orgBackfilled++;
                foreach (var cellule in dept.Poles ?? [])
                {
                    if (await UpsertCelluleAsync(cellule.Id, cellule.Name, dept.Id, ct)) orgBackfilled++;
                    foreach (var service in cellule.Cells ?? [])
                    {
                        if (await UpsertServiceAsync(service.Id, service.Name, cellule.Id, ct)) orgBackfilled++;
                    }
                }
            }

            var primeEmployees = await client.GetFromJsonAsync<List<PrimeEmployeeJson>>("api/prime/employees", ct) ?? [];
            foreach (var pe in primeEmployees)
            {
                if (string.IsNullOrWhiteSpace(pe.Email)) continue;
                var exists = await db.Employees.AnyAsync(
                    e => e.Email.ToLower() == pe.Email.Trim().ToLower(), ct);
                if (exists) continue;

                if (!Guid.TryParse(pe.Id, out var id)) id = Guid.NewGuid();
                db.Employees.Add(new Employee
                {
                    Id = id,
                    Email = pe.Email.Trim(),
                    FirstName = pe.FirstName?.Trim() ?? "",
                    LastName = pe.LastName?.Trim() ?? "",
                    Role = KyntusRoleNames.NormalizePlanningRole(pe.Role ?? KyntusRoleNames.Employee),
                    ServiceId = pe.ServiceId,
                    CelluleId = pe.CelluleId,
                    PoleId = pe.PoleId,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                });
                employeesImported++;
            }

            if (orgBackfilled > 0 || employeesImported > 0)
                await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Directory reconcile: Prime sync failed");
        }

        return (orgBackfilled, employeesImported);
    }

    private async Task<int> FixUnmappedOrgReferencesAsync(CancellationToken ct)
    {
        var fixedCount = 0;
        var employees = await db.Employees.Where(e => e.IsActive).ToListAsync(ct);
        foreach (var e in employees)
        {
            if (string.IsNullOrWhiteSpace(e.ServiceId)) continue;
            var svc = await db.OrgServices.AsNoTracking().Include(s => s.Cellule)
                .FirstOrDefaultAsync(s => s.Id == e.ServiceId, ct);
            if (svc is null) continue;

            var changed = false;
            if (e.CelluleId != svc.CelluleId) { e.CelluleId = svc.CelluleId; changed = true; }
            if (e.PoleId != svc.Cellule.PoleId) { e.PoleId = svc.Cellule.PoleId; changed = true; }
            if (!changed) continue;

            e.UpdatedAt = DateTime.UtcNow;
            await EnqueueEmployeeChangedAsync(e, isDeleted: false, ct);
            fixedCount++;
        }

        if (fixedCount > 0)
            await db.SaveChangesAsync(ct);

        return fixedCount;
    }

    private async Task<int> RepublishActiveEmployeesAsync(CancellationToken ct)
    {
        var employees = await db.Employees.Where(e => e.IsActive).ToListAsync(ct);
        foreach (var e in employees)
            await EnqueueEmployeeChangedAsync(e, isDeleted: false, ct);

        if (employees.Count > 0)
            await db.SaveChangesAsync(ct);

        return employees.Count;
    }

    private async Task<bool> UpsertPoleAsync(string id, string name, CancellationToken ct)
    {
        if (await db.OrgPoles.AnyAsync(p => p.Id == id, ct)) return false;
        db.OrgPoles.Add(new OrgPole { Id = id.Trim(), Name = name.Trim() });
        return true;
    }

    private async Task<bool> UpsertCelluleAsync(string id, string name, string poleId, CancellationToken ct)
    {
        if (await db.OrgCellules.AnyAsync(c => c.Id == id, ct)) return false;
        db.OrgCellules.Add(new OrgCellule { Id = id.Trim(), Name = name.Trim(), PoleId = poleId.Trim() });
        return true;
    }

    private async Task<bool> UpsertServiceAsync(string id, string name, string celluleId, CancellationToken ct)
    {
        if (await db.OrgServices.AnyAsync(s => s.Id == id, ct)) return false;
        db.OrgServices.Add(new OrgService { Id = id.Trim(), Name = name.Trim(), CelluleId = celluleId.Trim() });
        return true;
    }

    private async Task EnqueueEmployeeChangedAsync(Employee employee, bool isDeleted, CancellationToken ct)
    {
        await outbox.EnqueueAsync(new DirectoryEmployeeChangedMessage
        {
            EmployeeId = employee.Id,
            Email = employee.Email,
            FirstName = employee.FirstName,
            LastName = employee.LastName,
            Role = employee.Role,
            ParentId = employee.ParentId,
            ServiceId = employee.ServiceId,
            CelluleId = employee.CelluleId,
            PoleId = employee.PoleId,
            IsActive = employee.IsActive,
            IsDeleted = isDeleted,
            IdTechnicien = employee.IdTechnicien,
            HtelCode = employee.HtelCode,
            HtelTechnicienName = employee.HtelTechnicienName,
        }, aggregateId: employee.Id.ToString(), ct: ct);
    }

    private HttpClient CreateClient(string service)
    {
        var key = service switch
        {
            "Prime" => "Prime:BaseUrl",
            "Planning" => "Planning:BaseUrl",
            _ => $"{service}:BaseUrl",
        };
        var defaultUrl = service switch
        {
            "Prime" => "http://prime-backend:8080/",
            "Planning" => "http://planning-backend:8080/",
            _ => "http://localhost:8080/",
        };
        var baseUrl = configuration[key]?.Trim() ?? defaultUrl;
        if (!baseUrl.EndsWith('/')) baseUrl += "/";

        var client = httpClientFactory.CreateClient("DirectoryReconcile");
        client.BaseAddress = new Uri(baseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        return client;
    }

    private sealed class PlanningVerifyJson
    {
        public int ActiveUsers { get; set; }
    }

    private sealed class PlanningUserJson
    {
        public string Email { get; set; } = "";
        public bool IsActive { get; set; }
    }

    private sealed class PrimeDepartmentJson
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<PrimePoleJson>? Poles { get; set; }
    }

    private sealed class PrimePoleJson
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<PrimeCellJson>? Cells { get; set; }
    }

    private sealed class PrimeCellJson
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<PrimeServiceJson>? Services { get; set; }
    }

    private sealed class PrimeServiceJson
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    private sealed class PrimeEmployeeJson
    {
        public string Id { get; set; } = "";
        public string Email { get; set; } = "";
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Role { get; set; }
        public string? ServiceId { get; set; }
        public string? CelluleId { get; set; }
        public string? PoleId { get; set; }
    }
}
