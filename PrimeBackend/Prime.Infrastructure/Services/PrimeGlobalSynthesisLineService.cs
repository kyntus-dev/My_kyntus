using Microsoft.EntityFrameworkCore;
using Prime.Infrastructure.Persistence;

namespace Prime.Infrastructure.Services;

public sealed class PrimeGlobalSynthesisLineService(
    PrimeDbContext db,
    PrimeAbsenceSanctionService absenceSanction)
{
    private const string StaleLineMessage =
        "Cette ligne n'existe plus ou la synthèse a été régénérée. Rechargez la page puis réessayez.";

    public async Task<(bool ok, string? error)> RejectLineAsync(
        Guid lineId,
        string userId,
        string role,
        string reason,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return (false, "Un motif de rejet est obligatoire.");

        if (!CanActAsValidator(role))
            return (false, "Seuls RH et Admin peuvent rejeter une ligne de synthèse.");

        try
        {
            return await MutateLineAsync(lineId, userId, role, ct, mutate: (line, uid, now) =>
            {
                var pendingErr = GuardPendingDecisionForRole(line, role);
                if (pendingErr is not null) return pendingErr;

                var trimmedReason = reason.Trim();
                ApplyRejectDecision(line, role, uid, now, trimmedReason);

                db.GlobalPoolSynthesisLineHistories.Add(new GlobalPoolSynthesisLineHistoryEntity
                {
                    Id = Guid.NewGuid(),
                    LineId = line.Id,
                    At = now,
                    Action = GlobalPoolSynthesisLineHistoryActions.LineRejected,
                    ActorUserId = uid,
                    ActorRole = NormalizeActingRole(role),
                    Comment = trimmedReason,
                });

                RecomputeLineStatus(line);
                return null;
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return (false, DbExceptionMessages.FromSaveChanges(ex));
        }
    }

    public async Task<(bool ok, string? error)> UpdateRegularizationAsync(
        Guid lineId,
        string userId,
        string role,
        decimal regularizationAmount,
        CancellationToken ct = default)
    {
        if (!ActsAsRh(role))
            return (false, "Seuls RH et Admin peuvent modifier la régularisation.");

        var divisor = await absenceSanction.GetDivisorDaysAsync(ct);

        try
        {
            return await MutateLineAsync(lineId, userId, role, ct, mutate: (line, uid, now) =>
            {
                if (!GlobalPoolLineDecisions.RegularizationEditable(line.RhDecision))
                    return "La régularisation n'est plus modifiable après validation RH.";

                line.RegularizationAmount = regularizationAmount;
                line.RegularizationUpdatedByUserId = uid;
                line.RegularizationUpdatedAt = now;
                absenceSanction.RecalculateFromStoredAbsences(line, divisor);

                db.GlobalPoolSynthesisLineHistories.Add(new GlobalPoolSynthesisLineHistoryEntity
                {
                    Id = Guid.NewGuid(),
                    LineId = line.Id,
                    At = now,
                    Action = GlobalPoolSynthesisLineHistoryActions.RegularizationUpdated,
                    ActorUserId = uid,
                    ActorRole = NormalizeActingRole(role),
                    Comment = regularizationAmount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                });

                return null;
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return (false, DbExceptionMessages.FromSaveChanges(ex));
        }
    }

    public async Task<(bool ok, string? error)> ApproveLineAsync(
        Guid lineId,
        string userId,
        string role,
        CancellationToken ct = default)
    {
        if (!CanActAsValidator(role))
            return (false, "Seuls RH et Admin peuvent valider une ligne de synthèse.");

        try
        {
            return await MutateLineAsync(
                lineId,
                userId,
                role,
                ct,
                skipIfAlreadyDone: line => IsIdempotentApprove(line, role),
                mutate: (line, uid, now) =>
                {
                    var pendingErr = GuardPendingDecisionForRole(line, role);
                    if (pendingErr is not null) return pendingErr;

                    ApplyApproveDecision(line, role, uid, now);

                db.GlobalPoolSynthesisLineHistories.Add(new GlobalPoolSynthesisLineHistoryEntity
                {
                    Id = Guid.NewGuid(),
                    LineId = line.Id,
                    At = now,
                    Action = GlobalPoolSynthesisLineHistoryActions.Approved,
                    ActorUserId = uid,
                    ActorRole = NormalizeActingRole(role),
                    Comment = null,
                });

                RecomputeLineStatus(line);
                return null;
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            return (false, DbExceptionMessages.FromSaveChanges(ex));
        }
    }

    private async Task<(bool ok, string? error)> MutateLineAsync(
        Guid lineId,
        string userId,
        string role,
        CancellationToken ct,
        Func<GlobalPoolSynthesisLineEntity, string, DateTimeOffset, string?> mutate,
        Func<GlobalPoolSynthesisLineEntity, bool>? skipIfAlreadyDone = null)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var line = await db.GlobalPoolSynthesisLines
            .FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line is null)
            return (false, StaleLineMessage);

        if (skipIfAlreadyDone?.Invoke(line) == true)
            return (true, null);

        var scope = await db.GlobalPoolScopeSyntheses
            .FirstOrDefaultAsync(s => s.Id == line.ScopeSynthesisId, ct);
        if (scope is null)
            return (false, StaleLineMessage);

        var now = DateTimeOffset.UtcNow;
        var uid = userId.Trim();
        var mutateErr = mutate(line, uid, now);
        if (mutateErr is not null)
            return (false, mutateErr);

        await RecomputeScopeRoleApprovalsAsync(scope, ct);
        scope.UpdatedAt = now;

        if (!await db.GlobalPoolSynthesisLines.AsNoTracking().AnyAsync(l => l.Id == lineId, ct))
            return (false, StaleLineMessage);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (true, null);
    }

    private static bool CanActAsValidator(string role) =>
        string.Equals(role, "RH", StringComparison.Ordinal) ||
        string.Equals(role, "Admin", StringComparison.Ordinal);

    private static string NormalizeActingRole(string role) =>
        string.Equals(role, "Admin", StringComparison.Ordinal) ? "Admin" : role;

    /// <summary>Approbation déjà enregistrée pour le rôle RH (Admin agit comme RH).</summary>
    private static bool IsIdempotentApprove(GlobalPoolSynthesisLineEntity line, string role)
    {
        if (!ActsAsRh(role)) return false;
        return string.Equals(line.RhDecision, GlobalPoolLineDecisions.Approved, StringComparison.Ordinal);
    }

    private static string? GuardPendingDecisionForRole(GlobalPoolSynthesisLineEntity line, string role)
    {
        if (ActsAsRh(role) &&
            !string.Equals(line.RhDecision, GlobalPoolLineDecisions.Pending, StringComparison.Ordinal))
        {
            return string.Equals(line.RhDecision, GlobalPoolLineDecisions.Approved, StringComparison.Ordinal)
                ? null
                : "Décision RH déjà enregistrée sur cette ligne.";
        }

        return null;
    }

    private static void ApplyApproveDecision(GlobalPoolSynthesisLineEntity line, string role, string userId, DateTimeOffset now)
    {
        if (ActsAsRh(role) &&
            string.Equals(line.RhDecision, GlobalPoolLineDecisions.Pending, StringComparison.Ordinal))
        {
            line.RhDecision = GlobalPoolLineDecisions.Approved;
            line.RhDecidedByUserId = userId;
            line.RhDecidedAt = now;
            line.RhRejectionReason = null;
        }

        ClearLegacyRejectIfRhApproved(line);
    }

    private static void ApplyRejectDecision(
        GlobalPoolSynthesisLineEntity line,
        string role,
        string userId,
        DateTimeOffset now,
        string reason)
    {
        if (ActsAsRh(role))
        {
            line.RhDecision = GlobalPoolLineDecisions.Rejected;
            line.RhDecidedByUserId = userId;
            line.RhDecidedAt = now;
            line.RhRejectionReason = reason;
        }

        line.RejectedByUserId = userId;
        line.RejectedByRole = NormalizeActingRole(role);
        line.RejectedAt = now;
        line.RejectionReason = reason;
    }

    private static bool ActsAsRh(string role) =>
        string.Equals(role, "RH", StringComparison.Ordinal) ||
        string.Equals(role, "Admin", StringComparison.Ordinal);

    private static void ClearLegacyRejectIfRhApproved(GlobalPoolSynthesisLineEntity line)
    {
        if (!string.Equals(line.RhDecision, GlobalPoolLineDecisions.Approved, StringComparison.Ordinal))
            return;

        line.RejectedByUserId = null;
        line.RejectedByRole = null;
        line.RejectedAt = null;
        line.RejectionReason = null;
    }

    internal static void RecomputeLineStatus(GlobalPoolSynthesisLineEntity line) =>
        line.LineStatus = GlobalPoolLineDecisions.DeriveLineStatus(line.RhDecision, line.ManagerDecision);

    private async Task RecomputeScopeRoleApprovalsAsync(GlobalPoolScopeSynthesisEntity scope, CancellationToken ct)
    {
        var lines = await db.GlobalPoolSynthesisLines
            .AsNoTracking()
            .Where(l => l.ScopeSynthesisId == scope.Id)
            .ToListAsync(ct);
        if (lines.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        var allRhDecided = lines.All(l =>
            !string.Equals(l.RhDecision, GlobalPoolLineDecisions.Pending, StringComparison.Ordinal));

        if (allRhDecided)
        {
            if (!scope.RhApprovedAt.HasValue)
            {
                scope.RhApprovedAt = now;
                scope.RhApprovedByUserId = lines
                    .Where(l => l.RhDecidedByUserId is not null)
                    .OrderByDescending(l => l.RhDecidedAt)
                    .Select(l => l.RhDecidedByUserId)
                    .FirstOrDefault();
            }
        }
        else
        {
            scope.RhApprovedAt = null;
            scope.RhApprovedByUserId = null;
        }
    }
}
