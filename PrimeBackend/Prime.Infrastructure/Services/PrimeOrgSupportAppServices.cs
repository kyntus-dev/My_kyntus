using Microsoft.EntityFrameworkCore;
using Prime.Application;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Domain.Entities;
using Prime.Infrastructure.Persistence;

namespace Prime.Infrastructure.Services;

public sealed class ServicePrimeIndicatorsAppService(PrimeDbContext db, PrimeOrgScopeService org)
    : IServicePrimeIndicatorsAppService
{
    private static ServicePrimeIndicatorDto Map(ServicePrimeIndicatorEntity e) =>
        new()
        {
            Id = e.Id,
            ServiceId = e.ServiceId,
            SortOrder = e.SortOrder,
            Label = e.Label,
            PonderationPrimePct = e.PonderationPrimePct,
            PonderationChallengePct = e.PonderationChallengePct,
            IsActive = e.IsActive,
            TemplateStableId = e.TemplateStableId,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt,
        };

    public async Task<IReadOnlyList<ServicePrimeIndicatorDto>> GetAsync(
        string serviceId,
        string supervisorUserId,
        string? employeeId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(supervisorUserId))
            throw new ArgumentException("supervisorUserId requis.");

        var celluleId = await org.GetCelluleIdForServiceAsync(serviceId, ct)
            ?? throw new KeyNotFoundException("Cellule introuvable.");
        if (!await org.SupervisorOwnsCelluleAsync(supervisorUserId, celluleId, ct))
            throw new UnauthorizedAccessException("Accès refusé pour ce périmètre.");

        var cid = serviceId.Trim();
        var list = await db.ServicePrimeIndicators.AsNoTracking()
            .Where(x => x.ServiceId == cid)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);
        var mapped = list.ConvertAll(Map);

        var empId = (employeeId ?? "").Trim();
        if (string.IsNullOrEmpty(empId) || mapped.Count == 0)
            return mapped;

        var ids = mapped.Select(x => x.Id).ToList();
        var overlays = await db.EmployeeServiceIndicatorPonderations.AsNoTracking()
            .Where(x => x.EmployeeId == empId && ids.Contains(x.ServicePrimeIndicatorId))
            .ToListAsync(ct);
        if (overlays.Count == 0)
            return mapped;

        var byIndicator = overlays.ToDictionary(x => x.ServicePrimeIndicatorId);
        return mapped.ConvertAll(dto =>
            byIndicator.TryGetValue(dto.Id, out var ov)
                ? EmployeeIndicatorPonderationResolver.Apply(dto, ov.PonderationPrimePct, ov.PonderationChallengePct)
                : dto);
    }

    public async Task<IReadOnlyList<ServicePrimeIndicatorDto>> PutAsync(
        string serviceId,
        string supervisorUserId,
        PutServicePrimeIndicatorsRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(supervisorUserId))
            throw new ArgumentException("supervisorUserId requis.");

        var celluleId = await org.GetCelluleIdForServiceAsync(serviceId, ct)
            ?? throw new KeyNotFoundException("Cellule introuvable.");
        if (!await org.SupervisorOwnsCelluleAsync(supervisorUserId, celluleId, ct))
            throw new UnauthorizedAccessException("Accès refusé pour ce périmètre.");

        var cid = serviceId.Trim();
        var now = DateTimeOffset.UtcNow;
        var existing = await db.ServicePrimeIndicators.Where(x => x.ServiceId == cid).ToListAsync(ct);
        var byId = existing.ToDictionary(x => x.Id);
        var keepIds = new HashSet<Guid>();

        foreach (var item in body.Indicators.OrderBy(i => i.SortOrder))
        {
            ServicePrimeIndicatorEntity row;
            if (item.Id is { } id && byId.TryGetValue(id, out var found))
            {
                row = found;
                keepIds.Add(id);
            }
            else
            {
                row = new ServicePrimeIndicatorEntity
                {
                    Id = Guid.NewGuid(),
                    ServiceId = cid,
                    CreatedAt = now,
                };
                db.ServicePrimeIndicators.Add(row);
            }

            row.SortOrder = item.SortOrder;
            row.Label = (item.Label ?? "").Trim();
            row.PonderationPrimePct = item.PonderationPrimePct;
            row.PonderationChallengePct = item.PonderationChallengePct;
            row.IsActive = item.IsActive;
            row.TemplateStableId = string.IsNullOrWhiteSpace(item.TemplateStableId) ? null : item.TemplateStableId.Trim();
            row.UpdatedAt = now;
        }

        var toRemove = existing.Where(x => !keepIds.Contains(x.Id)).ToList();
        if (toRemove.Count > 0)
            db.ServicePrimeIndicators.RemoveRange(toRemove);

        await db.SaveChangesAsync(ct);
        var list = await db.ServicePrimeIndicators.AsNoTracking()
            .Where(x => x.ServiceId == cid)
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);
        return list.ConvertAll(Map);
    }

    public async Task<ServicePrimeIndicatorAgentOverridesResponse> GetAgentOverridesAsync(
        string serviceId,
        string supervisorUserId,
        CancellationToken ct = default)
    {
        await EnsureSupervisorOwnsServiceAsync(serviceId, supervisorUserId, ct);
        return await LoadAgentOverridesAsync(serviceId.Trim(), ct);
    }

    public async Task<ServicePrimeIndicatorAgentOverridesResponse> PutAgentOverridesAsync(
        string serviceId,
        string supervisorUserId,
        PutServicePrimeIndicatorAgentOverridesRequest body,
        CancellationToken ct = default)
    {
        await EnsureSupervisorOwnsServiceAsync(serviceId, supervisorUserId, ct);

        var cid = serviceId.Trim();
        var indicators = await db.ServicePrimeIndicators.AsNoTracking()
            .Where(x => x.ServiceId == cid)
            .Select(x => x.Id)
            .ToListAsync(ct);
        var indicatorSet = indicators.ToHashSet();
        var pilots = await org.GetPilotsInServiceAsync(cid, ct);
        var pilotIds = pilots.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);

        var existing = await db.EmployeeServiceIndicatorPonderations
            .Where(x => indicatorSet.Contains(x.ServicePrimeIndicatorId))
            .ToListAsync(ct);
        db.EmployeeServiceIndicatorPonderations.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        var seen = new HashSet<(string Emp, Guid Ind)>();
        foreach (var item in body.Items ?? [])
        {
            var empId = (item.EmployeeId ?? "").Trim();
            if (string.IsNullOrEmpty(empId) || !pilotIds.Contains(empId)) continue;
            if (!indicatorSet.Contains(item.IndicatorId)) continue;
            if (!EmployeeIndicatorPonderationResolver.HasOverride(item.PonderationPrimePct, item.PonderationChallengePct))
                continue;
            if (!seen.Add((empId, item.IndicatorId))) continue;

            db.EmployeeServiceIndicatorPonderations.Add(new EmployeeServiceIndicatorPonderationEntity
            {
                Id = Guid.NewGuid(),
                EmployeeId = empId,
                ServicePrimeIndicatorId = item.IndicatorId,
                PonderationPrimePct = item.PonderationPrimePct,
                PonderationChallengePct = item.PonderationChallengePct,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        return await LoadAgentOverridesAsync(cid, ct);
    }

    private async Task EnsureSupervisorOwnsServiceAsync(string serviceId, string supervisorUserId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(supervisorUserId))
            throw new ArgumentException("supervisorUserId requis.");
        var celluleId = await org.GetCelluleIdForServiceAsync(serviceId, ct)
            ?? throw new KeyNotFoundException("Cellule introuvable.");
        if (!await org.SupervisorOwnsCelluleAsync(supervisorUserId, celluleId, ct))
            throw new UnauthorizedAccessException("Accès refusé pour ce périmètre.");
    }

    private async Task<ServicePrimeIndicatorAgentOverridesResponse> LoadAgentOverridesAsync(
        string serviceId,
        CancellationToken ct)
    {
        var pilots = await org.GetPilotsInServiceAsync(serviceId, ct);
        var employees = pilots.ConvertAll(p => new ServicePrimeAgentDto
        {
            EmployeeId = p.Id,
            EmployeeName = $"{p.FirstName} {p.LastName}".Trim(),
        });

        var indicatorIds = await db.ServicePrimeIndicators.AsNoTracking()
            .Where(x => x.ServiceId == serviceId)
            .Select(x => x.Id)
            .ToListAsync(ct);
        if (indicatorIds.Count == 0)
            return new ServicePrimeIndicatorAgentOverridesResponse { Employees = employees };

        var names = employees.ToDictionary(e => e.EmployeeId, e => e.EmployeeName, StringComparer.Ordinal);
        var rows = await db.EmployeeServiceIndicatorPonderations.AsNoTracking()
            .Where(x => indicatorIds.Contains(x.ServicePrimeIndicatorId))
            .ToListAsync(ct);

        var overrides = rows.ConvertAll(x => new ServicePrimeIndicatorAgentOverrideDto
        {
            EmployeeId = x.EmployeeId,
            EmployeeName = names.GetValueOrDefault(x.EmployeeId) ?? x.EmployeeId,
            IndicatorId = x.ServicePrimeIndicatorId,
            PonderationPrimePct = x.PonderationPrimePct,
            PonderationChallengePct = x.PonderationChallengePct,
        });

        return new ServicePrimeIndicatorAgentOverridesResponse
        {
            Employees = employees,
            Overrides = overrides,
        };
    }
}

