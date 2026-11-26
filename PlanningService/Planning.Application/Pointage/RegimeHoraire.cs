namespace Planning.Application.Pointage;

/// <summary>Jour travaillé d'un régime (jour ISO 1 = lundi … 7 = dimanche).</summary>
public sealed record RegimeJourDto(int JourSemaine, string Arrivee, int ProductionMinutes);

public sealed record RegimeAffectationDto(int Id, string Cible, int CibleId, string Libelle, string? Detail);

public sealed record RegimeHoraireDto(
    int Id,
    string Nom,
    int PauseAutoriseeMinutes,
    bool AccesCalculSortie,
    IReadOnlyList<RegimeJourDto> Jours,
    IReadOnlyList<RegimeAffectationDto> Affectations,
    DateTime UpdatedAt);

public sealed record RegimeHoraireRequest(
    string Nom,
    int PauseAutoriseeMinutes,
    bool AccesCalculSortie,
    IReadOnlyList<RegimeJourDto> Jours);

public sealed record RegimeAffectationRequest(string Cible, int CibleId);

/// <summary>Élément sélectionnable pour une affectation (pôle, cellule, service ou collaborateur).</summary>
public sealed record RegimeCibleDto(string Cible, int Id, string Libelle, string? Detail, int? RegimeId);

/// <summary>Régime effectif d'un collaborateur et l'affectation qui le lui donne.</summary>
public sealed record RegimeEffectif(
    int Id,
    string Nom,
    int PauseAutoriseeMinutes,
    bool AccesCalculSortie,
    IReadOnlyDictionary<int, (TimeOnly Arrivee, int ProductionMinutes)> Jours,
    string Cible);

/// <summary>Données du jour pour la page « Heure de sortie » et le tableau de bord.</summary>
public sealed record PointageAujourdhuiDto(
    DateOnly Jour,
    int UserId,
    string Collaborateur,
    bool AccesCalculSortie,
    string? Regime,
    string? RegimeCible,
    // Source : Planning / Regime / null (valeurs par défaut de l'Excel).
    string? Source,
    string? ArriveePrevue,
    int ProductionPrevueMinutes,
    int PauseAutoriseeMinutes,
    bool JourTravaille,
    IReadOnlyList<string> Pointages,
    string Statut,
    DateTime? DerniereSynchro,
    // Production mémorisée par le collaborateur : « 0840 », « 0740 » ou null (v18).
    string? ChoixProduction = null,
    // v43 : horaire de base (« 0840 » ou « 0740 ») et jour de la semaine où la journée à 07h40 a déjà été prise.
    string? ProductionBase = null,
    DateOnly? Jour0740Semaine = null,
    // v65 : journée prévue au planning de l'agent. Fausse (samedi hors planning, jour de repos),
    // aucune production n'est attendue : tout ce qui est pointé est en heures supplémentaires.
    bool Planifie = true);

/// <summary>
/// Choix de production proposés dans « Heure de sortie ». v40 : deux valeurs fixes, 07h40 ou 08h40,
/// choisies par le collaborateur pour la journée en cours seulement (le lendemain, il choisit à nouveau).
/// Le choix ne sert qu'au calcul de l'heure de sortie : planning, solde et paie n'en dépendent pas.
/// </summary>
public static class ChoixProductionSortie
{
    /// <summary>08h40.</summary>
    public const string Standard = "0840";
    /// <summary>07h40.</summary>
    public const string Court = "0740";

    public static bool EstValide(string? choix) => choix is null or Standard or Court;

    /// <summary>Production pour un choix ; null = pas de choix aujourd'hui.</summary>
    public static int? ProductionMinutes(string? choix, DateOnly jour) => choix switch
    {
        Standard => 8 * 60 + 40,
        Court => 7 * 60 + 40,
        _ => null,
    };

    /// <summary>
    /// v43 : un horaire 08h40 a droit à une seule journée à 07h40 par semaine, du lundi au vendredi.
    /// Renvoie le jour (autre qu'aujourd'hui) où elle a déjà été prise, ou null si elle est disponible.
    /// Le samedi n'est pas concerné.
    /// </summary>
    public static DateOnly? JourneeCourteDejaPrise(string? productionBase, DateOnly aujourdhui, IEnumerable<(DateOnly Jour, string Choix)> choixSemaine)
    {
        if (productionBase == Court) return null;
        if (aujourdhui.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) return null;
        var lundi = aujourdhui.AddDays(-(((int)aujourdhui.DayOfWeek + 6) % 7));
        var vendredi = lundi.AddDays(4);
        foreach (var (jour, choix) in choixSemaine)
            if (jour >= lundi && jour <= vendredi && jour != aujourdhui && choix == Court)
                return jour;
        return null;
    }

