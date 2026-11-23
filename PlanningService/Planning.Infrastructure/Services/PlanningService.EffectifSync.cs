using Microsoft.EntityFrameworkCore;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Helpers;

namespace Planning.Infrastructure.Services;

/// <summary>
/// v61 — Synchronisation de l'effectif d'un planning avec les agents réels de la cellule.
/// Seuls les jours encore modifiables (fenêtre de régénération : pas aujourd'hui, ni demain après 15h)
/// sont touchés ; une journée déjà déclarée n'est jamais retirée. Rien n'est effacé du passé :
/// l'historique de planning, de présence et de paie reste intact.
/// </summary>
public partial class PlanningService
{
    /// <summary>
    /// Retire des jours à venir les agents sortis ; ajoute aux jours à venir les agents arrivés dans
    /// la cellule (créés, réactivés ou réaffectés), à partir de leur date d'entrée. Renvoie le nombre
    /// de journées retirées + ajoutées.
    /// </summary>
    public async Task<int> SynchroniserEffectifAsync(int weeklyPlanningId, CancellationToken ct = default)
    {
        var planning = await _context.WeeklyPlannings
            .Include(p => p.SubService)
            .FirstOrDefaultAsync(p => p.Id == weeklyPlanningId, ct);
        if (planning is null) return 0;
        // Planning pas encore généré : rien à synchroniser. Ajouter des journées à un brouillon vide
        // empêcherait la génération automatique (elle ignore les brouillons déjà remplis).
        if (!await _context.ShiftAssignments.AnyAsync(a => a.WeeklyPlanningId == planning.Id, ct))
            return 0;

        // v62 : motifs d'absence saisis dans Présence (arrêt maladie, congé…) appliqués à la semaine.
        var absences = await MarquerAbsencesPresenceAsync(planning, ct);
        return absences + await SynchroniserJoursModifiablesAsync(planning, ct);
    }

    /// <summary>
    /// v62 — Journées d'une semaine couvertes par un motif d'absence saisi dans Présence : marquées
    /// « absent » (plus de shift ni de pause affichés ni comptés), passé compris, la présence faisant
    /// foi. Le créneau prévu reste noté : si le motif est effacé, la journée retrouve son shift.
    /// </summary>
    private async Task<int> MarquerAbsencesPresenceAsync(WeeklyPlanning planning, CancellationToken ct)
    {
        var du = planning.WeekStartDate;
        var au = du.AddDays(6);
        var lignes = await _context.ShiftAssignments
            .Where(a => a.WeeklyPlanningId == planning.Id && !a.IsOnLeave && !a.IsHoliday)
            .ToListAsync(ct);
        if (lignes.Count == 0) return 0;
        var absences = await PresenceAbsencesPlanning.CongesVirtuelsAsync(
            _context, lignes.Select(a => a.UserId).Distinct().ToList(), du, au, ct);
        if (absences.Count == 0) return 0;
        var jours = absences.Select(c => (c.UserId, c.StartDate)).ToHashSet();
        var marquees = 0;
        foreach (var a in lignes.Where(a => jours.Contains((a.UserId, a.AssignedDate))))
        {
            // Seul l'indicateur change : créneau et pause restent notés (retour si le motif est effacé).
            a.IsOnLeave = true;
            marquees++;
        }
        if (marquees > 0)
            await _context.SaveChangesAsync(ct);
        return marquees;
    }

