using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Planning.Application.Pointage;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.Pointage;

/// <summary>
/// Pointages calculés (« Mon suivi », équipe, RH) et correspondances ID pointeuse → collaborateur.
/// Référence des retards : planning <b>publié</b> dans My-Kyntus ; les jours sans planning publié,
/// le régime horaire du collaborateur (heures fixes définies par l'Admin) s'il en a un.
/// Pause autorisée : 80 min (Excel « Combien il me reste »), ou celle du régime.
/// </summary>
public sealed class PointageQueryService(
    AppDbContext db,
    IPlanningPerimeterResolver perimeter,
    IRegimeHoraireService regimes,
    IOptionsMonitor<PointageOptions> options) : IPointageQueryService
{
    private PointageOptions O => options.CurrentValue;

    // ── Consultation ────────────────────────────────────────────────────────────

    public async Task<PointageSuiviDto?> GetMonSuiviAsync(PointageViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        var me = await FindViewerAsync(viewer, ct);
        return me is null ? null : (await CalculerAsync([me], du, au, ct)).Single();
    }

    public async Task<PointageSuiviDto?> GetSuiviCollaborateurAsync(PointageViewer viewer, int userId, DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        var me = await FindViewerAsync(viewer, ct);
        var cible = await db.Users.AsNoTracking().Include(u => u.SubService).FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (me is null || cible is null)
            return null;
        if (me.Id != cible.Id && !IsAdminOrRh(viewer.Role))
        {
            var scope = await perimeter.GetManagedSubServiceIdsAsync(me.Id, ct);
            if (cible.SubServiceId is not int sid || !scope.Contains(sid))
                return null;
        }
        return (await CalculerAsync([cible], du, au, ct)).Single();
    }

    public async Task<PointageEquipeDto?> GetEquipeAsync(PointageViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        var me = await FindViewerAsync(viewer, ct);
        if (me is null)
            return null;

        var (membres, libelle) = await MembresAsync(me, viewer, ct);
        if (membres.Count == 0 && libelle is null)
            return new PointageEquipeDto("Aucune équipe rattachée", du, au, O.ToleranceMinutes, await DerniereSynchroAsync(ct), []);

        var suivis = await CalculerAsync(membres, du, au, ct);
        var lignes = suivis
            .Select(s => new PointageEquipeLigneDto(
                s.UserId, s.Collaborateur, s.Cellule, s.IdsPointeuse.Count > 0,
                s.Totaux.RetardTotalMinutes, s.Totaux.JoursEnRetard, s.Totaux.JoursAHeure, s.Totaux.JoursPointes,
                s.Totaux.JoursPointageManquant.Count, s.Totaux.JoursSansPointage.Count,
                s.Totaux.ProductionMinutes, s.Totaux.ProductionPrevueMinutes, s.Regime,
                s.Totaux.HeuresSupplementairesMinutes))
            .OrderByDescending(l => l.RetardTotalMinutes)
            .ThenBy(l => l.Collaborateur, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new PointageEquipeDto(libelle!, du, au, O.ToleranceMinutes, await DerniereSynchroAsync(ct), lignes);
    }

    public async Task<PointageEquipeDetailDto?> GetEquipeDetailAsync(PointageViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        var me = await FindViewerAsync(viewer, ct);
        if (me is null)
            return null;
        if (au < du) (du, au) = (au, du);
        var max = Math.Clamp(O.PeriodeEquipeDetailMaxJours, 1, O.PeriodeMaxJours);
        if (au.DayNumber - du.DayNumber >= max)
            du = au.AddDays(-(max - 1));

        var (membres, libelle) = await MembresAsync(me, viewer, ct);
        if (membres.Count == 0 && libelle is null)
            return new PointageEquipeDetailDto("Aucune équipe rattachée", du, au, await DerniereSynchroAsync(ct), []);

        var suivis = (await CalculerAsync(membres, du, au, ct))
            .OrderBy(s => s.Cellule ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(s => s.Collaborateur, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new PointageEquipeDetailDto(libelle!, du, au, await DerniereSynchroAsync(ct), suivis);
    }

    public async Task<IReadOnlyList<PointageSuiviDto>> CalculerPourAsync(IReadOnlyList<int> userIds, DateOnly du, DateOnly au, CancellationToken ct = default)
    {
        if (userIds.Count == 0) return [];
        if (au < du) (du, au) = (au, du);
        if (au.DayNumber - du.DayNumber >= O.PeriodeMaxJours)
            du = au.AddDays(-(O.PeriodeMaxJours - 1));
        var ids = userIds.Distinct().ToList();
        var users = await db.Users.AsNoTracking().Include(u => u.SubService)
            .Where(u => ids.Contains(u.Id)).ToListAsync(ct);
        return await CalculerAsync(users, du, au, ct);
    }

    public async Task<PointageAujourdhuiDto?> GetAujourdhuiAsync(PointageViewer viewer, CancellationToken ct = default)
    {
        var me = await FindViewerAsync(viewer, ct);
        if (me is null)
            return null;
        var today = Aujourdhui();
        var suivi = (await CalculerAsync([me], today, today, ct)).Single();
        var jour = suivi.Jours.Single();
        var regime = (await regimes.ResoudreAsync([me.Id], ct)).GetValueOrDefault(me.Id);

        // Production et pause du jour : planning (publié ou régime) ; sinon valeurs de l'Excel.
        // v65 : une journée hors planning a une production prévue de 0 — rien n'est attendu, tout ce
        // qui est pointé est en heures supplémentaires.
        var production = jour.Planifie
            ? jour.ProductionPrevueMinutes ?? RegimeResolver.ProductionExcelMinutes(today)
            : 0;
        var pause = jour.Source is null ? regime?.PauseAutoriseeMinutes ?? RegimeResolver.PauseExcelMinutes : jour.PauseAutoriseeMinutes;
        // v67 : une journée de récupération (planning IT « Récup ») n'est pas travaillée.
        var travaille = jour.Statut is not (PointageJourStatuts.Repos or PointageJourStatuts.Conge or PointageJourStatuts.Ferie or PointageJourStatuts.Recup)
                        || jour.Pointages.Count > 0;

        var acces = await db.HeureSortieAcces.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == me.Id, ct);

        return new PointageAujourdhuiDto(
            today,
            me.Id,
            suivi.Collaborateur,
            // v67 — Accès donné par le Super Admin compte par compte ; le Super Admin y a toujours
            // accès. Un Admin sans autorisation n'y accède plus d'office.
            viewer.SuperAdmin || acces is not null,
            regime?.Nom,
            regime?.Cible,
            jour.Source,
            jour.Planning,
            production,
            pause,
            travaille,
            jour.Pointages,
            jour.Statut,
            suivi.DerniereSynchro,
            // v40 : choix valable pour la journée où il a été fait ; le lendemain, la personne choisit à nouveau.
            ChoixProductionSortie.ChoixDuJour(acces?.ChoixProduction, JourLocal(acces?.ChoixModifieLe), today),
            acces?.ProductionBase,
            ChoixProductionSortie.JourneeCourteDejaPrise(acces?.ProductionBase, today, await ChoixSemaineAsync(me.Id, today, ct)),
            jour.Planifie);
    }

    public async Task<bool> EnregistrerChoixProductionAsync(PointageViewer viewer, string? choix, CancellationToken ct = default)
    {
        if (!ChoixProductionSortie.EstValide(choix))
            throw new ArgumentException("Choix inconnu : « 0840 » (08h40) ou « 0740 » (07h40).");
        var me = await FindViewerAsync(viewer, ct);
        if (me is null)
            return false;
        var acces = await db.HeureSortieAcces.FirstOrDefaultAsync(a => a.UserId == me.Id, ct);
        if (acces is null)
        {
            // Seuls les comptes autorisés par le Super Admin mémorisent un choix ; le Super Admin y a
            // toujours accès (v67 : plus l'Admin d'office).
            if (!viewer.SuperAdmin)
                return false;
            acces = new HeureSortieAcces { UserId = me.Id, GrantedBy = me.Id };
            db.HeureSortieAcces.Add(acces);
        }
        acces.ChoixProduction = choix;
        acces.ChoixModifieLe = DateTime.UtcNow;

        // v42 : le choix est aussi gardé pour la journée : le solde compare chaque jour à son choix.
        var today = Aujourdhui();
        // v43 : horaire 08h40 : une seule journée à 07h40 par semaine (lundi → vendredi).
        if (choix == ChoixProductionSortie.Court
            && ChoixProductionSortie.JourneeCourteDejaPrise(acces.ProductionBase, today, await ChoixSemaineAsync(me.Id, today, ct)) is DateOnly prise)
            throw new InvalidOperationException(
                $"Vous avez déjà pris votre journée à 07h40 cette semaine (le {prise:dd/MM}). Une seule par semaine, du lundi au vendredi.");
        var jour = await db.HeureSortieChoixJours.FirstOrDefaultAsync(c => c.UserId == me.Id && c.Jour == today, ct);
        if (choix is null)
        {
            if (jour is not null) db.HeureSortieChoixJours.Remove(jour);
        }
        else if (jour is null)
        {
            db.HeureSortieChoixJours.Add(new HeureSortieChoixJour { UserId = me.Id, Jour = today, Choix = choix, ModifieLe = DateTime.UtcNow });
        }
        else
        {
            jour.Choix = choix;
            jour.ModifieLe = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> EnregistrerChoixProductionJourAsync(PointageViewer viewer, DateOnly jour, string? choix, CancellationToken ct = default)
    {
        var today = Aujourdhui();
        if (jour == today)
            return await EnregistrerChoixProductionAsync(viewer, choix, ct);
        if (!ChoixProductionSortie.EstValide(choix))
            throw new ArgumentException("Choix inconnu : « 0840 » (08h40) ou « 0740 » (07h40).");
        // v45 : correction d'une journée passée de la période de paie en cours (depuis le 24).
        var debutPeriode = today.Day >= 24 ? new DateOnly(today.Year, today.Month, 24) : new DateOnly(today.Year, today.Month, 24).AddMonths(-1);
        if (jour > today || jour < debutPeriode)
            throw new ArgumentException($"Seules les journées depuis le {debutPeriode:dd/MM} peuvent être corrigées.");
        var me = await FindViewerAsync(viewer, ct);
        if (me is null)
            return false;
        var acces = await db.HeureSortieAcces.AsNoTracking().FirstOrDefaultAsync(a => a.UserId == me.Id, ct);
        if (acces is null && !viewer.SuperAdmin)
            return false;
        if (choix == ChoixProductionSortie.Court
            && ChoixProductionSortie.JourneeCourteDejaPrise(acces?.ProductionBase, jour, await ChoixSemaineAsync(me.Id, jour, ct)) is DateOnly prise)
            throw new InvalidOperationException(
                $"La journée à 07h40 de cette semaine est déjà prise le {prise:dd/MM}. Une seule par semaine, du lundi au vendredi.");

        var ligne = await db.HeureSortieChoixJours.FirstOrDefaultAsync(c => c.UserId == me.Id && c.Jour == jour, ct);
        if (choix is null)
        {
            if (ligne is not null) db.HeureSortieChoixJours.Remove(ligne);
        }
        else if (ligne is null)
        {
            db.HeureSortieChoixJours.Add(new HeureSortieChoixJour { UserId = me.Id, Jour = jour, Choix = choix, ModifieLe = DateTime.UtcNow });
        }
        else
        {
            ligne.Choix = choix;
            ligne.ModifieLe = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>v43 : choix faits du lundi au vendredi de la semaine du jour donné.</summary>
    private async Task<List<(DateOnly Jour, string Choix)>> ChoixSemaineAsync(int userId, DateOnly jour, CancellationToken ct)
    {
        var lundi = jour.AddDays(-(((int)jour.DayOfWeek + 6) % 7));
        var vendredi = lundi.AddDays(4);
        var lignes = await db.HeureSortieChoixJours.AsNoTracking()
            .Where(c => c.UserId == userId && c.Jour >= lundi && c.Jour <= vendredi)
            .Select(c => new { c.Jour, c.Choix })
            .ToListAsync(ct);
        return lignes.Select(c => (c.Jour, c.Choix)).ToList();
    }

    public async Task<IReadOnlyList<ChoixProductionJourDto>> GetMesChoixProductionAsync(PointageViewer viewer, DateOnly au, CancellationToken ct = default)
    {
        var me = await FindViewerAsync(viewer, ct);
        if (me is null)
            return Array.Empty<ChoixProductionJourDto>();
        // v67 — Données de la page « Heure de sortie » : réservées aux comptes autorisés (et au Super Admin).
        if (!viewer.SuperAdmin && !await db.HeureSortieAcces.AsNoTracking().AnyAsync(a => a.UserId == me.Id, ct))
            return Array.Empty<ChoixProductionJourDto>();
        var lignes = await db.HeureSortieChoixJours.AsNoTracking()
            .Where(c => c.UserId == me.Id && c.Jour <= au)
            .OrderBy(c => c.Jour)
            .ToListAsync(ct);
        return lignes.Select(c => new ChoixProductionJourDto(c.Jour, c.Choix)).ToList();
    }

    /// <summary>
    /// Collaborateurs visibles : tous (RH / Admin) ou le périmètre du responsable.
    /// Libellé null : responsable sans équipe rattachée.
    /// </summary>
    private async Task<(List<User> Membres, string? Libelle)> MembresAsync(User me, PointageViewer viewer, CancellationToken ct)
    {
        if (IsAdminOrRh(viewer.Role))
        {
            var tous = await db.Users.AsNoTracking().Include(u => u.SubService)
                .Where(u => u.IsActive).ToListAsync(ct);
            return (tous, "Toute l'entreprise");
        }

        var scope = await perimeter.GetManagedSubServiceIdsAsync(me.Id, ct);
        if (scope.Count == 0)
            return ([], null);
        var membres = await db.Users.AsNoTracking().Include(u => u.SubService)
            .Where(u => u.IsActive && u.Id != me.Id && u.SubServiceId != null && scope.Contains(u.SubServiceId.Value))
            .ToListAsync(ct);
        return (membres, $"Votre équipe ({membres.Count} collaborateur(s))");
    }

    /// <summary>Calcule les journées de plusieurs collaborateurs en trois requêtes (pointages, plannings, congés).</summary>
    private async Task<IReadOnlyList<PointageSuiviDto>> CalculerAsync(IReadOnlyList<User> users, DateOnly du, DateOnly au, CancellationToken ct)
    {
        if (au < du) (du, au) = (au, du);
        if (au.DayNumber - du.DayNumber >= O.PeriodeMaxJours)
            du = au.AddDays(-(O.PeriodeMaxJours - 1));

        var userIds = users.Select(u => u.Id).ToList();
        var correspondances = await db.PointageCorrespondances.AsNoTracking()
            .Where(c => c.UserId != null && userIds.Contains(c.UserId.Value) && c.Statut != PointageCorrespondanceStatuts.Ignore)
            .ToListAsync(ct);
        var codesParUser = correspondances.GroupBy(c => c.UserId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(c => c.EmpCode).ToList());
        var userParCode = correspondances.ToDictionary(c => c.EmpCode, c => c.UserId!.Value);
        var codes = userParCode.Keys.ToList();

        var debut = du.ToDateTime(TimeOnly.MinValue);
        var fin = au.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var lus = await db.PointagesBruts.AsNoTracking()
            .Where(p => codes.Contains(p.EmpCode) && p.PunchTime >= debut && p.PunchTime < fin)
            .Select(p => new { p.EmpCode, p.PunchTime, p.TerminalAlias, p.TerminalSn })
            .ToListAsync(ct);
        // Badgeages d'accès à une zone (armoire technique…) : ni arrivée, ni départ, ni pause.
        var motifsExclus = PointageTerminaux.Motifs(O.TerminauxExclus);
        var pointages = lus.Where(p => !PointageTerminaux.EstExclu(p.TerminalAlias, p.TerminalSn, motifsExclus)).ToList();
        var pointagesParJour = pointages
            .GroupBy(p => (userParCode[p.EmpCode], DateOnly.FromDateTime(p.PunchTime)))
            // v33 : la pointeuse est gardée : deux badgeages rapprochés sur deux pointeuses différentes
            // (ex. 6e puis 5e étage) sont deux passages, pas un double badgeage.
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => (Heure: p.PunchTime, Pointeuse: string.IsNullOrWhiteSpace(p.TerminalSn) ? p.TerminalAlias : p.TerminalSn)).ToList());

        var affectations = await db.ShiftAssignments.AsNoTracking()
            .Include(a => a.SubServiceShiftConfig)
            .Include(a => a.Shift)
            .Where(a => userIds.Contains(a.UserId)
                        && a.AssignedDate >= du && a.AssignedDate <= au
                        && a.WeeklyPlanning.Status == PlanningStatus.Published)
            .ToListAsync(ct);
        var regimeParUser = await regimes.ResoudreAsync(userIds, ct);
        // Planning IT : horaires saisis par les membres IT, référence prioritaire.
        var horairesIt = await db.PlanningItHoraires.AsNoTracking()
            .Where(h => userIds.Contains(h.UserId) && h.Jour >= du && h.Jour <= au)
            .Select(h => new { h.UserId, h.Jour, h.Statut, h.Arrivee })
            .ToListAsync(ct);
        var itParJour = horairesIt.ToDictionary(h => (h.UserId, h.Jour), h => (h.Statut, h.Arrivee));
        // v31 : membres IT (planning IT) — un jour férié français sans saisie est un jour férié.
        var rolesParUser = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.Role != null)
            .Select(u => new { u.Id, Role = u.Role.Name })
            .ToListAsync(ct);
        var membresIt = rolesParUser.Where(u => KyntusRoleNames.IsEquipeIt(u.Role)).Select(u => u.Id).ToHashSet();
        var planningParJour = affectations
            .GroupBy(a => (a.UserId, a.AssignedDate))
            .ToDictionary(
                g => g.Key,
                // v45 : un congé ou un férié inscrit au planning l'emporte sur une autre ligne du même jour.
                g => ToPlanningJour(g.OrderByDescending(a => a.IsOnLeave || a.IsHoliday).ThenBy(a => a.Id).First(), regimeParUser.GetValueOrDefault(g.Key.UserId)));

        var conges = await db.Conges.AsNoTracking()
            .Where(c => userIds.Contains(c.UserId) && c.Status == CongeStatus.Approved && c.StartDate <= au && c.EndDate >= du)
            .Select(c => new { c.UserId, c.StartDate, c.EndDate })
            .ToListAsync(ct);

        var congesParUser = conges.GroupBy(c => c.UserId).ToDictionary(g => g.Key, g => g.ToList());

        // v44 : production prévue = choix du jour dans « Heure de sortie » (07h40 ou 08h40), sinon l'horaire
        // de base du compte ; elle remplace celle du planning ou du régime (plus de 07h40 imposé le vendredi).
        var choixParJour = (await db.HeureSortieChoixJours.AsNoTracking()
                .Where(c => userIds.Contains(c.UserId) && c.Jour >= du && c.Jour <= au)
                .Select(c => new { c.UserId, c.Jour, c.Choix })
                .ToListAsync(ct))
            .ToDictionary(c => (c.UserId, c.Jour), c => c.Choix);
        var baseParUser = await db.HeureSortieAcces.AsNoTracking()
            .Where(a => userIds.Contains(a.UserId))
            .ToDictionaryAsync(a => a.UserId, a => a.ProductionBase, ct);
        var today = Aujourdhui();
        var derniereSynchro = await DerniereSynchroAsync(ct);
        var result = new List<PointageSuiviDto>(users.Count);
        foreach (var user in users)
        {
            var regime = regimeParUser.GetValueOrDefault(user.Id);
            var jours = new List<PointageJourDto>();
            for (var jour = du; jour <= au; jour = jour.AddDays(1))
            {
                // Ordre de référence : planning IT (saisi par le membre), puis planning publié,
                // puis régime horaire du collaborateur.
                PlanningJour? planning;
                if (itParJour.TryGetValue((user.Id, jour), out var it))
                    planning = RegimeResolver.PlanningDuJourIt(it.Statut, it.Arrivee, regime, jour,
                        O.PauseSelonShift ? O.PauseParDefautMinutes : O.PauseAutoriseeMinutes);
                else if (!planningParJour.ContainsKey((user.Id, jour)) && JoursFeries.Nom(jour) is { } nomFerie)
                    // v45 : jour férié (même calendrier que la génération des plannings) sans planning publié :
                    // férié pour tous, jamais compté comme absence ni comme heures manquantes.
                    planning = new PlanningJour(null, 0, false, true, nomFerie, null,
                        membresIt.Contains(user.Id) ? PlanningSources.PlanningIt : PlanningSources.Regime);
                else if (!planningParJour.TryGetValue((user.Id, jour), out planning) && regime is not null)
                    planning = RegimeResolver.PlanningDuJour(regime, jour);

                // v65 — Le jour est-il prévu en production pour CET agent ? On s'appuie sur son
                // propre planning, quel que soit son service ou son département : planning publié,
                // planning IT « Travail », ou jour travaillé de son régime horaire. Le planning IT
                // n'est plus une référence commune : il ne vaut que pour les agents qui en ont un.
                string? statutIt = itParJour.TryGetValue((user.Id, jour), out var itDuJour) ? itDuJour.Statut : null;
                var planifieJour = planningParJour.ContainsKey((user.Id, jour))
                                   || (statutIt is not null && statutIt != PlanningItStatuts.Repos)
                                   || RegimeResolver.JourTravailleDuRegime(regime, jour);
                // Jour de repos pour cet agent : son régime horaire ou son planning IT le disent ;
                // à défaut de l'un et de l'autre, c'est le samedi ou le dimanche. Une journée de
                // semaine dont le planning n'est pas encore publié n'entre pas dans ce cas : son
                // traitement reste celui d'avant (aucun effet de bord sur les autres services).
                var jourDeRepos = !planifieJour
                                  && (regime is not null
                                      || statutIt == PlanningItStatuts.Repos
                                      || jour.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);

                var productionChoisie = ChoixProductionSortie.ProductionMinutes(choixParJour.GetValueOrDefault((user.Id, jour)), jour);
                // v65 — Le choix du jour et l'horaire de base (« Heure de sortie ») ne fixent plus de
                // production attendue un jour de repos. Un samedi hors planning héritait de 08h40 :
                // la journée affichait jusqu'à 7 h 33 d'heures manquantes alors que l'agent avait
                // travaillé en plus de son planning.
                if (productionChoisie is int choisie && !jourDeRepos)
                    planning = (planning ?? new PlanningJour(null, regime?.PauseAutoriseeMinutes ?? O.PauseAutoriseeMinutes, false, false, null))
                        with { ProductionPrevueMinutes = choisie };
                else if (planning is not null && !jourDeRepos
                         && ChoixProductionSortie.ProductionMinutes(baseParUser.GetValueOrDefault(user.Id), jour) is int deBase)
                    planning = planning with { ProductionPrevueMinutes = deBase };

                // v65 — Jour de repos travaillé (samedi hors planning, renfort…) : production
                // attendue nulle. Avec des pointages, les heures réalisées deviennent des heures
                // supplémentaires ; sans pointage, la journée reste un repos, jamais une absence.
                // Un samedi déjà prévu en production garde son planning : pas de double comptage.
                if (jourDeRepos)
                    planning = (planning ?? new PlanningJour(null, regime?.PauseAutoriseeMinutes ?? O.PauseAutoriseeMinutes, false, false, null))
                        with { ProductionPrevueMinutes = 0, Planifie = false };
                // v67 — Récupération (planning IT « Récup ») : aucune production attendue ; la production
                // qui aurait été prévue (choix du jour ou horaire de base « Heure de sortie », sinon celle
                // du planning IT) est déduite du solde d'heures supplémentaires.
                if (statutIt == PlanningItStatuts.Recup && planning is not null)
                    planning = planning with
                    {
                        ProductionPrevueMinutes = 0,
                        Planifie = false,
                        RecupMinutes = productionChoisie
                                       ?? ChoixProductionSortie.ProductionMinutes(baseParUser.GetValueOrDefault(user.Id), jour)
                                       ?? planning.RecupMinutes,
                    };
                if (congesParUser.TryGetValue(user.Id, out var siens) && siens.Any(c => c.StartDate <= jour && c.EndDate >= jour))
                    planning = (planning ?? new PlanningJour(null, 0, false, false, null)) with { Conge = true };
                pointagesParJour.TryGetValue((user.Id, jour), out var bruts);
                // v65 : le délai de double badgeage est plafonné par la pause minimale — un retour de
                // pause d'une minute n'est plus pris pour un double passage du doigt et supprimé.
                var fusionnes = PointageCalculator.FusionnerDoublesBadgeages(
                    bruts ?? new List<(DateTime Heure, string? Pointeuse)>(),
                    O.DoubleBadgeSecondes,
                    O.PauseMinimaleSecondes);
                // Doubles badgeages déjà fusionnés pointeuse par pointeuse : aucune autre fusion (0 s).
                var r = PointageCalculator.CalculerJour(
                    jour, fusionnes, planning, today, O.ToleranceMinutes, 0,
                    O.PauseSelonShift ? O.PauseParDefautMinutes : O.PauseAutoriseeMinutes);
                jours.Add(ToDto(r, planning?.Libelle, planning?.Source));
            }

            var totaux = PointageCalculator.Totaliser(jours.Select(FromDto));
            result.Add(new PointageSuiviDto(
                user.Id,
                $"{user.LastName} {user.FirstName}".Trim(),
                user.SubService?.Name,
                codesParUser.GetValueOrDefault(user.Id) ?? [],
                du,
                au,
                O.ToleranceMinutes,
                O.Enabled,
                derniereSynchro,
                new PointageTotauxDto(totaux.RetardTotalMinutes, totaux.JoursEnRetard, totaux.JoursAHeure,
                    totaux.JoursPointes, totaux.JoursPointageManquant, totaux.JoursSansPointage,
                    totaux.ProductionMinutes, totaux.ProductionPrevueMinutes, totaux.JoursProductionCalculee,
                    totaux.HeuresSupplementairesMinutes, totaux.JoursHeuresSupplementaires,
                    totaux.RecupMinutes, totaux.JoursRecup),
                jours,
                regime?.Nom));
        }
        return result;
    }

    /// <summary>
    /// Planning publié d'un jour. Pause autorisée : celle du régime du collaborateur, sinon 80 min
    /// (ou la pause du shift si <c>Pointage__PauseSelonShift=true</c>, comportement v10).
    /// Production prévue : heures de travail du shift (WorkHours).
    /// </summary>
    private PlanningJour ToPlanningJour(ShiftAssignment a, RegimeEffectif? regime) =>
        new(
            a.IsOnLeave || a.IsHoliday ? null : a.SubServiceShiftConfig?.StartTime ?? a.Shift?.StartTime,
            O.PauseSelonShift
                ? a.SubServiceShiftConfig?.BreakDurationMinutes ?? O.PauseParDefautMinutes
                : regime?.PauseAutoriseeMinutes ?? O.PauseAutoriseeMinutes,
            a.IsOnLeave,
            a.IsHoliday,
            a.SubServiceShiftConfig?.Label ?? a.Shift?.Label,
            a.SubServiceShiftConfig is { WorkHours: > 0 } c ? c.WorkHours * 60 : null,
            PlanningSources.Publie);

    private static PointageJourDto ToDto(PointageJourResult r, string? libelle, string? source) =>
        new(
            r.Jour,
            r.Statut,
            r.Planning?.ToString("HH:mm"),
            libelle,
            r.PauseAutoriseeMinutes,
            r.Arrivee?.ToString("HH:mm:ss"),
            r.Depart?.ToString("HH:mm:ss"),
            r.Pointages.Select(p => p.ToString("HH:mm:ss")).ToList(),
            r.PauseMinutes,
            r.RetardEntreeMinutes,
            r.RetardPauseMinutes,
            r.RetardTotalMinutes,
            r.ProductionMinutes,
            r.ProductionPrevueMinutes,
            r.EcartProductionMinutes,
            source,
            r.Planifie,
            r.HeuresSupplementairesMinutes,
            r.RecupMinutes,
            r.ProductionPointeeMinutes);

    private static PointageJourResult FromDto(PointageJourDto d) =>
        new(d.Jour, d.Statut, null, d.PauseAutoriseeMinutes, null, null, [], d.PauseMinutes, d.RetardEntreeMinutes, d.RetardPauseMinutes,
            d.ProductionMinutes, d.ProductionPrevueMinutes, d.Planifie, d.HeuresSupplementairesMinutes, d.RecupMinutes,
            d.ProductionPointeeMinutes);

    // ── Correspondances ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PointageCorrespondanceDto>> GetCorrespondancesAsync(string? statut, CancellationToken ct = default)
    {
        var q = db.PointageCorrespondances.AsNoTracking().Include(c => c.User).AsQueryable();
        if (!string.IsNullOrWhiteSpace(statut))
            q = q.Where(c => c.Statut == statut);
        var rows = await q.ToListAsync(ct);

        var derniers = await db.PointagesBruts.AsNoTracking()
            .GroupBy(p => p.EmpCode)
            .Select(g => new { EmpCode = g.Key, Dernier = g.Max(p => p.PunchTime) })
            .ToDictionaryAsync(x => x.EmpCode, x => x.Dernier, ct);

        return rows
            .Select(c => ToDto(c, derniers.TryGetValue(c.EmpCode, out var d) ? d : null))
            .OrderBy(c => StatutOrdre(c.Statut))
            .ThenByDescending(c => c.DernierPointage)
            .ThenBy(c => c.NomBioTime, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<PointageCorrespondanceDto?> UpdateCorrespondanceAsync(
        string empCode,
        PointageCorrespondanceUpdateRequest request,
        PointageViewer viewer,
        CancellationToken ct = default)
    {
        var c = await db.PointageCorrespondances.FirstOrDefaultAsync(x => x.EmpCode == empCode, ct);
        if (c is null)
            return null;

        if (request.Ignorer)
        {
            c.UserId = null;
            c.Statut = PointageCorrespondanceStatuts.Ignore;
        }
        else if (request.UserId is int uid)
        {
            if (!await db.Users.AnyAsync(u => u.Id == uid && u.IsActive, ct))
                throw new InvalidOperationException("Collaborateur introuvable ou inactif.");
            c.UserId = uid;
            c.Statut = PointageCorrespondanceStatuts.Valide;
        }
        else
        {
            c.UserId = null;
            c.Statut = PointageCorrespondanceStatuts.AValider;
        }

        c.UpdatedAt = DateTime.UtcNow;
        c.UpdatedBy = (await FindViewerAsync(viewer, ct))?.Id;
        await db.SaveChangesAsync(ct);

        var withUser = await db.PointageCorrespondances.AsNoTracking().Include(x => x.User).FirstAsync(x => x.EmpCode == empCode, ct);
        var dernier = await db.PointagesBruts.AsNoTracking().Where(p => p.EmpCode == empCode)
            .MaxAsync(p => (DateTime?)p.PunchTime, ct);
        return ToDto(withUser, dernier);
    }

    public async Task<int> ValiderCorrespondancesAutoAsync(PointageViewer viewer, CancellationToken ct = default)
    {
        var by = (await FindViewerAsync(viewer, ct))?.Id;
        var autos = await db.PointageCorrespondances
            .Where(c => c.Statut == PointageCorrespondanceStatuts.Auto && c.UserId != null)
            .ToListAsync(ct);
        foreach (var c in autos)
        {
            c.Statut = PointageCorrespondanceStatuts.Valide;
            c.UpdatedAt = DateTime.UtcNow;
            c.UpdatedBy = by;
        }
        await db.SaveChangesAsync(ct);
        return autos.Count;
    }

    public async Task<PointageEtatDto> GetEtatAsync(CancellationToken ct = default)
    {
        var state = await db.PointageSyncStates.AsNoTracking().FirstOrDefaultAsync(s => s.Id == PointageSyncState.SingletonId, ct);
        return new PointageEtatDto(
            O.Enabled,
            O.BaseUrl,
            state?.DernierEssai,
            state?.DerniereSynchro,
            state?.DerniereRelectureComplete,
            state?.DerniersPointagesRecus ?? 0,
            state?.DerniereErreur,
            state?.DerniereErreurAt,
            await db.PointagesBruts.LongCountAsync(ct),
            await db.PointageCorrespondances.CountAsync(c => c.Statut == PointageCorrespondanceStatuts.AValider, ct),
            await db.PointageCorrespondances.CountAsync(c => c.Statut == PointageCorrespondanceStatuts.Auto, ct));
    }

    private static PointageCorrespondanceDto ToDto(PointageCorrespondance c, DateTime? dernier) =>
        new(
            c.EmpCode,
            c.NomBioTime,
            c.DepartementBioTime,
            c.UserId,
            c.User is null ? null : $"{c.User.LastName} {c.User.FirstName}".Trim(),
            c.Statut,
            dernier,
            c.UpdatedAt);

    private static int StatutOrdre(string statut) => statut switch
    {
        PointageCorrespondanceStatuts.AValider => 0,
        PointageCorrespondanceStatuts.Auto => 1,
        PointageCorrespondanceStatuts.Valide => 2,
        _ => 3,
    };

    // ── Utilitaires ─────────────────────────────────────────────────────────────

    private async Task<DateTime?> DerniereSynchroAsync(CancellationToken ct) =>
        await db.PointageSyncStates.AsNoTracking()
            .Where(s => s.Id == PointageSyncState.SingletonId)
            .Select(s => s.DerniereSynchro)
            .FirstOrDefaultAsync(ct);

    /// <summary>v40 : date locale (fuseau du pointage) d'un horodatage UTC.</summary>
    private DateOnly? JourLocal(DateTime? utc)
    {
        if (utc is not DateTime u) return null;
        var enUtc = u.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(u, DateTimeKind.Utc) : u.ToUniversalTime();
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(O.FuseauHoraire);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(enUtc, tz));
        }
        catch (Exception)
        {
            return DateOnly.FromDateTime(enUtc);
        }
    }

    private DateOnly Aujourdhui()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(O.FuseauHoraire);
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
        }
        catch (Exception)
        {
            return DateOnly.FromDateTime(DateTime.Now);
        }
    }

    internal static bool IsAdminOrRh(string? role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);

    /// <summary>Responsables qui voient les pointages de leur périmètre.</summary>
    internal static bool IsResponsable(string? role) =>
        KyntusRoleNames.IsSuperviseur(role)
        || KyntusRoleNames.IsReferentTechnique(role)
        || KyntusRoleNames.IsChefDeProjet(role)
        || KyntusRoleNames.IsSupportManager(role);

    private async Task<User?> FindViewerAsync(PointageViewer viewer, CancellationToken ct)
    {
        if (viewer.AuthUserId is int authId && authId > 0)
        {
            var byAuth = await db.Users.AsNoTracking().Include(u => u.SubService).FirstOrDefaultAsync(u => u.AuthUserId == authId, ct);
            if (byAuth is not null)
                return byAuth;
        }
        if (viewer.SubjectId is Guid subject && subject != Guid.Empty)
        {
            var bySubject = await db.Users.AsNoTracking().Include(u => u.SubService).FirstOrDefaultAsync(u => u.Guid == subject, ct);
            if (bySubject is not null)
                return bySubject;
        }
        var email = viewer.Email?.Trim().ToLower();
        if (string.IsNullOrEmpty(email))
            return null;
        return await db.Users.AsNoTracking().Include(u => u.SubService).FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);
    }
}
