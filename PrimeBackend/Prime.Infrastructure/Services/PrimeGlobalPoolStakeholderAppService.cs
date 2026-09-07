using Microsoft.EntityFrameworkCore;
using Prime.Application;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;

namespace Prime.Infrastructure.Services;

public sealed class PrimeGlobalPoolStakeholderAppService(
    PrimeDbContext db,
    IPrimeRequestUserResolver userResolver,
    GlobalPoolWorkflowService poolWf) : IPrimeGlobalPoolStakeholderAppService
{
    private static bool LegacyPoolUnlocked(SupervisorCellulePrimeDraft d) =>
        GlobalPoolWorkflowService.LegacyPoolUnlocked(d);

    private static CelluleDraftGlobalPoolStateDto MapState(SupervisorCellulePrimeDraft d, bool unlocked) => new()
    {
        DraftId = d.Id,
        CelluleId = d.CelluleId,
        Period = d.Period,
        HasFile = d.GlobalPoolExcelContent is { Length: > 0 },
        FileName = d.GlobalPoolFileName,
        UploadedAt = d.GlobalPoolUploadedAt,
        ManagerApprovedAt = d.GlobalPoolManagerApprovedAt,
        RhApprovedAt = d.GlobalPoolRhApprovedAt,
        ComptaAckAt = d.GlobalPoolComptaAckAt,
        PoolDistributionUnlocked = unlocked,
    };

    private async Task<GlobalPoolInboxItemDto> MapInboxAsync(
        SupervisorCellulePrimeDraft d,
        string employeeRole,
        CancellationToken ct)
    {
        var pending = await poolWf.UsesConfigurableWorkflowAsync(ct)
            ? await poolWf.PendingActionForUserAsync(d, employeeRole, ct)
            : employeeRole switch
            {
                "RH" => !d.GlobalPoolRhApprovedAt.HasValue,
                "Comptable" or "Comptabilité" => LegacyPoolUnlocked(d) && !d.GlobalPoolComptaAckAt.HasValue,
                "Admin" => !d.GlobalPoolRhApprovedAt.HasValue ||
                           (LegacyPoolUnlocked(d) && !d.GlobalPoolComptaAckAt.HasValue),
                _ => false,
            };
        var unlocked = await poolWf.UsesConfigurableWorkflowAsync(ct)
            ? await poolWf.PoolDistributionUnlockedAsync(d, ct)
            : LegacyPoolUnlocked(d);
        List<GlobalPoolInboxStepStatusDto>? stepStatuses = null;
        Guid? suggestedStep = null;
        if (await poolWf.UsesConfigurableWorkflowAsync(ct))
        {
            stepStatuses = await poolWf.ListInboxStepStatusesAsync(d, ct);
            suggestedStep = await poolWf.GetSuggestedApproveStepIdAsync(d, employeeRole, ct);
        }
        return new GlobalPoolInboxItemDto
        {
            DraftId = d.Id,
            SupervisorUserId = d.SupervisorUserId,
            CelluleId = d.CelluleId,
            Period = d.Period,
            HasFile = d.GlobalPoolExcelContent is { Length: > 0 },
            FileName = d.GlobalPoolFileName,
            UploadedAt = d.GlobalPoolUploadedAt,
            ManagerApprovedAt = d.GlobalPoolManagerApprovedAt,
            RhApprovedAt = d.GlobalPoolRhApprovedAt,
            ComptaAckAt = d.GlobalPoolComptaAckAt,
            PoolDistributionUnlocked = unlocked,
            PendingActionForUser = pending,
            StepStatuses = stepStatuses,
            SuggestedApproveStepId = suggestedStep,
        };
    }

    private async Task<string?> RoleOfUserAsync(string userId, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId.Trim(), ct);
        return e?.Role;
    }

    private async Task<SupervisorCellulePrimeDraft> GetDraftAsync(Guid draftId, CancellationToken ct) =>
        await db.SupervisorCellulePrimeDrafts.FirstOrDefaultAsync(x => x.Id == draftId, ct)
        ?? throw new KeyNotFoundException();

    public async Task<IReadOnlyList<GlobalPoolInboxItemDto>> GetInboxAsync(
        string userId,
        string? role,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId est requis.");

        var resolved = await userResolver.TryResolveForValidationAsync(userId, role, ct)
            ?? throw new PrimeApiException(401, "Utilisateur invalide.");
        var realRole = resolved.Employee.Role?.Trim() ?? "";
        var declared = resolved.Role?.Trim() ?? "";
        var managesOperational = await db.BusinessDepartments.AsNoTracking()
            .AnyAsync(d => d.ManagerEmployeeId == resolved.UserId && d.IsActive && d.Kind == "Operational", ct);
        var poolRole = PrimeGlobalPoolActorResolver.ResolveActingRole(
            resolved.Employee, realRole, declared, managesOperational)
            ?? throw new PrimeApiException(403, "Rôle non autorisé sur la file synthèse globale.");

        var list = await db.SupervisorCellulePrimeDrafts.AsNoTracking()
            .Where(d => d.GlobalPoolExcelContent != null && d.GlobalPoolExcelContent.Length > 0)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync(ct);

        var result = new List<GlobalPoolInboxItemDto>();
        foreach (var d in list)
            result.Add(await MapInboxAsync(d, poolRole, ct));
        return result;
    }

    public async Task<FileExportResultDto> DownloadExcelAsync(Guid draftId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("userId est requis.");

        var d = await GetDraftAsync(draftId, ct);
        if (d.GlobalPoolExcelContent is not { Length: > 0 })
            throw new KeyNotFoundException("Aucun fichier de synthèse globale pour ce brouillon.");

        var role = await RoleOfUserAsync(userId, ct);
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Utilisateur inconnu.");

        var legacyOk = LegacyPoolUnlocked(d);
        var fullyUnlocked = await poolWf.UsesConfigurableWorkflowAsync(ct)
            ? await poolWf.PoolDistributionUnlockedAsync(d, ct)
            : legacyOk;
        var allowRole = string.Equals(role, "Superviseur", StringComparison.Ordinal) ||
                        string.Equals(role, "Admin", StringComparison.Ordinal) ||
                        string.Equals(role, "RH", StringComparison.Ordinal) ||
                        string.Equals(role, "Manager", StringComparison.Ordinal) ||
                        PrimeFicheValidationRoles.IsOperationalApprover(role) ||
                        string.Equals(role, "Comptable", StringComparison.Ordinal) ||
                        string.Equals(role, "Comptabilité", StringComparison.Ordinal);
        if (!allowRole)
            throw new PrimeApiException(403, "Rôle non autorisé à télécharger le fichier global.");
        if (!PrimeFicheDistributionAccess.CanDownloadGlobalPoolSynthesis(role, legacyOk, fullyUnlocked))
            throw new PrimeApiException(403, "Fichier non diffusé : en attente des validations PRIME.");

        var name = string.IsNullOrWhiteSpace(d.GlobalPoolFileName) ? "prime-global-pool.xlsx" : d.GlobalPoolFileName.Trim();
        return new FileExportResultDto(
            d.GlobalPoolExcelContent,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            name);
    }

    public async Task<CelluleDraftGlobalPoolStateDto> ApproveStepAsync(
        Guid draftId,
        GlobalPoolApproveStepRequest body,
        CancellationToken ct = default)
    {
        var ru = await userResolver.TryResolveAsync(body.UserId, body.Role, ct)
            ?? throw new PrimeApiException(401, "Utilisateur invalide.");
        var role = await RoleOfUserAsync(ru.UserId, ct);
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Utilisateur inconnu.");

        var d = await GetDraftAsync(draftId, ct);
        if (!await poolWf.UsesConfigurableWorkflowAsync(ct))
            throw new ArgumentException("Workflow global non configuré — utilisez les routes approve-manager / approve-rh / ack-compta.");

        var (ok, msg) = await poolWf.TryApproveStepAsync(d, body.StepId, ru.UserId, role, ct);
        if (!ok)
            throw new ArgumentException(msg ?? "Approbation impossible.");
        var unlocked = await poolWf.PoolDistributionUnlockedAsync(d, ct);
        return MapState(d, unlocked);
    }

    public Task<CelluleDraftGlobalPoolStateDto> ApproveManagerAsync(
        Guid draftId,
        GlobalPoolActingUserRequest body,
        CancellationToken ct = default)
    {
        _ = (draftId, body, ct);
        throw new PrimeApiException(410, PrimeGlobalPoolActorResolver.ManagerStepGoneMessage);
    }

    public async Task<CelluleDraftGlobalPoolStateDto> ApproveRhAsync(
        Guid draftId,
        GlobalPoolActingUserRequest body,
        CancellationToken ct = default)
    {
        if (await poolWf.UsesConfigurableWorkflowAsync(ct))
            throw new ArgumentException("Utilisez POST .../approve-step avec l'identifiant d'étape (workflow configurable).");
        if (string.IsNullOrWhiteSpace(body.UserId))
            throw new ArgumentException("UserId est requis.");
        var role = await RoleOfUserAsync(body.UserId, ct);
        if (!string.Equals(role, "RH", StringComparison.Ordinal) && !string.Equals(role, "Admin", StringComparison.Ordinal))
            throw new PrimeApiException(403, "Seul le rôle RH (ou Admin) peut valider cette étape.");

        var d = await GetDraftAsync(draftId, ct);
        if (d.GlobalPoolExcelContent is not { Length: > 0 })
            throw new ArgumentException("Aucun fichier de synthèse globale sur ce brouillon.");

        var now = DateTimeOffset.UtcNow;
        d.GlobalPoolRhApprovedAt = now;
        d.GlobalPoolRhApprovedByUserId = body.UserId.Trim();
        d.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return MapState(d, LegacyPoolUnlocked(d));
    }

    public async Task<CelluleDraftGlobalPoolStateDto> AckComptaAsync(
        Guid draftId,
        GlobalPoolActingUserRequest body,
        CancellationToken ct = default)
    {
        if (await poolWf.UsesConfigurableWorkflowAsync(ct))
            throw new ArgumentException("Utilisez POST .../approve-step avec l'identifiant d'étape (workflow configurable).");
        if (string.IsNullOrWhiteSpace(body.UserId))
            throw new ArgumentException("UserId est requis.");
        var role = await RoleOfUserAsync(body.UserId, ct);
        if (!string.Equals(role, "Comptable", StringComparison.Ordinal) &&
            !string.Equals(role, "Comptabilité", StringComparison.Ordinal) &&
            !string.Equals(role, "Admin", StringComparison.Ordinal))
            throw new PrimeApiException(403, "Seul le rôle Comptabilité (ou Admin) peut accuser réception.");

        var d = await GetDraftAsync(draftId, ct);
        if (!LegacyPoolUnlocked(d))
            throw new ArgumentException("La validation RH doit être complétée avant la comptabilité.");

        var now = DateTimeOffset.UtcNow;
        d.GlobalPoolComptaAckAt = now;
        d.GlobalPoolComptaAckByUserId = body.UserId.Trim();
        d.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return MapState(d, LegacyPoolUnlocked(d));
    }
}