    /// <summary>v40 : un choix ne vaut que pour le jour où il a été fait (date locale).</summary>
    public static string? ChoixDuJour(string? choix, DateOnly? choisiLe, DateOnly aujourdhui) =>
        choisiLe == aujourdhui && EstValide(choix) ? choix : null;
}

public sealed record ChoixProductionRequest(string? Choix);

/// <summary>v42 : choix fait pour une journée (« 0740 » ou « 0840 »).</summary>
public sealed record ChoixProductionJourDto(DateOnly Jour, string Choix);

/// <summary>Résolution des régimes : la cible la plus précise l'emporte.</summary>
public static class RegimeResolver
{
    /// <summary>
    /// Régime d'un collaborateur : son affectation personnelle, sinon celle de son service,
    /// de sa cellule, puis de son pôle. Null : aucun régime.
    /// </summary>
    public static (int RegimeId, string Cible)? Resoudre(
        int userId,
        int? serviceId,
        int? celluleId,
        int? poleId,
        IReadOnlyDictionary<(string Cible, int CibleId), int> affectations)
    {
        if (affectations.TryGetValue((Domain.Entities.RegimeCibles.Utilisateur, userId), out var r))
            return (r, Domain.Entities.RegimeCibles.Utilisateur);
        if (serviceId is int s && affectations.TryGetValue((Domain.Entities.RegimeCibles.Service, s), out r))
            return (r, Domain.Entities.RegimeCibles.Service);
        if (celluleId is int c && affectations.TryGetValue((Domain.Entities.RegimeCibles.Cellule, c), out r))
            return (r, Domain.Entities.RegimeCibles.Cellule);
        if (poleId is int p && affectations.TryGetValue((Domain.Entities.RegimeCibles.Pole, p), out r))
            return (r, Domain.Entities.RegimeCibles.Pole);
        return null;
    }