public sealed class PrimePeriodRecapReportsAppService(PrimeDbContext db) : IPrimePeriodRecapReportsAppService
{
    public async Task<FileExportResultDto> DownloadPeriodRecapAsync(
        string period,
        string actingUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(period))
            throw new ArgumentException("period est requis.");
        if (string.IsNullOrWhiteSpace(actingUserId))
            throw new ArgumentException("actingUserId est requis.");

        var uid = actingUserId.Trim();
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == uid, ct);
        var role = e?.Role;
        var allow = string.Equals(role, "Admin", StringComparison.Ordinal) ||
                    string.Equals(role, "RH", StringComparison.Ordinal) ||
                    string.Equals(role, "Manager", StringComparison.Ordinal) ||
                    string.Equals(role, "Comptable", StringComparison.Ordinal);
        if (!allow)
            throw new PrimeApiException(403, "Rôle non autorisé (Admin, RH, Manager ou Comptable).");

        var bytes = await PrimeGlobalRecapExcelBuilder.BuildAsync(db, period.Trim(), ct);
        return new FileExportResultDto(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"PRIME_synthese_globale_{period.Trim()}.xlsx");
    }
}

public sealed class PrimeFicheImportAppService(PrimeFicheImportService import) : IPrimeFicheImportAppService
{
    public Task<(bool Ok, string? Error, ImportReadyFicheResponseDto? Result)> ImportAsync(
        ImportReadyFicheRequest body,
        CancellationToken ct = default) =>
        import.ImportReadyFicheAsync(body, ct);

