using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prime.Application;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;
using Shared.Kernel.Push;

namespace Prime.Infrastructure.Services;

public sealed class PrimeValidationAppService(
    PrimeDbContext db,
    IPrimeRequestUserResolver userResolver,
    PrimeValidationWorkflowRuntime wfRuntime,
    PrimeRbacReadService rbac,
    PrimeValidationListService validationList,
    PrimeFicheValidationSubmissionService submission,
    PrimeFicheValidationHistoryService validationHistory,
    PrimeAuditLogService auditWriter,
    AnomalyDetectionService anomalies,
    PrimeFicheMergedPreviewAccessService previewAccess,
    IDesktopPushClient desktopPush,
    ILogger<PrimeValidationAppService> logger) : IPrimeValidationAppService
{
    private const string GlobalPoolRoleFicheErrorMessage =
        "Ce rôle valide le fichier synthèse globale PRIME (écran « Synthèse globale »), pas les fiches individuelles.";

    private const string ValidationIdentityError =
        "Utilisateur introuvable ou identité de validation incomplète (userId / rôle requis).";

    private static bool IsBlockedGlobalPoolRoleOnFicheApi(string role) =>
        PrimeFicheValidationRoles.IsGlobalPoolStakeholder(role);

    private static bool ValidationIdentityRequired(string? userId, string? role) =>
        !string.IsNullOrWhiteSpace(userId) || !string.IsNullOrWhiteSpace(role);

    public async Task<ValidationReconcileResultDto> ReconcileReadyAsync(CancellationToken ct = default)
    {
        var repair = await RunValidationRepairAsync(ct);
        return new ValidationReconcileResultDto(
            repair.ReconciledGlobal + repair.ReconciledByPeriod,
            repair.DraftsValidated,
            repair.FichesEnsured,
            repair.ReconciledGlobal,
            repair.ReconciledByPeriod);
    }

    public async Task<WorkflowValidationMetaDto> GetWorkflowMetaAsync(string? role, CancellationToken ct = default)
    {
        var steps = await db.WorkflowSteps.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var dto = steps.Select(s => new WorkflowStepConfigDto
        {
            Id = s.Id,
            SortOrder = s.SortOrder,
            ApproverRole = s.ApproverRole,
            FromStatus = s.FromStatus,
            ToStatus = s.ToStatus,
            IsActive = s.IsActive,
            SlaHours = s.SlaHours,
            CapturesAmountsOnApproval = s.CapturesAmountsOnApproval,
            TerminalApproved = s.TerminalApproved,
            UpdatedAt = s.UpdatedAt,
        }).ToList();
        var terminals = await wfRuntime.GetTerminalStatusesAsync(ct);
        var actionable = string.IsNullOrWhiteSpace(role)
            ? []
            : await wfRuntime.GetActionableFromStatusesForRoleAsync(role.Trim(), ct);
        return new WorkflowValidationMetaDto
        {
            Steps = dto,
            TerminalStatuses = terminals,
            ActionableFromStatuses = actionable,
        };
    }

    public async Task<IReadOnlyList<EmployeePrimeServiceFicheValidationDto>> ListAsync(
        string? period,
        string? status,
        string? serviceId,
        string? celluleId,
        string? userId,
        string? role,
        bool? readyOnly,
        CancellationToken ct = default)
    {
        var applyReadyOnly = readyOnly ?? PrimeValidationListService.ShouldDefaultReadyOnly(role);

        var ruEarly = await ResolveValidationUserOrNullAsync(userId, role, ct);
        EnsureValidationIdentity(ruEarly, userId, role);
        if (ruEarly is not null && IsBlockedGlobalPoolRoleOnFicheApi(ruEarly.Role))
            throw new PrimeApiException(400, GlobalPoolRoleFicheErrorMessage);

        await ReconcileForValidationReadAsync(period, ct);

        var query = db.EmployeePrimeServiceFiches.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(period)) query = query.Where(f => f.Period == period.Trim());
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!await wfRuntime.IsValidFilterStatusAsync(status.Trim(), ct))
                throw new PrimeApiException(400, "Statut invalide ou inconnu du workflow.");
            query = query.Where(f => f.ValidationStatus == status.Trim());
        }
        else if (!applyReadyOnly)
            query = query.Where(f => f.ValidationStatus != PrimeValidationWorkflowService.AwaitingData);

        if (applyReadyOnly)
            query = validationList.ApplyReadyForValidationFilter(query);

        if (!string.IsNullOrWhiteSpace(serviceId)) query = query.Where(f => f.ServiceId == serviceId.Trim());
        if (!string.IsNullOrWhiteSpace(celluleId)) query = query.Where(f => f.CelluleId == celluleId.Trim());

        var items = await query.OrderByDescending(f => f.UpdatedAt).Take(5000).ToListAsync(ct);
        var ru = ruEarly ?? await ResolveValidationUserOrNullAsync(userId, role, ct);
        items = await FilterItemsByValidationRbacAsync(items, ru, ct);

        return await validationList.MapValidationDtosAsync(items, ct);
    }

    public async Task<WorkflowValidationSummaryDto> GetSummaryAsync(
        string? period,
        string? serviceId,
        string? celluleId,
        string? userId,
        string? role,
        bool? readyOnly,
        CancellationToken ct = default)
    {
        var applyReadyOnly = readyOnly ?? PrimeValidationListService.ShouldDefaultReadyOnly(role);

        var ruEarly = await ResolveValidationUserOrNullAsync(userId, role, ct);
        EnsureValidationIdentity(ruEarly, userId, role);

        await ReconcileForValidationReadAsync(period, ct);

        var query = db.EmployeePrimeServiceFiches.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(period)) query = query.Where(f => f.Period == period.Trim());
        if (!string.IsNullOrWhiteSpace(serviceId)) query = query.Where(f => f.ServiceId == serviceId.Trim());
        if (!string.IsNullOrWhiteSpace(celluleId)) query = query.Where(f => f.CelluleId == celluleId.Trim());
        if (!applyReadyOnly)
            query = query.Where(f => f.ValidationStatus != PrimeValidationWorkflowService.AwaitingData);
        if (applyReadyOnly)
            query = validationList.ApplyReadyForValidationFilter(query);

        var items = await query.Take(5000).ToListAsync(ct);
        var ru = ruEarly ?? await ResolveValidationUserOrNullAsync(userId, role, ct);
        items = await FilterItemsByValidationRbacAsync(items, ru, ct);

        var grouped = items.GroupBy(f => f.ValidationStatus)
            .Select(g => new WorkflowStatusCountDto { Status = g.Key, Count = g.Count() })
            .OrderBy(x => x.Status)
            .ToList();
        var terminals = await wfRuntime.GetTerminalStatusesAsync(ct);

        var readyNotSubmitted = 0;
        if (ru is not null)
        {
            var actor = PrimeRbacReadService.WithActingRole(ru.Employee, ru.Role);
            var preQuery = db.EmployeePrimeServiceFiches.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(period)) preQuery = preQuery.Where(f => f.Period == period.Trim());
            if (!string.IsNullOrWhiteSpace(serviceId)) preQuery = preQuery.Where(f => f.ServiceId == serviceId.Trim());
            if (!string.IsNullOrWhiteSpace(celluleId)) preQuery = preQuery.Where(f => f.CelluleId == celluleId.Trim());
            preQuery = preQuery.Where(f =>
                (f.ValidationStatus == PrimeValidationWorkflowService.AwaitingData ||
                 f.ValidationStatus == "NotStarted") &&
                f.FillingStatus.ToLower() == "complete");
            if (applyReadyOnly)
                preQuery = validationList.ApplyReadyForValidationFilter(preQuery);

            var preItems = await preQuery.Take(5000).ToListAsync(ct);
            foreach (var f in preItems)
            {
                if (!await rbac.CanAccessFicheAsync(actor, f, "Read", ct)) continue;
                if (await submission.ComputeIsReadyForValidationAsync(f, ct))
                    readyNotSubmitted++;
            }
        }

        return new WorkflowValidationSummaryDto
        {
            StatusCounts = grouped,
            TerminalStatuses = terminals,
            Total = items.Count,
            ReadyNotSubmittedCount = readyNotSubmitted,
        };
    }

    public async Task<IReadOnlyList<PrimeFicheValidationHistoryFeedItemDto>> GetHistoryFeedAsync(
        string? userId,
        string? role,
        string? period,
        bool? mineOnly,
        string? action,
        CancellationToken ct = default)
    {
        var ru = await ResolveValidationUserOrNullAsync(userId, role, ct);
        EnsureValidationIdentity(ru, userId, role);
        if (ru is not null && IsBlockedGlobalPoolRoleOnFicheApi(ru.Role))
            throw new PrimeApiException(400, GlobalPoolRoleFicheErrorMessage);

        if (!string.IsNullOrWhiteSpace(action))
        {
            var a = action.Trim();
            if (!string.Equals(a, PrimeFicheValidationHistoryActions.Approved, StringComparison.Ordinal) &&
                !string.Equals(a, PrimeFicheValidationHistoryActions.Rejected, StringComparison.Ordinal) &&
                !string.Equals(a, PrimeFicheValidationHistoryActions.Submitted, StringComparison.Ordinal))
                throw new PrimeApiException(400, "Filtre action invalide (Submitted, Approved ou Rejected).");
        }

        // Le suivi doit également réparer les évènements Submitted manquants
        // des fiches déjà Pending avant l'ajout de cette trace.
        await ReconcileForValidationReadAsync(period, ct);

        return await validationHistory.ListFeedAsync(ru, rbac, period, mineOnly ?? true, action, 500, ct);
    }

    public async Task<IReadOnlyList<string>> ListPeriodsAsync(CancellationToken ct = default) =>
        await db.EmployeePrimeServiceFiches.AsNoTracking()
            .Select(f => f.Period)
            .Distinct()
            .OrderByDescending(p => p)
            .Take(120)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PrimeFicheValidationHistoryDto>> GetFicheHistoryAsync(
        Guid id,
        string? userId,
        string? role,
        CancellationToken ct = default)
    {
        var ru = await ResolveValidationUserOrNullAsync(userId, role, ct);
        EnsureValidationIdentity(ru, userId, role);

        var fiche = await db.EmployeePrimeServiceFiches.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new KeyNotFoundException();

        if (ru is not null)
        {
            var actor = PrimeRbacReadService.WithActingRole(ru.Employee, ru.Role);
            if (!await rbac.CanAccessFicheAsync(actor, fiche, "Read", ct))
                throw new PrimeApiException(403, "Périmètre RBAC insuffisant pour cette fiche.");
        }

        return await validationHistory.ListForFicheAsync(id, ct);
    }

    public async Task<EmployeePrimeServiceFicheValidationDto> ApproveAsync(
        Guid id,
        ApproveServiceFicheRequest body,
        CancellationToken ct = default)
    {
        var ru = await ResolveValidationUserOrNullAsync(body.UserId, body.Role, ct)
            ?? throw new PrimeApiException(401, ValidationIdentityError);

        if (IsBlockedGlobalPoolRoleOnFicheApi(ru.Role))
            throw new PrimeApiException(400, GlobalPoolRoleFicheErrorMessage);
        if (!PrimeFicheValidationRoles.IsOperationalApprover(ru.Role))
            throw new PrimeApiException(403, "Seuls les rôles opérationnels (Référent technique, Superviseur, Chef de projet) valident les fiches.");
        if (!PrimeEmployeeFicheAmountService.IsNonNegative(body.PrimeAmount) ||
            !PrimeEmployeeFicheAmountService.IsNonNegative(body.ChallengeAmount) ||
            !PrimeEmployeeFicheAmountService.IsNonNegative(body.TotalAmount))
            throw new PrimeApiException(400, DbExceptionMessages.NonNegativePrimeAmountsRequired);

        var fiche = await db.EmployeePrimeServiceFiches.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new KeyNotFoundException();

        var actor = PrimeRbacReadService.WithActingRole(ru.Employee, ru.Role);
        if (!await rbac.RoleHasActionAsync(ru.Role, "Validate", ct))
            throw new PrimeApiException(403, "Action « Validate » non autorisée pour ce rôle (RBAC).");
        if (!await rbac.CanAccessFicheAsync(actor, fiche, "Validate", ct))
            throw new PrimeApiException(403, "Périmètre RBAC insuffisant pour cette fiche.");

        var (ok, err, next, step) = await wfRuntime.TryResolveApprovalAsync(fiche, ru.Role, ct);
        if (!ok || next is null)
            throw new PrimeApiException(400, err ?? "Transition impossible.");

        var fromStatus = fiche.ValidationStatus;
        PrimeValidationWorkflowService.ApplyApproval(fiche, next, ru.UserId, DateTimeOffset.UtcNow);
        var amounts = PrimeEmployeeFicheAmountService.ExtractFromFiche(fiche);
        if (!PrimeEmployeeFicheAmountService.AreNonNegative(amounts))
            throw new PrimeApiException(400, DbExceptionMessages.NonNegativePrimeAmountsRequired);
        if (step?.CapturesAmountsOnApproval == true)
            PrimeEmployeeFicheAmountService.ApplySnapshotToEntity(fiche, amounts);

        await validationHistory.AppendApprovedAsync(fiche, fromStatus, next, ru, amounts, ct);
        await RecordValidationAuditAsync(ru, fiche.Id, "ValidationApproved", fromStatus, next, amounts, ct);

        await db.SaveChangesAsync(ct);
        await anomalies.RecomputeForFicheAsync(fiche.Id, ct);
        if (await wfRuntime.IsTerminalStatusAsync(next, ct)
            && !string.Equals(next, PrimeValidationWorkflowService.Rejected, StringComparison.Ordinal))
        {
            await NotifyEmployeeDesktopAsync(fiche.EmployeeId, approved: true, ct);
        }
        return await validationList.MapValidationDtoAsync(fiche, ct);
    }

    public async Task<EmployeePrimeServiceFicheValidationDto> RejectAsync(
        Guid id,
        RejectServiceFicheRequest body,
        CancellationToken ct = default)
    {
        var global = await db.WorkflowGlobalConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (global?.RequireRejectReason == true && string.IsNullOrWhiteSpace(body.Reason))
            throw new PrimeApiException(400, "Un motif de rejet est obligatoire.");

        var ru = await ResolveValidationUserOrNullAsync(body.UserId, body.Role, ct)
            ?? throw new PrimeApiException(401, ValidationIdentityError);

        if (IsBlockedGlobalPoolRoleOnFicheApi(ru.Role))
            throw new PrimeApiException(400, GlobalPoolRoleFicheErrorMessage);
        if (!PrimeFicheValidationRoles.IsOperationalApprover(ru.Role))
            throw new PrimeApiException(403, "Seuls les rôles opérationnels (Référent technique, Superviseur, Chef de projet) valident les fiches.");

        var fiche = await db.EmployeePrimeServiceFiches.FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new KeyNotFoundException();

        var actor = PrimeRbacReadService.WithActingRole(ru.Employee, ru.Role);
        if (!await rbac.RoleHasActionAsync(ru.Role, "Validate", ct))
            throw new PrimeApiException(403, "Action « Validate » non autorisée pour ce rôle (RBAC).");
        if (!await rbac.CanAccessFicheAsync(actor, fiche, "Validate", ct))
            throw new PrimeApiException(403, "Périmètre RBAC insuffisant pour cette fiche.");

        if (!await wfRuntime.CanRejectAsync(fiche.ValidationStatus, ru.Role, ct))
            throw new PrimeApiException(400, $"Le rôle « {ru.Role} » ne peut pas rejeter depuis l'état « {fiche.ValidationStatus} ».");
        if (string.IsNullOrWhiteSpace(body.Reason))
            throw new PrimeApiException(400, "Un motif de rejet est obligatoire.");

        var fromStatus = fiche.ValidationStatus;
        var reason = body.Reason.Trim();
        PrimeValidationWorkflowService.ApplyReject(fiche, ru.UserId, reason, DateTimeOffset.UtcNow);
        var amounts = PrimeEmployeeFicheAmountService.ExtractFromFiche(fiche);

        await validationHistory.AppendRejectedAsync(
            fiche, fromStatus, PrimeValidationWorkflowService.Rejected, ru, reason, amounts, ct);
        await RecordValidationAuditAsync(ru, fiche.Id, "ValidationRejected", fromStatus, PrimeValidationWorkflowService.Rejected, amounts, ct);

        await db.SaveChangesAsync(ct);
        await anomalies.RecomputeForFicheAsync(fiche.Id, ct);
        await NotifyEmployeeDesktopAsync(fiche.EmployeeId, approved: false, ct);
        return await validationList.MapValidationDtoAsync(fiche, ct);
    }

    public async Task<BulkApproveResultDto> BulkApproveAsync(
        BulkApproveServiceFicheRequest body,
        CancellationToken ct = default)
    {
        var global = await db.WorkflowGlobalConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        if (global?.AllowBulkApprove == false)
            throw new PrimeApiException(400, "L'approbation groupée est désactivée dans la configuration workflow.");

        var ru = await ResolveValidationUserOrNullAsync(body.UserId, body.Role, ct)
            ?? throw new PrimeApiException(401, ValidationIdentityError);

        if (IsBlockedGlobalPoolRoleOnFicheApi(ru.Role))
            throw new PrimeApiException(400, GlobalPoolRoleFicheErrorMessage);
        if (!PrimeFicheValidationRoles.IsOperationalApprover(ru.Role))
            throw new PrimeApiException(403, "Seuls les rôles opérationnels (Référent technique, Superviseur, Chef de projet) valident les fiches.");

        if (!await rbac.RoleHasActionAsync(ru.Role, "Validate", ct))
            throw new PrimeApiException(403, "Action « Validate » non autorisée pour ce rôle (RBAC).");

        if (body.FicheIds is null || body.FicheIds.Count == 0)
            throw new PrimeApiException(400, "Aucune fiche fournie.");

        var actor = PrimeRbacReadService.WithActingRole(ru.Employee, ru.Role);
        var fiches = await db.EmployeePrimeServiceFiches
            .Where(f => body.FicheIds.Contains(f.Id))
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        var approved = new List<Guid>();
        var ignored = new List<Guid>();
        foreach (var f in fiches)
        {
            if (!await rbac.CanAccessFicheAsync(actor, f, "Validate", ct))
            {
                ignored.Add(f.Id);
                continue;
            }
            var (ok, _, next, step) = await wfRuntime.TryResolveApprovalAsync(f, ru.Role, ct);
            if (!ok || next is null)
            {
                ignored.Add(f.Id);
                continue;
            }
            var fromStatus = f.ValidationStatus;
            PrimeValidationWorkflowService.ApplyApproval(f, next, ru.UserId, now);
            var amounts = PrimeEmployeeFicheAmountService.ExtractFromFiche(f);
            if (!PrimeEmployeeFicheAmountService.AreNonNegative(amounts))
            {
                ignored.Add(f.Id);
                continue;
            }
            if (step?.CapturesAmountsOnApproval == true)
                PrimeEmployeeFicheAmountService.ApplySnapshotToEntity(f, amounts);
            await validationHistory.AppendApprovedAsync(f, fromStatus, next, ru, amounts, ct);
            await RecordValidationAuditAsync(ru, f.Id, "ValidationApproved", fromStatus, next, amounts, ct);
            approved.Add(f.Id);
            if (await wfRuntime.IsTerminalStatusAsync(next, ct)
                && !string.Equals(next, PrimeValidationWorkflowService.Rejected, StringComparison.Ordinal))
            {
                await NotifyEmployeeDesktopAsync(f.EmployeeId, approved: true, ct);
            }
        }
        await db.SaveChangesAsync(ct);
        foreach (var ficheId in approved)
            await anomalies.RecomputeForFicheAsync(ficheId, ct);
        return new BulkApproveResultDto(approved, ignored);
    }

    public async Task<FileExportResultDto> ExportCsvAsync(
        Guid id,
        string? userId,
        string? role,
        CancellationToken ct = default)
    {
        var fiche = await db.EmployeePrimeServiceFiches.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new KeyNotFoundException();
        await GuardPiloteExportAsync(fiche, userId, role, ct);

        var sb = new StringBuilder();
        sb.AppendLine("Period,EmployeeId,ServiceId,CelluleId,ValidationStatus,FillingStatus,PrimeAmount,ChallengeAmount,TotalAmount");
        sb.Append(CsvEscape(fiche.Period)).Append(',');
        sb.Append(CsvEscape(fiche.EmployeeId)).Append(',');
        sb.Append(CsvEscape(fiche.ServiceId)).Append(',');
        sb.Append(CsvEscape(fiche.CelluleId)).Append(',');
        sb.Append(CsvEscape(fiche.ValidationStatus)).Append(',');
        sb.Append(CsvEscape(fiche.FillingStatus)).Append(',');
        sb.Append(fiche.PrimeAmount?.ToString("F2") ?? "").Append(',');
        sb.Append(fiche.ChallengeAmount?.ToString("F2") ?? "").Append(',');
        sb.Append(fiche.TotalAmount?.ToString("F2") ?? "");
        sb.AppendLine();
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return new FileExportResultDto(bytes, "text/csv; charset=utf-8", $"fiche-prime-{fiche.Id}.csv");
    }

    public async Task<FileExportResultDto> ExportXlsxAsync(
        Guid id,
        string? userId,
        string? role,
        CancellationToken ct = default)
    {
        var fiche = await db.EmployeePrimeServiceFiches.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct)
            ?? throw new KeyNotFoundException();
        await GuardPiloteExportAsync(fiche, userId, role, ct);

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Fiche");
        ws.Cell(1, 1).Value = "Période";
        ws.Cell(1, 2).Value = fiche.Period;
        ws.Cell(2, 1).Value = "Employé";
        ws.Cell(2, 2).Value = fiche.EmployeeId;
        ws.Cell(3, 1).Value = "Statut validation";
        ws.Cell(3, 2).Value = fiche.ValidationStatus;
        ws.Cell(4, 1).Value = "Prime";
        ws.Cell(4, 2).Value = fiche.PrimeAmount;
        ws.Cell(5, 1).Value = "Challenge";
        ws.Cell(5, 2).Value = fiche.ChallengeAmount;
        ws.Cell(6, 1).Value = "Total";
        ws.Cell(6, 2).Value = fiche.TotalAmount;
        await using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return new FileExportResultDto(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"fiche-prime-{fiche.Id}.xlsx");
    }

    private static void EnsureValidationIdentity(PrimeResolvedUser? ru, string? userId, string? role)
    {
        if (ru is null && ValidationIdentityRequired(userId, role))
            throw new PrimeApiException(401, ValidationIdentityError);
    }

    private Task<PrimeResolvedUser?> ResolveValidationUserOrNullAsync(
        string? userId,
        string? role,
        CancellationToken ct) =>
        userResolver.TryResolveForValidationAsync(userId, role, ct);

    private async Task RecordValidationAuditAsync(
        PrimeResolvedUser actor,
        Guid ficheId,
        string action,
        string fromStatus,
        string toStatus,
        PrimeEmployeeFicheAmounts amounts,
        CancellationToken ct)
    {
        var display = $"{actor.Employee.FirstName} {actor.Employee.LastName}".Trim();
        await auditWriter.RecordAsync(
            actor.UserId,
            display,
            actor.Role,
            action,
            nameof(EmployeePrimeServiceFiche),
            ficheId.ToString("D"),
            PrimeFicheValidationHistoryService.BuildAuditDetailJson(fromStatus, toStatus, amounts),
            ct);
    }

    private async Task ReconcileForValidationReadAsync(string? period, CancellationToken ct)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(period))
                await submission.SyncValidatedDraftsForPeriodAsync(period.Trim(), ct);
            else
                await submission.SyncAllValidatedDraftsAsync(ct);

            await RunValidationReconcileOnlyAsync(ct);
            if (!string.IsNullOrWhiteSpace(period))
                await submission.ReconcileReadySubmissionsForPeriodAsync(period.Trim(), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "PRIME validation : reconcile lecture ignoré (période {Period}).", period);
        }
    }

    private async Task<PrimeValidationReconcile.Result> RunValidationRepairAsync(CancellationToken ct) =>
        await PrimeValidationReconcile.ApplyAsync(db, submission, logger, ct);

    private async Task<PrimeValidationReconcile.Result> RunValidationReconcileOnlyAsync(CancellationToken ct) =>
        await PrimeValidationReconcile.ReconcileOnlyAsync(db, submission, logger, ct);

    private async Task<List<EmployeePrimeServiceFiche>> FilterItemsByValidationRbacAsync(
        List<EmployeePrimeServiceFiche> items,
        PrimeResolvedUser? ru,
        CancellationToken ct)
    {
        if (ru is null) return items;
        var actor = PrimeRbacReadService.WithActingRole(ru.Employee, ru.Role);
        var filtered = new List<EmployeePrimeServiceFiche>();
        foreach (var f in items)
        {
            if (await rbac.CanAccessFicheAsync(actor, f, "Read", ct) ||
                await rbac.CanAccessFicheAsync(actor, f, "Validate", ct))
                filtered.Add(f);
        }
        return filtered;
    }

    private async Task GuardPiloteExportAsync(
        EmployeePrimeServiceFiche fiche,
        string? userId,
        string? role,
        CancellationToken ct)
    {
        var ru = await userResolver.TryResolveAsync(userId, role, ct);
        var actorRole = ru?.Role ?? role;
        if (!PrimeFicheDistributionAccess.RoleMustWaitForPrimeDistribution(actorRole))
            return;
        var approved = await previewAccess.FicheApprovedByBothWorkflowsAsync(fiche, ct);
        if (!approved)
            throw new PrimeApiException(403, "Export indisponible : validations PRIME en cours.");
    }

    private async Task NotifyEmployeeDesktopAsync(string employeeId, bool approved, CancellationToken ct)
    {
        var authUserIds = new List<int>();
        var subjectIds = new List<Guid>();
        if (int.TryParse(employeeId, out var numericId) && numericId > 0)
            authUserIds.Add(numericId);
        if (Guid.TryParse(employeeId, out var subjectId) && subjectId != Guid.Empty)
            subjectIds.Add(subjectId);
        if (authUserIds.Count == 0 && subjectIds.Count == 0)
            return;

        await desktopPush.NotifyAsync(
            new DesktopPushRequest(
                authUserIds,
                subjectIds,
                "PRIME",
                approved ? "Votre prime a été approuvée." : "Votre prime a été refusée.",
                "prime",
                "/prime"),
            ct);
    }

    private static string CsvEscape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }
}
