using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Application.PilotPlanning;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services.EmployeeImport;

namespace Planning.Infrastructure.Services;

public sealed class PilotPlanningPersonalEmailService(
    AppDbContext db,
    IPlanningEmailSender emailSender,
    ILogger<PilotPlanningPersonalEmailService> logger) : IPilotPlanningPersonalEmailService
{
    public async Task SendToAssignedPilotsAsync(int planningId, bool updated, CancellationToken ct = default)
    {
        try
        {
            await SendCoreAsync(planningId, updated, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Envoi email planning pilotes échoué (planningId={PlanningId}).", planningId);
        }
    }

    private async Task SendCoreAsync(int planningId, bool updated, CancellationToken ct)
    {
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

        var pilots = await db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => userIds.Contains(u.Id) && u.IsActive)
            .ToListAsync(ct);

        var personalEmails = await db.UserHrProfiles
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.EmailPersonnel })
            .ToListAsync(ct);
        var emailByUser = personalEmails.ToDictionary(x => x.UserId, x => x.EmailPersonnel);

        foreach (var user in pilots)
        {
            if (!EmployeeImportRoleNames.IsPilote(user.Role?.Name))
                continue;

            emailByUser.TryGetValue(user.Id, out var personal);
            if (!PilotPlanningEmailComposer.IsValidPersonalEmail(personal))
            {
                logger.LogWarning(
                    "Pilote {UserId} sans email personnel — planning {WeekCode} non envoyé.",
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
            var subject = PilotPlanningEmailComposer.Subject(planning.WeekCode, updated);
            await emailSender.SendHtmlAsync(personal!.Trim(), subject, html, ct);
        }
    }
}
