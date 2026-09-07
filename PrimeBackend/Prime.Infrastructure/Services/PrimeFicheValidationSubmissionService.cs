using Microsoft.EntityFrameworkCore;
using Prime.Application.Abstractions;
using Prime.Domain.Entities;
using Prime.Infrastructure.Persistence;

namespace Prime.Infrastructure.Services;

/// <summary>
/// Soumission automatique des fiches employé au workflow de validation lorsque
/// la partie commune est validée (brouillon <c>Validated</c>) et la partie cellule est complète.
/// </summary>
public sealed class PrimeFicheValidationSubmissionService(
    PrimeDbContext db,
    PrimeValidationWorkflowRuntime wfRuntime,
    PrimeOrgScopeService org,
    PrimeFicheValidationHistoryService? history = null)
{
    public static bool IsDraftValidated(string? draftStatus) =>
        string.Equals(draftStatus?.Trim(), "Validated", StringComparison.OrdinalIgnoreCase);

    public static bool IsCellPartComplete(string? fillingStatus) =>
        string.Equals(fillingStatus?.Trim(), "Complete", StringComparison.OrdinalIgnoreCase);

    public static bool IsReadyForValidation(
        SupervisorCellulePrimeDraft draft,
        EmployeePrimeServiceFiche fiche) =>
        IsDraftValidated(draft.Status) && IsCellPartComplete(fiche.FillingStatus);

    public static bool ComputeIsReadyForValidation(
        SupervisorCellulePrimeDraft draft,
        EmployeePrimeServiceFiche fiche) => IsReadyForValidation(draft, fiche);

    /// <summary>Calcule « prête » via <see cref="ResolveDraftForFicheAsync"/> (même logique que le reconcile).</summary>
    public async Task<bool> ComputeIsReadyForValidationAsync(
        EmployeePrimeServiceFiche fiche,
        CancellationToken ct = default)
    {
        var draft = await ResolveDraftForFicheAsync(fiche, ct);
        return draft is not null && IsReadyForValidation(draft, fiche);
    }

    /// <summary>Aligne la fiche sur le brouillon validé résolu (lien + superviseur).</summary>
    public static void ApplyResolvedDraftToFiche(
        EmployeePrimeServiceFiche fiche,
        SupervisorCellulePrimeDraft draft)
    {
        if (fiche.CellulePrimeDraftId != draft.Id)
            fiche.CellulePrimeDraftId = draft.Id;
        if (!string.Equals(fiche.SupervisorUserId, draft.SupervisorUserId, StringComparison.Ordinal))
            fiche.SupervisorUserId = draft.SupervisorUserId;
    }

    private static bool IsPreSubmissionStatus(string? validationStatus)
    {
        var s = validationStatus?.Trim() ?? "";
        return string.Equals(s, PrimeValidationWorkflowService.AwaitingData, StringComparison.Ordinal) ||
               string.Equals(s, "NotStarted", StringComparison.OrdinalIgnoreCase) ||
               string.IsNullOrEmpty(s);
    }

    public async Task SyncValidationSubmissionStatusAsync(
        EmployeePrimeServiceFiche fiche,
        SupervisorCellulePrimeDraft draft,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        if (string.Equals(fiche.ValidationStatus, PrimeValidationWorkflowService.Rejected, StringComparison.Ordinal))
            return;
        if (await wfRuntime.IsTerminalStatusAsync(fiche.ValidationStatus, ct))
            return;

        var ready = IsReadyForValidation(draft, fiche);

        if (ready)
        {
            var submittedFromStatus = PrimeValidationWorkflowService.AwaitingData;
            if (IsPreSubmissionStatus(fiche.ValidationStatus))
            {
                submittedFromStatus = string.IsNullOrWhiteSpace(fiche.ValidationStatus)
                    ? PrimeValidationWorkflowService.AwaitingData
                    : fiche.ValidationStatus.Trim();
                fiche.ValidationStatus = PrimeValidationWorkflowService.Pending;
                fiche.UpdatedAt = now;
            }

            // Répare aussi le journal des fiches déjà entrées dans le circuit
            // avant l'introduction de l'évènement Submitted.
            await AppendSubmittedHistoryAsync(fiche, submittedFromStatus, ct);
            return;
        }

        if (string.Equals(fiche.ValidationStatus, PrimeValidationWorkflowService.Pending, StringComparison.Ordinal) &&
            fiche.LastApprovedAt is null)
        {
            fiche.ValidationStatus = PrimeValidationWorkflowService.AwaitingData;
            fiche.UpdatedAt = now;
        }
    }

    public async Task SyncForDraftAsync(Guid draftId, CancellationToken ct = default)
    {
        var draft = await db.SupervisorCellulePrimeDrafts.FirstOrDefaultAsync(x => x.Id == draftId, ct);
        if (draft is null || !IsDraftValidated(draft.Status)) return;

        var fiches = await GetFichesInValidatedDraftScopeAsync(draft, ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var fiche in fiches)
        {
            ApplyResolvedDraftToFiche(fiche, draft);
            await SyncValidationSubmissionStatusAsync(fiche, draft, now, ct);
        }
    }

    /// <summary>
    /// Toutes les fiches pilotes du périmètre du brouillon validé (même superviseur, période, cellule ou pôle racine).
    /// </summary>
    private async Task<List<EmployeePrimeServiceFiche>> GetFichesInValidatedDraftScopeAsync(
        SupervisorCellulePrimeDraft draft,
        CancellationToken ct)
    {
        var per = draft.Period.Trim();
        var rootPole = draft.RootPoleId.Trim();
        var draftCell = draft.CelluleId.Trim();

        var celluleIds = await db.Cellules.AsNoTracking()
            .Where(c => c.Id == draftCell || c.PoleId == rootPole)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (celluleIds.Count == 0)
            celluleIds = [draftCell];

        // Tous les pilotes des cellules du périmètre (pas un seul employé en dur).
        return await db.EmployeePrimeServiceFiches
            .Where(f => f.Period == per && celluleIds.Contains(f.CelluleId))
            .ToListAsync(ct);
    }

    /// <summary>Synchronise chaque brouillon Validated avec les fiches pilotes de son périmètre.</summary>
    public async Task<int> SyncAllValidatedDraftsAsync(CancellationToken ct = default)
    {
        var draftIds = await db.SupervisorCellulePrimeDrafts.AsNoTracking()
            .Where(d => d.Status.ToLower() == "validated")
            .Select(d => d.Id)
            .ToListAsync(ct);
        foreach (var id in draftIds)
            await SyncForDraftAsync(id, ct);
        if (draftIds.Count > 0)
            await db.SaveChangesAsync(ct);
        return draftIds.Count;
    }

    public async Task<int> SyncValidatedDraftsForPeriodAsync(string period, CancellationToken ct = default)
    {
        var per = period.Trim();
        var draftIds = await db.SupervisorCellulePrimeDrafts.AsNoTracking()
            .Where(d => d.Period == per && d.Status.ToLower() == "validated")
            .Select(d => d.Id)
            .ToListAsync(ct);
        foreach (var id in draftIds)
            await SyncForDraftAsync(id, ct);
        if (draftIds.Count > 0)
            await db.SaveChangesAsync(ct);
        return draftIds.Count;
    }

    /// <summary>Résout le brouillon pôle validé pour une fiche (lien direct, cellule, ou pôle racine / période).</summary>
    public async Task<SupervisorCellulePrimeDraft?> ResolveDraftForFicheAsync(
        EmployeePrimeServiceFiche fiche,
        CancellationToken ct = default)
    {
        var per = fiche.Period.Trim();
        var ficheCell = fiche.CelluleId.Trim();

        var bySupervisor = await db.SupervisorCellulePrimeDrafts
            .Where(d =>
                d.SupervisorUserId == fiche.SupervisorUserId &&
                d.Period == per &&
                d.Status.ToLower() == "validated")
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

        var fichePoleId = await db.Cellules.AsNoTracking()
            .Where(c => c.Id == ficheCell)
            .Select(c => c.PoleId)
            .FirstOrDefaultAsync(ct);

        var picked = PickValidatedDraft(fiche, bySupervisor, fichePoleId);
        if (picked is not null) return picked;

        var byCellule = await db.SupervisorCellulePrimeDrafts
            .Where(d =>
                d.Period == per &&
                d.CelluleId == ficheCell &&
                d.Status.ToLower() == "validated")
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

        picked = PickValidatedDraft(fiche, byCellule, fichePoleId);
        if (picked is not null) return picked;

        if (string.IsNullOrWhiteSpace(fichePoleId)) return null;

        var byRootPole = await db.SupervisorCellulePrimeDrafts
            .Where(d =>
                d.Period == per &&
                d.RootPoleId == fichePoleId &&
                d.Status.ToLower() == "validated")
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

        return PickValidatedDraft(fiche, byRootPole, fichePoleId);
    }

    private static SupervisorCellulePrimeDraft? PickValidatedDraft(
        EmployeePrimeServiceFiche fiche,
        IReadOnlyList<SupervisorCellulePrimeDraft> candidates,
        string? fichePoleId)
    {
        if (candidates.Count == 0) return null;

        if (fiche.CellulePrimeDraftId != Guid.Empty)
        {
            var linked = candidates.FirstOrDefault(d => d.Id == fiche.CellulePrimeDraftId);
            if (linked is not null) return linked;
        }

        var ficheCell = fiche.CelluleId.Trim();
        var byCell = candidates.FirstOrDefault(d =>
            string.Equals(d.CelluleId, ficheCell, StringComparison.Ordinal));
        if (byCell is not null) return byCell;

        if (!string.IsNullOrWhiteSpace(fichePoleId))
        {
            var byRoot = candidates.FirstOrDefault(d =>
                string.Equals(d.RootPoleId, fichePoleId, StringComparison.Ordinal));
            if (byRoot is not null) return byRoot;
        }

        return candidates[0];
    }

    /// <summary>
    /// Passe en <see cref="PrimeValidationWorkflowService.Pending"/> toutes les fiches prêtes encore en attente de données.
    /// </summary>
    public async Task<int> ReconcileReadySubmissionsAsync(CancellationToken ct = default)
    {
        var fiches = await db.EmployeePrimeServiceFiches
            .Where(f =>
                (f.ValidationStatus == PrimeValidationWorkflowService.AwaitingData ||
                 f.ValidationStatus == "NotStarted") &&
                f.FillingStatus.ToLower() == "complete")
            .ToListAsync(ct);

        return await ReconcileFichesCoreAsync(fiches, ct);
    }

    public async Task<int> ReconcileReadySubmissionsForSupervisorPeriodAsync(
        string supervisorUserId,
        string period,
        CancellationToken ct = default)
    {
        var sup = supervisorUserId.Trim();
        var per = period.Trim();
        var celluleIds = await org.GetSupervisedCelluleIdsAsync(sup, ct);
        if (celluleIds.Count == 0) return 0;

        var fiches = await db.EmployeePrimeServiceFiches
            .Where(f =>
                f.Period == per &&
                celluleIds.Contains(f.CelluleId) &&
                (f.ValidationStatus == PrimeValidationWorkflowService.AwaitingData ||
                 f.ValidationStatus == "NotStarted") &&
                f.FillingStatus.ToLower() == "complete")
            .ToListAsync(ct);

        return await ReconcileFichesCoreAsync(fiches, ct);
    }

    /// <summary>
    /// Passe en <see cref="PrimeValidationWorkflowService.Pending"/> les fiches prêtes de la période
    /// encore en attente de données (toutes cellules / superviseurs).
    /// </summary>
    public async Task<int> ReconcileReadySubmissionsForPeriodAsync(string period, CancellationToken ct = default)
    {
        var per = period.Trim();
        var fiches = await db.EmployeePrimeServiceFiches
            .Where(f =>
                f.Period == per &&
                (f.ValidationStatus == PrimeValidationWorkflowService.AwaitingData ||
                 f.ValidationStatus == "NotStarted") &&
                f.FillingStatus.ToLower() == "complete")
            .ToListAsync(ct);

        return await ReconcileFichesCoreAsync(fiches, ct);
    }

    private async Task<int> ReconcileFichesCoreAsync(
        List<EmployeePrimeServiceFiche> fiches,
        CancellationToken ct)
    {
        if (fiches.Count == 0) return 0;

        var changed = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var fiche in fiches)
        {
            var draft = await ResolveDraftForFicheAsync(fiche, ct);
            if (draft is null) continue;
            ApplyResolvedDraftToFiche(fiche, draft);
            var before = fiche.ValidationStatus;
            await SyncValidationSubmissionStatusAsync(fiche, draft, now, ct);
            if (!string.Equals(before, fiche.ValidationStatus, StringComparison.Ordinal))
                changed++;
        }

        if (changed > 0)
            await db.SaveChangesAsync(ct);
        return changed;
    }

    private async Task AppendSubmittedHistoryAsync(
        EmployeePrimeServiceFiche fiche,
        string fromStatus,
        CancellationToken ct)
    {
        if (history is null) return;
        var supId = (fiche.SupervisorUserId ?? "").Trim();
        if (supId.Length == 0) return;

        var empEntity = await org.GetEmployeeAsync(supId, ct);
        var employee = empEntity is null
            ? new Employee
            {
                Id = supId,
                Role = PrimeFicheValidationRoles.Superviseur,
            }
            : new Employee
            {
                Id = empEntity.Id,
                FirstName = empEntity.FirstName,
                LastName = empEntity.LastName,
                Role = string.IsNullOrWhiteSpace(empEntity.Role)
                    ? PrimeFicheValidationRoles.Superviseur
                    : empEntity.Role,
                ParentId = empEntity.ParentId,
                ServiceId = empEntity.ServiceId,
                PoleId = empEntity.PoleId ?? "",
                CelluleId = empEntity.CelluleId,
                Email = empEntity.Email,
                Avatar = empEntity.Avatar,
                BusinessDepartmentId = empEntity.BusinessDepartmentId,
                BusinessDepartmentKind = empEntity.BusinessDepartmentKind,
            };

        var actor = new PrimeResolvedUser(supId, PrimeFicheValidationRoles.Superviseur, employee);
        var amounts = PrimeEmployeeFicheAmountService.ResolveWorkflowDisplayAmounts(fiche);
        await history.AppendSubmittedAsync(fiche, fromStatus, actor, amounts, ct);
    }
}