    public async Task<IReadOnlyList<PrimeHistoricalFicheListItemDto>> ListHistoricalAsync(
        string supervisorUserId,
        string? period,
        string? role,
        CancellationToken ct = default) =>
        await import.ListHistoricalAsync(supervisorUserId, period, role, ct);

    public Task<(bool Ok, string? Error, PrimeHistoricalFicheDetailSnapshotDto? Result)> GetHistoricalDetailSnapshotAsync(
        Guid id,
        string supervisorUserId,
        string? role,
        CancellationToken ct = default) =>
        import.GetHistoricalDetailSnapshotAsync(id, supervisorUserId, role, ct);
}

public sealed class PrimeCoreQueryAppService(PrimeDbContext? db, PrimeOrgScopeService org) : IPrimeCoreQueryAppService
{
    public async Task<PrimeHealthStatusDto> GetHealthAsync(CancellationToken ct = default)
    {
        if (db is null)
            return new PrimeHealthStatusDto("ok", "memory-only", null, null);
        try
        {
            var ok = await db.Database.CanConnectAsync(ct);
            return ok
                ? new PrimeHealthStatusDto("ok", null, "prime_db", null)
                : new PrimeHealthStatusDto("db-unreachable", null, null, null);
        }
        catch (Exception ex)
        {
            return new PrimeHealthStatusDto("db-error", null, null, ex.Message);
        }
    }

    public async Task<IReadOnlyList<Department>> GetLegacyDepartmentsAsync(CancellationToken ct) =>
        await org.GetLegacyDepartmentTreeAsync(ct);

    public Task<OperationalOrgTreeDto> GetOperationalDepartmentsAsync(CancellationToken ct) =>
        org.GetOperationalOrgTreeAsync(ct);

    public async Task<IReadOnlyList<Employee>> GetLegacyEmployeesAsync(CancellationToken ct) =>
        await org.GetLegacyEmployeesAsync(ct);

    public async Task<IReadOnlyList<PrimeResult>> GetPrimeResultsAsync(CancellationToken ct) =>
        await org.GetPrimeResultsFromFichesAsync(500, ct);

    public async Task<IReadOnlyList<PrimeResult>> GetMyPrimeResultsAsync(string employeeId, CancellationToken ct)
    {
        var list = await org.GetPrimeResultsFromFichesAsync(500, ct);
        return list.Where(r => r.EmployeeId == employeeId.Trim()).ToList();
    }

