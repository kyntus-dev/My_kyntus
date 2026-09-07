using Microsoft.EntityFrameworkCore;
using Prime.Application;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;

namespace Prime.Infrastructure.Services;

public sealed class PrimeGlobalPoolScopeAppService(
    PrimeDbContext db,
    IPrimeRequestUserResolver userResolver,
    GlobalPoolWorkflowService poolWf,
    PrimeGlobalSynthesisReadinessService readiness,
    PrimeGlobalSynthesisService synthesis,
    PrimeGlobalSynthesisLineService lineService,
    PrimeGlobalSynthesisPaymentService paymentService,
    PrimeFicheValidationHistoryService validationHistory,
    PrimeRbacReadService rbac) : IPrimeGlobalPoolScopeAppService
{
    private static bool LegacyScopeUnlocked(GlobalPoolScopeSynthesisEntity s) =>
        GlobalPoolWorkflowService.LegacyScopePoolUnlocked(s);

    private async Task<string?> RoleOfUserAsync(string userId, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId.Trim(), ct);
        return e?.Role;
    }

    private async Task<bool> ManagesOperationalDepartmentAsync(string userId, CancellationToken ct) =>
        await db.BusinessDepartments.AsNoTracking()
            .AnyAsync(d => d.ManagerEmployeeId == userId && d.IsActive && d.Kind == "Operational", ct);

    private async Task<PrimeResolvedUser> ResolvePoolActorAsync(string? userId, string? declaredRole, CancellationToken ct)
    {
        var resolved = await userResolver.TryResolveForValidationAsync(userId, declaredRole, ct)
            ?? throw new PrimeApiException(401, "Utilisateur invalide.");

        var realRole = resolved.Employee.Role?.Trim() ?? "";
        var declared = resolved.Role?.Trim() ?? "";
        var managesOperational = await ManagesOperationalDepartmentAsync(resolved.UserId, ct);
        var actingRole = PrimeGlobalPoolActorResolver.ResolveActingRole(
            resolved.Employee, realRole, declared, managesOperational)
            ?? throw new PrimeApiException(403, "Rôle non autorisé pour la validation de synthèse.");

        return new PrimeResolvedUser(resolved.UserId, actingRole, resolved.Employee);
    }

    public Task<GlobalPoolReadinessDto> GetReadinessAsync(string period, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(period))
            throw new ArgumentException("period est requis.");
        return readiness.GetReadinessAsync(period, ct);
    }

    public async Task<GlobalSynthesisLinesResponseDto> GetSynthesisLinesAsync(
        string period,
        string scopeType,
        string scopeId,
        Guid? scopeSynthesisId,
        string? userId,
        CancellationToken ct = default)
    {
        if (!GlobalPoolScopeTypes.IsValid(scopeType))
            throw new ArgumentException("scopeType invalide.");

        Guid? sid = scopeSynthesisId;
        if (!sid.HasValue && !string.IsNullOrWhiteSpace(userId))
        {
            var role = await RoleOfUserAsync(userId, ct);
            if (PrimeGlobalPoolActorResolver.IsPoolStakeholderRole(role) &&
                await readiness.IsScopeReadyAsync(period, scopeType, scopeId, ct))
            {
                var entity = await synthesis.EnsureAsync(period, scopeType, scopeId, userId.Trim(), ct);
                sid = entity?.Id;
            }
        }

        var lines = await synthesis.ListLinesAsync(period, scopeType, scopeId, sid, ct);
        return new GlobalSynthesisLinesResponseDto
        {
            ScopeSynthesisId = sid,
            ValidationReady = sid.HasValue && lines.Any(l => l.LineId.HasValue),
            Lines = lines,
        };
    }

    public async Task<GlobalSynthesisSummaryDto> GetSynthesisSummaryAsync(
        string period,
        string scopeType,
        string scopeId,
        Guid? scopeSynthesisId,
        CancellationToken ct = default)
    {
        var lines = await synthesis.ListLinesAsync(period, scopeType, scopeId, scopeSynthesisId, ct);
        return PrimeGlobalSynthesisService.Summarize(lines);
    }

    public async Task<GenerateSynthesisResultDto> GenerateSynthesisAsync(
        GenerateScopeSynthesisRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.UserId))
            throw new ArgumentException("userId est requis.");
        var role = await RoleOfUserAsync(body.UserId, ct);
        if (role is not ("RH" or "Admin"))
            throw new PrimeApiException(403, "Génération réservée à RH ou Admin.");

        try
        {
            var (entity, _) = await synthesis.GenerateAsync(body.Period, body.ScopeType, body.ScopeId, body.UserId, ct);
            return new GenerateSynthesisResultDto(entity.Id, entity.FileName, entity.GeneratedAt);
        }
        catch (InvalidOperationException ex)
        {
            throw new ArgumentException(ex.Message);
        }
    }

    public async Task<EnsureSynthesisResultDto> EnsureSynthesisAsync(
        GenerateScopeSynthesisRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.UserId))
            throw new ArgumentException("userId est requis.");
        if (!GlobalPoolScopeTypes.IsValid(body.ScopeType))
            throw new ArgumentException("scopeType invalide.");
        var role = await RoleOfUserAsync(body.UserId, ct);
        if (!PrimeGlobalPoolActorResolver.IsPoolStakeholderRole(role))
            throw new PrimeApiException(403, "Rôle non autorisé.");

        try
        {
            var entity = await synthesis.EnsureAsync(body.Period, body.ScopeType, body.ScopeId, body.UserId, ct);
            if (entity is null)
                return new EnsureSynthesisResultDto(null, false, null, null, "Périmètre non prêt pour la synthèse.");
            return new EnsureSynthesisResultDto(entity.Id, true, entity.FileName, entity.GeneratedAt, null);
        }
        catch (InvalidOperationException ex)
        {
            throw new ArgumentException(ex.Message);
        }
    }

    public async Task<IReadOnlyList<GlobalPoolScopeSynthesisInboxItemDto>> GetScopeInboxAsync(
        string userId,
        string? role,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId est requis.");

        var actor = await ResolvePoolActorAsync(userId, role, ct);
        var list = await db.GlobalPoolScopeSyntheses.AsNoTracking()
            .Where(s => s.ExcelContent != null && s.ExcelContent.Length > 0)
            .OrderByDescending(s => s.UpdatedAt)
            .ToListAsync(ct);

        var result = new List<GlobalPoolScopeSynthesisInboxItemDto>();
        foreach (var s in list)
            result.Add(await MapScopeInboxAsync(s, actor.Role, ct));
        return result;
    }

    public async Task<FileExportResultDto> DownloadScopeExcelAsync(
        Guid scopeSynthesisId,
        string userId,
        CancellationToken ct = default)
    {
        var s = await db.GlobalPoolScopeSyntheses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == scopeSynthesisId, ct);
        if (s?.ExcelContent is not { Length: > 0 })
            throw new KeyNotFoundException();

        var role = await RoleOfUserAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Utilisateur inconnu.");

        var legacyOk = LegacyScopeUnlocked(s);
        var fullyUnlocked = await poolWf.PoolDistributionUnlockedAsync(s, ct);
        var hasApprovedLines = await db.GlobalPoolSynthesisLines.AsNoTracking()
            .AnyAsync(l => l.ScopeSynthesisId == s.Id && l.LineStatus == GlobalPoolSynthesisLineStatuses.Approved, ct);
        if (!PrimeFicheDistributionAccess.CanDownloadGlobalPoolSynthesis(role, legacyOk, fullyUnlocked, hasApprovedLines))
            throw new PrimeApiException(403, "Fichier non diffusé : validations en attente.");

        var approvedExcel = await synthesis.BuildApprovedExportExcelAsync(scopeSynthesisId, ct);
        var content = approvedExcel is { Length: > 0 } ? approvedExcel : s.ExcelContent;
        var name = string.IsNullOrWhiteSpace(s.FileName) ? "prime-synthese.xlsx" : s.FileName.Trim();
        return new FileExportResultDto(
            content!,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            name);
    }

    public async Task<GlobalPoolScopeSynthesisInboxItemDto> ApproveScopeStepAsync(
        Guid scopeSynthesisId,
        GlobalPoolApproveStepRequest body,
        CancellationToken ct = default)
    {
        var ru = await userResolver.TryResolveAsync(body.UserId, body.Role, ct)
            ?? throw new PrimeApiException(401, "Utilisateur invalide.");
        var role = await RoleOfUserAsync(ru.UserId, ct);
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Utilisateur inconnu.");

        var s = await db.GlobalPoolScopeSyntheses.FirstOrDefaultAsync(x => x.Id == scopeSynthesisId, ct)
            ?? throw new KeyNotFoundException();
        if (!await poolWf.UsesConfigurableWorkflowAsync(ct))
            throw new ArgumentException("Workflow configurable requis ou routes legacy scope.");

        var (ok, msg) = await poolWf.TryApproveScopeStepAsync(s, body.StepId, ru.UserId, role, ct);
        if (!ok)
            throw new ArgumentException(msg ?? "Approbation impossible.");
        return await MapScopeInboxAsync(s, role, ct);
    }

    public Task<GlobalPoolScopeSynthesisInboxItemDto> ApproveScopeManagerAsync(
        Guid scopeSynthesisId,
        GlobalPoolActingUserRequest body,
        CancellationToken ct = default)
    {
        _ = (scopeSynthesisId, body, ct);
        throw new PrimeApiException(410, PrimeGlobalPoolActorResolver.ManagerStepGoneMessage);
    }

    public async Task<GlobalPoolScopeSynthesisInboxItemDto> ApproveScopeRhAsync(
        Guid scopeSynthesisId,
        GlobalPoolActingUserRequest body,
        CancellationToken ct = default)
    {
        if (await poolWf.UsesConfigurableWorkflowAsync(ct))
            throw new ArgumentException("Utilisez approve-step.");
        var role = await RoleOfUserAsync(body.UserId, ct);
        if (role is not ("RH" or "Admin"))
            throw new PrimeApiException(403, "RH uniquement.");

        var s = await db.GlobalPoolScopeSyntheses.FirstOrDefaultAsync(x => x.Id == scopeSynthesisId, ct)
            ?? throw new KeyNotFoundException();
        var now = DateTimeOffset.UtcNow;
        s.RhApprovedAt = now;
        s.RhApprovedByUserId = body.UserId.Trim();
        s.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return await MapScopeInboxAsync(s, role!, ct);
    }

    public async Task<GlobalPoolScopeSynthesisInboxItemDto> AckScopeComptaAsync(
        Guid scopeSynthesisId,
        GlobalPoolActingUserRequest body,
        CancellationToken ct = default)
    {
        if (await poolWf.UsesConfigurableWorkflowAsync(ct))
            throw new ArgumentException("Utilisez approve-step.");
        var role = await RoleOfUserAsync(body.UserId, ct);
        if (role is not ("Comptable" or "Comptabilité" or "Admin"))
            throw new PrimeApiException(403, "Compta uniquement.");

        var s = await db.GlobalPoolScopeSyntheses.FirstOrDefaultAsync(x => x.Id == scopeSynthesisId, ct)
            ?? throw new KeyNotFoundException();
        if (!LegacyScopeUnlocked(s))
            throw new ArgumentException("Validation RH requise avant la comptabilité.");

        var now = DateTimeOffset.UtcNow;
        var approvedExcel = await synthesis.BuildApprovedExportExcelAsync(scopeSynthesisId, ct);
        if (approvedExcel is { Length: > 0 })
            s.ExcelContent = approvedExcel;
        s.ComptaAckAt = now;
        s.ComptaAckByUserId = body.UserId.Trim();
        s.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return await MapScopeInboxAsync(s, role!, ct);
    }

    public async Task RejectLineAsync(Guid lineId, RejectSynthesisLineRequest body, CancellationToken ct = default)
    {
        var ru = await ResolvePoolActorAsync(body.UserId, body.Role, ct);
        try
        {
            var (ok, msg) = await lineService.RejectLineAsync(lineId, ru.UserId, ru.Role, body.Reason, ct);
            if (!ok)
                throw new ArgumentException(msg ?? "Rejet impossible.");
        }
        catch (DbUpdateException ex)
        {
            throw new PrimeApiException(409, DbExceptionMessages.FromSaveChanges(ex));
        }
    }

    public async Task ApproveLineAsync(Guid lineId, GlobalPoolActingUserRequest body, CancellationToken ct = default)
    {
        var ru = await ResolvePoolActorAsync(body.UserId, body.Role, ct);
        try
        {
            var (ok, msg) = await lineService.ApproveLineAsync(lineId, ru.UserId, ru.Role, ct);
            if (!ok)
                throw new ArgumentException(msg ?? "Approbation impossible.");
        }
        catch (DbUpdateException ex)
        {
            throw new PrimeApiException(409, DbExceptionMessages.FromSaveChanges(ex));
        }
    }

    public async Task UpdateLineAdjustmentsAsync(
        Guid lineId,
        UpdateSynthesisLineAdjustmentsRequest body,
        CancellationToken ct = default)
    {
        var ru = await ResolvePoolActorAsync(body.UserId, body.Role, ct);
        try
        {
            var (ok, msg) = await lineService.UpdateRegularizationAsync(
                lineId, ru.UserId, ru.Role, body.RegularizationAmount, ct);
            if (!ok)
                throw new ArgumentException(msg ?? "Mise à jour impossible.");
        }
        catch (DbUpdateException ex)
        {
            throw new PrimeApiException(409, DbExceptionMessages.FromSaveChanges(ex));
        }
    }

    public async Task<IReadOnlyList<SupervisorSynthesisTrackingItemDto>> GetSupervisorSynthesisTrackingAsync(
        string supervisorUserId,
        string? period,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(supervisorUserId))
            throw new ArgumentException("supervisorUserId est requis.");

        var sup = supervisorUserId.Trim();
        var per = period?.Trim();

        var rows = await (
            from f in db.EmployeePrimeServiceFiches.AsNoTracking()
            where f.SupervisorUserId == sup && (per == null || per == "" || f.Period == per)
            join emp in db.Employees.AsNoTracking() on f.EmployeeId equals emp.Id
            join srv in db.Services.AsNoTracking() on f.ServiceId equals srv.Id
            join cel in db.Cellules.AsNoTracking() on srv.CelluleId equals cel.Id
            select new { f, emp, srv, cel }
        ).ToListAsync(ct);
        if (rows.Count == 0) return [];

        var ficheIds = rows.Select(r => r.f.Id).ToList();
        var lines = await (
            from l in db.GlobalPoolSynthesisLines.AsNoTracking()
            where ficheIds.Contains(l.FicheId)
            join syn in db.GlobalPoolScopeSyntheses.AsNoTracking() on l.ScopeSynthesisId equals syn.Id
            select new { l, syn }
        ).ToListAsync(ct);

        var lineByFiche = lines
            .GroupBy(x => x.l.FicheId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.syn.UpdatedAt).First());

        return rows.Select(r =>
        {
            lineByFiche.TryGetValue(r.f.Id, out var match);
            var syn = match?.syn;
            var unlocked = syn is not null && GlobalPoolWorkflowService.LegacyScopePoolUnlocked(syn);
            return new SupervisorSynthesisTrackingItemDto
            {
                FicheId = r.f.Id,
                EmployeeId = r.emp.Id,
                EmployeeDisplayName = $"{r.emp.FirstName} {r.emp.LastName}".Trim(),
                CelluleName = r.cel.Name,
                ServiceName = r.srv.Name,
                ValidationStatus = r.f.ValidationStatus,
                LineStatus = match?.l.LineStatus,
                RhDecision = match?.l.RhDecision ?? GlobalPoolLineDecisions.Pending,
                ManagerDecision = match?.l.ManagerDecision ?? GlobalPoolLineDecisions.Pending,
                RhRejectionReason = match?.l.RhRejectionReason,
                ManagerRejectionReason = match?.l.ManagerRejectionReason,
                RejectedByRole = match?.l.RejectedByRole,
                PaymentStatus = match?.l.PaymentStatus ?? GlobalPoolPaymentStatuses.Unpaid,
                PaidAt = match?.l.PaidAt,
                ManagerApproved = syn?.ManagerApprovedAt.HasValue ?? false,
                RhApproved = syn?.RhApprovedAt.HasValue ?? false,
                PoolDistributionUnlocked = unlocked,
                ScopeSynthesisId = syn?.Id,
                ScopeLabel = syn is null ? null : $"{syn.ScopeType} {syn.ScopeDisplayName} ({syn.Period})",
            };
        })
        .OrderBy(x => x.CelluleName).ThenBy(x => x.ServiceName).ThenBy(x => x.EmployeeDisplayName)
        .ToList();
    }

    public async Task<IReadOnlyList<EmployeePrimePaymentTrackingDto>> GetMySynthesisTrackingAsync(
        string? userId,
        string? role,
        CancellationToken ct = default)
    {
        var ru = await userResolver.TryResolveAsync(userId, role, ct)
            ?? throw new PrimeApiException(401, "Utilisateur invalide.");
        var realRole = ru.Employee.Role?.Trim() ?? "";
        if (realRole is not ("Pilote" or "Admin"))
            throw new PrimeApiException(403, "Réservé au pilote.");

        var employeeId = ru.UserId;
        var rows = await (
            from f in db.EmployeePrimeServiceFiches.AsNoTracking()
            where f.EmployeeId == employeeId
            join srv in db.Services.AsNoTracking() on f.ServiceId equals srv.Id
            join cel in db.Cellules.AsNoTracking() on srv.CelluleId equals cel.Id
            select new { f, srv, cel }
        ).ToListAsync(ct);
        if (rows.Count == 0) return [];

        var ficheIds = rows.Select(r => r.f.Id).ToList();
        var lines = await (
            from l in db.GlobalPoolSynthesisLines.AsNoTracking()
            where ficheIds.Contains(l.FicheId)
            join s in db.GlobalPoolScopeSyntheses.AsNoTracking() on l.ScopeSynthesisId equals s.Id
            select new { l, s }
        ).ToListAsync(ct);
        var lineByFiche = lines
            .GroupBy(x => x.l.FicheId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.s.UpdatedAt).First().l);

        return rows.Select(r =>
        {
            lineByFiche.TryGetValue(r.f.Id, out var line);
            var approved = line is not null &&
                string.Equals(line.LineStatus, GlobalPoolSynthesisLineStatuses.Approved, StringComparison.Ordinal);
            return new EmployeePrimePaymentTrackingDto
            {
                FicheId = r.f.Id,
                Period = r.f.Period,
                CelluleName = r.cel.Name,
                ServiceName = r.srv.Name,
                PrimeAmount = line?.PrimeAmount,
                ChallengeAmount = line?.ChallengeAmount,
                TotalAmount = line?.TotalAmount,
                NetPayableAmount = line?.NetPayableAmount ?? line?.TotalAmount,
                LineStatus = line?.LineStatus,
                PaymentStatus = line?.PaymentStatus ?? GlobalPoolPaymentStatuses.Unpaid,
                PaidAt = line?.PaidAt,
                PaymentReference = line?.PaymentReference,
                CanViewFiche = approved,
            };
        })
        .OrderByDescending(x => x.Period).ThenBy(x => x.ServiceName)
        .ToList();
    }

    public async Task<IReadOnlyList<PrimeFicheValidationHistoryFeedItemDto>> GetSynthesisTrackingFeedAsync(
        string? userId,
        string? role,
        string? period,
        bool? mineOnly,
        string? action,
        CancellationToken ct = default)
    {
        PrimeResolvedUser? ru = null;
        if (!string.IsNullOrWhiteSpace(userId))
            ru = await ResolvePoolActorAsync(userId, role, ct);

        if (!string.IsNullOrWhiteSpace(action))
        {
            var a = action.Trim();
            if (!string.Equals(a, PrimeFicheValidationHistoryActions.Approved, StringComparison.Ordinal) &&
                !string.Equals(a, PrimeFicheValidationHistoryActions.Rejected, StringComparison.Ordinal) &&
                !string.Equals(a, "LineRejected", StringComparison.Ordinal) &&
                !string.Equals(a, GlobalPoolSynthesisLineHistoryActions.Paid, StringComparison.Ordinal) &&
                !string.Equals(a, GlobalPoolSynthesisLineHistoryActions.Unpaid, StringComparison.Ordinal))
                throw new ArgumentException("Filtre action invalide.");
        }

        return await validationHistory.ListSynthesisTrackingFeedAsync(
            ru, rbac, period, mineOnly ?? true, action, 500, ct);
    }

    public async Task<IReadOnlyList<GlobalPoolSynthesisLineHistoryDto>> GetSynthesisLineHistoryAsync(
        Guid lineId,
        string? userId,
        string? role,
        CancellationToken ct = default)
    {
        PrimeResolvedUser? ru = null;
        if (!string.IsNullOrWhiteSpace(userId))
            ru = await ResolvePoolActorAsync(userId, role, ct);

        var rows = await validationHistory.ListSynthesisLineHistoryAsync(lineId, ru, rbac, ct);
        if (rows.Count == 0)
        {
            var exists = await db.GlobalPoolSynthesisLines.AsNoTracking()
                .AnyAsync(l => l.Id == lineId, ct);
            if (!exists)
                throw new KeyNotFoundException();
        }
        return rows;
    }

    public async Task SetLinePaymentAsync(Guid lineId, SetSynthesisLinePaymentRequest body, CancellationToken ct = default)
    {
        var ru = await ResolvePoolActorAsync(body.UserId, body.Role, ct);
        var (ok, msg) = await paymentService.SetLinePaymentAsync(
            lineId, ru.UserId, ru.Role, body.Paid, body.PaidAt, body.Reference, ct);
        if (!ok)
            throw new ArgumentException(msg ?? "Paiement impossible.");
    }

    public async Task PayAllAsync(Guid scopeSynthesisId, PaySynthesisAllRequest body, CancellationToken ct = default)
    {
        var ru = await ResolvePoolActorAsync(body.UserId, body.Role, ct);
        var (ok, msg) = await paymentService.PayAllAsync(
            scopeSynthesisId, ru.UserId, ru.Role, body.PaidAt, body.Reference, ct);
        if (!ok)
            throw new ArgumentException(msg ?? "Paiement impossible.");
    }

    public async Task<IReadOnlyList<string>> ListPeriodsAsync(CancellationToken ct = default) =>
        await db.EmployeePrimeServiceFiches.AsNoTracking()
            .Select(f => f.Period)
            .Distinct()
            .OrderByDescending(p => p)
            .ToListAsync(ct);

    private async Task<GlobalPoolScopeSynthesisInboxItemDto> MapScopeInboxAsync(
        GlobalPoolScopeSynthesisEntity s,
        string employeeRole,
        CancellationToken ct)
    {
        var pending = await poolWf.UsesConfigurableWorkflowAsync(ct)
            ? await poolWf.PendingActionForScopeAsync(s, employeeRole, ct)
            : employeeRole switch
            {
                "RH" => !s.RhApprovedAt.HasValue,
                "Comptable" or "Comptabilité" => LegacyScopeUnlocked(s) && !s.ComptaAckAt.HasValue,
                "Admin" => !s.RhApprovedAt.HasValue ||
                           (LegacyScopeUnlocked(s) && !s.ComptaAckAt.HasValue),
                _ => false,
            };
        var unlocked = await poolWf.PoolDistributionUnlockedAsync(s, ct);
        var totalLines = 0;
        var paidLines = 0;
        var rhDecidedLines = 0;
        var managerDecidedLines = 0;
        var approvedLines = 0;
        var rejectedLines = 0;
        var lines = await db.GlobalPoolSynthesisLines.AsNoTracking()
            .Where(l => l.ScopeSynthesisId == s.Id)
            .Select(l => new { l.PaymentStatus, l.RhDecision, l.ManagerDecision, l.LineStatus })
            .ToListAsync(ct);
        totalLines = lines.Count;
        paidLines = lines.Count(l => l.PaymentStatus == GlobalPoolPaymentStatuses.Paid);
        rhDecidedLines = lines.Count(l => l.RhDecision != GlobalPoolLineDecisions.Pending);
        managerDecidedLines = lines.Count(l => l.ManagerDecision != GlobalPoolLineDecisions.Pending);
        approvedLines = lines.Count(l => l.LineStatus == GlobalPoolSynthesisLineStatuses.Approved);
        rejectedLines = lines.Count(l => l.LineStatus == GlobalPoolSynthesisLineStatuses.LineRejected);

        var paymentState = PrimeGlobalSynthesisPaymentService.DeriveState(paidLines, approvedLines);
        List<GlobalPoolInboxStepStatusDto>? stepStatuses = null;
        Guid? suggestedStep = null;
        if (await poolWf.UsesConfigurableWorkflowAsync(ct))
        {
            stepStatuses = await poolWf.ListScopeInboxStepStatusesAsync(s, ct);
            suggestedStep = await poolWf.GetSuggestedScopeApproveStepIdAsync(s, employeeRole, ct);
        }
        return new GlobalPoolScopeSynthesisInboxItemDto
        {
            ScopeSynthesisId = s.Id,
            Period = s.Period,
            ScopeType = s.ScopeType,
            ScopeId = s.ScopeId,
            ScopeDisplayName = s.ScopeDisplayName,
            HasFile = s.ExcelContent is { Length: > 0 },
            FileName = s.FileName,
            GeneratedAt = s.GeneratedAt,
            ManagerApprovedAt = s.ManagerApprovedAt,
            RhApprovedAt = s.RhApprovedAt,
            ComptaAckAt = s.ComptaAckAt,
            PoolDistributionUnlocked = unlocked,
            PendingActionForUser = pending,
            StepStatuses = stepStatuses,
            SuggestedApproveStepId = suggestedStep,
            PaymentState = paymentState,
            PaidLines = paidLines,
            TotalLines = totalLines,
            RhDecidedLines = rhDecidedLines,
            ManagerDecidedLines = managerDecidedLines,
            ApprovedLines = approvedLines,
            RejectedLines = rejectedLines,
        };
    }
}
