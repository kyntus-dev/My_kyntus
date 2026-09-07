using Microsoft.EntityFrameworkCore;
using Prime.Infrastructure.Persistence;

namespace Prime.Infrastructure.Services;

/// <summary>Paiement par employé (ligne de synthèse) marqué par le comptable après validation RH.</summary>
public sealed class PrimeGlobalSynthesisPaymentService(PrimeDbContext db)
{
    private static bool IsAccountant(string? role) =>
        role is "Comptable" or "Comptabilité" or "Admin";

    /// <summary>Une prime est payable dès que sa ligne est validée par la RH (LineStatus = Approved).</summary>
    private static bool LineApproved(GlobalPoolSynthesisLineEntity line) =>
        string.Equals(line.LineStatus, GlobalPoolSynthesisLineStatuses.Approved, StringComparison.Ordinal);

    /// <summary>Etat agrégé du paiement d'une synthèse à partir du compte des lignes payées.</summary>
    public static string DeriveState(int paidLines, int totalLines)
    {
        if (totalLines <= 0 || paidLines <= 0) return GlobalPoolPaymentState.Unpaid;
        return paidLines >= totalLines ? GlobalPoolPaymentState.Paid : GlobalPoolPaymentState.Partial;
    }

    public async Task<(bool ok, string? error)> SetLinePaymentAsync(
        Guid lineId,
        string userId,
        string role,
        bool paid,
        DateTimeOffset? paidAt,
        string? reference,
        CancellationToken ct = default)
    {
        if (!IsAccountant(role))
            return (false, "Seul le comptable peut marquer le paiement.");

        var line = await db.GlobalPoolSynthesisLines
            .Include(l => l.ScopeSynthesis)
            .FirstOrDefaultAsync(l => l.Id == lineId, ct);
        if (line is null) return (false, "Ligne introuvable.");
        if (paid && !LineApproved(line))
            return (false, "Cette prime n'est pas encore validée par la RH.");

        ApplyPayment(line, userId, role, paid, paidAt, reference);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool ok, string? error)> PayAllAsync(
        Guid scopeSynthesisId,
        string userId,
        string role,
        DateTimeOffset? paidAt,
        string? reference,
        CancellationToken ct = default)
    {
        if (!IsAccountant(role))
            return (false, "Seul le comptable peut marquer le paiement.");

        var synthesis = await db.GlobalPoolScopeSyntheses
            .Include(s => s.Lines)
            .FirstOrDefaultAsync(s => s.Id == scopeSynthesisId, ct);
        if (synthesis is null) return (false, "Synthèse introuvable.");

        // Ne payer que les primes validées par la RH et non déjà payées.
        var payable = synthesis.Lines
            .Where(l => LineApproved(l) &&
                        !string.Equals(l.PaymentStatus, GlobalPoolPaymentStatuses.Paid, StringComparison.Ordinal))
            .ToList();
        if (payable.Count == 0)
            return (false, "Aucune prime validée à payer sur ce périmètre.");

        foreach (var line in payable)
            ApplyPayment(line, userId, role, true, paidAt, reference);

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    private void ApplyPayment(
        GlobalPoolSynthesisLineEntity line,
        string userId,
        string role,
        bool paid,
        DateTimeOffset? paidAt,
        string? reference)
    {
        var now = DateTimeOffset.UtcNow;
        var uid = userId.Trim();
        if (paid)
        {
            line.PaymentStatus = GlobalPoolPaymentStatuses.Paid;
            line.PaidAt = paidAt ?? now;
            line.PaidByUserId = uid;
            line.PaymentReference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        }
        else
        {
            line.PaymentStatus = GlobalPoolPaymentStatuses.Unpaid;
            line.PaidAt = null;
            line.PaidByUserId = null;
            line.PaymentReference = null;
        }

        line.ScopeSynthesis.UpdatedAt = now;
        db.GlobalPoolSynthesisLineHistories.Add(new GlobalPoolSynthesisLineHistoryEntity
        {
            Id = Guid.NewGuid(),
            LineId = line.Id,
            At = now,
            Action = paid ? GlobalPoolSynthesisLineHistoryActions.Paid : GlobalPoolSynthesisLineHistoryActions.Unpaid,
            ActorUserId = uid,
            ActorRole = role,
            Comment = paid && !string.IsNullOrWhiteSpace(reference) ? reference.Trim() : null,
        });
    }
}
