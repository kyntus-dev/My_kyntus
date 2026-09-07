using Microsoft.EntityFrameworkCore;
using Prime.Infrastructure.Persistence;
using Prime.Application.DTOs;

namespace Prime.Infrastructure.Services;

public sealed class PrimeGlobalSynthesisService(
    PrimeDbContext db,
    PrimeGlobalSynthesisReadinessService readiness,
    PrimeValidationWorkflowRuntime wfRuntime,
    PrimeAbsenceSanctionService absenceSanction)
{
    public async Task<List<GlobalSynthesisLineDto>> ListLinesAsync(
        string period,
        string scopeType,
        string scopeId,
        Guid? scopeSynthesisId,
        CancellationToken ct = default)
    {
        var ficheIds = await GetFicheIdsInScopeAsync(period, scopeType, scopeId, ct);
        if (ficheIds.Count == 0) return [];

        if (scopeSynthesisId is { } sid)
            await EnsureScopeLinesAsync(sid, period, scopeType, scopeId, ct);

        var linesByFiche = scopeSynthesisId is { } scopeId2
            ? await db.GlobalPoolSynthesisLines
                .Where(l => l.ScopeSynthesisId == scopeId2)
                .ToDictionaryAsync(l => l.FicheId, ct)
            : new Dictionary<Guid, GlobalPoolSynthesisLineEntity>();

        if (scopeSynthesisId is { } refreshSid && linesByFiche.Count > 0)
            await RefreshPendingLineAbsencesAsync(refreshSid, period, linesByFiche.Values, ct);

        var raw = await (
            from f in db.EmployeePrimeServiceFiches.AsNoTracking()
            where ficheIds.Contains(f.Id)
            join emp in db.Employees.AsNoTracking() on f.EmployeeId equals emp.Id
            join srv in db.Services.AsNoTracking() on f.ServiceId equals srv.Id
            join cel in db.Cellules.AsNoTracking() on srv.CelluleId equals cel.Id
            join pole in db.Poles.AsNoTracking() on cel.PoleId equals pole.Id
            join drf in db.SupervisorCellulePrimeDrafts.AsNoTracking() on f.CellulePrimeDraftId equals drf.Id into drfg
            from drf in drfg.DefaultIfEmpty()
            select new { f, emp, srv, cel, pole, drf }
        ).ToListAsync(ct);

        return raw.Select(x =>
        {
            linesByFiche.TryGetValue(x.f.Id, out var line);
            var amounts = ResolveAmounts(x.f);
            var templateId = x.drf != null ? x.drf.TemplateId : "";
            return MapLineDto(x.f, x.emp, x.srv, x.cel, x.pole, line, amounts, templateId);
        }).OrderBy(l => l.PoleName).ThenBy(l => l.CelluleName).ThenBy(l => l.ServiceName).ThenBy(l => l.EmployeeDisplayName)
            .ToList();
    }

    /// <summary>Régénère l'Excel avec uniquement les lignes approuvées par la RH.</summary>
    public async Task<byte[]?> BuildApprovedExportExcelAsync(Guid scopeSynthesisId, CancellationToken ct = default)
    {
        var scope = await db.GlobalPoolScopeSyntheses.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == scopeSynthesisId, ct);
        if (scope is null) return null;

        var lineEntities = await db.GlobalPoolSynthesisLines.AsNoTracking()
            .Where(l => l.ScopeSynthesisId == scopeSynthesisId
                        && l.LineStatus == GlobalPoolSynthesisLineStatuses.Approved)
            .ToListAsync(ct);
        if (lineEntities.Count == 0) return null;

        var ficheIds = lineEntities.Select(l => l.FicheId).ToList();
        var fiches = await db.EmployeePrimeServiceFiches.AsNoTracking()
            .Where(f => ficheIds.Contains(f.Id))
            .ToDictionaryAsync(f => f.Id, ct);
        var empIds = fiches.Values.Select(f => f.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);
        var svcIds = fiches.Values.Select(f => f.ServiceId).Distinct().ToList();
        var services = await db.Services.AsNoTracking()
            .Where(s => svcIds.Contains(s.Id))
            .Include(s => s.Cellule)
            .ToDictionaryAsync(s => s.Id, ct);

        var dtos = lineEntities.Select(le =>
        {
            fiches.TryGetValue(le.FicheId, out var fiche);
            employees.TryGetValue(fiche?.EmployeeId ?? "", out var emp);
            services.TryGetValue(fiche?.ServiceId ?? le.ServiceId, out var srv);
            var cel = srv?.Cellule;
            return new GlobalSynthesisLineDto
            {
                FicheId = le.FicheId,
                EmployeeId = le.EmployeeId,
                EmployeeDisplayName = emp is null ? le.EmployeeId : $"{emp.FirstName} {emp.LastName}".Trim(),
                EmployeeRole = emp?.Role ?? "",
                PoleId = cel?.PoleId ?? "",
                PoleName = "",
                CelluleId = cel?.Id ?? "",
                CelluleName = cel?.Name ?? "",
                ServiceId = le.ServiceId,
                ServiceName = srv?.Name ?? le.ServiceId,
                PrimeAmount = le.PrimeAmount,
                ChallengeAmount = le.ChallengeAmount,
                TotalAmount = le.TotalAmount,
                AbsenceDayCount = le.AbsenceDayCount,
                SanctionAmount = le.SanctionAmount,
                RegularizationAmount = le.RegularizationAmount,
                NetPayableAmount = le.NetPayableAmount,
                ValidationStatus = fiche?.ValidationStatus ?? "",
                FillingStatus = fiche?.FillingStatus ?? "",
                LineStatus = le.LineStatus,
            };
        }).ToList();

        return PrimeGlobalSynthesisExcelBuilder.Build(scope.Period, scope.ScopeDisplayName, dtos);
    }

    private static GlobalSynthesisLineDto MapLineDto(
        EmployeePrimeServiceFiche f,
        EmployeeEntity emp,
        ServiceEntity srv,
        CelluleEntity cel,
        PoleEntity pole,
        GlobalPoolSynthesisLineEntity? line,
        PrimeEmployeeFicheAmounts amounts,
        string templateId) => new()
    {
        LineId = line?.Id,
        FicheId = f.Id,
        EmployeeId = emp.Id,
        EmployeeDisplayName = $"{emp.FirstName} {emp.LastName}".Trim(),
        EmployeeRole = emp.Role,
        PoleId = pole.Id,
        PoleName = pole.Name,
        CelluleId = cel.Id,
        CelluleName = cel.Name,
        ServiceId = srv.Id,
        ServiceName = srv.Name,
        PrimeAmount = line?.PrimeAmount ?? amounts.PrimeAmount,
        ChallengeAmount = line?.ChallengeAmount ?? amounts.ChallengeAmount,
        TotalAmount = line?.TotalAmount ?? amounts.TotalAmount,
        AbsenceDayCount = line?.AbsenceDayCount ?? 0,
        SanctionAmount = line?.SanctionAmount ?? 0m,
        RegularizationAmount = line?.RegularizationAmount ?? 0m,
        NetPayableAmount = line?.NetPayableAmount ?? line?.TotalAmount ?? amounts.TotalAmount,
        ValidationStatus = f.ValidationStatus,
        FillingStatus = f.FillingStatus,
        LineStatus = line?.LineStatus,
        LineRejectionReason = line?.RejectionReason,
        RhDecision = line?.RhDecision ?? GlobalPoolLineDecisions.Pending,
        ManagerDecision = line?.ManagerDecision ?? GlobalPoolLineDecisions.Pending,
        RhRejectionReason = line?.RhRejectionReason,
        ManagerRejectionReason = line?.ManagerRejectionReason,
        RejectedByRole = line?.RejectedByRole,
        PaymentStatus = line?.PaymentStatus ?? GlobalPoolPaymentStatuses.Unpaid,
        PaidAt = line?.PaidAt,
        PaymentReference = line?.PaymentReference,
        SupervisorUserId = f.SupervisorUserId,
        TemplateId = templateId,
    };

    public static GlobalSynthesisSummaryDto Summarize(IReadOnlyList<GlobalSynthesisLineDto> lines) => new()
    {
        LineCount = lines.Count,
        TotalPrime = lines.Sum(l => l.PrimeAmount ?? 0m),
        TotalChallenge = lines.Sum(l => l.ChallengeAmount ?? 0m),
        TotalAmount = lines.Sum(l => l.TotalAmount ?? 0m),
        TotalSanction = lines.Sum(l => l.SanctionAmount),
        TotalRegularization = lines.Sum(l => l.RegularizationAmount),
        TotalNetPayable = lines.Sum(l => l.NetPayableAmount ?? l.TotalAmount ?? 0m),
        LinesRejected = lines.Count(l =>
            string.Equals(l.LineStatus, GlobalPoolSynthesisLineStatuses.LineRejected, StringComparison.Ordinal)),
    };

    /// <summary>
    /// Prépare la synthèse à l'ouverture d'un périmètre prêt : crée l'enregistrement + lignes de validation,
    /// génère l'Excel si absent (sans réinitialiser les validations existantes).
    /// </summary>
    public async Task<GlobalPoolScopeSynthesisEntity?> EnsureAsync(
        string period,
        string scopeType,
        string scopeId,
        string userId,
        CancellationToken ct = default)
    {
        if (!GlobalPoolScopeTypes.IsValid(scopeType)) return null;
        if (!await readiness.IsScopeReadyAsync(period, scopeType, scopeId, ct))
            return null;

        var per = period.Trim();
        var displayName = await ResolveScopeDisplayNameAsync(scopeType, scopeId, ct);
        var now = DateTimeOffset.UtcNow;

        var existing = await db.GlobalPoolScopeSyntheses
            .FirstOrDefaultAsync(s => s.Period == per && s.ScopeType == scopeType && s.ScopeId == scopeId, ct);

        if (existing is null)
        {
            existing = new GlobalPoolScopeSynthesisEntity
            {
                Id = Guid.NewGuid(),
                Period = per,
                ScopeType = scopeType,
                ScopeId = scopeId,
                ScopeDisplayName = displayName,
                UpdatedAt = now,
            };
            db.GlobalPoolScopeSyntheses.Add(existing);
            await db.SaveChangesAsync(ct);
        }

        await EnsureScopeLinesAsync(existing.Id, period, scopeType, scopeId, ct);

        if (existing.ExcelContent is not { Length: > 0 })
        {
            var lineDtos = await ListValidatedFichesForScopeAsync(period, scopeType, scopeId, ct);
            var safeScope = scopeId.Replace('/', '-');
            existing.ExcelContent = PrimeGlobalSynthesisExcelBuilder.Build(per, displayName, lineDtos);
            existing.FileName = $"PRIME_synthese_{scopeType}_{safeScope}_{per}.xlsx";
            existing.GeneratedAt = now;
            existing.GeneratedByUserId = userId.Trim();
            existing.ScopeDisplayName = displayName;
            existing.UpdatedAt = now;
            await db.SaveChangesAsync(ct);
        }

        return existing;
    }

    public async Task<(GlobalPoolScopeSynthesisEntity entity, byte[] excel)> GenerateAsync(
        string period,
        string scopeType,
        string scopeId,
        string userId,
        CancellationToken ct = default)
    {
        if (!GlobalPoolScopeTypes.IsValid(scopeType))
            throw new ArgumentException("scopeType invalide (Service, Cellule, Pole).", nameof(scopeType));

        if (!await readiness.IsScopeReadyAsync(period, scopeType, scopeId, ct))
            throw new InvalidOperationException("Le périmètre n'est pas prêt pour la génération de synthèse.");

        var displayName = await ResolveScopeDisplayNameAsync(scopeType, scopeId, ct);
        var lineDtos = await ListValidatedFichesForScopeAsync(period, scopeType, scopeId, ct);
        var excel = PrimeGlobalSynthesisExcelBuilder.Build(period.Trim(), displayName, lineDtos);

        var per = period.Trim();
        var existing = await db.GlobalPoolScopeSyntheses
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Period == per && s.ScopeType == scopeType && s.ScopeId == scopeId, ct);

        var now = DateTimeOffset.UtcNow;
        var safeScope = scopeId.Replace('/', '-');
        var fileName = $"PRIME_synthese_{scopeType}_{safeScope}_{per}.xlsx";

        if (existing is null)
        {
            existing = new GlobalPoolScopeSynthesisEntity
            {
                Id = Guid.NewGuid(),
                Period = per,
                ScopeType = scopeType,
                ScopeId = scopeId,
                ScopeDisplayName = displayName,
            };
            db.GlobalPoolScopeSyntheses.Add(existing);
        }

        db.GlobalPoolSynthesisLines.RemoveRange(existing.Lines);
        var oldApprovals = await db.GlobalPoolApprovals.Where(a => a.ScopeSynthesisId == existing.Id).ToListAsync(ct);
        db.GlobalPoolApprovals.RemoveRange(oldApprovals);

        existing.ScopeDisplayName = displayName;
        existing.ExcelContent = excel;
        existing.FileName = fileName;
        existing.GeneratedAt = now;
        existing.GeneratedByUserId = userId.Trim();
        existing.ManagerApprovedAt = null;
        existing.ManagerApprovedByUserId = null;
        existing.RhApprovedAt = null;
        existing.RhApprovedByUserId = null;
        existing.ComptaAckAt = null;
        existing.ComptaAckByUserId = null;
        existing.UpdatedAt = now;

        foreach (var dto in lineDtos)
        {
            var amounts = new PrimeEmployeeFicheAmounts(dto.PrimeAmount, dto.ChallengeAmount, dto.TotalAmount);
            var line = new GlobalPoolSynthesisLineEntity
            {
                Id = Guid.NewGuid(),
                ScopeSynthesisId = existing.Id,
                FicheId = dto.FicheId,
                EmployeeId = dto.EmployeeId,
                ServiceId = dto.ServiceId,
                PrimeAmount = amounts.PrimeAmount,
                ChallengeAmount = amounts.ChallengeAmount,
                TotalAmount = amounts.TotalAmount,
                LineStatus = GlobalPoolSynthesisLineStatuses.PendingReview,
                RhDecision = GlobalPoolLineDecisions.Pending,
                ManagerDecision = GlobalPoolLineDecisions.Pending,
            };
            await absenceSanction.ApplyAbsenceSanctionAsync(line, per, ct);
            db.GlobalPoolSynthesisLines.Add(line);
        }

        await db.SaveChangesAsync(ct);
        return (existing, excel);
    }

    private async Task<List<GlobalSynthesisLineDto>> ListValidatedFichesForScopeAsync(
        string period,
        string scopeType,
        string scopeId,
        CancellationToken ct)
    {
        var terminals = (await wfRuntime.GetTerminalStatusesAsync(ct))
            .Where(s => !string.Equals(s, PrimeValidationWorkflowService.Rejected, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var all = await ListLinesAsync(period, scopeType, scopeId, null, ct);
        return all
            .Where(l => string.Equals(l.FillingStatus, "Complete", StringComparison.OrdinalIgnoreCase)
                        && terminals.Contains(l.ValidationStatus))
            .ToList();
    }

    private async Task<List<Guid>> GetFicheIdsInScopeAsync(string period, string scopeType, string scopeId, CancellationToken ct)
    {
        var per = period.Trim();
        var fiches = db.EmployeePrimeServiceFiches.AsNoTracking().Where(f => f.Period == per);

        List<string> svcIds;
        if (string.Equals(scopeType, GlobalPoolScopeTypes.Service, StringComparison.Ordinal))
        {
            svcIds = [scopeId];
        }
        else if (string.Equals(scopeType, GlobalPoolScopeTypes.Cellule, StringComparison.Ordinal))
        {
            svcIds = await db.Services.AsNoTracking()
                .Where(s => s.CelluleId == scopeId)
                .Select(s => s.Id)
                .ToListAsync(ct);
        }
        else if (string.Equals(scopeType, GlobalPoolScopeTypes.Pole, StringComparison.Ordinal))
        {
            svcIds = await (
                from s in db.Services.AsNoTracking()
                join c in db.Cellules.AsNoTracking() on s.CelluleId equals c.Id
                where c.PoleId == scopeId
                select s.Id
            ).ToListAsync(ct);
        }
        else
        {
            return [];
        }

        if (svcIds.Count == 0) return [];

        // Ne retenir que les fiches des pilotes ACTUELLEMENT rattachés (source DB) : une fiche
        // d'un pilote retiré/déplacé ne doit plus apparaître dans la synthèse.
        var currentPilotIds = await db.Employees.AsNoTracking()
            .Where(e => e.Role == "Pilote" && e.ServiceId != null && svcIds.Contains(e.ServiceId))
            .Select(e => e.Id)
            .ToListAsync(ct);
        if (currentPilotIds.Count == 0) return [];

        return await fiches
            .Where(f => svcIds.Contains(f.ServiceId) && currentPilotIds.Contains(f.EmployeeId))
            .Select(f => f.Id)
            .ToListAsync(ct);
    }

    /// <summary>Crée les lignes manquantes pour une synthèse existante (rétrocompatibilité).</summary>
    private async Task EnsureScopeLinesAsync(
        Guid scopeSynthesisId,
        string period,
        string scopeType,
        string scopeId,
        CancellationToken ct)
    {
        // Ne pas exiger l'Excel : les lignes (données des fiches approuvées) doivent être créées
        // dès qu'un périmètre est prêt, indépendamment de la génération du fichier Excel.
        var scope = await db.GlobalPoolScopeSyntheses
            .FirstOrDefaultAsync(s => s.Id == scopeSynthesisId, ct);
        if (scope is null) return;

        var ficheIds = await GetFicheIdsInScopeAsync(period, scopeType, scopeId, ct);
        if (ficheIds.Count == 0) return;

        // Source de vérité = lignes réellement persistées (évite les conflits de tracking en parallèle).
        var existing = await db.GlobalPoolSynthesisLines.AsNoTracking()
            .Where(l => l.ScopeSynthesisId == scope.Id)
            .Select(l => l.FicheId)
            .ToListAsync(ct);
        var existingSet = existing.ToHashSet();
        var missingIds = ficheIds.Where(id => !existingSet.Contains(id)).ToList();
        if (missingIds.Count == 0) return;

        var terminals = (await wfRuntime.GetTerminalStatusesAsync(ct))
            .Where(s => !string.Equals(s, PrimeValidationWorkflowService.Rejected, StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        var fiches = await db.EmployeePrimeServiceFiches.AsNoTracking()
            .Where(f => missingIds.Contains(f.Id))
            .ToListAsync(ct);

        var added = false;
        foreach (var f in fiches)
        {
            if (!string.Equals(f.FillingStatus, "Complete", StringComparison.OrdinalIgnoreCase)) continue;
            if (!terminals.Contains(f.ValidationStatus)) continue;
            var amounts = ResolveAmounts(f);
            var line = new GlobalPoolSynthesisLineEntity
            {
                Id = Guid.NewGuid(),
                ScopeSynthesisId = scope.Id,
                FicheId = f.Id,
                EmployeeId = f.EmployeeId,
                ServiceId = f.ServiceId,
                PrimeAmount = amounts.PrimeAmount,
                ChallengeAmount = amounts.ChallengeAmount,
                TotalAmount = amounts.TotalAmount,
                LineStatus = GlobalPoolSynthesisLineStatuses.PendingReview,
                RhDecision = GlobalPoolLineDecisions.Pending,
                ManagerDecision = GlobalPoolLineDecisions.Pending,
            };
            await absenceSanction.ApplyAbsenceSanctionAsync(line, period, ct);
            db.GlobalPoolSynthesisLines.Add(line);
            added = true;
        }

        if (!added) return;
        scope.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<string> ResolveScopeDisplayNameAsync(string scopeType, string scopeId, CancellationToken ct)
    {
        if (string.Equals(scopeType, GlobalPoolScopeTypes.Service, StringComparison.Ordinal))
        {
            var s = await db.Services.AsNoTracking().FirstOrDefaultAsync(x => x.Id == scopeId, ct);
            return s?.Name ?? scopeId;
        }
        if (string.Equals(scopeType, GlobalPoolScopeTypes.Cellule, StringComparison.Ordinal))
        {
            var c = await db.Cellules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == scopeId, ct);
            return c?.Name ?? scopeId;
        }
        var p = await db.Poles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == scopeId, ct);
        return p?.Name ?? scopeId;
    }

    private static PrimeEmployeeFicheAmounts ResolveAmounts(EmployeePrimeServiceFiche f) =>
        PrimeEmployeeFicheAmountService.ResolveWorkflowDisplayAmounts(f);

    private async Task RefreshPendingLineAbsencesAsync(
        Guid scopeSynthesisId,
        string period,
        IEnumerable<GlobalPoolSynthesisLineEntity> lines,
        CancellationToken ct)
    {
        var pending = lines
            .Where(l => string.Equals(l.RhDecision, GlobalPoolLineDecisions.Pending, StringComparison.Ordinal)
                        && string.Equals(l.ManagerDecision, GlobalPoolLineDecisions.Pending, StringComparison.Ordinal))
            .ToList();
        if (pending.Count == 0) return;

        var counts = await absenceSanction.FetchAbsenceCountsAsync(
            period,
            pending.Select(l => l.EmployeeId),
            ct);
        var divisor = await absenceSanction.GetDivisorDaysAsync(ct);

        var changed = false;
        foreach (var line in pending)
        {
            counts.TryGetValue(line.EmployeeId, out var absenceDays);
            if (line.AbsenceDayCount == absenceDays && line.AbsenceComputedAt.HasValue)
                continue;

            line.AbsenceDayCount = absenceDays;
            absenceSanction.RecalculateFromStoredAbsences(line, divisor);
            line.AbsenceComputedAt = DateTimeOffset.UtcNow;
            changed = true;
        }

        if (!changed) return;
        var scope = await db.GlobalPoolScopeSyntheses.FirstOrDefaultAsync(s => s.Id == scopeSynthesisId, ct);
        if (scope is not null) scope.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