    public async Task<IReadOnlyList<PrimeType>> GetPrimeTypesAsync(CancellationToken ct)
    {
        var types = new List<PrimeType>
        {
            new()
            {
                Id = "fiche-service",
                Name = "Prime de service",
                Type = "Variable",
                PoleId = "",
                Status = "Active",
                Description = "Prime calculée sur les fiches service (indicateurs et pondérations).",
            },
        };
        if (db is null) return types;

        var allowances = await db.AllowanceTypes.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Label)
            .ToListAsync(ct);
        foreach (var a in allowances)
        {
            types.Add(new PrimeType
            {
                Id = a.Id.ToString("D"),
                Name = string.IsNullOrWhiteSpace(a.Label) ? a.Code : a.Label,
                Type = string.IsNullOrWhiteSpace(a.Category) ? "Allowance" : a.Category,
                PoleId = "",
                Status = "Active",
                Description = a.Code,
            });
        }
        return types;
    }

    public async Task<IReadOnlyList<PrimeRule>> GetPrimeRulesAsync(CancellationToken ct)
    {
        if (db is null) return [];

        var indicators = await db.ServicePrimeIndicators.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.ServiceId)
            .ThenBy(x => x.SortOrder)
            .ToListAsync(ct);

        return indicators.Select(x => new PrimeRule
        {
            Id = x.Id.ToString("D"),
            PrimeTypeId = "fiche-service",
            ServiceId = x.ServiceId,
            ConditionField = x.Label,
            ConditionType = ">=",
            TargetValue = 0,
            CalculationMethod = "Percentage",
            Amount = (int)Math.Clamp(x.PonderationPrimePct ?? 0, 0, int.MaxValue),
            Period = "Monthly",
        }).ToList();
    }

    public Task<object> GetDashboardStatsAsync(CancellationToken ct) =>
        org.BuildDashboardStatsAsync(ct);
}

public sealed class AllowanceQueryAppService(PrimeDbContext db, AllowanceScopeService scope) : IAllowanceQueryAppService
{
    public async Task<IReadOnlyList<BusinessDepartmentMirrorDto>> ListBusinessDepartmentsAsync(CancellationToken ct = default)
    {
        var rows = await db.BusinessDepartments.AsNoTracking()
            .Include(d => d.PoleAssignments)
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);
        return rows.Select(d => new BusinessDepartmentMirrorDto(
            d.Id, d.Code, d.Name, d.Kind, d.ManagerEmployeeId, d.IsActive,
            d.PoleAssignments.Select(p => p.PoleId).ToList())).ToList();
    }

    public async Task<AllowanceUserContextDto> GetMyContextAsync(string userId, string role, CancellationToken ct = default)
    {
        var emp = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == userId, ct);
        var managedDept = await db.BusinessDepartments.AsNoTracking()
            .FirstOrDefaultAsync(d => d.ManagerEmployeeId == userId && d.IsActive, ct);
        var managedIds = await scope.GetDirectReportIdsAsync(userId, ct);
        var directReportCount = managedIds.Count;
        return new AllowanceUserContextDto(
            userId,
            role,
            emp?.BusinessDepartmentId,
            emp?.BusinessDepartmentKind,
            managedDept?.Kind == "Support",
            managedDept?.Kind == "Operational",
            managedDept?.Id,
            managedDept?.Kind,
            managedDept?.Name,
            managedDept?.Code,
            directReportCount);
    }

    public async Task<IReadOnlyList<object>> GetTeamAsync(string userId, CancellationToken ct = default)
    {
        if (!await scope.IsSupportDepartmentManagerAsync(userId, ct))
            throw new UnauthorizedAccessException();
        var deptId = await scope.GetManagerDepartmentIdAsync(userId, ct);
        var managedIds = await scope.GetDirectReportIdsAsync(userId, ct);
        if (managedIds.Count == 0) return [];

        var query = db.Employees.AsNoTracking()
            .Where(e => managedIds.Contains(e.Id) && e.BusinessDepartmentKind == "Support");
        if (!string.IsNullOrWhiteSpace(deptId))
            query = query.Where(e => e.BusinessDepartmentId == deptId);
        var team = await query
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Select(e => new { id = e.Id, firstName = e.FirstName, lastName = e.LastName, email = e.Email })
            .ToListAsync(ct);
        return team.Cast<object>().ToList();
    }
}