    /// <summary>Planning issu du régime pour un jour ; null si le jour n'est pas travaillé.</summary>
    public static PlanningJour? PlanningDuJour(RegimeEffectif regime, DateOnly jour)
    {
        var iso = jour.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)jour.DayOfWeek;
        // v44 : l'heure d'arrivée vient du planning publié, jamais du régime : le régime ne fixe que la
        // production et la pause. Sans planning publié, aucun retard d'arrivée n'est calculé.
        return regime.Jours.TryGetValue(iso, out var j)
            ? new PlanningJour(null, regime.PauseAutoriseeMinutes, false, false, regime.Nom, j.ProductionMinutes, PlanningSources.Regime)
            : null;
    }

    /// <summary>
    /// Planning d'un jour d'après le planning IT : « Travail » = arrivée saisie, production du régime
    /// (sinon règle de l'Excel), pause du régime (sinon pause par défaut) ; « Conge » = congé ;
    /// « Repos » = jour non travaillé (null) ; v67 « Recup » = récupération déduite des heures sup.
    /// Le planning IT prime sur le planning publié et le régime.
    /// </summary>
    public static PlanningJour? PlanningDuJourIt(string statut, TimeOnly? arrivee, RegimeEffectif? regime, DateOnly jour, int pauseParDefaut)
    {
        if (statut == Domain.Entities.PlanningItStatuts.Repos)
            return null;
        if (statut == Domain.Entities.PlanningItStatuts.Conge)
            return new PlanningJour(null, 0, true, false, "Planning IT", null, PlanningSources.PlanningIt);
        if (statut == Domain.Entities.PlanningItStatuts.Recup)
        {
            // v67 — Récupération : aucune production attendue ; la production qui aurait été prévue ce
            // jour-là (régime, sinon règle de l'Excel ; 07h40 un samedi) est déduite des heures sup.
            var duJour = regime is null ? null : PlanningDuJour(regime, jour)?.ProductionPrevueMinutes;
            var excel = ProductionExcelMinutes(jour);
            return new PlanningJour(null, regime?.PauseAutoriseeMinutes ?? pauseParDefaut, false, false, "Récup",
                0, PlanningSources.PlanningIt, Planifie: false, RecupMinutes: duJour ?? (excel == 0 ? 460 : excel));
        }
        var production = regime is null ? null : PlanningDuJour(regime, jour)?.ProductionPrevueMinutes;
        // v65 : « Travail » saisi un samedi est une journée prévue en production — la règle de l'Excel
        // renvoyant désormais 0 le week-end, on retient la production d'une journée courte (07h40).
        var parDefaut = ProductionExcelMinutes(jour);
        if (parDefaut == 0) parDefaut = 460;
        return new PlanningJour(
            arrivee,
            regime?.PauseAutoriseeMinutes ?? pauseParDefaut,
            false,
            false,
            "Planning IT",
            production ?? parDefaut,
            PlanningSources.PlanningIt);
    }

    /// <summary>
    /// Production de l'Excel « Combien il me reste » (cellule B3) quand aucun régime ne s'applique :
    /// 07:40 le vendredi, 08:40 du lundi au jeudi.
    /// <para>
    /// v65 : le samedi ne vaut plus 07:40 d'office. Un samedi n'est une journée de production que
    /// s'il figure au planning de l'agent (planning publié, planning IT ou régime horaire) ; dans ce
    /// cas la production vient de ce planning. Hors planning, il n'y a aucune production attendue :
    /// la règle de l'Excel renvoie 0 et les heures pointées sont des heures supplémentaires. Avant,
    /// un samedi travaillé hors planning créait jusqu'à 7 h 40 d'« heures manquantes ».
    /// </para>
    /// </summary>
    public static int ProductionExcelMinutes(DateOnly jour) => jour.DayOfWeek switch
    {
        DayOfWeek.Saturday or DayOfWeek.Sunday => 0,
        DayOfWeek.Friday => 460,
        _ => 520,
    };

    /// <summary>
    /// v65 — Le jour est-il un jour travaillé du régime horaire de l'agent ? Sert à savoir si un
    /// samedi (ou tout autre jour) est prévu en production pour cette personne précise, quel que soit
    /// son service ou son département — et non d'après un planning commun.
    /// </summary>
    public static bool JourTravailleDuRegime(RegimeEffectif? regime, DateOnly jour)
    {
        if (regime is null) return false;
        var iso = jour.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)jour.DayOfWeek;
        return regime.Jours.ContainsKey(iso);
    }

    /// <summary>Pause de l'Excel (cellule I4 « Pause 10min » = 01:20).</summary>
    public const int PauseExcelMinutes = 80;

    /// <summary>Contrôles d'un régime saisi ; retourne le message d'erreur ou null.</summary>
    public static string? Valider(RegimeHoraireRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Nom))
            return "Le nom du régime est obligatoire.";
        if (r.Nom.Trim().Length > 100)
            return "Le nom du régime ne doit pas dépasser 100 caractères.";
        if (r.PauseAutoriseeMinutes is < 1 or > 240)
            return "La pause autorisée doit être comprise entre 1 minute et 4 h.";
        var jours = r.Jours ?? [];
        if (jours.Count == 0)
            return "Indiquez au moins un jour travaillé.";
        if (jours.Select(j => j.JourSemaine).Distinct().Count() != jours.Count)
            return "Chaque jour de la semaine ne peut apparaître qu'une fois.";
        foreach (var j in jours)
        {
            if (j.JourSemaine is < 1 or > 7)
                return "Jour de la semaine invalide (1 = lundi … 7 = dimanche).";
            // v44 : l'arrivée vient du planning ; une valeur vide est acceptée.
            if (!string.IsNullOrWhiteSpace(j.Arrivee)
                && !TimeOnly.TryParse(j.Arrivee, System.Globalization.CultureInfo.InvariantCulture, out _))
                return $"Heure d'arrivée invalide : « {j.Arrivee} ».";
            if (j.ProductionMinutes is <= 0 or > 16 * 60)
                return "La production d'un jour doit être comprise entre 1 minute et 16 h.";
        }
        return null;
    }
}

/// <summary>Compte et son accès à « Heure de sortie ».</summary>
public sealed record HeureSortieAccesDto(int UserId, string Collaborateur, string? Email, string? Role, string? Cellule, bool Acces, string? ProductionBase = null);

/// <summary>v43 : horaire de base d'un compte (« 0840 » ou « 0740 »).</summary>
public sealed record HeureSortieBaseRequest(string Base);

/// <summary>
/// Accès « Heure de sortie » accordé ou retiré à une liste de comptes. v67 : à l'attribution, le Super
/// Admin choisit l'horaire de base (« 0840 » : 08h40 avec une journée à 07h40 par semaine, ou
/// « 0740 »). Sans choix : horaire du planning ou du régime (comportement d'origine).
/// </summary>
public sealed record HeureSortieAccesRequest(IReadOnlyList<int> UserIds, bool Acces, string? ProductionBase = null);