    /// <summary>v61 — Agents sortis retirés, agents arrivés ajoutés, sur les jours encore modifiables.</summary>
    private async Task<int> SynchroniserJoursModifiablesAsync(WeeklyPlanning planning, CancellationToken ct)
    {
        var premierModifiable = PlanningRegenWindow.GetEarliestRegenerableDate(DateTime.Now);
        var jours = GetWeekDays(planning.WeekStartDate).Where(j => j.date >= premierModifiable).ToList();
        if (jours.Count == 0) return 0;
        var premier = jours[0].date;
        var dernier = jours[^1].date;
        var celluleId = planning.SubServiceId;
        var aujourdhui = DateOnly.FromDateTime(DateTime.Now);

        // 1. Agents sortis ou réaffectés ailleurs (v63) : journées à venir retirées (jamais une journée
        //    déjà déclarée ; renforts et demandes exceptionnelles d'autres cellules gardés).
        var aVenir = await _context.ShiftAssignments
            .Where(a => a.WeeklyPlanningId == planning.Id
                        && a.AssignedDate >= premier
                        && !a.Declarations.Any())
            .ToListAsync(ct);
        var etatsAVenir = await ChargerEtatsAgentsAsync(aVenir.Select(a => a.UserId).Distinct().ToList(), ct);
        var sortants = aVenir
            .Where(a => etatsAVenir.TryGetValue(a.UserId, out var ag)
                        && (PlanningEffectifRules.MasqueeApresSortie(ag.Actif, ag.DateSortie, a.AssignedDate, aujourdhui)
                            || PlanningEffectifRules.ReaffecteAilleurs(ag, celluleId, a.IsReinforcement || a.IsExceptionalRequest)))
            .ToList();
        if (sortants.Count > 0)
        {
            _context.ShiftAssignments.RemoveRange(sortants);
            await _context.SaveChangesAsync(ct);
        }

        // 2. Agents arrivés : ajoutés aux jours à venir où ils n'ont encore aucune journée.
        var employes = await _context.Users
            .AsNoTracking()
            .Where(u => u.SubServiceId == celluleId && u.IsActive)
            .Where(PostesProduction.Planifiable)
            .Select(u => new { u.Id, u.Level })
            .ToListAsync(ct);
        if (employes.Count == 0) return sortants.Count;
        var ids = employes.Select(e => e.Id).ToList();
        var etats = await ChargerEtatsAgentsAsync(ids, ct);
        // Index unique (agent, date) sur tous les plannings : on ne double jamais une journée.
        var occupes = (await _context.ShiftAssignments
                .AsNoTracking()
                .Where(a => ids.Contains(a.UserId) && a.AssignedDate >= premier && a.AssignedDate <= dernier)
                .Select(a => new { a.UserId, a.AssignedDate })
                .ToListAsync(ct))
            .Select(x => (x.UserId, x.AssignedDate))
            .ToHashSet();

        var manquants = new List<(int Id, int Level, DayOfWeekEnum Jour, DateOnly Date)>();
        foreach (var e in employes)
        {
            if (!etats.TryGetValue(e.Id, out var agent)) continue;
            foreach (var (day, date) in jours)
            {
                if (!occupes.Contains((e.Id, date)) && PlanningEffectifRules.DoitFigurer(agent, celluleId, date))
                    manquants.Add((e.Id, e.Level, day, date));
            }
        }
        if (manquants.Count == 0) return sortants.Count;

        var configs = ShiftConfigScope.Weekday(await _context.SubServiceShiftConfigs
            .AsNoTracking()
            .Where(c => c.SubServiceId == celluleId && !c.IsTemplate && c.WeekCode == planning.WeekCode)
            .ToListAsync(ct));
        if (configs.Count == 0) return sortants.Count; // semaine sans horaires : rien à proposer

        var nouveauxIds = manquants.Select(m => m.Id).Distinct().ToList();
        var modes = new Dictionary<int, int>();
        if (planning.SubService?.MultiShiftModesEnabled == true)
        {
            try
            {
                modes = await ResolveEmployeeModeMapAsync(celluleId, planning.WeekCode, nouveauxIds);
            }
            catch (InvalidOperationException)
            {
                modes = new Dictionary<int, int>();
            }
        }

        var feries = FrenchHolidayHelper.GetHolidays(planning.WeekStartDate.Year);
        var conges = await _context.Conges
            .AsNoTracking()
            .Where(c => nouveauxIds.Contains(c.UserId)
                        && c.Status == CongeStatus.Approved
                        && c.StartDate <= dernier
                        && c.EndDate >= premier)
            .ToListAsync(ct);
        conges.AddRange(await PresenceAbsencesPlanning.CongesVirtuelsAsync(_context, nouveauxIds, premier, dernier, ct));

        // Occupation actuelle des créneaux et des pauses, jour par jour.
        var dejaPlanifies = await _context.ShiftAssignments
            .AsNoTracking()
            .Where(a => a.WeeklyPlanningId == planning.Id && a.AssignedDate >= premier && a.SubServiceShiftConfigId != null && !a.IsOnLeave)
            .Select(a => new { a.AssignedDate, ConfigId = a.SubServiceShiftConfigId!.Value, a.BreakTime })
            .ToListAsync(ct);
        var parCreneau = dejaPlanifies
            .GroupBy(x => (x.AssignedDate, x.ConfigId))
            .ToDictionary(g => g.Key, g => g.Count());
        var parPause = dejaPlanifies
            .Where(x => x.BreakTime != null)
            .GroupBy(x => (x.AssignedDate, x.ConfigId, Pause: x.BreakTime!.Value))
            .ToDictionary(g => g.Key, g => g.Count());

        var ajouts = new List<ShiftAssignment>();
        foreach (var (employeId, niveau, day, date) in manquants)
        {
            int? modeId = modes.TryGetValue(employeId, out var m) ? m : null;
            var nouvelle = new ShiftAssignment
            {
                WeeklyPlanningId = planning.Id,
                UserId = employeId,
                AssignedDate = date,
                DayOfWeek = day,
                IsSaturday = false,
                IsNewEmployee = niveau == 1,
                ShiftModeProfileId = modeId,
            };

            if (feries.Contains(date))
            {
                nouvelle.IsHoliday = true;
            }
            else if (conges.Any(c => c.UserId == employeId && c.StartDate <= date && c.EndDate >= date))
            {
                nouvelle.IsOnLeave = true;
            }
            else
            {
                var candidats = modeId is int mid && configs.Any(c => c.ShiftModeProfileId == mid)
                    ? configs.Where(c => c.ShiftModeProfileId == mid).ToList()
                    : configs;
                var choisi = PlanningEffectifRules.ChoisirCreneau(candidats.Select(c => new PlanningEffectifRules.Creneau(
                    c.Id,
                    c.RequiredCount,
                    parCreneau.TryGetValue((date, c.Id), out var n) ? n : 0,
                    c.DisplayOrder)));
                var config = candidats.FirstOrDefault(c => c.Id == choisi);
                if (config is not null)
                {
                    var pauses = BreakSlotPlanner.ResolveBreakSlots(config).Distinct().ToList();
                    var occupation = pauses.ToDictionary(
                        p => p,
                        p => parPause.TryGetValue((date, config.Id, p), out var k) ? k : 0);
                    var pause = PlanningEffectifRules.ChoisirPause(pauses, occupation);
                    nouvelle.SubServiceShiftConfigId = config.Id;
                    nouvelle.BreakTime = pause;
                    nouvelle.ShiftModeProfileId ??= config.ShiftModeProfileId;
                    parCreneau[(date, config.Id)] = (parCreneau.TryGetValue((date, config.Id), out var c0) ? c0 : 0) + 1;
                    if (pause is TimeOnly p0)
                        parPause[(date, config.Id, p0)] = (parPause.TryGetValue((date, config.Id, p0), out var c1) ? c1 : 0) + 1;
                }
            }
            ajouts.Add(nouvelle);
        }

        _context.ShiftAssignments.AddRange(ajouts);
        await _context.SaveChangesAsync(ct);
        return sortants.Count + ajouts.Count;
    }

