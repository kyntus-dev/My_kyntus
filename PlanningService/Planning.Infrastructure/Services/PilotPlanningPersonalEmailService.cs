using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Application.PilotPlanning;
using Planning.Infrastructure.Persistence;

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
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Envoi email planning employés échoué (planningId={PlanningId}).", planningId);
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

        var employees = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.IsActive)
            .OrderBy(u => u.Id)
            .ToListAsync(ct);

        var personalEmails = await db.UserHrProfiles
            .AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.EmailPersonnel })
            .ToListAsync(ct);
        var emailByUser = personalEmails.ToDictionary(x => x.UserId, x => x.EmailPersonnel);

        foreach (var user in employees)
        {
            ct.ThrowIfCancellationRequested();

            emailByUser.TryGetValue(user.Id, out var personal);
            if (!PilotPlanningEmailComposer.IsValidPersonalEmail(personal))
            {
                logger.LogWarning(
                    "Employé {UserId} sans email personnel valide — planning {WeekCode} non envoyé.",
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
            try
            {
                await emailSender.SendRequiredHtmlAsync(personal!.Trim(), subject, html, ct);
                logger.LogInformation(
                    "Planning {WeekCode} envoyé à l'employé {UserId}.",
                    planning.WeekCode,
                    user.Id);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Envoi email planning {WeekCode} échoué pour l'employé {UserId}. Les autres envois continuent.",
                    planning.WeekCode,
                    user.Id);
            }
        }
    }
}
