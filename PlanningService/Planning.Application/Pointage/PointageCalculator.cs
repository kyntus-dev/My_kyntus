using System.Globalization;
using System.Text;

namespace Planning.Application.Pointage;

/// <summary>État d'une journée de pointage.</summary>
public static class PointageJourStatuts
{
    public const string AHeure = "AHeure";
    public const string Retard = "Retard";
    /// <summary>Nombre impair de pointages sur une journée passée : journée non calculée.</summary>
    public const string PointageManquant = "PointageManquant";
    /// <summary>Journée du jour, encore en cours.</summary>
    public const string EnCours = "EnCours";
    /// <summary>Planning publié, aucun pointage, pas de congé.</summary>
    public const string SansPointage = "SansPointage";
    /// <summary>Pointages sans planning publié ce jour : affichés, retard non calculé.</summary>
    public const string SansPlanning = "SansPlanning";
    public const string Conge = "Conge";
    public const string Ferie = "Ferie";
    /// <summary>Ni planning ni pointage.</summary>
    public const string Repos = "Repos";
    public const string AVenir = "AVenir";
}

/// <summary>
/// Planning d'une journée pour un collaborateur : planning publié, ou à défaut son régime horaire.
/// <paramref name="ProductionPrevueMinutes"/> : production attendue, pauses exclues (null si inconnue).
/// </summary>
public sealed record PlanningJour(
    TimeOnly? Debut,
    int PauseMinutes,
    bool Conge,
    bool Ferie,
    string? Libelle,
    int? ProductionPrevueMinutes = null,
    string? Source = null);

/// <summary>Origine du planning d'une journée.</summary>
public static class PlanningSources
{
    public const string Publie = "Planning";
    public const string Regime = "Regime";
    /// <summary>Horaire saisi dans le planning IT (référence prioritaire pour les membres IT).</summary>
    public const string PlanningIt = "PlanningIT";
}

public sealed record PointageJourResult(
    DateOnly Jour,
    string Statut,
    TimeOnly? Planning,
    int PauseAutoriseeMinutes,
    TimeOnly? Arrivee,
    TimeOnly? Depart,
    IReadOnlyList<TimeOnly> Pointages,
    int PauseMinutes,
    int RetardEntreeMinutes,
    int RetardPauseMinutes,
    int? ProductionMinutes = null,
    int? ProductionPrevueMinutes = null)
{
    public int RetardTotalMinutes => RetardEntreeMinutes + RetardPauseMinutes;

    /// <summary>Production réalisée moins production prévue (null si l'une manque).</summary>
    public int? EcartProductionMinutes =>
        ProductionMinutes is int p && ProductionPrevueMinutes is int prevue ? p - prevue : null;
}

public sealed record PointageTotaux(
    int RetardTotalMinutes,
    int JoursEnRetard,
    int JoursAHeure,
    int JoursPointes,
    IReadOnlyList<DateOnly> JoursPointageManquant,
    IReadOnlyList<DateOnly> JoursSansPointage,
    int ProductionMinutes = 0,
    int ProductionPrevueMinutes = 0,
    int JoursProductionCalculee = 0);

/// <summary>
/// Calcul d'une journée à partir des pointages bruts, comparés au planning publié.
///
/// Les pointeuses n'enregistrent pas l'état (entrée / sortie / pause) : il est déduit de l'ordre.
/// 1er pointage = arrivée, dernier = départ, paires intermédiaires = pauses (sortie puis retour).
/// Règles (identiques à HTEL) :
/// - arrivée dans les <c>tolérance</c> minutes qui suivent l'heure de planning : pas de retard ;
///   au-delà, tout le retard compte (9 h 06 pour 9 h = 6 minutes) ;
/// - pauses : même tolérance. Jusqu'à pause autorisée + tolérance (1 h 20 + 5 = 1 h 25) : pas de
///   retard ; au-delà, tout le dépassement compte (1 h 26 de pauses pour 1 h 20 = 6 minutes) ;
/// - nombre impair de pointages sur une journée passée = pointage manquant, journée non calculée ;
/// - production = départ − arrivée − pauses (08:00 → 17:00 avec 1 h de pause = 8 h).
/// </summary>
public static class PointageCalculator
{
    public static IReadOnlyList<DateTime> FusionnerDoublesBadgeages(IEnumerable<DateTime> pointages, int doubleBadgeSecondes)
    {
        var kept = new List<DateTime>();
        foreach (var p in pointages.OrderBy(x => x))
        {
            if (kept.Count > 0 && (p - kept[^1]).TotalSeconds < doubleBadgeSecondes)
                continue;
            kept.Add(p);
        }
        return kept;
    }

