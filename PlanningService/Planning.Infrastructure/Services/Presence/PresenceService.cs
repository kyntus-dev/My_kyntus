using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Planning.Application.Pointage;
using Planning.Application.Presence;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.Presence;

/// <summary>
/// Tableau des présences : journées calculées depuis le pointage, complétées par les saisies des
/// responsables (motif d'absence, retard autorisé, commentaire). Chaque responsable ne voit et ne
/// modifie que les agents de son périmètre ; RH et Admin voient tout.
/// </summary>
public sealed class PresenceService(
    AppDbContext db,
    IPointageQueryService pointage,
    IPlanningPerimeterResolver perimeter,
    IOptionsMonitor<PointageOptions> options) : IPresenceService
{
    /// <summary>Période affichée au plus (une période de paie du 24 au 23 tient dans 31 jours).</summary>
    public const int PeriodeMaxJours = 31;

    public async Task<PresenceGrilleDto?> GetGrilleAsync(PresenceViewer viewer, DateOnly du, DateOnly au, bool peutModifier, CancellationToken ct = default)
    {
        (du, au) = Borner(du, au);
        var me = await FindViewerAsync(viewer, ct);
        if (me is null && !viewer.Global)
            return null;

        var (ids, libelle) = await AgentsVisiblesAsync(me, viewer.Global, ct);
        var agents = await ConstruireAsync(ids, du, au, ct);
        return new PresenceGrilleDto(
            libelle,
            du,
            au,
            options.CurrentValue.ToleranceMinutes,
            await DerniereSynchroAsync(ct),
            peutModifier,
            PresenceCodes.Tous,
            agents);
    }

    public async Task<PresenceAgentDto> SaisirAsync(
        PresenceViewer viewer, PresenceSaisieRequest request, DateOnly grilleDu, DateOnly grilleAu, CancellationToken ct = default)
    {
        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim().ToUpperInvariant();
        if (code is not null && !PresenceCodes.EstValide(code))
            throw new PresenceRefusException($"Code « {code} » inconnu.");
        var fin = request.Au is DateOnly a && a >= request.Jour ? a : request.Jour;
        if (fin.DayNumber - request.Jour.DayNumber >= 62)
            throw new PresenceRefusException("Période trop longue : 62 jours au plus.");

        var me = await FindViewerAsync(viewer, ct);
        if (!viewer.Global)
        {
            if (me is null)
                throw new PresenceRefusException("Compte introuvable dans le Planning.", horsPerimetre: true);
            var (ids, _) = await AgentsVisiblesAsync(me, false, ct);
            if (!ids.Contains(request.UserId))
                throw new PresenceRefusException("Cet agent n'est pas dans votre périmètre.", horsPerimetre: true);
        }
        else if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct))
        {
            throw new PresenceRefusException("Agent introuvable.");
        }

        var commentaire = Nettoyer(request.Commentaire, 1000);
        var motif = Nettoyer(request.MotifAutorisation, 300);
        var auteur = me is null ? viewer.Email : NomCourt(me);
        var existantes = await db.PresenceJours
            .Where(p => p.UserId == request.UserId && p.Jour >= request.Jour && p.Jour <= fin)
            .ToDictionaryAsync(p => p.Jour, ct);

        for (var jour = request.Jour; jour <= fin; jour = jour.AddDays(1))
        {
            var vide = code is null && !request.RetardAutorise && !request.RetardPauseAutorise && commentaire is null;
            if (!existantes.TryGetValue(jour, out var ligne))
            {
                if (vide) continue;
                ligne = new PresenceJour { UserId = request.UserId, Jour = jour };
                db.PresenceJours.Add(ligne);
            }
            else if (vide)
            {
                db.PresenceJours.Remove(ligne); // retour au calcul du pointage
                continue;
            }
            ligne.Code = code;
            ligne.RetardAutorise = request.RetardAutorise;
            ligne.RetardPauseAutorise = request.RetardPauseAutorise;
            ligne.MotifAutorisation = request.RetardAutorise || request.RetardPauseAutorise ? motif : null;
            ligne.Commentaire = commentaire;
            ligne.UpdatedAt = DateTime.UtcNow;
            ligne.UpdatedBy = me?.Id;
            ligne.UpdatedByNom = auteur;
        }
        await db.SaveChangesAsync(ct);

        (grilleDu, grilleAu) = Borner(grilleDu, grilleAu);
        return (await ConstruireAsync([request.UserId], grilleDu, grilleAu, ct)).Single();
    }

    // ── Construction ────────────────────────────────────────────────────────────

    private async Task<List<PresenceAgentDto>> ConstruireAsync(IReadOnlyList<int> ids, DateOnly du, DateOnly au, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var suivis = await pointage.CalculerPourAsync(ids, du, au, ct);

        var users = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                Service = u.SubService != null ? u.SubService.Name : null,
                Cellule = u.SubService != null ? u.SubService.Service.Name : null,
                CelluleId = u.SubService != null ? u.SubService.Service.PrimeCelluleId : null,
                Pole = u.SubService != null ? u.SubService.Service.Floor.Name : null,
                PoleId = u.SubService != null ? u.SubService.Service.Floor.PrimePoleId : null,
            })
            .ToDictionaryAsync(u => u.Id, ct);
        var profils = await db.UserHrProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, ct);
        var saisies = await db.PresenceJours.AsNoTracking()
            .Where(p => ids.Contains(p.UserId) && p.Jour >= du && p.Jour <= au)
            .ToDictionaryAsync(p => (p.UserId, p.Jour), ct);
        var conges = await db.Conges.AsNoTracking()
            .Where(c => ids.Contains(c.UserId) && c.Status == CongeStatus.Approved && c.StartDate <= au && c.EndDate >= du)
            .Select(c => new { c.UserId, c.StartDate, c.EndDate, c.AbsenceType })
            .ToListAsync(ct);

        var result = new List<PresenceAgentDto>(suivis.Count);
        foreach (var s in suivis)
        {
            var org = users.GetValueOrDefault(s.UserId);
            var profil = profils.GetValueOrDefault(s.UserId);
            var jours = s.Jours.Select(j =>
            {
                var conge = conges.FirstOrDefault(c => c.UserId == s.UserId && c.StartDate <= j.Jour && c.EndDate >= j.Jour);
                var codeAuto = PresenceRegles.CodeAuto(
                    j,
                    conge is null ? null : PresenceCodes.DepuisConge(conge.AbsenceType),
                    EnFormation(profil, j.Jour));
                saisies.TryGetValue((s.UserId, j.Jour), out var saisie);
                var code = saisie?.Code ?? codeAuto;
                var entreeOk = saisie?.RetardAutorise ?? false;
                var pauseOk = saisie?.RetardPauseAutorise ?? false;
                return new PresenceJourDto(
                    j.Jour,
                    code,
                    saisie?.Code is null,
                    codeAuto,
                    j.Statut,
                    j.Planning,
                    j.Arrivee,
                    j.Depart,
                    j.Pointages,
                    j.RetardEntreeMinutes,
                    j.RetardPauseMinutes,
                    entreeOk,
                    pauseOk,
                    saisie?.MotifAutorisation,
                    PresenceRegles.RetardRetenu(code, j.RetardEntreeMinutes, j.RetardPauseMinutes, entreeOk, pauseOk),
                    PresenceRegles.RetardAutorise(code, j.RetardEntreeMinutes, j.RetardPauseMinutes, entreeOk, pauseOk),
                    j.Statut == PointageJourStatuts.PointageManquant,
                    saisie?.Commentaire,
                    saisie?.UpdatedByNom,
                    saisie?.UpdatedAt);
            }).ToList();

            result.Add(new PresenceAgentDto(
                s.UserId,
                s.Collaborateur,
                string.IsNullOrWhiteSpace(profil?.NumeroCarteAutoentrepreneur) ? "Salarié" : "Sous-traitant",
                org?.Pole,
                org?.PoleId,
                org?.Cellule,
                org?.CelluleId,
                org?.Service ?? s.Cellule,
                profil?.DateEntree,
                profil?.DateSortie,
                jours,
                Totaux(jours)));
        }
        return result
            .OrderBy(a => a.Cellule ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static PresenceTotauxDto Totaux(IReadOnlyList<PresenceJourDto> jours)
    {
        var absences = jours
            .Where(j => j.Code is not null and not PresenceCodes.Present and not PresenceCodes.Formation)
            .GroupBy(j => j.Code!)
            .ToDictionary(g => g.Key, g => g.Count());
        return new PresenceTotauxDto(
            jours.Count(j => j.Code == PresenceCodes.Present),
            jours.Count(j => j.Code == PresenceCodes.Formation),
            absences,
            jours.Sum(j => j.RetardRetenuMinutes),
            jours.Sum(j => j.RetardAutoriseMinutes),
            jours.Count(j => j.RetardRetenuMinutes > 0),
            jours.Count(j => j.ManquePointage),
            jours.Count(j => j.Auto && j.Code == PresenceCodes.AbsenceNonAutorisee));
    }

    private static bool EnFormation(UserHrProfile? p, DateOnly jour) =>
        p is not null && p.EnFormation
        && (p.DateDebutFormation is null || p.DateDebutFormation <= jour)
        && (p.DateFinFormationPrevue is null || p.DateFinFormationPrevue >= jour);

    // ── Périmètre ───────────────────────────────────────────────────────────────

    /// <summary>Agents visibles : tous les agents placés (RH / Admin) ou ceux du périmètre du responsable.</summary>
    private async Task<(List<int> Ids, string Libelle)> AgentsVisiblesAsync(User? me, bool global, CancellationToken ct)
    {
        if (global)
        {
            var tous = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.SubServiceId != null)
                .Select(u => u.Id).ToListAsync(ct);
            return (tous, "Toute l'entreprise");
        }
        var scope = await perimeter.GetManagedSubServiceIdsAsync(me!.Id, ct);
        if (scope.Count == 0)
            return ([], "Aucun périmètre rattaché à votre compte");
        var ids = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Id != me.Id && u.SubServiceId != null && scope.Contains(u.SubServiceId.Value))
            .Select(u => u.Id).ToListAsync(ct);
        return (ids, $"Votre périmètre ({ids.Count} agent(s))");
    }

    private async Task<User?> FindViewerAsync(PresenceViewer viewer, CancellationToken ct)
    {
        if (viewer.AuthUserId is int authId && authId > 0)
        {
            var byAuth = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.AuthUserId == authId, ct);
            if (byAuth is not null) return byAuth;
        }
        if (viewer.SubjectId is Guid subject && subject != Guid.Empty)
        {
            var bySubject = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Guid == subject, ct);
            if (bySubject is not null) return bySubject;
        }
        var email = viewer.Email?.Trim().ToLower();
        return string.IsNullOrEmpty(email)
            ? null
            : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);
    }

    // ── Outils ──────────────────────────────────────────────────────────────────

    private async Task<DateTime?> DerniereSynchroAsync(CancellationToken ct) =>
        await db.PointageSyncStates.AsNoTracking()
            .Where(s => s.Id == PointageSyncState.SingletonId)
            .Select(s => s.DerniereSynchro)
            .FirstOrDefaultAsync(ct);

    private static (DateOnly Du, DateOnly Au) Borner(DateOnly du, DateOnly au)
    {
        if (au < du) (du, au) = (au, du);
        if (au.DayNumber - du.DayNumber >= PeriodeMaxJours)
            au = du.AddDays(PeriodeMaxJours - 1);
        return (du, au);
    }

    private static string? Nettoyer(string? texte, int max)
    {
        var t = (texte ?? string.Empty).Trim();
        if (t.Length == 0) return null;
        return t.Length > max ? t[..max] : t;
    }

    /// <summary>« prenom.nom » comme dans l'export paie actuel.</summary>
    private static string NomCourt(User u)
    {
        var local = (u.Email ?? string.Empty).Split('@')[0];
        return local.Length > 0 ? local : $"{u.FirstName} {u.LastName}".Trim();
    }
}
