using Kyntus.Messaging.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Messaging.Consumers;

/// <summary>
/// Congé validé (Conge.API) → absence Planning + sync plannings existants (chirurgical / regen).
/// </summary>
public sealed class CongeValideConsumer(
    AppDbContext db,
    IPlanningLeaveImpactService leaveImpact,
    ILogger<CongeValideConsumer> logger) : IConsumer<CongeValideMessage>
{
    public async Task Consume(ConsumeContext<CongeValideMessage> context)
    {
        var msg = context.Message;
        if (msg.EmployeId == Guid.Empty || msg.DemandeId == Guid.Empty)
        {
            logger.LogWarning("CongeValideMessage incomplet (EmployeId/DemandeId).");
            return;
        }

        var user = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Guid == msg.EmployeId, context.CancellationToken);
        if (user is null)
        {
            logger.LogWarning(
                "Employé {EmployeId} introuvable pour congé validé {DemandeId}.",
                msg.EmployeId,
                msg.DemandeId);
            return;
        }

        var existing = await db.Conges
            .FirstOrDefaultAsync(c => c.SourceDemandeId == msg.DemandeId, context.CancellationToken);

        var start = DateOnly.FromDateTime(msg.DateDebut);
        var end = DateOnly.FromDateTime(msg.DateFin);
        if (end < start)
            (start, end) = (end, start);

        var absenceType = MapAbsenceType(msg.TypeConge, msg.TypeExceptionnel);
        var reason = BuildReason(msg);

        // v64.2 : congé validé dont les dates ont été modifiées dans Congés — l'ancienne période
        // est rendue au planning avant de poser la nouvelle.
        (DateOnly Debut, DateOnly Fin)? anciennePeriode = existing is not null
            && (existing.StartDate != start || existing.EndDate != end)
                ? (existing.StartDate, existing.EndDate)
                : null;

        if (existing is not null)
        {
            existing.StartDate = start;
            existing.EndDate = end;
            existing.AbsenceType = absenceType;
            existing.Reason = reason;
            existing.Status = CongeStatus.Approved;
        }
        else
        {
            db.Conges.Add(new Conge
            {
                UserId = user.Id,
                StartDate = start,
                EndDate = end,
                Reason = reason,
                AbsenceType = absenceType,
                Status = CongeStatus.Approved,
                SourceDemandeId = msg.DemandeId,
                CreatedAt = DateTime.UtcNow,
            });
        }

        await db.SaveChangesAsync(context.CancellationToken);

        logger.LogInformation(
            "Absence Planning créée/màj pour {EmployeId} (demande {DemandeId}) du {Debut} au {Fin}.",
            msg.EmployeId,
            msg.DemandeId,
            start,
            end);

        try
        {
            if (anciennePeriode is { } ap)
                await leaveImpact.SyncAfterAbsenceChangeAsync(
                    user.Id, ap.Debut, ap.Fin, absenceRemoved: true, context.CancellationToken);
            await leaveImpact.SyncAfterAbsenceChangeAsync(
                user.Id, start, end, absenceRemoved: false, context.CancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Sync planning après congé validé échouée (demande {DemandeId}, user {UserId}).",
                msg.DemandeId,
                user.Id);
        }
    }

    private static AbsenceType MapAbsenceType(string? typeConge, string? typeExceptionnel)
    {
        var type = (typeConge ?? "Annuel").Trim();
        var ex = (typeExceptionnel ?? string.Empty).Trim();

        if (type.Equals("Maladie", StringComparison.OrdinalIgnoreCase))
            return AbsenceType.ArretMaladie;
        if (type.Equals("Maternite", StringComparison.OrdinalIgnoreCase)
            || ex.Equals("Maternite", StringComparison.OrdinalIgnoreCase))
            return AbsenceType.Maternite;
        if (type.Equals("Paternite", StringComparison.OrdinalIgnoreCase))
            return AbsenceType.Paternite;
        if (ex.Contains("Deces", StringComparison.OrdinalIgnoreCase)
            || ex.Contains("Deuil", StringComparison.OrdinalIgnoreCase))
            return AbsenceType.DeuilFamilial;
        if (type.Equals("Exceptionnel", StringComparison.OrdinalIgnoreCase))
            return AbsenceType.CongesPayes;

        return AbsenceType.CongesPayes;
    }

    private static string BuildReason(CongeValideMessage msg)
    {
        var label = string.IsNullOrWhiteSpace(msg.TypeExceptionnel)
            ? msg.TypeConge
            : $"{msg.TypeConge}/{msg.TypeExceptionnel}";
        return msg.Modification
            ? $"Congé validé ({label}), dates modifiées — {msg.NombreJours} j."
            : $"Congé validé ({label}) — {msg.NombreJours} j.";
    }
}