    public static PointageJourResult CalculerJour(
        DateOnly jour,
        IEnumerable<DateTime> pointagesBruts,
        PlanningJour? planning,
        DateOnly aujourdhui,
        int toleranceMinutes = 5,
        int doubleBadgeSecondes = 120,
        int pauseParDefautMinutes = 60)
    {
        var pointages = FusionnerDoublesBadgeages(pointagesBruts, doubleBadgeSecondes);
        var heures = pointages.Select(TimeOnly.FromDateTime).ToList();
        var debut = planning?.Debut;
        var pauseAutorisee = planning is null ? 0 : (planning.PauseMinutes > 0 ? planning.PauseMinutes : pauseParDefautMinutes);
        TimeOnly? arrivee = heures.Count > 0 ? heures[0] : null;
        TimeOnly? depart = heures.Count >= 2 ? heures[^1] : null;

        var prevue = planning is { Conge: false, Ferie: false } ? planning.ProductionPrevueMinutes : null;

        PointageJourResult Result(string statut, int pause = 0, int retardEntree = 0, int retardPause = 0, int? production = null) =>
            new(jour, statut, debut, pauseAutorisee, arrivee, depart, heures, pause, retardEntree, retardPause, production, prevue);

        if (planning?.Ferie == true)
            return Result(PointageJourStatuts.Ferie);
        if (planning?.Conge == true)
            return Result(PointageJourStatuts.Conge);

        if (heures.Count == 0)
        {
            if (jour > aujourdhui) return Result(debut is null ? PointageJourStatuts.Repos : PointageJourStatuts.AVenir);
            if (debut is null) return Result(PointageJourStatuts.Repos);
            return Result(jour == aujourdhui ? PointageJourStatuts.AVenir : PointageJourStatuts.SansPointage);
        }

        if (heures.Count % 2 == 1)
            return Result(jour >= aujourdhui ? PointageJourStatuts.EnCours : PointageJourStatuts.PointageManquant);

        var pause = 0.0;
        for (var i = 1; i + 1 < pointages.Count - 1; i += 2)
            pause += (pointages[i + 1] - pointages[i]).TotalMinutes;
        var pauseMinutes = (int)Math.Floor(pause);
        var production = (int)Math.Floor((pointages[^1] - pointages[0]).TotalMinutes - pause);

        if (debut is null)
            return Result(PointageJourStatuts.SansPlanning, pauseMinutes, production: production);

        var retardEntree = (int)Math.Floor((pointages[0] - jour.ToDateTime(debut.Value)).TotalMinutes);
        if (retardEntree <= toleranceMinutes)
            retardEntree = 0;
        var retardPause = RetardAvecTolerance(pauseMinutes - pauseAutorisee, toleranceMinutes);

        return Result(
            retardEntree + retardPause > 0 ? PointageJourStatuts.Retard : PointageJourStatuts.AHeure,
            pauseMinutes,
            retardEntree,
            retardPause,
            production);
    }

    /// <summary>Dépassement dans la tolérance : 0 ; au-delà : le dépassement entier.</summary>
    public static int RetardAvecTolerance(int depassementMinutes, int toleranceMinutes) =>
        depassementMinutes <= toleranceMinutes ? 0 : depassementMinutes;

    public static PointageTotaux Totaliser(IEnumerable<PointageJourResult> jours)
    {
        var list = jours.ToList();
        return new PointageTotaux(
            list.Sum(j => j.RetardTotalMinutes),
            list.Count(j => j.Statut == PointageJourStatuts.Retard),
            list.Count(j => j.Statut == PointageJourStatuts.AHeure),
            list.Count(j => j.Statut is PointageJourStatuts.Retard or PointageJourStatuts.AHeure),
            list.Where(j => j.Statut == PointageJourStatuts.PointageManquant).Select(j => j.Jour).ToList(),
            list.Where(j => j.Statut == PointageJourStatuts.SansPointage).Select(j => j.Jour).ToList(),
            list.Sum(j => j.ProductionMinutes ?? 0),
            list.Where(j => j.ProductionMinutes is not null).Sum(j => j.ProductionPrevueMinutes ?? 0),
            list.Count(j => j.ProductionMinutes is not null));
    }
}

/// <summary>Rapprochement d'un nom ZKBio Time avec un collaborateur (sans accents, ordre des mots libre).</summary>
public static class PointageNameMatcher
{
    public static string Normaliser(string? value)
    {
        var decomposed = (value ?? string.Empty).Replace(' ', ' ').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Clé insensible à l'ordre prénom / nom.</summary>
    public static string Cle(string? prenom, string? nom) =>
        string.Join(' ', Normaliser($"{prenom} {nom}").Split(' ', StringSplitOptions.RemoveEmptyEntries).OrderBy(t => t, StringComparer.Ordinal));
}
