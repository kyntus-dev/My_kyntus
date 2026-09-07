using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Prime.Infrastructure.Persistence;
using Prime.Application.DTOs;

namespace Prime.Infrastructure.Services;

public static class PrimeFicheValidationHistoryActions
{
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
    public const string Submitted = "Submitted";
}

public sealed class PrimeFicheValidationHistoryService(PrimeDbContext? db)
{
    public async Task AppendApprovedAsync(
        EmployeePrimeServiceFiche fiche,
        string fromStatus,
        string toStatus,
        PrimeResolvedUser actor,
        PrimeEmployeeFicheAmounts amountsSnapshot,
        CancellationToken ct = default)
    {
        if (db is null) return;
        await AppendCoreAsync(
            fiche,
            PrimeFicheValidationHistoryActions.Approved,
            fromStatus,
            toStatus,
            actor,
            null,
            amountsSnapshot,
            ct);
    }

    public async Task AppendRejectedAsync(
        EmployeePrimeServiceFiche fiche,
        string fromStatus,
        string toStatus,
        PrimeResolvedUser actor,
        string reason,
        PrimeEmployeeFicheAmounts amountsSnapshot,
        CancellationToken ct = default)
    {
        if (db is null) return;
        await AppendCoreAsync(
            fiche,
            PrimeFicheValidationHistoryActions.Rejected,
            fromStatus,
            toStatus,
            actor,
            reason,
            amountsSnapshot,
            ct);
    }

    public async Task AppendSubmittedAsync(
        EmployeePrimeServiceFiche fiche,
        string fromStatus,
        PrimeResolvedUser actor,
        PrimeEmployeeFicheAmounts amountsSnapshot,
        CancellationToken ct = default)
    {
        if (db is null) return;
        var alreadyTracked = db.EmployeePrimeFicheValidationHistories.Local.Any(h =>
            h.FicheId == fiche.Id &&
            h.Action == PrimeFicheValidationHistoryActions.Submitted);
        if (alreadyTracked ||
            await db.EmployeePrimeFicheValidationHistories.AsNoTracking().AnyAsync(h =>
                h.FicheId == fiche.Id &&
                h.Action == PrimeFicheValidationHistoryActions.Submitted, ct))
            return;

        await AppendCoreAsync(
            fiche,
            PrimeFicheValidationHistoryActions.Submitted,
            fromStatus,
            PrimeValidationWorkflowService.Pending,
            actor,
            null,
            amountsSnapshot,
            ct);
    }

    private async Task AppendCoreAsync(
        EmployeePrimeServiceFiche fiche,
        string action,
        string fromStatus,
        string toStatus,
        PrimeResolvedUser actor,
        string? comment,
        PrimeEmployeeFicheAmounts amountsSnapshot,
        CancellationToken ct)
    {
        var display = $"{actor.Employee.FirstName} {actor.Employee.LastName}".Trim();
        db!.EmployeePrimeFicheValidationHistories.Add(new EmployeePrimeFicheValidationHistory
        {
            Id = Guid.NewGuid(),
            FicheId = fiche.Id,
            At = DateTimeOffset.UtcNow,
            Action = action,
            FromStatus = fromStatus.Trim(),
            ToStatus = toStatus.Trim(),
            ActorUserId = actor.UserId.Trim(),
            ActorRole = actor.Role.Trim(),
            ActorDisplayName = string.IsNullOrWhiteSpace(display) ? actor.UserId : display,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            PrimeAmount = amountsSnapshot.PrimeAmount,
            ChallengeAmount = amountsSnapshot.ChallengeAmount,
            TotalAmount = amountsSnapshot.TotalAmount,
        });
    }

