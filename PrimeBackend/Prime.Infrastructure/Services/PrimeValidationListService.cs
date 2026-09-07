using Microsoft.EntityFrameworkCore;
using Prime.Infrastructure.Persistence;
using Prime.Application.DTOs;

namespace Prime.Infrastructure.Services;

/// <summary>Requêtes et projection pour les listes de validation (fiches prêtes, libellés RH).</summary>
public sealed class PrimeValidationListService(
    PrimeDbContext db,
    PrimeFicheValidationSubmissionService submission)
{
    public IQueryable<EmployeePrimeServiceFiche> ApplyReadyForValidationFilter(
        IQueryable<EmployeePrimeServiceFiche> query) =>
        query.Where(f =>
            f.FillingStatus.ToLower() == "complete" &&
            (
                (f.ValidationStatus != PrimeValidationWorkflowService.AwaitingData &&
                 f.ValidationStatus != "NotStarted") ||
                db.SupervisorCellulePrimeDrafts.Any(d =>
                    d.Period == f.Period &&
                    d.Status.ToLower() == "validated" &&
                    (d.Id == f.CellulePrimeDraftId ||
                     d.CelluleId == f.CelluleId ||
                     db.Cellules.Any(c => c.Id == f.CelluleId && c.PoleId == d.RootPoleId)))));

    public static bool ShouldDefaultReadyOnly(string? role) =>
        !string.IsNullOrWhiteSpace(role) && PrimeFicheValidationRoles.IsOperationalApprover(role.Trim());

    public async Task<List<EmployeePrimeServiceFicheValidationDto>> MapValidationDtosAsync(
        IReadOnlyList<EmployeePrimeServiceFiche> fiches,
        CancellationToken ct = default)
    {
        if (fiches.Count == 0) return [];

        var draftIds = fiches.Select(f => f.CellulePrimeDraftId).Distinct().ToList();
        var empIds = fiches.Select(f => f.EmployeeId).Distinct().ToList();
        var serviceIds = fiches.Select(f => f.ServiceId).Distinct().ToList();
        var celluleIds = fiches.Select(f => f.CelluleId).Distinct().ToList();

        var drafts = await db.SupervisorCellulePrimeDrafts.AsNoTracking()
            .Where(d => draftIds.Contains(d.Id))
            .ToDictionaryAsync(d => d.Id, ct);
        var employees = await db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);
        var services = await db.Services.AsNoTracking()
            .Where(s => serviceIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        var cellules = await db.Cellules.AsNoTracking()
            .Where(c => celluleIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);
        var poleIds = cellules.Values.Select(c => c.PoleId).Distinct().ToList();
        var poles = poleIds.Count == 0
            ? new Dictionary<string, PoleEntity>()
            : await db.Poles.AsNoTracking()
                .Where(p => poleIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, ct);

        var result = new List<EmployeePrimeServiceFicheValidationDto>(fiches.Count);
        foreach (var f in fiches)
            result.Add(await MapOneAsync(f, drafts, employees, services, cellules, poles, ct));
        return result;
    }

    public async Task<EmployeePrimeServiceFicheValidationDto> MapValidationDtoAsync(
        EmployeePrimeServiceFiche fiche,
        CancellationToken ct = default)
    {
        var list = await MapValidationDtosAsync([fiche], ct);
        return list[0];
    }

    private async Task<EmployeePrimeServiceFicheValidationDto> MapOneAsync(
        EmployeePrimeServiceFiche f,
        IReadOnlyDictionary<Guid, SupervisorCellulePrimeDraft> drafts,
        IReadOnlyDictionary<string, EmployeeEntity> employees,
        IReadOnlyDictionary<string, ServiceEntity> services,
        IReadOnlyDictionary<string, CelluleEntity> cellules,
        IReadOnlyDictionary<string, PoleEntity> poles,
        CancellationToken ct)
    {
        var resolvedDraft = await submission.ResolveDraftForFicheAsync(f, ct);
        drafts.TryGetValue(f.CellulePrimeDraftId, out var linkedDraft);
        var draft = resolvedDraft ?? linkedDraft;
        employees.TryGetValue(f.EmployeeId, out var emp);
        services.TryGetValue(f.ServiceId, out var svc);
        cellules.TryGetValue(f.CelluleId, out var cell);
        var poleName = cell is not null && poles.TryGetValue(cell.PoleId, out var pole) ? pole.Name : null;

        var ready = draft is not null &&
                    PrimeFicheValidationSubmissionService.ComputeIsReadyForValidation(draft, f);

        var amounts = PrimeEmployeeFicheAmountService.ResolveWorkflowDisplayAmounts(f);

        return new EmployeePrimeServiceFicheValidationDto
        {
            Id = f.Id,
            EmployeeId = f.EmployeeId,
            EmployeeDisplayName = emp is null ? f.EmployeeId : $"{emp.FirstName} {emp.LastName}".Trim(),
            EmployeeRole = emp?.Role ?? "",
            SupervisorUserId = f.SupervisorUserId,
            ServiceId = f.ServiceId,
            ServiceName = svc?.Name ?? f.ServiceId,
            CelluleId = f.CelluleId,
            CelluleName = cell?.Name ?? f.CelluleId,
            PoleName = poleName,
            Period = f.Period,
            FillingStatus = f.FillingStatus,
            ValidationStatus = f.ValidationStatus,
            CommonPartStatus = draft?.Status,
            IsReadyForValidation = ready,
            LastApproverUserId = f.LastApproverUserId,
            LastApprovedAt = f.LastApprovedAt,
            RejectedByUserId = f.RejectedByUserId,
            RejectedAt = f.RejectedAt,
            RejectionReason = f.RejectionReason,
            PrimeAmount = amounts.PrimeAmount,
            ChallengeAmount = amounts.ChallengeAmount,
            TotalAmount = amounts.TotalAmount,
            UpdatedAt = f.UpdatedAt,
        };
    }
}
