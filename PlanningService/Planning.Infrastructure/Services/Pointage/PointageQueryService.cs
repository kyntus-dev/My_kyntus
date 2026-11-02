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
                s.Totaux.ProductionMinutes, s.Totaux.ProductionPrevueMinutes, s.Regime))
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
        var production = jour.ProductionPrevueMinutes ?? RegimeResolver.ProductionExcelMinutes(today);
        var pause = jour.Source is null ? regime?.PauseAutoriseeMinutes ?? RegimeResolver.PauseExcelMinutes : jour.PauseAutoriseeMinutes;
        var travaille = jour.Statut is not (PointageJourStatuts.Repos or PointageJourStatuts.Conge or PointageJourStatuts.Ferie)
                        || jour.Pointages.Count > 0;

        return new PointageAujourdhuiDto(
            today,
            me.Id,
            suivi.Collaborateur,
            // Accès donné par l'Admin compte par compte (l'Admin y a toujours accès).
            string.Equals(viewer.Role, "Admin", StringComparison.OrdinalIgnoreCase) || await regimes.ADroitHeureSortieAsync(me.Id, ct),
            regime?.Nom,
            regime?.Cible,
            jour.Source,
            jour.Planning,
            production,
            pause,
            travaille,
            jour.Pointages,
            jour.Statut,
            suivi.DerniereSynchro);
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
        var pointages = await db.PointagesBruts.AsNoTracking()
            .Where(p => codes.Contains(p.EmpCode) && p.PunchTime >= debut && p.PunchTime < fin)
            .Select(p => new { p.EmpCode, p.PunchTime })
            .ToListAsync(ct);
        var pointagesParJour = pointages
            .GroupBy(p => (userParCode[p.EmpCode], DateOnly.FromDateTime(p.PunchTime)))
            .ToDictionary(g => g.Key, g => g.Select(p => p.PunchTime).ToList());

        var affectations = await db.ShiftAssignments.AsNoTracking()
            .Include(a => a.SubServiceShiftConfig)
            .Include(a => a.Shift)
            .Where(a => userIds.Contains(a.UserId)
                        && a.AssignedDate >= du && a.AssignedDate <= au
                        && a.WeeklyPlanning.Status == PlanningStatus.Published)
            .ToListAsync(ct);
        var regimeParUser = await regimes.ResoudreAsync(userIds, ct);
        var planningParJour = affectations
            .GroupBy(a => (a.UserId, a.AssignedDate))
            .ToDictionary(
                g => g.Key,
                g => ToPlanningJour(g.OrderBy(a => a.IsOnLeave).ThenBy(a => a.Id).First(), regimeParUser.GetValueOrDefault(g.Key.UserId)));

        var conges = await db.Conges.AsNoTracking()
            .Where(c => userIds.Contains(c.UserId) && c.Status == CongeStatus.Approved && c.StartDate <= au && c.EndDate >= du)
            .Select(c => new { c.UserId, c.StartDate, c.EndDate })
            .ToListAsync(ct);

        var today = Aujourdhui();
        var derniereSynchro = await DerniereSynchroAsync(ct);
        var result = new List<PointageSuiviDto>(users.Count);
        foreach (var user in users)
        {
            var regime = regimeParUser.GetValueOrDefault(user.Id);
            var jours = new List<PointageJourDto>();
            for (var jour = du; jour <= au; jour = jour.AddDays(1))
            {
                // Planning publié en priorité ; à défaut, le régime horaire du collaborateur.
                if (!planningParJour.TryGetValue((user.Id, jour), out var planning) && regime is not null)
                    planning = RegimeResolver.PlanningDuJour(regime, jour);
                if (conges.Any(c => c.UserId == user.Id && c.StartDate <= jour && c.EndDate >= jour))
                    planning = (planning ?? new PlanningJour(null, 0, false, false, null)) with { Conge = true };
                pointagesParJour.TryGetValue((user.Id, jour), out var bruts);
                var r = PointageCalculator.CalculerJour(
                    jour, bruts ?? [], planning, today, O.ToleranceMinutes, O.DoubleBadgeSecondes,
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
                    totaux.ProductionMinutes, totaux.ProductionPrevueMinutes, totaux.JoursProductionCalculee),
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
            source);

    private static PointageJourResult FromDto(PointageJourDto d) =>
        new(d.Jour, d.Statut, null, d.PauseAutoriseeMinutes, null, null, [], d.PauseMinutes, d.RetardEntreeMinutes, d.RetardPauseMinutes,
            d.ProductionMinutes, d.ProductionPrevueMinutes);

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
