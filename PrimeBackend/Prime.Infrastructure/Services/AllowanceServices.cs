using Microsoft.EntityFrameworkCore;
using Prime.Application.Abstractions;
using Prime.Application.Allowance;
using Prime.Application.DTOs;
using Prime.Infrastructure.Persistence;

namespace Prime.Infrastructure.Services;

public sealed class AllowanceCatalogService(PrimeDbContext db)
{
    public async Task<List<AllowanceTypeDto>> ListTypesAsync(CancellationToken ct) =>
        await db.AllowanceTypes.AsNoTracking()
            .OrderBy(t => t.Category).ThenBy(t => t.Label)
            .Select(t => MapType(t))
            .ToListAsync(ct);

    public async Task<List<AllowanceTypeDto>> ListEligibleTypesAsync(string? businessDepartmentId, CancellationToken ct)
    {
        var q = db.AllowanceTypes.AsNoTracking().Where(t => t.IsActive);
        if (!string.IsNullOrWhiteSpace(businessDepartmentId))
        {
            q = q.Where(t =>
                !t.DepartmentLinks.Any()
                || t.DepartmentLinks.Any(l => l.BusinessDepartmentId == businessDepartmentId));
        }
        return await q.OrderBy(t => t.Category).ThenBy(t => t.Label).Select(t => MapType(t)).ToListAsync(ct);
    }

    public async Task<AllowanceTypeDto> CreateTypeAsync(CreateAllowanceTypeRequest req, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var entity = new AllowanceTypeEntity
        {
            Id = Guid.NewGuid(),
            Code = req.Code.Trim().ToUpperInvariant(),
            Label = req.Label.Trim(),
            Category = req.Category.Trim(),
            CalculationMode = req.CalculationMode ?? "Manual",
            DefaultAmount = req.DefaultAmount,
            MinAmount = req.MinAmount,
            MaxAmount = req.MaxAmount,
            RequiresJustification = req.RequiresJustification,
            ApplicableDepartmentKinds = req.ApplicableDepartmentKinds ?? "Support",
            IsActive = true,
            CreatedAt = now,
        };
        db.AllowanceTypes.Add(entity);
        await db.SaveChangesAsync(ct);
        return MapType(entity);
    }

    private static AllowanceTypeDto MapType(AllowanceTypeEntity t) => new(
        t.Id, t.Code, t.Label, t.Category, t.CalculationMode,
        t.DefaultAmount, t.MinAmount, t.MaxAmount, t.RequiresJustification,
        t.ApplicableDepartmentKinds, t.IsActive);
}

