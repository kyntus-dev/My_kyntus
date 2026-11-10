using System.Globalization;
using Kyntus.Messaging.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Messaging.Consumers;

/// <summary>
/// Congés par e-mail : nouvelle demande → valideur ; congé validé, refusé ou annulé → employé.
/// File RabbitMQ dédiée : n'interfère pas avec la mise à jour des absences Planning.
/// </summary>
public sealed class CongeNotificationEmailConsumer(AppDbContext db, INotificationEmailQueue queue)
    : IConsumer<CongeDemandeMessage>, IConsumer<CongeValideMessage>, IConsumer<CongeRefuseMessage>
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public async Task Consume(ConsumeContext<CongeDemandeMessage> context)
    {
        var msg = context.Message;
        if (msg.ManagerId == Guid.Empty || msg.DemandeId == Guid.Empty)
            return;
        var employee = await EmployeeNameAsync(msg.EmployeId, context.CancellationToken);
        await queue.EnqueueNotificationAsync(
            new NotificationEmailRecipients(EmployeeIds: [msg.ManagerId]),
            new NotificationEmailContent(
                NotificationEmailSources.Conge,
                "Nouvelle demande de congé",
                $"{employee} demande un congé {Period(msg.DateDebut, msg.DateFin)}. La demande attend votre validation.",
                "/conges/validation",
                $"conge-demande:{msg.DemandeId:N}"),
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<CongeValideMessage> context)
    {
        var msg = context.Message;
        if (msg.EmployeId == Guid.Empty || msg.DemandeId == Guid.Empty)
            return;
        var by = string.IsNullOrWhiteSpace(msg.ValidateurNom) ? string.Empty : $" par {msg.ValidateurNom.Trim()}";
        await queue.EnqueueNotificationAsync(
            new NotificationEmailRecipients(EmployeeIds: [msg.EmployeId]),
            new NotificationEmailContent(
                NotificationEmailSources.Conge,
                "Congé validé",
                $"Votre congé {Period(msg.DateDebut, msg.DateFin)} ({Days(msg.NombreJours)}) a été validé{by}.",
                "/mes-conges",
                $"conge-valide:{msg.DemandeId:N}:{msg.DateValidation:yyyyMMddHHmmss}"),
            context.CancellationToken);
    }

    public async Task Consume(ConsumeContext<CongeRefuseMessage> context)
    {
        var msg = context.Message;
        if (msg.EmployeId == Guid.Empty || msg.DemandeId == Guid.Empty)
            return;
        var motif = msg.Motif?.Trim() ?? string.Empty;
        var cancelled = motif.StartsWith("Annulation", StringComparison.OrdinalIgnoreCase);
        var body = cancelled
            ? "Votre congé validé a été annulé."
            : "Votre demande de congé a été refusée.";
        if (motif.Length > 0 && !cancelled)
            body += $" Motif : {motif}";
        await queue.EnqueueNotificationAsync(
            new NotificationEmailRecipients(EmployeeIds: [msg.EmployeId]),
            new NotificationEmailContent(
                NotificationEmailSources.Conge,
                cancelled ? "Congé annulé" : "Demande de congé refusée",
                body,
                "/mes-conges",
                $"conge-refus:{msg.DemandeId:N}:{(context.MessageId ?? Guid.NewGuid()):N}"),
            context.CancellationToken);
    }

    private async Task<string> EmployeeNameAsync(Guid employeeId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Guid == employeeId)
            .Select(u => new { u.FirstName, u.LastName })
            .FirstOrDefaultAsync(ct);
        var name = user is null ? string.Empty : $"{user.FirstName} {user.LastName}".Trim();
        return name.Length == 0 ? "Un collaborateur" : name;
    }

    public static string Period(DateTime start, DateTime end)
    {
        if (end < start)
            (start, end) = (end, start);
        return start.Date == end.Date
            ? $"le {start.ToString("dddd d MMMM yyyy", Fr)}"
            : $"du {start.ToString("d MMMM yyyy", Fr)} au {end.ToString("d MMMM yyyy", Fr)}";
    }

    private static string Days(double days) =>
        days == 1 ? "1 jour" : $"{days.ToString("0.#", Fr)} jours";
}
