using Microsoft.EntityFrameworkCore;
using Planning.Application.Presence;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.PlanningExport;

/// <summary>
/// Planning RH publié, jour par jour, pour l'export Excel. Lecture seule. Le périmètre est celui du
/// tableau des présences : RH et Admin voient tout, un responsable ses agents.
/// </summary>
public sealed class PlanningExportService(AppDbContext db, IPresenceService presences) : IPlanningExportService
{
    public const int JoursMax = 93;

    public async Task<PlanningExportDto> GetPlanningRhAsync(PresenceViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        if (au < du) (du, au) = (au, du);
        if (au.DayNumber - du.DayNumber >= JoursMax) au = du.AddDays(JoursMax - 1);

        var auteur = await presences.ResoudreAsync(viewer, ct);
        var affectationsQ = db.ShiftAssignments.AsNoTracking()
            .Where(a => a.AssignedDate >= du && a.AssignedDate <= au && a.WeeklyPlanning.Status == PlanningStatus.Published);
        List<int> ids;
        if (auteur.Agents is { } perimetre)
        {
            ids = perimetre.ToList();
            affectationsQ = affectationsQ.Where(a => ids.Contains(a.UserId));
        }
        else
        {
            ids = await affectationsQ.Select(a => a.UserId).Distinct().ToListAsync(ct);
        }

        var affectations = await affectationsQ
            .Select(a => new
            {
                a.UserId,
                a.AssignedDate,
                a.Id,
                a.IsOnLeave,
                a.IsHoliday,
                a.IsReinforcement,
                a.IsHalfDaySaturday,
                Libelle = a.SubServiceShiftConfig != null ? a.SubServiceShiftConfig.Label : a.Shift != null ? a.Shift.Label : null,
                Debut = a.SubServiceShiftConfig != null ? (TimeOnly?)a.SubServiceShiftConfig.StartTime : a.Shift != null ? (TimeOnly?)a.Shift.StartTime : null,
                Heures = a.SubServiceShiftConfig != null ? (int?)a.SubServiceShiftConfig.WorkHours : null,
                Pause = a.SubServiceShiftConfig != null ? (int?)a.SubServiceShiftConfig.BreakDurationMinutes : null,
            })
            .ToListAsync(ct);
        var parJour = affectations
            .GroupBy(a => (a.UserId, a.AssignedDate))
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.IsOnLeave).ThenBy(a => a.Id).First());

        var conges = await db.Conges.AsNoTracking()
            .Where(c => ids.Contains(c.UserId) && c.Status == CongeStatus.Approved && c.StartDate <= au && c.EndDate >= du)
            .Select(c => new { c.UserId, c.StartDate, c.EndDate })
            .ToListAsync(ct);

        var users = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                Service = u.SubService != null ? u.SubService.Name : null,
                Cellule = u.SubService != null ? u.SubService.Service.Name : null,
                Pole = u.SubService != null ? u.SubService.Service.Floor.Name : null,
            })
            .ToListAsync(ct);

        var agents = users
            .Select(u => new PlanningExportAgentDto(
                u.Id,
                $"{(u.LastName ?? string.Empty).Trim().ToUpperInvariant()} {(u.FirstName ?? string.Empty).Trim()}".Trim(),
                u.Pole,
                u.Cellule,
                u.Service,
                Jours(du, au).Select(jour =>
                {
                    var conge = conges.Any(c => c.UserId == u.Id && c.StartDate <= jour && c.EndDate >= jour);
                    if (!parJour.TryGetValue((u.Id, jour), out var a))
                        return new PlanningExportJourDto(jour, conge ? "Congé" : null, null, null, 0, conge, false, false);
                    if (a.IsOnLeave || conge)
                        return new PlanningExportJourDto(jour, "Congé", null, null, 0, true, false, a.IsReinforcement);
                    if (a.IsHoliday)
                        return new PlanningExportJourDto(jour, "Férié", null, null, 0, false, true, a.IsReinforcement);
                    var minutes = a.IsHalfDaySaturday ? 4 * 60 : (a.Heures ?? 0) * 60;
                    var fin = a.Debut is { } d && minutes > 0
                        ? d.AddMinutes(minutes + (a.IsHalfDaySaturday ? 0 : a.Pause ?? 0))
                        : (TimeOnly?)null;
                    return new PlanningExportJourDto(
                        jour, a.Libelle, a.Debut?.ToString("HH:mm"), fin?.ToString("HH:mm"), minutes, false, false, a.IsReinforcement);
                }).ToList()))
            .OrderBy(a => a.Cellule ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var libelle = auteur.Agents is null ? "Toute l'entreprise" : $"Votre périmètre ({agents.Count} agent(s))";
        return new PlanningExportDto(du, au, libelle, agents);
    }

    private static IEnumerable<DateOnly> Jours(DateOnly du, DateOnly au)
    {
        for (var d = du; d <= au; d = d.AddDays(1)) yield return d;
    }
}
