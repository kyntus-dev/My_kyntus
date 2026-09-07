using Microsoft.EntityFrameworkCore;
using Prime.Application;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Infrastructure.Persistence;

namespace Prime.Infrastructure.Services;

public sealed class PrimeCelluleDraftGlobalPoolAppService(
    PrimeDbContext db,
    PrimeOrgScopeService org,
    GlobalPoolWorkflowService poolWf) : IPrimeCelluleDraftGlobalPoolAppService
{
    private static bool PoolUnlocked(SupervisorCellulePrimeDraft d) =>
        GlobalPoolWorkflowService.LegacyPoolUnlocked(d);

    private static CelluleDraftGlobalPoolStateDto MapState(SupervisorCellulePrimeDraft d) => new()
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
        PoolDistributionUnlocked = PoolUnlocked(d),
    };

    private async Task<SupervisorCellulePrimeDraft> GetOwnedDraftAsync(
        Guid draftId,
        string supervisorUserId,
        CancellationToken ct)
    {
        var sup = supervisorUserId.Trim();
        var d = await db.SupervisorCellulePrimeDrafts.FirstOrDefaultAsync(x => x.Id == draftId, ct)
            ?? throw new KeyNotFoundException();
        if (!string.Equals(d.SupervisorUserId, sup, StringComparison.Ordinal) ||
            !await org.SupervisorOwnsCelluleAsync(sup, d.CelluleId, ct))
            throw new UnauthorizedAccessException("Accès refusé pour ce brouillon.");
        return d;
    }

    private async Task<string?> RoleOfUserAsync(string userId, CancellationToken ct)
    {
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId.Trim(), ct);
        return e?.Role;
    }

    public async Task<CelluleDraftGlobalPoolStateDto> GetStateAsync(
        Guid draftId,
        string supervisorUserId,
        CancellationToken ct = default)
    {
        var d = await GetOwnedDraftAsync(draftId, supervisorUserId, ct);
        return MapState(d);
    }

    public async Task<FileExportResultDto> DownloadExcelAsync(
        Guid draftId,
        string supervisorUserId,
        string? actingUserId,
        CancellationToken ct = default)
    {
        var d = await GetOwnedDraftAsync(draftId, supervisorUserId, ct);
        if (d.GlobalPoolExcelContent is not { Length: > 0 })
            throw new KeyNotFoundException(
                "Aucun fichier de synthèse globale disponible pour ce brouillon. Générez-le via POST …/global-pool/generate.");

        var uid = (actingUserId ?? supervisorUserId).Trim();
        var role = await RoleOfUserAsync(uid, ct);
        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Utilisateur inconnu.");

        var legacyOk = PoolUnlocked(d);
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

    public async Task<CelluleDraftGlobalPoolStateDto> GenerateLegacyExcelAsync(
        Guid draftId,
        string supervisorUserId,
        CancellationToken ct = default)
    {
        var d = await GetOwnedDraftAsync(draftId, supervisorUserId, ct);
        var bytes = await PrimeGlobalRecapExcelBuilder.BuildAsync(db, d.Period, ct);
        var now = DateTimeOffset.UtcNow;
        var safePeriod = string.IsNullOrWhiteSpace(d.Period) ? "periode" : d.Period.Trim().Replace('/', '-');
        d.GlobalPoolExcelContent = bytes;
        d.GlobalPoolFileName = $"PRIME_synthese_globale_{safePeriod}.xlsx";
        d.GlobalPoolUploadedAt = now;
        d.GlobalPoolUploadedByUserId = "generated";
        d.GlobalPoolManagerApprovedAt = null;
        d.GlobalPoolManagerApprovedByUserId = null;
        d.GlobalPoolRhApprovedAt = null;
        d.GlobalPoolRhApprovedByUserId = null;
        d.GlobalPoolComptaAckAt = null;
        d.GlobalPoolComptaAckByUserId = null;
        d.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return MapState(d);
    }

    public Task<CelluleDraftGlobalPoolStateDto> ApproveManagerAsync(
        Guid draftId,
        string supervisorUserId,
        GlobalPoolActingUserRequest body,
        CancellationToken ct = default)
    {
        _ = (draftId, supervisorUserId, body, ct);
        throw new PrimeApiException(410, PrimeGlobalPoolActorResolver.ManagerStepGoneMessage);
    }

    public async Task<CelluleDraftGlobalPoolStateDto> ApproveRhAsync(
        Guid draftId,
        string supervisorUserId,
        GlobalPoolActingUserRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.UserId))
            throw new ArgumentException("UserId est requis.");
        var role = await RoleOfUserAsync(body.UserId, ct);
        if (!string.Equals(role, "RH", StringComparison.Ordinal) && !string.Equals(role, "Admin", StringComparison.Ordinal))
            throw new PrimeApiException(403, "Seul le rôle RH (ou Admin) peut valider cette étape.");

        var d = await GetOwnedDraftAsync(draftId, supervisorUserId, ct);
        if (d.GlobalPoolExcelContent is not { Length: > 0 })
            throw new ArgumentException("Générez d'abord le fichier de synthèse globale (POST …/global-pool/generate).");

        var now = DateTimeOffset.UtcNow;
        d.GlobalPoolRhApprovedAt = now;
        d.GlobalPoolRhApprovedByUserId = body.UserId.Trim();
        d.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return MapState(d);
    }

    public async Task<CelluleDraftGlobalPoolStateDto> AckComptaAsync(
        Guid draftId,
        string supervisorUserId,
        GlobalPoolActingUserRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.UserId))
            throw new ArgumentException("UserId est requis.");
        var role = await RoleOfUserAsync(body.UserId, ct);
        if (!string.Equals(role, "Comptable", StringComparison.Ordinal) && !string.Equals(role, "Admin", StringComparison.Ordinal))
            throw new PrimeApiException(403, "Seul le rôle Comptable (ou Admin) peut accuser réception.");

        var d = await GetOwnedDraftAsync(draftId, supervisorUserId, ct);
        if (!PoolUnlocked(d))
            throw new ArgumentException("La validation RH doit être complétée avant la comptabilité.");

        var now = DateTimeOffset.UtcNow;
        d.GlobalPoolComptaAckAt = now;
        d.GlobalPoolComptaAckByUserId = body.UserId.Trim();
        d.UpdatedAt = now;
        await db.SaveChangesAsync(ct);
        return MapState(d);
    }
}
