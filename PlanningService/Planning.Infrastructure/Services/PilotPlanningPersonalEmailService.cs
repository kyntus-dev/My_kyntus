using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Application.PilotPlanning;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// À la publication (ou republication) d'un planning, chaque agent actif affecté reçoit son planning
/// de la semaine sur son adresse professionnelle. L'e-mail passe par la file d'envoi
/// (cadence limitée, nouvelles tentatives) ; il est toujours envoyé, indépendamment de la préférence
/// « notifications par e-mail ».
/// </summary>
public sealed class PilotPlanningPersonalEmailService(
    AppDbContext db,
    INotificationEmailQueue emailQueue,
    ILogger<PilotPlanningPersonalEmailService> logger) : IPilotPlanningPersonalEmailService
{
    public const string Category = "planning";

    public async Task SendToAssignedPilotsAsync(int planningId, bool updated, CancellationToken ct = default)
    {
        try
        {
            await SendCoreAsync(planningId, updated, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Mise en file des e-mails planning échouée (planningId={PlanningId}).", planningId);
        }
    }

    private async Task SendCoreAsync(int planningId, bool updated, CancellationToken ct)
    {
        if (!await emailQueue.IsEnabledAsync(ct))
        {
            logger.LogInformation("Envoi e-mail non configuré : planning {PlanningId} non envoyé par e-mail.", planningId);
            return;
        }

        var planning = await db.WeeklyPlannings
            .AsNoTracking()
            .Include(p => p.SubService)
            .Include(p => p.ShiftAssignments)
                .ThenInclude(a => a.Shift)
            .Include(p => p.ShiftAssignments)
                .ThenInclude(a => a.SubServiceShiftConfig)
            .FirstOrDefaultAsync(p => p.Id == planningId, ct);
        if (planning is null)
            return;

        var userIds = planning.ShiftAssignments.Select(a => a.UserId).Distinct().ToList();
        if (userIds.Count == 0)
            return;

        var agents = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.IsActive)
            .ToListAsync(ct);

        // Un double clic sur « Publier » ne produit qu'un e-mail ; une nouvelle publication plus tard, si.
        var stamp = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        var subject = PilotPlanningEmailComposer.Subject(planning.WeekCode, updated);
        var queued = 0;

        foreach (var user in agents)
        {
            if (!PilotPlanningEmailComposer.IsValidPersonalEmail(user.Email))
            {
                logger.LogWarning(
                    "Agent {UserId} sans adresse professionnelle valide — planning {WeekCode} non envoyé.",
                    user.Id,
                    planning.WeekCode);
                continue;
            }

            var days = planning.ShiftAssignments
                .Where(a => a.UserId == user.Id)
                .Select(a => new PilotPlanningDayRow(
                    a.AssignedDate,
                    a.SubServiceShiftConfig?.Label ?? a.Shift?.Label ?? "—",
                    a.SubServiceShiftConfig?.StartTime.ToString("HH:mm") ?? a.Shift?.StartTime.ToString("HH:mm") ?? "",
                    a.SubServiceShiftConfig?.EndTime.ToString("HH:mm") ?? "",
                    a.BreakTime?.ToString("HH:mm"),
                    a.IsOnLeave,
                    a.IsHoliday))
                .ToList();

            var html = PilotPlanningEmailComposer.BuildHtml(
                user.FirstName,
                user.LastName,
                planning.WeekCode,
                planning.SubService?.Name ?? "",
                planning.WeekStartDate,
                days,
                user.HtelTechnicienName,
                user.IdTechnicien);

            var key = $"planning:{planning.Id}:{(updated ? "maj" : "pub")}:{stamp}:u{user.Id}";
            if (await emailQueue.EnqueueDirectAsync(user.Id, user.Email, Category, subject, html, key, ct))
                queued++;
        }

        logger.LogInformation(
            "Planning {WeekCode} : {Queued} e-mail(s) agent mis en file ({Agents} agent(s) affecté(s)).",
            planning.WeekCode,
            queued,
            agents.Count);
    }
}