    public async Task<List<PrimeFicheValidationHistoryDto>> ListForFicheAsync(Guid ficheId, CancellationToken ct = default)
    {
        if (db is null) return [];
        var rows = await db.EmployeePrimeFicheValidationHistories.AsNoTracking()
            .Where(h => h.FicheId == ficheId)
            .OrderBy(h => h.At)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    /// <summary>Flux agrégé pour l'écran Suivi validation (RBAC + filtres).</summary>
    public async Task<List<PrimeFicheValidationHistoryFeedItemDto>> ListFeedAsync(
        PrimeResolvedUser? actor,
        PrimeRbacReadService rbac,
        string? period,
        bool mineOnly,
        string? actionFilter,
        int limit = 500,
        CancellationToken ct = default)
    {
        if (db is null) return [];

        var cap = Math.Clamp(limit, 1, 2000);
        var per = period?.Trim();
        var action = actionFilter?.Trim();

        var historyQuery = db.EmployeePrimeFicheValidationHistories.AsNoTracking().AsQueryable();
        if (mineOnly && actor is not null)
            historyQuery = historyQuery.Where(h => h.ActorUserId == actor.UserId);
        if (!string.IsNullOrWhiteSpace(action))
            historyQuery = historyQuery.Where(h => h.Action == action);

        var histories = await historyQuery
            .OrderByDescending(h => h.At)
            .Take(cap * 3)
            .ToListAsync(ct);
        if (histories.Count == 0) return [];

        var ficheIds = histories.Select(h => h.FicheId).Distinct().ToList();
        var ficheQuery = db.EmployeePrimeServiceFiches.AsNoTracking()
            .Where(f => ficheIds.Contains(f.Id));
        if (!string.IsNullOrWhiteSpace(per))
            ficheQuery = ficheQuery.Where(f => f.Period == per);

        var fiches = await ficheQuery.ToListAsync(ct);
        var ficheById = fiches.ToDictionary(f => f.Id);

        var acting = actor is null ? null : PrimeRbacReadService.WithActingRole(actor.Employee, actor.Role);
        var result = new List<PrimeFicheValidationHistoryFeedItemDto>();
        foreach (var h in histories)
        {
            if (!ficheById.TryGetValue(h.FicheId, out var fiche)) continue;
            if (acting is not null)
            {
                var canRead = await rbac.CanAccessFicheAsync(acting, fiche, "Read", ct) ||
                              await rbac.CanAccessFicheAsync(acting, fiche, "Validate", ct);
                if (!canRead) continue;
            }

            result.Add(new PrimeFicheValidationHistoryFeedItemDto
            {
                Id = h.Id,
                FicheId = h.FicheId,
                At = h.At,
                Action = h.Action,
                FromStatus = h.FromStatus,
                ToStatus = h.ToStatus,
                ActorUserId = h.ActorUserId,
                ActorRole = h.ActorRole,
                ActorDisplayName = h.ActorDisplayName,
                Comment = h.Comment,
                PrimeAmount = h.PrimeAmount,
                ChallengeAmount = h.ChallengeAmount,
                TotalAmount = h.TotalAmount,
                EmployeeId = fiche.EmployeeId,
                EmployeeDisplayName = fiche.EmployeeId,
                Period = fiche.Period,
                CelluleName = fiche.CelluleId,
                ServiceName = fiche.ServiceId,
                CurrentValidationStatus = fiche.ValidationStatus,
                Phase = "Fiche",
            });

            if (result.Count >= cap) break;
        }

        if (result.Count == 0) return [];

        var empIds = result.Select(r => r.EmployeeId).Distinct().ToList();
        var svcIds = result.Select(r => r.ServiceName).Distinct().ToList();
        var cellIds = result.Select(r => r.CelluleName).Distinct().ToList();

        var employees = await db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);
        var services = await db.Services.AsNoTracking()
            .Where(s => svcIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        var cellules = await db.Cellules.AsNoTracking()
            .Where(c => cellIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var enriched = result.Select(r =>
        {
            employees.TryGetValue(r.EmployeeId, out var emp);
            services.TryGetValue(r.ServiceName, out var svc);
            cellules.TryGetValue(r.CelluleName, out var cell);
            var display = emp is null ? r.EmployeeDisplayName : $"{emp.FirstName} {emp.LastName}".Trim();
            return new PrimeFicheValidationHistoryFeedItemDto
            {
                Id = r.Id,
                FicheId = r.FicheId,
                At = r.At,
                Action = r.Action,
                FromStatus = r.FromStatus,
                ToStatus = r.ToStatus,
                ActorUserId = r.ActorUserId,
                ActorRole = r.ActorRole,
                ActorDisplayName = r.ActorDisplayName,
                Comment = r.Comment,
                PrimeAmount = r.PrimeAmount,
                ChallengeAmount = r.ChallengeAmount,
                TotalAmount = r.TotalAmount,
                EmployeeId = r.EmployeeId,
                EmployeeDisplayName = string.IsNullOrWhiteSpace(display) ? r.EmployeeId : display,
                Period = r.Period,
                CelluleName = cell?.Name ?? r.CelluleName,
                ServiceName = svc?.Name ?? r.ServiceName,
                CurrentValidationStatus = r.CurrentValidationStatus,
                Phase = r.Phase,
            };
        }).ToList();

        var poolEvents = await ListPoolLineFeedAsync(actor, rbac, per, mineOnly, cap, ct);
        var scopeEvents = await ListScopeApprovalFeedAsync(actor, rbac, per, mineOnly, action, cap, ct);
        return enriched.Concat(poolEvents).Concat(scopeEvents)
            .OrderByDescending(x => x.At).Take(cap).ToList();
    }

    /// <summary>Flux synthèse globale uniquement (RH / Manager / Comptabilité / Admin).</summary>
    public async Task<List<PrimeFicheValidationHistoryFeedItemDto>> ListSynthesisTrackingFeedAsync(
        PrimeResolvedUser? actor,
        PrimeRbacReadService rbac,
        string? period,
        bool mineOnly,
        string? actionFilter,
        int limit = 500,
        CancellationToken ct = default)
    {
        if (db is null) return [];
        var cap = Math.Clamp(limit, 1, 2000);
        var per = period?.Trim();
        var poolEvents = await ListPoolLineFeedAsync(actor, rbac, per, mineOnly, cap, ct);
        var scopeEvents = await ListScopeApprovalFeedAsync(actor, rbac, per, mineOnly, actionFilter, cap, ct);
        var combined = poolEvents.Concat(scopeEvents).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(actionFilter))
        {
            var a = actionFilter.Trim();
            combined = combined.Where(x =>
                string.Equals(x.Action, a, StringComparison.Ordinal) ||
                (string.Equals(a, PrimeFicheValidationHistoryActions.Rejected, StringComparison.Ordinal) &&
                 string.Equals(x.Action, "LineRejected", StringComparison.Ordinal)));
        }
        return combined.OrderByDescending(x => x.At).Take(cap).ToList();
    }

    public async Task<List<GlobalPoolSynthesisLineHistoryDto>> ListSynthesisLineHistoryAsync(
        Guid lineId,
        PrimeResolvedUser? actor,
        PrimeRbacReadService rbac,
        CancellationToken ct = default)
    {
        if (db is null) return [];
        var line = await db.GlobalPoolSynthesisLines.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line is null) return [];
        var fiche = await db.EmployeePrimeServiceFiches.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == line.FicheId, ct);
        if (fiche is null) return [];
        if (actor is not null)
        {
            var acting = PrimeRbacReadService.WithActingRole(actor.Employee, actor.Role);
            var canRead = await rbac.CanAccessFicheAsync(acting, fiche, "Read", ct) ||
                          await rbac.CanAccessFicheAsync(acting, fiche, "Validate", ct);
            if (!canRead) return [];
        }

