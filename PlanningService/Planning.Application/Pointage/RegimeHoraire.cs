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
    DateTime? DerniereSynchro);

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
        return regime.Jours.TryGetValue(iso, out var j)
            ? new PlanningJour(j.Arrivee, regime.PauseAutoriseeMinutes, false, false, regime.Nom, j.ProductionMinutes, PlanningSources.Regime)
            : null;
    }

    /// <summary>
    /// Production de l'Excel « Combien il me reste » (cellule B3) quand aucun régime ne s'applique :
    /// 07:40 le vendredi et le samedi, 08:40 les autres jours.
    /// </summary>
    public static int ProductionExcelMinutes(DateOnly jour) =>
        jour.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday ? 460 : 520;

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
            if (!TimeOnly.TryParse(j.Arrivee, System.Globalization.CultureInfo.InvariantCulture, out _))
                return $"Heure d'arrivée invalide : « {j.Arrivee} ».";
            if (j.ProductionMinutes is <= 0 or > 16 * 60)
                return "La production d'un jour doit être comprise entre 1 minute et 16 h.";
        }
        return null;
    }
}