public sealed class AllowanceRequestService(
    PrimeDbContext db,
    AllowanceScopeService scope,
    PrimeAuditLogService audit)
{
    public async Task<List<AllowanceRequestDto>> ListAsync(
        string actorUserId, string actorRole, string? departmentId, string? period, CancellationToken ct)
    {
        var q = db.AllowanceRequests.AsNoTracking().Include(r => r.AllowanceType).AsQueryable();
        if (!string.IsNullOrWhiteSpace(period))
            q = q.Where(r => r.Period == period.Trim());
        if (!string.IsNullOrWhiteSpace(departmentId))
            q = q.Where(r => r.BusinessDepartmentId == departmentId.Trim());

        if (actorRole.Equals("Manager", StringComparison.OrdinalIgnoreCase))
        {
            var deptId = await scope.GetManagerDepartmentIdAsync(actorUserId, ct);
            if (deptId is null) return [];
            q = q.Where(r => r.BusinessDepartmentId == deptId);
        }
        else if (actorRole.Equals("Pilote", StringComparison.OrdinalIgnoreCase)
                 || actorRole.Equals("Employee", StringComparison.OrdinalIgnoreCase))
        {
            q = q.Where(r => r.EmployeeId == actorUserId);
        }

        var rows = await q.OrderByDescending(r => r.CreatedAt).Take(500).ToListAsync(ct);
        return rows.Select(MapRequest).ToList();
    }

    public async Task<List<AllowanceRequestDto>> InboxAsync(string actorUserId, string actorRole, CancellationToken ct)
    {
        var status = actorRole switch
        {
            var r when r.Equals("RH", StringComparison.OrdinalIgnoreCase) => AllowanceRequestStatuses.ManagerApproved,
            var r when r.Equals("Comptabilité", StringComparison.OrdinalIgnoreCase)
                      || r.Equals("Comptable", StringComparison.OrdinalIgnoreCase) => AllowanceRequestStatuses.RhApproved,
            _ => null,
        };
        if (status is null) return [];

        var rows = await db.AllowanceRequests.AsNoTracking().Include(r => r.AllowanceType)
            .Where(r => r.Status == status)
            .OrderBy(r => r.CreatedAt)
            .Take(200)
            .ToListAsync(ct);
        return rows.Select(MapRequest).ToList();
    }

    public async Task<AllowanceRequestDto> CreateAsync(
        string actorUserId, CreateAllowanceRequestBody body, CancellationToken ct)
    {
        if (!await scope.IsSupportDepartmentManagerAsync(actorUserId, ct))
            throw new UnauthorizedAccessException("Réservé au manager de département Support.");
        if (!await scope.CanManagerAccessEmployeeAsync(actorUserId, body.EmployeeId, ct))
            throw new UnauthorizedAccessException("Employé hors périmètre manager Support.");

        var emp = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == body.EmployeeId, ct);
        var type = await db.AllowanceTypes.FirstAsync(t => t.Id == body.AllowanceTypeId && t.IsActive, ct);
        await EnsureNoDuplicateRequestAsync(body.EmployeeId, body.AllowanceTypeId, body.Period.Trim(), null, ct);
        ValidateAmount(type, body.Amount, body.Reason);

        var now = DateTimeOffset.UtcNow;
        var entity = new AllowanceRequestEntity
        {
            Id = Guid.NewGuid(),
            EmployeeId = body.EmployeeId,
            BusinessDepartmentId = emp.BusinessDepartmentId ?? "",
            AllowanceTypeId = type.Id,
            Period = body.Period.Trim(),
            Amount = body.Amount,
            Currency = body.Currency ?? "MAD",
            Reason = body.Reason?.Trim() ?? "",
            Source = string.IsNullOrWhiteSpace(body.Source) ? "Manual" : body.Source.Trim(),
            Status = AllowanceRequestStatuses.Draft,
            CreatedByUserId = actorUserId,
            CreatedAt = now,
        };
        db.AllowanceRequests.Add(entity);
        await ClearNoBonusMarkerAsync(body.EmployeeId, body.Period.Trim(), ct);
        await db.SaveChangesAsync(ct);
        entity = await db.AllowanceRequests.Include(r => r.AllowanceType)
            .FirstAsync(r => r.Id == entity.Id, ct);
        return MapRequest(entity);
    }

    public async Task<AllowanceRequestDto> SubmitAsync(Guid id, string actorUserId, CancellationToken ct)
    {
        if (!await scope.IsSupportDepartmentManagerAsync(actorUserId, ct))
            throw new UnauthorizedAccessException("Réservé au manager de département Support.");

        var entity = await db.AllowanceRequests.Include(r => r.AllowanceType)
            .FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new KeyNotFoundException();
        if (entity.Status != AllowanceRequestStatuses.Draft)
            throw new InvalidOperationException("Seul un brouillon peut être soumis.");
        if (entity.CreatedByUserId != actorUserId)
            throw new UnauthorizedAccessException();

        ValidateAmount(entity.AllowanceType!, entity.Amount, entity.Reason);
        await TransitionAsync(entity, AllowanceRequestStatuses.ManagerApproved, actorUserId, "Manager", "Submitted", null, ct);
        entity.ManagerApprovedByUserId = actorUserId;
        entity.ManagerApprovedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return MapRequest(entity);
    }

    public async Task<AllowanceRequestDto> UpdateDraftAsync(
        Guid id, string actorUserId, UpdateAllowanceRequestBody body, CancellationToken ct)
    {
        if (!await scope.IsSupportDepartmentManagerAsync(actorUserId, ct))
            throw new UnauthorizedAccessException("Réservé au manager de département Support.");

        var entity = await db.AllowanceRequests.Include(r => r.AllowanceType)
            .FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new KeyNotFoundException();
        if (entity.Status != AllowanceRequestStatuses.Draft)
            throw new InvalidOperationException("Seule une demande en brouillon peut être modifiée.");
        if (entity.CreatedByUserId != actorUserId)
            throw new UnauthorizedAccessException();

        if (body.AllowanceTypeId.HasValue && body.AllowanceTypeId.Value != entity.AllowanceTypeId)
        {
            var type = await db.AllowanceTypes.FirstAsync(
                t => t.Id == body.AllowanceTypeId.Value && t.IsActive, ct);
            var period = !string.IsNullOrWhiteSpace(body.Period) ? body.Period.Trim() : entity.Period;
            await EnsureNoDuplicateRequestAsync(entity.EmployeeId, type.Id, period, entity.Id, ct);
            entity.AllowanceTypeId = type.Id;
            entity.AllowanceType = type;
        }

        if (!string.IsNullOrWhiteSpace(body.Period))
            entity.Period = body.Period.Trim();
        if (body.Amount.HasValue)
            entity.Amount = body.Amount.Value;
        if (body.Reason is not null)
            entity.Reason = body.Reason.Trim();

        ValidateAmount(entity.AllowanceType!, entity.Amount, entity.Reason);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return MapRequest(entity);
    }

    public async Task<AllowanceRequestDto> ApproveAsync(Guid id, string actorUserId, string actorRole, CancellationToken ct)
    {
        var entity = await db.AllowanceRequests.Include(r => r.AllowanceType)
            .FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new KeyNotFoundException();
        if (!AllowanceValidationRoles.CanActAtStatus(actorRole, entity.Status))
            throw new UnauthorizedAccessException("Rôle non autorisé pour cette étape.");

        var next = AllowanceValidationRoles.NextStatusAfterApproval(entity.Status);
        await TransitionAsync(entity, next, actorUserId, actorRole, "Approved", null, ct);

        if (next == AllowanceRequestStatuses.ManagerApproved)
        {
            entity.ManagerApprovedByUserId = actorUserId;
            entity.ManagerApprovedAt = DateTimeOffset.UtcNow;
        }
        else if (next == AllowanceRequestStatuses.RhApproved)
        {
            entity.RhApprovedByUserId = actorUserId;
            entity.RhApprovedAt = DateTimeOffset.UtcNow;
        }
        else if (next == AllowanceRequestStatuses.ComptaApproved)
        {
            entity.ComptaApprovedByUserId = actorUserId;
            entity.ComptaApprovedAt = DateTimeOffset.UtcNow;
        }
        else if (next == AllowanceRequestStatuses.Paid)
        {
            entity.PaidAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return MapRequest(entity);
    }

    public async Task<AllowanceRequestDto> RejectAsync(
        Guid id, string actorUserId, string actorRole, string reason, CancellationToken ct)
    {
        var entity = await db.AllowanceRequests.Include(r => r.AllowanceType)
            .FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw new KeyNotFoundException();
        if (!AllowanceValidationRoles.CanActAtStatus(actorRole, entity.Status))
            throw new UnauthorizedAccessException();

        entity.RejectionReason = reason.Trim();
        await TransitionAsync(entity, AllowanceRequestStatuses.Rejected, actorUserId, actorRole, "Rejected", reason, ct);
        return MapRequest(entity);
    }

    private async Task TransitionAsync(
        AllowanceRequestEntity entity, string toStatus, string actorUserId, string actorRole,
        string action, string? comment, CancellationToken ct)
    {
        var from = entity.Status;
        entity.Status = toStatus;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
        db.AllowanceRequestHistories.Add(new AllowanceRequestHistoryEntity
        {
            Id = Guid.NewGuid(),
            AllowanceRequestId = entity.Id,
            Action = action,
            FromStatus = from,
            ToStatus = toStatus,
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            Comment = comment,
            At = DateTimeOffset.UtcNow,
        });
        await audit.RecordAsync(actorUserId, actorUserId, actorRole, action, "AllowanceRequest",
            entity.Id.ToString(), $"{{\"from\":\"{from}\",\"to\":\"{toStatus}\"}}", ct);
        await db.SaveChangesAsync(ct);
    }

    private async Task ClearNoBonusMarkerAsync(string employeeId, string period, CancellationToken ct)
    {
        var marker = await db.AllowanceNoBonusMarkers
            .FirstOrDefaultAsync(m => m.EmployeeId == employeeId && m.Period == period, ct);
        if (marker is not null)
            db.AllowanceNoBonusMarkers.Remove(marker);
    }

    private async Task EnsureNoDuplicateRequestAsync(
        string employeeId, Guid allowanceTypeId, string period, Guid? excludeRequestId, CancellationToken ct)
    {
        var exists = await db.AllowanceRequests.AnyAsync(r =>
            r.EmployeeId == employeeId
            && r.AllowanceTypeId == allowanceTypeId
            && r.Period == period
            && r.Status != AllowanceRequestStatuses.Rejected
            && (excludeRequestId == null || r.Id != excludeRequestId.Value), ct);
        if (exists)
            throw new InvalidOperationException(
                "Une demande existe déjà pour ce collaborateur, ce type et cette période.");
    }

    private static void ValidateAmount(AllowanceTypeEntity type, decimal amount, string? reason)
    {
        if (type.MinAmount.HasValue && amount < type.MinAmount.Value)
            throw new InvalidOperationException($"Montant inférieur au minimum ({type.MinAmount}).");
        if (type.MaxAmount.HasValue && amount > type.MaxAmount.Value)
            throw new InvalidOperationException($"Montant supérieur au maximum ({type.MaxAmount}).");
        if (type.RequiresJustification && string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Motif obligatoire pour ce type de prime.");
    }

    internal static AllowanceRequestDto MapRequestPublic(AllowanceRequestEntity r) => MapRequest(r);

    private static AllowanceRequestDto MapRequest(AllowanceRequestEntity r) => new(
        r.Id, r.EmployeeId, r.BusinessDepartmentId, r.AllowanceTypeId,
        r.AllowanceType?.Code ?? "", r.AllowanceType?.Label ?? "",
        r.Period, r.Amount, r.Currency, r.Reason, r.Source, r.Status,
        r.CreatedByUserId, r.RejectionReason,
        r.ManagerApprovedAt, r.RhApprovedAt, r.ComptaApprovedAt, r.PaidAt, r.CreatedAt);
}

public sealed class AllowanceTeamPilotageService(
    PrimeDbContext db,
    AllowanceScopeService scope,
    AllowanceCatalogService catalog)
{
    public async Task<AllowanceTeamProgressDto> GetTeamProgressAsync(string managerUserId, string period, CancellationToken ct)
    {
        if (!await scope.IsSupportDepartmentManagerAsync(managerUserId, ct))
            throw new UnauthorizedAccessException("Réservé au manager de département Support.");

        period = period.Trim();
        var deptId = await scope.GetManagerDepartmentIdAsync(managerUserId, ct);
        var team = await LoadTeamMembersAsync(managerUserId, deptId, ct);
        var allRequests = await LoadDeptPeriodRequestsAsync(deptId, period, ct);
        var noBonusMarkers = await LoadNoBonusMarkersAsync(deptId, period, ct);
        var noBonusByEmployee = noBonusMarkers.ToDictionary(m => m.EmployeeId);

        var members = new List<AllowanceTeamMemberProgressDto>();
        var notStarted = 0;
        var inProgress = 0;
        var submitted = 0;
        var validated = 0;
        var noBonus = 0;
        decimal totalAmount = 0;

        foreach (var emp in team)
        {
            var empReqs = allRequests.Where(r => r.EmployeeId == emp.Id).ToList();
            noBonusByEmployee.TryGetValue(emp.Id, out var marker);
            var status = DeriveTreatmentStatus(empReqs, marker is not null);
            var draftCount = empReqs.Count(r => r.Status == AllowanceRequestStatuses.Draft);
            var submittedCount = empReqs.Count(r =>
                r.Status == AllowanceRequestStatuses.ManagerApproved
                || r.Status == AllowanceRequestStatuses.Submitted);

            totalAmount += empReqs.Sum(r => r.Amount);

            switch (status)
            {
                case "NotStarted": notStarted++; break;
                case "HasDrafts": inProgress++; break;
                case "Submitted": submitted++; break;
                case "Validated": validated++; break;
                case "NoBonus": noBonus++; break;
            }

            members.Add(new AllowanceTeamMemberProgressDto(
                emp.Id, emp.FirstName, emp.LastName, emp.Email,
                empReqs.Count, draftCount, submittedCount, status, marker is not null));
        }

        var summary = new AllowanceTeamProgressSummaryDto(
            team.Count, notStarted, inProgress, submitted, validated, noBonus, totalAmount);
        return new AllowanceTeamProgressDto(period, summary, members);
    }

    public async Task MarkNoBonusAsync(
        string managerUserId, string employeeId, string period, string? comment, CancellationToken ct)
    {
        if (!await scope.IsSupportDepartmentManagerAsync(managerUserId, ct))
            throw new UnauthorizedAccessException("Réservé au manager de département Support.");
        if (!await scope.CanManagerAccessEmployeeAsync(managerUserId, employeeId, ct))
            throw new UnauthorizedAccessException("Employé hors périmètre manager Support.");

        period = period.Trim();
        var hasActive = await db.AllowanceRequests.AnyAsync(r =>
            r.EmployeeId == employeeId && r.Period == period
            && r.Status != AllowanceRequestStatuses.Rejected, ct);
        if (hasActive)
            throw new InvalidOperationException("Impossible de marquer sans prime : des demandes existent déjà pour cette période.");

        var deptId = await scope.GetManagerDepartmentIdAsync(managerUserId, ct) ?? "";
        var existing = await db.AllowanceNoBonusMarkers
            .FirstOrDefaultAsync(m => m.EmployeeId == employeeId && m.Period == period, ct);
        if (existing is not null)
        {
            existing.Comment = comment?.Trim();
            existing.MarkedByUserId = managerUserId;
            existing.CreatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            db.AllowanceNoBonusMarkers.Add(new AllowanceNoBonusMarkerEntity
            {
                Id = Guid.NewGuid(),
                EmployeeId = employeeId,
                BusinessDepartmentId = deptId,
                Period = period,
                MarkedByUserId = managerUserId,
                Comment = comment?.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task ClearNoBonusAsync(string managerUserId, string employeeId, string period, CancellationToken ct)
    {
        if (!await scope.IsSupportDepartmentManagerAsync(managerUserId, ct))
            throw new UnauthorizedAccessException("Réservé au manager de département Support.");
        if (!await scope.CanManagerAccessEmployeeAsync(managerUserId, employeeId, ct))
            throw new UnauthorizedAccessException("Employé hors périmètre manager Support.");

        var marker = await db.AllowanceNoBonusMarkers
            .FirstOrDefaultAsync(m => m.EmployeeId == employeeId && m.Period == period.Trim(), ct);
        if (marker is null) return;
        db.AllowanceNoBonusMarkers.Remove(marker);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<AllowanceHistoryEntryDto>> GetHistoryAsync(
        string managerUserId, string? fromPeriod, string? toPeriod, CancellationToken ct)
    {
        if (!await scope.IsSupportDepartmentManagerAsync(managerUserId, ct))
            throw new UnauthorizedAccessException("Réservé au manager de département Support.");

        var deptId = await scope.GetManagerDepartmentIdAsync(managerUserId, ct);
        if (deptId is null) return [];

        var q = db.AllowanceRequests.AsNoTracking()
            .Include(r => r.AllowanceType)
            .Where(r => r.BusinessDepartmentId == deptId);
        if (!string.IsNullOrWhiteSpace(fromPeriod))
            q = q.Where(r => string.Compare(r.Period, fromPeriod.Trim()) >= 0);
        if (!string.IsNullOrWhiteSpace(toPeriod))
            q = q.Where(r => string.Compare(r.Period, toPeriod.Trim()) <= 0);

        var rows = await q.OrderByDescending(r => r.Period).ThenBy(r => r.EmployeeId)
            .Take(500).ToListAsync(ct);
        var empIds = rows.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        return rows.Select(r =>
        {
            employees.TryGetValue(r.EmployeeId, out var emp);
            return new AllowanceHistoryEntryDto(
                AllowanceRequestService.MapRequestPublic(r),
                emp?.FirstName ?? "",
                emp?.LastName ?? "");
        }).ToList();
    }

    public async Task<List<AllowancePeriodSummaryDto>> GetPeriodSummariesAsync(
        string managerUserId, CancellationToken ct)
    {
        if (!await scope.IsSupportDepartmentManagerAsync(managerUserId, ct))
            throw new UnauthorizedAccessException("Réservé au manager de département Support.");

        var deptId = await scope.GetManagerDepartmentIdAsync(managerUserId, ct);
        if (deptId is null) return [];

        var requests = await db.AllowanceRequests.AsNoTracking()
            .Where(r => r.BusinessDepartmentId == deptId)
            .ToListAsync(ct);
        var markers = await db.AllowanceNoBonusMarkers.AsNoTracking()
            .Where(m => m.BusinessDepartmentId == deptId)
            .ToListAsync(ct);

        var periods = requests.Select(r => r.Period)
            .Concat(markers.Select(m => m.Period))
            .Distinct()
            .OrderByDescending(p => p)
            .Take(24)
            .ToList();

        return periods.Select(period =>
        {
            var periodReqs = requests.Where(r => r.Period == period).ToList();
            var periodMarkers = markers.Count(m => m.Period == period);
            return new AllowancePeriodSummaryDto(
                period,
                periodReqs.Count,
                periodReqs.Count(r => r.Status == AllowanceRequestStatuses.Draft),
                periodReqs.Count(r => r.Status is AllowanceRequestStatuses.ManagerApproved or AllowanceRequestStatuses.Submitted),
                periodReqs.Count(r => r.Status is AllowanceRequestStatuses.RhApproved
                    or AllowanceRequestStatuses.ComptaApproved or AllowanceRequestStatuses.Paid),
                periodMarkers,
                periodReqs.Sum(r => r.Amount));
        }).ToList();
    }

    public async Task<AllowanceEmployeeAllocationsDto> GetEmployeeAllocationsAsync(
        string managerUserId, string employeeId, string period, CancellationToken ct)
    {
        if (!await scope.IsSupportDepartmentManagerAsync(managerUserId, ct))
            throw new UnauthorizedAccessException("Réservé au manager de département Support.");
        if (!await scope.CanManagerAccessEmployeeAsync(managerUserId, employeeId, ct))
            throw new UnauthorizedAccessException("Employé hors périmètre manager Support.");

        period = period.Trim();
        var deptId = await scope.GetManagerDepartmentIdAsync(managerUserId, ct);
        var eligibleTypes = await catalog.ListEligibleTypesAsync(deptId, ct);

        var empRequests = await db.AllowanceRequests.AsNoTracking()
            .Include(r => r.AllowanceType)
            .Where(r => r.EmployeeId == employeeId && r.Period == period)
            .OrderBy(r => r.AllowanceType!.Label)
            .ToListAsync(ct);

        var usedTypeIds = empRequests
            .Where(r => r.Status != AllowanceRequestStatuses.Rejected)
            .Select(r => r.AllowanceTypeId)
            .ToHashSet();

        var availableTypes = eligibleTypes.Where(t => !usedTypeIds.Contains(t.Id)).ToList();
        var requestDtos = empRequests.Select(AllowanceRequestService.MapRequestPublic).ToList();
        var marker = await db.AllowanceNoBonusMarkers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.EmployeeId == employeeId && m.Period == period, ct);

        return new AllowanceEmployeeAllocationsDto(
            employeeId, period, requestDtos, availableTypes,
            marker is not null, marker?.Comment, marker?.CreatedAt);
    }

    private async Task<List<AllowanceNoBonusMarkerEntity>> LoadNoBonusMarkersAsync(
        string? deptId, string period, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deptId)) return [];
        return await db.AllowanceNoBonusMarkers.AsNoTracking()
            .Where(m => m.BusinessDepartmentId == deptId && m.Period == period)
            .ToListAsync(ct);
    }

    private async Task<List<EmployeeEntity>> LoadTeamMembersAsync(
        string managerUserId, string? deptId, CancellationToken ct)
    {
        var managedIds = await scope.GetDirectReportIdsAsync(managerUserId, ct);
        if (managedIds.Count == 0) return [];

        var query = db.Employees.AsNoTracking()
            .Where(e => managedIds.Contains(e.Id) && e.BusinessDepartmentKind == "Support");
        if (!string.IsNullOrWhiteSpace(deptId))
            query = query.Where(e => e.BusinessDepartmentId == deptId);
        return await query.OrderBy(e => e.LastName).ThenBy(e => e.FirstName).ToListAsync(ct);
    }

    private async Task<List<AllowanceRequestEntity>> LoadDeptPeriodRequestsAsync(
        string? deptId, string period, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deptId)) return [];
        return await db.AllowanceRequests.AsNoTracking()
            .Where(r => r.BusinessDepartmentId == deptId && r.Period == period)
            .ToListAsync(ct);
    }

    public static string DeriveTreatmentStatus(IReadOnlyList<AllowanceRequestEntity> reqs, bool noBonusMarked = false)
    {
        if (reqs.Any(r => r.Status == AllowanceRequestStatuses.Draft)) return "HasDrafts";

        var active = reqs.Where(r => r.Status != AllowanceRequestStatuses.Rejected).ToList();
        if (active.Count == 0)
        {
            if (noBonusMarked) return "NoBonus";
            return "NotStarted";
        }

        if (active.All(r => r.Status is AllowanceRequestStatuses.RhApproved
                or AllowanceRequestStatuses.ComptaApproved
                or AllowanceRequestStatuses.Paid))
            return "Validated";

        return "Submitted";
    }
}

public sealed class AllowanceRuleEngineService(
    PrimeDbContext db,
    AllowanceRequestService requests,
    AllowanceScopeService scope)
{
    // Conservés pour la future implémentation Phase 4 (éligibilité Planning/Congés).
    private static readonly HashSet<string> PlanningTypeCodes = ["HOURS_OT", "HOURS_NIGHT"];
    private static readonly HashSet<string> CongesTypeCodes = ["ATTENDANCE"];

    /// <summary>Phase 4 — désactivé tant que l’éligibilité Planning/Congés n’est pas branchée.</summary>
    public Task<int> GenerateProposalsAsync(string period, string businessDepartmentId, string actorUserId, CancellationToken ct)
    {
        // Empêche les propositions auto pour tous (EmployeeEligibleForRuleAsync était un stub toujours true).
        _ = (db, requests, scope, period, businessDepartmentId, actorUserId, ct, PlanningTypeCodes, CongesTypeCodes);
        return Task.FromResult(0);
    }
}

public sealed class AllowanceOperationsAppService(
    AllowanceCatalogService catalog,
    AllowanceRequestService requests,
    AllowanceTeamPilotageService pilotage,
    AllowanceScopeService scope,
    AllowanceRuleEngineService ruleEngine) : IAllowanceOperationsAppService
{
    public Task<List<AllowanceTypeDto>> ListTypesAsync(CancellationToken ct = default) =>
        catalog.ListTypesAsync(ct);

    public Task<AllowanceTypeDto> CreateTypeAsync(CreateAllowanceTypeRequest body, CancellationToken ct = default) =>
        catalog.CreateTypeAsync(body, ct);

    public Task<List<AllowanceTypeDto>> ListEligibleTypesAsync(string? businessDepartmentId, CancellationToken ct = default) =>
        catalog.ListEligibleTypesAsync(businessDepartmentId, ct);

    public Task<string?> GetManagerDepartmentIdAsync(string userId, CancellationToken ct = default) =>
        scope.GetManagerDepartmentIdAsync(userId, ct);

    public Task<bool> IsSupportDepartmentManagerAsync(string userId, CancellationToken ct = default) =>
        scope.IsSupportDepartmentManagerAsync(userId, ct);

    public Task<List<AllowanceRequestDto>> ListRequestsAsync(
        string userId, string role, string? departmentId, string? period, CancellationToken ct = default) =>
        requests.ListAsync(userId, role, departmentId, period, ct);

    public Task<List<AllowanceRequestDto>> InboxAsync(string userId, string role, CancellationToken ct = default) =>
        requests.InboxAsync(userId, role, ct);

    public Task<AllowanceRequestDto> CreateRequestAsync(string userId, CreateAllowanceRequestBody body, CancellationToken ct = default) =>
        requests.CreateAsync(userId, body, ct);

    public Task<AllowanceRequestDto> UpdateDraftAsync(Guid id, string userId, UpdateAllowanceRequestBody body, CancellationToken ct = default) =>
        requests.UpdateDraftAsync(id, userId, body, ct);

    public Task<AllowanceRequestDto> SubmitAsync(Guid id, string userId, CancellationToken ct = default) =>
        requests.SubmitAsync(id, userId, ct);

    public Task<AllowanceRequestDto> ApproveAsync(Guid id, string userId, string role, CancellationToken ct = default) =>
        requests.ApproveAsync(id, userId, role, ct);

    public Task<AllowanceRequestDto> RejectAsync(Guid id, string userId, string role, string reason, CancellationToken ct = default) =>
        requests.RejectAsync(id, userId, role, reason, ct);

    public Task<int> GenerateProposalsAsync(string period, string businessDepartmentId, string userId, CancellationToken ct = default) =>
        ruleEngine.GenerateProposalsAsync(period, businessDepartmentId, userId, ct);

    public Task<AllowanceTeamProgressDto> GetTeamProgressAsync(string userId, string period, CancellationToken ct = default) =>
        pilotage.GetTeamProgressAsync(userId, period, ct);

    public Task<AllowanceEmployeeAllocationsDto> GetEmployeeAllocationsAsync(
        string userId, string employeeId, string period, CancellationToken ct = default) =>
        pilotage.GetEmployeeAllocationsAsync(userId, employeeId, period, ct);

    public Task MarkNoBonusAsync(string userId, string employeeId, string period, string? comment, CancellationToken ct = default) =>
        pilotage.MarkNoBonusAsync(userId, employeeId, period, comment, ct);

    public Task ClearNoBonusAsync(string userId, string employeeId, string period, CancellationToken ct = default) =>
        pilotage.ClearNoBonusAsync(userId, employeeId, period, ct);

    public Task<List<AllowanceHistoryEntryDto>> GetHistoryAsync(
        string userId, string? fromPeriod, string? toPeriod, CancellationToken ct = default) =>
        pilotage.GetHistoryAsync(userId, fromPeriod, toPeriod, ct);

    public Task<List<AllowancePeriodSummaryDto>> GetPeriodSummariesAsync(string userId, CancellationToken ct = default) =>
        pilotage.GetPeriodSummariesAsync(userId, ct);
}