    /// <summary>Actif, cellule, dates d'entrée et de sortie des agents.</summary>
    private async Task<Dictionary<int, PlanningEffectifRules.Agent>> ChargerEtatsAgentsAsync(
        IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<int, PlanningEffectifRules.Agent>();
        var users = await _context.Users
            .AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.IsActive, u.SubServiceId, u.HireDate })
            .ToListAsync(ct);
        var profils = (await _context.UserHrProfiles
                .AsNoTracking()
                .Where(p => ids.Contains(p.UserId))
                .ToListAsync(ct))
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.First());
        return users.ToDictionary(
            u => u.Id,
            u =>
            {
                // v64 : même période sous contrat que le générateur (entrée réelle, sortie effective).
                var contrat = ContratPeriode.De(profils.GetValueOrDefault(u.Id), u.HireDate);
                return new PlanningEffectifRules.Agent(u.Id, u.IsActive, u.SubServiceId, contrat.Entree, contrat.Sortie);
            });
    }

    /// <summary>Dates de sortie des agents (masquage des journées après la sortie).</summary>
    private async Task<Dictionary<int, DateOnly?>> ChargerDatesSortieAsync(IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return new Dictionary<int, DateOnly?>();
        return (await _context.UserHrProfiles
                .AsNoTracking()
                .Where(p => ids.Contains(p.UserId))
                .Select(p => new { p.UserId, p.DateSortie })
                .ToListAsync())
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.First().DateSortie);
    }
}