        var rows = await db.GlobalPoolSynthesisLineHistories.AsNoTracking()
            .Where(h => h.LineId == lineId)
            .ToListAsync(ct);
        rows = rows.OrderBy(h => h.At).ToList();
        if (rows.Count == 0) return [];

        var actorIds = rows.Select(r => r.ActorUserId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking()
            .Where(e => actorIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        return rows.Select(h =>
        {
            employees.TryGetValue(h.ActorUserId, out var emp);
            var display = emp is null ? h.ActorRole : $"{emp.FirstName} {emp.LastName}".Trim();
            return new GlobalPoolSynthesisLineHistoryDto
            {
                Id = h.Id,
                LineId = h.LineId,
                At = h.At,
                Action = h.Action,
                ActorUserId = h.ActorUserId,
                ActorRole = h.ActorRole,
                ActorDisplayName = string.IsNullOrWhiteSpace(display) ? h.ActorRole : display,
                Comment = h.Comment,
            };
        }).ToList();
    }

    /// <summary>
    /// Evènements d'approbation au niveau synthèse (Manager / RH / Comptabilité), surfacés par employé
    /// pour le suivi du second workflow (Phase « Synthèse »).
    /// </summary>
    private async Task<List<PrimeFicheValidationHistoryFeedItemDto>> ListScopeApprovalFeedAsync(
        PrimeResolvedUser? actor,
        PrimeRbacReadService rbac,
        string? period,
        bool mineOnly,
        string? actionFilter,
        int cap,
        CancellationToken ct)
    {
        if (db is null) return [];
        // Seul un filtre "Approved" (ou aucun) doit retourner ces approbations.
        if (!string.IsNullOrWhiteSpace(actionFilter) &&
            !string.Equals(actionFilter, PrimeFicheValidationHistoryActions.Approved, StringComparison.Ordinal))
            return [];

        var synQuery = db.GlobalPoolScopeSyntheses.AsNoTracking()
            .Where(s => s.ManagerApprovedAt != null || s.RhApprovedAt != null || s.ComptaAckAt != null);
        if (!string.IsNullOrWhiteSpace(period))
            synQuery = synQuery.Where(s => s.Period == period);

        var syntheses = await synQuery.ToListAsync(ct);
        syntheses = syntheses.OrderByDescending(s => s.UpdatedAt).Take(cap).ToList();
        if (syntheses.Count == 0) return [];

        var synIds = syntheses.Select(s => s.Id).ToList();
        var rows = await (
            from line in db.GlobalPoolSynthesisLines.AsNoTracking()
            where synIds.Contains(line.ScopeSynthesisId)
            join fiche in db.EmployeePrimeServiceFiches.AsNoTracking() on line.FicheId equals fiche.Id
            select new { line.ScopeSynthesisId, fiche }
        ).ToListAsync(ct);

        var synById = syntheses.ToDictionary(s => s.Id);
        var acting = actor is null ? null : PrimeRbacReadService.WithActingRole(actor.Employee, actor.Role);

        var result = new List<PrimeFicheValidationHistoryFeedItemDto>();
        foreach (var row in rows)
        {
            if (!synById.TryGetValue(row.ScopeSynthesisId, out var syn)) continue;
            if (acting is not null)
            {
                var canRead = await rbac.CanAccessFicheAsync(acting, row.fiche, "Read", ct) ||
                              await rbac.CanAccessFicheAsync(acting, row.fiche, "Validate", ct);
                if (!canRead) continue;
            }

            var scopeLabel = $"{syn.ScopeType} {syn.ScopeDisplayName} ({syn.Period})";
            void Add(DateTimeOffset at, string role, string? byUser, string label)
            {
                if (mineOnly && actor is not null && !string.Equals(byUser, actor.UserId, StringComparison.Ordinal))
                    return;
                result.Add(new PrimeFicheValidationHistoryFeedItemDto
                {
                    Id = Guid.NewGuid(),
                    FicheId = row.fiche.Id,
                    At = at,
                    Action = PrimeFicheValidationHistoryActions.Approved,
                    FromStatus = "",
                    ToStatus = label,
                    ActorUserId = byUser ?? "",
                    ActorRole = role,
                    ActorDisplayName = role,
                    EmployeeId = row.fiche.EmployeeId,
                    EmployeeDisplayName = row.fiche.EmployeeId,
                    Period = row.fiche.Period,
                    CelluleName = row.fiche.CelluleId,
                    ServiceName = row.fiche.ServiceId,
                    CurrentValidationStatus = row.fiche.ValidationStatus,
                    Phase = "GlobalPool",
                    ScopeLabel = scopeLabel,
                });
            }

            if (syn.ManagerApprovedAt is { } mAt) Add(mAt, "Manager", syn.ManagerApprovedByUserId, "Synthèse validée (Manager)");
            if (syn.RhApprovedAt is { } rAt) Add(rAt, "RH", syn.RhApprovedByUserId, "Synthèse validée (RH)");
            if (syn.ComptaAckAt is { } cAt) Add(cAt, "Comptabilité", syn.ComptaAckByUserId, "Synthèse prise en charge (Comptabilité)");
        }

        if (result.Count == 0) return [];
        var empIds = result.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);
        var svcIds = result.Select(r => r.ServiceName).Distinct().ToList();
        var services = await db.Services.AsNoTracking()
            .Where(s => svcIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        var cellIds = result.Select(r => r.CelluleName).Distinct().ToList();
        var cellules = await db.Cellules.AsNoTracking()
            .Where(c => cellIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        return result.Select(r =>
        {
            employees.TryGetValue(r.EmployeeId, out var emp);
            services.TryGetValue(r.ServiceName, out var svc);
            cellules.TryGetValue(r.CelluleName, out var cell);
            var display = emp is null ? r.EmployeeDisplayName : $"{emp.FirstName} {emp.LastName}".Trim();
            return new PrimeFicheValidationHistoryFeedItemDto
            {
                Id = r.Id,
                FicheId = r.FicheId,
                At = r.At,
                Action = r.Action,
                FromStatus = r.FromStatus,
                ToStatus = r.ToStatus,
                ActorUserId = r.ActorUserId,
                ActorRole = r.ActorRole,
                ActorDisplayName = r.ActorDisplayName,
                Comment = r.Comment,
                EmployeeId = r.EmployeeId,
                EmployeeDisplayName = string.IsNullOrWhiteSpace(display) ? r.EmployeeId : display,
                Period = r.Period,
                CelluleName = cell?.Name ?? r.CelluleName,
                ServiceName = svc?.Name ?? r.ServiceName,
                CurrentValidationStatus = r.CurrentValidationStatus,
                Phase = r.Phase,
                ScopeLabel = r.ScopeLabel,
            };
        }).ToList();
    }

    private async Task<List<PrimeFicheValidationHistoryFeedItemDto>> ListPoolLineFeedAsync(
        PrimeResolvedUser? actor,
        PrimeRbacReadService rbac,
        string? period,
        bool mineOnly,
        int cap,
        CancellationToken ct)
    {
        if (db is null) return [];
        var lineHistories = await (
            from h in db.GlobalPoolSynthesisLineHistories.AsNoTracking()
            join line in db.GlobalPoolSynthesisLines.AsNoTracking() on h.LineId equals line.Id
            join syn in db.GlobalPoolScopeSyntheses.AsNoTracking() on line.ScopeSynthesisId equals syn.Id
            join fiche in db.EmployeePrimeServiceFiches.AsNoTracking() on line.FicheId equals fiche.Id
            select new { h, line, syn, fiche }
        ).Take(cap * 4).ToListAsync(ct);
        lineHistories = lineHistories.OrderByDescending(x => x.h.At).Take(cap * 2).ToList();

        var acting = actor is null ? null : PrimeRbacReadService.WithActingRole(actor.Employee, actor.Role);
        var isCompta = actor is not null &&
                       (string.Equals(actor.Role, "Comptable", StringComparison.Ordinal) ||
                        string.Equals(actor.Role, "Comptabilité", StringComparison.Ordinal));

        var result = new List<PrimeFicheValidationHistoryFeedItemDto>();
        foreach (var row in lineHistories)
        {
            if (!string.IsNullOrWhiteSpace(period) && row.fiche.Period != period) continue;
            if (mineOnly && actor is not null && row.h.ActorUserId != actor.UserId) continue;
            if (acting is not null)
            {
                var canRead = await rbac.CanAccessFicheAsync(acting, row.fiche, "Read", ct) ||
                              await rbac.CanAccessFicheAsync(acting, row.fiche, "Validate", ct);
                if (!canRead) continue;
            }
            if (isCompta && !GlobalPoolWorkflowService.LegacyScopePoolUnlocked(row.syn))
                continue;

            var scopeLabel = $"{row.syn.ScopeType} {row.syn.ScopeDisplayName} ({row.syn.Period})";
            var isPayment =
                string.Equals(row.h.Action, GlobalPoolSynthesisLineHistoryActions.Paid, StringComparison.Ordinal) ||
                string.Equals(row.h.Action, GlobalPoolSynthesisLineHistoryActions.Unpaid, StringComparison.Ordinal);
            var action = string.Equals(row.h.Action, GlobalPoolSynthesisLineHistoryActions.LineRejected, StringComparison.Ordinal)
                ? "LineRejected"
                : row.h.Action;
            result.Add(new PrimeFicheValidationHistoryFeedItemDto
            {
                Id = row.h.Id,
                FicheId = row.fiche.Id,
                LineId = row.line.Id,
                At = row.h.At,
                Action = action,
                FromStatus = isPayment ? "" : row.line.LineStatus,
                ToStatus = isPayment ? "" : row.line.LineStatus,
                LineStatus = row.line.LineStatus,
                ActorUserId = row.h.ActorUserId,
                ActorRole = row.h.ActorRole,
                ActorDisplayName = row.h.ActorRole,
                Comment = row.h.Comment,
                EmployeeId = row.fiche.EmployeeId,
                EmployeeDisplayName = row.fiche.EmployeeId,
                Period = row.fiche.Period,
                CelluleName = row.fiche.CelluleId,
                ServiceName = row.fiche.ServiceId,
                CurrentValidationStatus = row.fiche.ValidationStatus,
                Phase = isPayment ? "Paiement" : "GlobalPool",
                ScopeLabel = scopeLabel,
                LineRejectionReason = row.line.RhRejectionReason ?? row.line.ManagerRejectionReason ?? row.line.RejectionReason,
                RejectedByRole = row.line.RejectedByRole,
            });
        }

        if (result.Count == 0) return [];
        var empIds = result.Select(r => r.EmployeeId).Distinct().ToList();
        var employees = await db.Employees.AsNoTracking()
            .Where(e => empIds.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);
        return result.Select(r =>
        {
            employees.TryGetValue(r.EmployeeId, out var emp);
            var display = emp is null ? r.EmployeeDisplayName : $"{emp.FirstName} {emp.LastName}".Trim();
            return new PrimeFicheValidationHistoryFeedItemDto
            {
                Id = r.Id,
                FicheId = r.FicheId,
                LineId = r.LineId,
                At = r.At,
                Action = r.Action,
                FromStatus = r.FromStatus,
                ToStatus = r.ToStatus,
                LineStatus = r.LineStatus,
                ActorUserId = r.ActorUserId,
                ActorRole = r.ActorRole,
                ActorDisplayName = r.ActorDisplayName,
                Comment = r.Comment,
                EmployeeId = r.EmployeeId,
                EmployeeDisplayName = string.IsNullOrWhiteSpace(display) ? r.EmployeeId : display,
                Period = r.Period,
                CelluleName = r.CelluleName,
                ServiceName = r.ServiceName,
                CurrentValidationStatus = r.CurrentValidationStatus,
                Phase = r.Phase,
                ScopeLabel = r.ScopeLabel,
                LineRejectionReason = r.LineRejectionReason,
                RejectedByRole = r.RejectedByRole,
            };
        }).ToList();
    }

    private static PrimeFicheValidationHistoryDto Map(EmployeePrimeFicheValidationHistory h) =>
        new()
        {
            Id = h.Id,
            FicheId = h.FicheId,
            At = h.At,
            Action = h.Action,
            FromStatus = h.FromStatus,
            ToStatus = h.ToStatus,
            ActorUserId = h.ActorUserId,
            ActorRole = h.ActorRole,
            ActorDisplayName = h.ActorDisplayName,
            Comment = h.Comment,
            PrimeAmount = h.PrimeAmount,
            ChallengeAmount = h.ChallengeAmount,
            TotalAmount = h.TotalAmount,
        };

    public static string BuildAuditDetailJson(
        string fromStatus,
        string toStatus,
        PrimeEmployeeFicheAmounts amounts) =>
        JsonSerializer.Serialize(new
        {
            fromStatus,
            toStatus,
            amounts.PrimeAmount,
            amounts.ChallengeAmount,
            amounts.TotalAmount,
        });
}
