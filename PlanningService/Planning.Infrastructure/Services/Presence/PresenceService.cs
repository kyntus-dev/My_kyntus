using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;
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
    IOptionsMonitor<PointageOptions> options,
    IMemoryCache? cache = null,
    IPaieValidationService? paie = null,
    IAppNotifier? notifier = null,
    ILogger<PresenceService>? logger = null) : IPresenceService
{
    /// <summary>
    /// v58 — Période de paie validée par les RH : plus personne n'y touche, pas même celui qui a
    /// le droit de saisir. Pour corriger, les RH rouvrent la période (Présences et paie › Export
    /// paie › Rouvrir), ce qui est tracé.
    /// </summary>
    private async Task RefuserSiPaieValideeAsync(DateOnly debut, DateOnly fin, CancellationToken ct)
    {
        if (paie is null) return;
        for (var jour = debut; jour <= fin; jour = jour.AddDays(1))
        {
            var verrou = await paie.VerrouDuJourAsync(jour, ct);
            if (verrou is null) continue;
            throw new PresenceRefusException(
                $"Tableau de paie validé par {verrou.ValideParNom} le "
                + $"{verrou.ValideLe.ToLocalTime():dd/MM/yyyy} : cette période n'est plus modifiable. "
                + "Demandez aux RH de la rouvrir.");
        }
    }

    /// <summary>
    /// Calcul du pointage gardé 30 s : passer du tableau aux retards puis à l'export paie ne
    /// recalcule pas tout. Les saisies des responsables (motifs, autorisations) sont relues à chaque fois.
    /// </summary>
    private static readonly TimeSpan DureeCache = TimeSpan.FromSeconds(30);

    /// <summary>Période affichée au plus (une période de paie du 24 au 23 tient dans 31 jours).</summary>
    public const int PeriodeMaxJours = 31;

    public async Task<PresenceGrilleDto?> GetGrilleAsync(PresenceViewer viewer, DateOnly du, DateOnly au, bool peutModifier, CancellationToken ct = default)
    {
        (du, au) = Borner(du, au);
        var me = await FindViewerAsync(viewer, ct);
        if (me is null && !viewer.Global)
            return null;

        // v37 : la grille (et donc l'export paie) montre aussi au responsable ses agents en formation,
        // en lecture : la saisie de leurs présences reste à l'équipe formation.
        var (ids, libelle) = await AgentsVisiblesAsync(me, viewer, ct, inclureFormation: true);
        var agents = await ConstruireAsync(ids, du, au, ct);
        return new PresenceGrilleDto(
            libelle,
            du,
            au,
            options.CurrentValue.ToleranceMinutes,
            await DerniereSynchroAsync(ct),
            peutModifier,
            PresenceCodes.Tous,
            agents,
            PeutAutoriserRetard(viewer.Role),
            VoitEncadrement(viewer),
            peutModifier && (viewer.Global ? viewer.EncadrementAgir ?? true : SaisitEncadrement(viewer)),
            viewer.Global,
            viewer.Global ? viewer.EncadrementVoir ?? true : VoitEncadrement(viewer));
    }

    /// <summary>v46 : superviseurs et référents techniques n'autorisent ni retard ni dépassement de pause.</summary>
    internal static bool PeutAutoriserRetard(string? role) =>
        !KyntusRoleNames.IsSuperviseur(role) && !KyntusRoleNames.IsReferentTechnique(role);

    /// <summary>
    /// v61 : rubrique « Présences des superviseurs et référents › Consulter » (responsable non global).
    /// Sans réponse du catalogue (null) : règle v46, réservée au chef de projet (RP).
    /// </summary>
    internal static bool VoitEncadrement(PresenceViewer viewer) =>
        !viewer.Global && (viewer.EncadrementVoir ?? KyntusRoleNames.IsChefDeProjet(viewer.Role));

    /// <summary>v61 : « … › Modifier » : saisir les présences des superviseurs et référents du périmètre.</summary>
    internal static bool SaisitEncadrement(PresenceViewer viewer) =>
        VoitEncadrement(viewer) && (viewer.EncadrementAgir ?? KyntusRoleNames.IsChefDeProjet(viewer.Role));

    private static bool EstEncadrement(string? role) =>
        KyntusRoleNames.IsSuperviseur(role) || KyntusRoleNames.IsReferentTechnique(role);

    public async Task<PresenceAgentDto> SaisirAsync(
        PresenceViewer viewer, PresenceSaisieRequest request, DateOnly grilleDu, DateOnly grilleAu, CancellationToken ct = default)
    {
        var code = string.IsNullOrWhiteSpace(request.Code) ? null : request.Code.Trim().ToUpperInvariant();
        if (code is not null && !PresenceCodes.EstValide(code))
            throw new PresenceRefusException($"Code « {code} » inconnu.");
        var fin = request.Au is DateOnly a && a >= request.Jour ? a : request.Jour;
        if (fin.DayNumber - request.Jour.DayNumber >= 62)
            throw new PresenceRefusException("Période trop longue : 62 jours au plus.");

        await RefuserSiPaieValideeAsync(request.Jour, fin, ct);

        var me = await FindViewerAsync(viewer, ct);
        if (!viewer.Global)
        {
            if (me is null)
                throw new PresenceRefusException("Compte introuvable dans le Planning.", horsPerimetre: true);
            var (ids, _) = await AgentsVisiblesAsync(me, viewer, ct);
            if (!ids.Contains(request.UserId))
                throw new PresenceRefusException("Cet agent n'est pas dans votre périmètre.", horsPerimetre: true);
            // v61 : superviseurs et référents : saisie selon la rubrique dédiée (consultation seule sinon).
            if (VoitEncadrement(viewer) && !SaisitEncadrement(viewer))
            {
                var roleCible = await db.Users.AsNoTracking()
                    .Where(u => u.Id == request.UserId)
                    .Select(u => u.Role != null ? u.Role.Name : null)
                    .FirstOrDefaultAsync(ct);
                if (EstEncadrement(roleCible))
                    throw new PresenceRefusException(
                        "Vous pouvez consulter les présences des superviseurs et référents, pas les modifier "
                        + "(droit « Présences des superviseurs et référents › Modifier »).", horsPerimetre: true);
            }
        }
        else if (!await db.Users.AnyAsync(u => u.Id == request.UserId, ct))
        {
            throw new PresenceRefusException("Agent introuvable.");
        }
        else if (viewer.EncadrementAgir == false)
        {
            // v62 : RH, Admin ou portée globale sans « Présences des superviseurs et référents › Modifier ».
            var roleCible = await db.Users.AsNoTracking()
                .Where(u => u.Id == request.UserId)
                .Select(u => u.Role != null ? u.Role.Name : null)
                .FirstOrDefaultAsync(ct);
            if (EstEncadrement(roleCible))
                throw new PresenceRefusException(
                    "Vous pouvez consulter les présences des superviseurs et référents, pas les modifier "
                    + "(droit « Présences des superviseurs et référents › Modifier »).", horsPerimetre: true);
        }

        // v64 : hors contrat (avant l'entrée réelle, après la sortie effective), aucune présence ni absence.
        if (code is not null || request.AbsencePartielle || request.RetardAutorise || request.RetardPauseAutorise)
        {
            var embaucheCible = await db.Users.AsNoTracking()
                .Where(u => u.Id == request.UserId)
                .Select(u => (DateTime?)u.HireDate)
                .FirstOrDefaultAsync(ct);
            var profilCible = await db.UserHrProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == request.UserId, ct);
            var contratCible = ContratPeriode.De(profilCible, embaucheCible);
            for (var jourHc = request.Jour; jourHc <= fin; jourHc = jourHc.AddDays(1))
            {
                if (contratCible.MotifHorsContrat(jourHc) is string motifHc)
                    throw new PresenceRefusException(
                        $"Journée du {jourHc:dd/MM/yyyy} hors contrat ({motifHc}) : aucune présence ni absence ne peut y être saisie.");
            }
        }

        var commentaire = Nettoyer(request.Commentaire, 1000);
        var motif = Nettoyer(request.MotifAutorisation, 300);
        var auteur = me is null ? viewer.Email : NomCourt(me);
        var existantes = await db.PresenceJours
            .Where(p => p.UserId == request.UserId && p.Jour >= request.Jour && p.Jour <= fin)
            .ToDictionaryAsync(p => p.Jour, ct);

        // v46 : un superviseur ou un référent technique ne change jamais une autorisation de retard.
        if (!PeutAutoriserRetard(viewer.Role))
        {
            for (var j = request.Jour; j <= fin; j = j.AddDays(1))
            {
                existantes.TryGetValue(j, out var precedente);
                if (request.RetardAutorise != (precedente?.RetardAutorise ?? false)
                    || request.RetardPauseAutorise != (precedente?.RetardPauseAutorise ?? false))
                    throw new PresenceRefusException(
                        "Seuls le chef de projet, les RH et l'Admin peuvent autoriser un retard ou un dépassement de pause.");
            }
        }

        // v62 : état avant la saisie (planning et notification à l'agent).
        var avant = existantes.ToDictionary(
            e => e.Key,
            e => new EtatPresence(e.Value.Code, e.Value.AbsencePartielle, e.Value.RetardAutorise || e.Value.RetardPauseAutorise));

        for (var jour = request.Jour; jour <= fin; jour = jour.AddDays(1))
        {
            var vide = code is null && !request.RetardAutorise && !request.RetardPauseAutorise && commentaire is null
                       && !request.AbsencePartielle;
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
            ligne.AbsencePartielle = request.AbsencePartielle;
            ligne.UpdatedAt = DateTime.UtcNow;
            ligne.UpdatedBy = me?.Id;
            ligne.UpdatedByNom = auteur;
        }
        await db.SaveChangesAsync(ct);
        await ApresSaisieAsync(request, code, fin, avant, auteur, ct);

        (grilleDu, grilleAu) = Borner(grilleDu, grilleAu);
        return (await ConstruireAsync([request.UserId], grilleDu, grilleAu, ct)).Single();
    }

    private sealed record EtatPresence(string? Code, bool Partielle, bool RetardAutorise);

    /// <summary>
    /// v62 — Après une saisie : le planning suit le motif (arrêt maladie, congé… = aucun shift ; motif
    /// retiré = shift rendu), puis l'agent est notifié. Rien ici ne fait échouer la saisie.
    /// </summary>
    private async Task ApresSaisieAsync(
        PresenceSaisieRequest request, string? code, DateOnly fin,
        IReadOnlyDictionary<DateOnly, EtatPresence> avant, string? auteur, CancellationToken ct)
    {
        var jours = new List<DateOnly>();
        for (var j = request.Jour; j <= fin; j = j.AddDays(1))
            jours.Add(j);
        bool BloquaitAvant(DateOnly j) =>
            avant.TryGetValue(j, out var a) && !a.Partielle && PresenceCodes.RetireDuPlanning(a.Code);
        var bloqueApres = !request.AbsencePartielle && PresenceCodes.RetireDuPlanning(code);
        var poses = bloqueApres ? jours.Where(j => !BloquaitAvant(j)).ToList() : new List<DateOnly>();
        var retires = bloqueApres ? new List<DateOnly>() : jours.Where(BloquaitAvant).ToList();

        try
        {
            var n = await PresenceAbsencesPlanning.AppliquerAuPlanningAsync(db, request.UserId, poses, retires, ct);
            if (n > 0)
                logger?.LogInformation("Présence → planning : {Nombre} journée(s) mise(s) à jour pour l'agent {UserId}.", n, request.UserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            logger?.LogWarning(ex, "Planning non mis à jour après la saisie de présence de l'agent {UserId}.", request.UserId);
        }

        if (notifier is null)
            return;
        var notification = NotificationSaisie(request, code, fin, avant, auteur, poses, retires);
        if (notification is not null)
            await notifier.NotifierAsync(new NotificationEmailRecipients(UserIds: [request.UserId]), notification, ct: ct);
    }

    /// <summary>v62 : message à l'agent après une saisie de son responsable (null : rien d'utile à dire).</summary>
    private static AppNotification? NotificationSaisie(
        PresenceSaisieRequest request, string? code, DateOnly fin, IReadOnlyDictionary<DateOnly, EtatPresence> avant,
        string? auteur, IReadOnlyCollection<DateOnly> poses, IReadOnlyCollection<DateOnly> retires)
    {
        var par = string.IsNullOrWhiteSpace(auteur) ? string.Empty : $" par {auteur.Trim()}";
        var periode = request.Jour == fin ? $"le {request.Jour:dd/MM/yyyy}" : $"du {request.Jour:dd/MM/yyyy} au {fin:dd/MM/yyyy}";
        var cle = $"PRESENCE-{request.UserId}-{request.Jour:yyyyMMdd}";
        if (poses.Count > 0)
        {
            var libelle = PresenceCodes.Tous.FirstOrDefault(c => c.Code == code)?.Libelle ?? code;
            return new AppNotification("presences", $"Absence enregistrée : {libelle}",
                $"{libelle} enregistré(e) {periode}{par}. Votre planning est mis à jour : aucun shift ne vous est attribué ces jours-là.",
                "/mes-plannings", cle);
        }
        if (retires.Count > 0)
            return new AppNotification("presences", "Absence retirée",
                $"L'absence {periode} a été retirée{par}. Vous êtes de nouveau attendu(e) selon votre planning.",
                "/mes-plannings", cle);
        var nouveauRetardAutorise = (request.RetardAutorise || request.RetardPauseAutorise)
            && !avant.Values.Any(a => a.RetardAutorise);
        if (nouveauRetardAutorise)
            return new AppNotification("presences", "Retard autorisé",
                $"Votre retard {periode} a été autorisé{par}"
                + (string.IsNullOrWhiteSpace(request.MotifAutorisation) ? "." : $" (motif : {request.MotifAutorisation.Trim()})."),
                "/pointage/mon-suivi", cle);
        return null;
    }

    public async Task<PresenceAuteur> ResoudreAsync(PresenceViewer viewer, CancellationToken ct = default)
    {
        var me = await FindViewerAsync(viewer, ct);
        var nom = me is null ? viewer.Email : NomCourt(me);
        if (viewer.Global)
            return new PresenceAuteur(me?.Id, nom, null);
        if (me is null)
            return new PresenceAuteur(null, nom, new HashSet<int>());
        var (ids, _) = await AgentsVisiblesAsync(me, viewer, ct);
        return new PresenceAuteur(me.Id, nom, ids.ToHashSet());
    }

    // ── Construction ────────────────────────────────────────────────────────────

    private async Task<List<PresenceAgentDto>> ConstruireAsync(IReadOnlyList<int> ids, DateOnly du, DateOnly au, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var suivis = await CalculerAvecCacheAsync(ids, du, au, ct);

        var users = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                Service = u.SubService != null ? u.SubService.Name : null,
                Cellule = u.SubService != null ? u.SubService.Service.Name : null,
                CelluleId = u.SubService != null ? u.SubService.Service.PrimeCelluleId : null,
                Pole = u.SubService != null ? u.SubService.Service.Floor.Name : null,
                PoleId = u.SubService != null ? u.SubService.Service.Floor.PrimePoleId : null,
                Role = u.Role != null ? u.Role.Name : null,
                u.Guid,
                u.HireDate,
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

        var congesParAgent = conges.GroupBy(c => c.UserId).ToDictionary(g => g.Key, g => g.ToList());

        // v61 : périmètre de chaque superviseur / référent : son placement, plus le pôle et la cellule des
        // agents affichés qui le désignent sur leur fiche (jamais au-delà des agents visibles).
        var encadres = new Dictionary<int, (HashSet<string> PoleIds, HashSet<string> Poles, HashSet<string> Cellules)>();
        var encadrantsParGuid = users.Values
            .Where(x => EstEncadrement(x.Role) && x.Guid != Guid.Empty)
            .GroupBy(x => x.Guid)
            .ToDictionary(grp => grp.Key, grp => grp.First().Id);
        void AjouterEncadre(int encadrant, string? poleId, string? pole, string? cellule)
        {
            if (!encadres.TryGetValue(encadrant, out var t))
            {
                t = (new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                     new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                     new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                encadres[encadrant] = t;
            }
            if (!string.IsNullOrWhiteSpace(poleId)) t.PoleIds.Add(poleId);
            if (!string.IsNullOrWhiteSpace(pole)) t.Poles.Add(pole);
            if (!string.IsNullOrWhiteSpace(cellule)) t.Cellules.Add(cellule);
        }
        foreach (var orgEncadrant in users.Values.Where(x => EstEncadrement(x.Role)))
            AjouterEncadre(orgEncadrant.Id, orgEncadrant.PoleId, orgEncadrant.Pole, orgEncadrant.Cellule);
        foreach (var profilAgent in profils.Values)
        {
            if (!users.TryGetValue(profilAgent.UserId, out var agentOrg)) continue;
            foreach (var designe in new[] { profilAgent.SuperviseurId, profilAgent.ReferentTechniqueId })
            {
                if (designe is Guid guidDesigne && encadrantsParGuid.TryGetValue(guidDesigne, out var idEncadrant)
                    && idEncadrant != profilAgent.UserId)
                    AjouterEncadre(idEncadrant, agentOrg.PoleId, agentOrg.Pole, agentOrg.Cellule);
            }
        }

        var result = new List<PresenceAgentDto>(suivis.Count);
        foreach (var s in suivis)
        {
            var org = users.GetValueOrDefault(s.UserId);
            var profil = profils.GetValueOrDefault(s.UserId);
            // v64 : période sous contrat. Hors contrat, une journée sans pointage ni saisie n'a ni
            // présence ni absence (case grisée) ; un pointage ou une saisie existants restent affichés.
            var contrat = ContratPeriode.De(profil, org?.HireDate);
            var jours = s.Jours.Select(jBrut =>
            {
                var j = !contrat.Couvre(jBrut.Jour) && jBrut.Pointages.Count == 0
                    ? jBrut with { Statut = PointageJourStatuts.HorsContrat }
                    : jBrut;
                var conge = congesParAgent.TryGetValue(s.UserId, out var siens)
                    ? siens.FirstOrDefault(c => c.StartDate <= j.Jour && c.EndDate >= j.Jour)
                    : null;
                var codeAuto = PresenceRegles.CodeAuto(
                    j,
                    conge is null ? null : PresenceCodes.DepuisConge(conge.AbsenceType),
                    EnFormation(profil, j.Jour));
                saisies.TryGetValue((s.UserId, j.Jour), out var saisie);
                var code = saisie?.Code ?? codeAuto;
                var entreeOk = saisie?.RetardAutorise ?? false;
                var pauseOk = saisie?.RetardPauseAutorise ?? false;
                var hs = PresenceRegles.HeuresSupplementaires(
                    j.Jour, j.Statut, code, j.Planning, j.PauseAutoriseeMinutes, j.ProductionPrevueMinutes, j.Pointages);
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
                    saisie?.UpdatedAt,
                    saisie?.AbsencePartielle ?? false,
                    hs.FinPrevue,
                    hs.Avant21Minutes,
                    hs.Apres21Minutes,
                    hs.DimancheFerieMinutes);
            }).ToList();

            result.Add(new PresenceAgentDto(
                s.UserId,
                s.Collaborateur,
                // v64 : type de la fiche employé (même règle partout : fiche, présences, paie, exports).
                TypesEmploye.Libelle(profil),
                org?.Pole,
                org?.PoleId,
                org?.Cellule,
                org?.CelluleId,
                org?.Service ?? s.Cellule,
                // v64 : bornes du contrat (entrée réelle, sortie effective), mêmes que le planning et la paie.
                contrat.Entree,
                contrat.Sortie,
                jours,
                Totaux(jours),
                ADejaEteEnFormation(profil) ? profil!.DateDebutFormation ?? profil.DateEntree : null,
                profil?.FinFormationEffective(),
                org is null ? null : $"{(org.LastName ?? string.Empty).Trim().ToUpperInvariant()} {(org.FirstName ?? string.Empty).Trim()}".Trim(),
                profil?.EnFormation ?? false,
                org?.Role,
                // v48 : type de formation (contractuel / non contractuel).
                profil?.TypeFormation,
                encadres.TryGetValue(s.UserId, out var perimetreEncadrant) ? perimetreEncadrant.PoleIds.OrderBy(x => x).ToList() : null,
                perimetreEncadrant.Poles?.OrderBy(x => x).ToList(),
                perimetreEncadrant.Cellules?.OrderBy(x => x).ToList(),
                // v63.3 : sans identifiant de pointeuse, aucun pointage ne remonte : les journées
                // restent vides (ou « ABS N » les jours planifiés) tant qu'un motif n'est pas saisi.
                s.IdsPointeuse.Count > 0));
        }
        return result
            .OrderBy(a => a.Cellule ?? "", StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static PresenceTotauxDto Totaux(IReadOnlyList<PresenceJourDto> jours)
    {
        // v63.3 : un jour férié n'est pas une absence (il ne doit plus gonfler la colonne « Abs. »).
        var absences = jours
            .Where(j => j.Code is not null and not PresenceCodes.Present and not PresenceCodes.Formation and not PresenceCodes.Ferie)
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

    /// <summary>
    /// v30 : jour de formation. Case cochée : formation en cours, même au-delà de la date prévue
    /// (elle peut être prolongée). Case décochée : jusqu'à la veille de l'intégration en production.
    /// </summary>
    private static bool EnFormation(UserHrProfile? p, DateOnly jour) => p is not null && p.EstEnFormationLe(jour);

    private static bool ADejaEteEnFormation(UserHrProfile? p) =>
        p is not null && (p.EnFormation || p.FinFormationEffective() is not null);

    /// <summary>v30 : formateurs et équipe formation suivent les agents en formation.</summary>
    internal static bool EstFormateur(string? role)
    {
        var r = (role ?? string.Empty).Replace("_", string.Empty).Replace(" ", string.Empty);
        return string.Equals(r, "Formateur", StringComparison.OrdinalIgnoreCase)
               || string.Equals(r, "EquipeFormation", StringComparison.OrdinalIgnoreCase);
    }

    // ── Périmètre ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Agents visibles :
    /// - RH / Admin : tous les agents placés, plus les agents en formation (même sans service) ;
    /// - formateurs et équipe formation (v30) : tous les agents dont la case « En formation » est cochée ;
    /// - autres responsables : leur périmètre, sans les agents encore en formation.
    /// - v37, grille et export paie (<paramref name="inclureFormation"/>) : plus leurs agents en formation
    ///   (dans leur périmètre, ou fiche « chef de projet / superviseur » à leur nom), en lecture seule.
    /// </summary>
    private async Task<(List<int> Ids, string Libelle)> AgentsVisiblesAsync(
        User? me, PresenceViewer viewer, CancellationToken ct, bool inclureFormation = false)
    {
        var enFormation = await db.UserHrProfiles.AsNoTracking()
            .Where(p => p.EnFormation && p.User.IsActive)
            .Select(p => p.UserId)
            .ToListAsync(ct);
        if (viewer.Global)
        {
            var tous = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.SubServiceId != null)
                .Select(u => u.Id).ToListAsync(ct);
            // v62 : superviseurs et référents sans cellule (rattachés par l'organisation) : proposés dans
            // leurs tableaux dédiés. Le tableau principal et l'export paie des RH n'en changent pas.
            var encadrementNonPlace = new List<int>();
            if (viewer.EncadrementVoir != false)
            {
                encadrementNonPlace = (await db.Users.AsNoTracking()
                        .Where(u => u.IsActive && u.SubServiceId == null && u.Role != null)
                        .Select(u => new { u.Id, Role = u.Role!.Name })
                        .ToListAsync(ct))
                    .Where(u => EstEncadrement(u.Role))
                    .Select(u => u.Id)
                    .ToList();
            }
            return (tous.Union(enFormation).Union(encadrementNonPlace).ToList(), "Toute l'entreprise");
        }
        if (EstFormateur(viewer.Role))
        {
            var stagiaires = enFormation.Where(id => id != me!.Id).ToList();
            return (stagiaires, $"Agents en formation ({stagiaires.Count})");
        }
        var scope = await perimeter.GetManagedSubServiceIdsAsync(me!.Id, ct);
        var ids = new List<int>();
        if (scope.Count > 0)
        {
            ids = await db.Users.AsNoTracking()
                .Where(u => u.IsActive && u.Id != me.Id && u.SubServiceId != null && scope.Contains(u.SubServiceId.Value))
                .Select(u => u.Id).ToListAsync(ct);
        }
        var horsFormation = ids.Except(enFormation).ToList();
        // v46 : un chef de projet voit aussi (et saisit) les superviseurs et référents techniques de son
        // périmètre : placés dans ses services, ou désignés sur la fiche de ses agents.
        // v61 : ce droit est la rubrique « Présences des superviseurs et référents › Consulter » (RP par défaut).
        if (VoitEncadrement(viewer) && ids.Count > 0)
        {
            var encadrement = await EncadrementAsync(me, ids, scope, ct);
            horsFormation = horsFormation.Union(encadrement).ToList();
        }
        else if (KyntusRoleNames.IsChefDeProjet(viewer.Role) && horsFormation.Count > 0)
        {
            // v61 : droit retiré au RP : ni superviseur ni référent dans son tableau (même placés dans ses services).
            var encadrants = (await db.Users.AsNoTracking()
                    .Where(u => horsFormation.Contains(u.Id) && u.Role != null)
                    .Select(u => new { u.Id, Role = u.Role!.Name })
                    .ToListAsync(ct))
                .Where(u => EstEncadrement(u.Role))
                .Select(u => u.Id)
                .ToHashSet();
            horsFormation = horsFormation.Where(id => !encadrants.Contains(id)).ToList();
        }
        if (!inclureFormation)
        {
            if (scope.Count == 0)
                return (new List<int>(), "Aucun périmètre rattaché à votre compte");
            return (horsFormation, $"Votre périmètre ({horsFormation.Count} agent(s))");
        }

        // v37 : agents en formation du responsable : rattachés à son périmètre, ou dont la fiche le
        // désigne comme chef de projet ou superviseur.
        var moi = me.Guid;
        var rattaches = await db.UserHrProfiles.AsNoTracking()
            .Where(p => p.EnFormation && p.User.IsActive && p.UserId != me.Id
                        && (p.ChefDeProjetId == moi || p.SuperviseurId == moi))
            .Select(p => p.UserId)
            .ToListAsync(ct);
        var formation = ids.Intersect(enFormation).Union(rattaches).Distinct().ToList();
        if (scope.Count == 0 && formation.Count == 0)
            return (new List<int>(), "Aucun périmètre rattaché à votre compte");
        var visibles = horsFormation.Concat(formation).Distinct().ToList();
        return (visibles, formation.Count == 0
            ? $"Votre périmètre ({visibles.Count} agent(s))"
            : $"Votre périmètre ({visibles.Count} agent(s), dont {formation.Count} en formation)");
    }

    /// <summary>
    /// v46 : superviseurs et référents techniques du périmètre d'un chef de projet : comptes actifs de ces
    /// rôles placés dans ses services, ou désignés comme superviseur / référent sur la fiche de ses agents.
    /// </summary>
    private async Task<List<int>> EncadrementAsync(User me, List<int> agents, HashSet<int> scope, CancellationToken ct)
    {
        var designes = await db.UserHrProfiles.AsNoTracking()
            .Where(p => agents.Contains(p.UserId))
            .Select(p => new { p.SuperviseurId, p.ReferentTechniqueId })
            .ToListAsync(ct);
        var guids = designes.SelectMany(p => new[] { p.SuperviseurId, p.ReferentTechniqueId })
            .Where(g => g is not null && g != Guid.Empty)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();
        var candidats = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.Id != me.Id && u.Role != null
                        && ((u.SubServiceId != null && scope.Contains(u.SubServiceId.Value)) || guids.Contains(u.Guid)))
            .Select(u => new { u.Id, Role = u.Role!.Name })
            .ToListAsync(ct);
        return candidats
            .Where(c => KyntusRoleNames.IsSuperviseur(c.Role) || KyntusRoleNames.IsReferentTechnique(c.Role))
            .Select(c => c.Id)
            .ToList();
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

    private async Task<IReadOnlyList<PointageSuiviDto>> CalculerAvecCacheAsync(IReadOnlyList<int> ids, DateOnly du, DateOnly au, CancellationToken ct)
    {
        if (cache is null || ids.Count < 2)
            return await pointage.CalculerPourAsync(ids, du, au, ct);
        // Clé complète (période + liste des agents) : jamais de mélange entre deux périmètres.
        var cle = $"presences:pointage:{du:yyyyMMdd}:{au:yyyyMMdd}:{string.Join(',', ids.OrderBy(x => x))}";
        if (cache.TryGetValue(cle, out IReadOnlyList<PointageSuiviDto>? deja) && deja is not null)
            return deja;
        var calcul = await pointage.CalculerPourAsync(ids, du, au, ct);
        cache.Set(cle, calcul, DureeCache);
        return calcul;
    }
}
