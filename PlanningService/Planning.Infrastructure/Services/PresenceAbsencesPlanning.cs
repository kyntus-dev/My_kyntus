using Microsoft.EntityFrameworkCore;
using Planning.Application.Presence;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v62 — Motifs d'absence saisis dans Présence (arrêt maladie, congé, absence…) vus par le planning.
/// Ils sont traités comme des congés validés : aucun shift ce jour-là, ni à la génération, ni à la
/// régénération, ni à l'affichage. Ce sont des congés « virtuels » : jamais enregistrés en base.
/// Une absence partielle ne retire pas l'agent du planning.
/// </summary>
public static class PresenceAbsencesPlanning
{
    public const string Raison = "Présence : motif saisi par un responsable";

    public static async Task<List<Conge>> CongesVirtuelsAsync(
        AppDbContext db, IReadOnlyCollection<int> userIds, DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        if (userIds.Count == 0 || au < du)
            return [];
        var lignes = await db.PresenceJours.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId) && p.Jour >= du && p.Jour <= au && p.Code != null && !p.AbsencePartielle)
            .Select(p => new { p.UserId, p.Jour, p.Code })
            .ToListAsync(ct);
        return Convertir(lignes.Select(l => (l.UserId, l.Jour, l.Code)));
    }

    /// <summary>Lignes de présence → congés virtuels (une journée chacun), motifs non bloquants ignorés.</summary>
    public static List<Conge> Convertir(IEnumerable<(int UserId, DateOnly Jour, string? Code)> lignes) =>
        lignes
            .Select(l => (l.UserId, l.Jour, Type: PresenceCodes.VersAbsenceType(l.Code)))
            .Where(l => l.Type is not null)
            .Select(l => new Conge
            {
                Id = 0,
                UserId = l.UserId,
                StartDate = l.Jour,
                EndDate = l.Jour,
                Status = CongeStatus.Approved,
                AbsenceType = l.Type!.Value,
                Reason = Raison,
            })
            .ToList();

    /// <summary>
    /// Motif posé ou retiré dans Présence : les journées de planning de l'agent passent en absence
    /// (aucun shift affiché ni compté) ou retrouvent leur shift. Seul l'indicateur d'absence change :
    /// le créneau et la pause prévus restent notés, pour revenir tels quels si le motif est effacé.
    /// Pas de régénération ni de dépublication : le trou laissé reste visible pour être couvert.
    /// Une journée encore couverte par un congé validé n'est jamais rendue.
    /// </summary>
    public static async Task<int> AppliquerAuPlanningAsync(
        AppDbContext db, int userId, IReadOnlyCollection<DateOnly> poses, IReadOnlyCollection<DateOnly> retires,
        CancellationToken ct = default)
    {
        if (poses.Count == 0 && retires.Count == 0)
            return 0;
        var jours = poses.Concat(retires).Distinct().ToList();
        var lignes = await db.ShiftAssignments
            .Where(a => a.UserId == userId && jours.Contains(a.AssignedDate)
                        && a.WeeklyPlanning.Status != PlanningStatus.Archived)
            .ToListAsync(ct);
        if (lignes.Count == 0)
            return 0;

        var changees = 0;
        var aPoser = poses.ToHashSet();
        foreach (var a in lignes.Where(a => aPoser.Contains(a.AssignedDate) && !a.IsOnLeave && !a.IsHoliday))
        {
            a.IsOnLeave = true;
            changees++;
        }

        if (retires.Count > 0)
        {
            var du = retires.Min();
            var au = retires.Max();
            var conges = await db.Conges.AsNoTracking()
                .Where(c => c.UserId == userId && c.Status == CongeStatus.Approved && c.StartDate <= au && c.EndDate >= du)
                .Select(c => new { c.StartDate, c.EndDate })
                .ToListAsync(ct);
            var aRendre = retires.Where(j => !conges.Any(c => c.StartDate <= j && c.EndDate >= j)).ToHashSet();
            foreach (var a in lignes.Where(a => aRendre.Contains(a.AssignedDate) && a.IsOnLeave
                                                && (a.SubServiceShiftConfigId != null || a.ShiftId != null)))
            {
                a.IsOnLeave = false;
                changees++;
            }
        }

        if (changees > 0)
            await db.SaveChangesAsync(ct);
        return changees;
    }

    /// <summary>
    /// Jours contigus d'une liste de dates, regroupés en périodes (du, au) : une synchronisation par période.
    /// </summary>
    public static List<(DateOnly Du, DateOnly Au)> Periodes(IEnumerable<DateOnly> jours)
    {
        var tries = jours.Distinct().OrderBy(j => j).ToList();
        var result = new List<(DateOnly Du, DateOnly Au)>();
        foreach (var j in tries)
        {
            if (result.Count > 0 && result[^1].Au.AddDays(1) == j)
                result[^1] = (result[^1].Du, j);
            else
                result.Add((j, j));
        }
        return result;
    }
}
