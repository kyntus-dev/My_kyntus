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
    /// <summary>v64 : avant la date d'entrée réelle ou après la date de sortie effective (case grisée).</summary>
    public const string HorsContrat = "HorsContrat";

    /// <summary>
    /// v65 : journée travaillée et pointée alors qu'aucune production n'était prévue au planning de
    /// l'agent (samedi non planifié, renfort…) : les heures réalisées sont des heures supplémentaires,
    /// jamais des heures manquantes.
    /// </summary>
    public const string HeuresSupplementaires = "HeuresSupplementaires";

    /// <summary>
    /// v67 : journée de récupération (planning IT « Récup ») non pointée : la production prévue du jour
    /// est déduite du solde d'heures supplémentaires ; jamais une absence ni des heures manquantes.
    /// </summary>
    public const string Recup = "Recup";
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
    string? Source = null,
    // v65 — Production réellement prévue ce jour-là dans le planning de l'agent (planning publié,
    // planning IT « Travail » ou jour travaillé de son régime horaire). Faux : aucune production
    // n'était prévue — les heures pointées sont des heures supplémentaires et il ne peut pas y avoir
    // d'heures manquantes. Vrai par défaut : comportement inchangé pour les journées planifiées.
    bool Planifie = true,
    // v67 — Journée de récupération (planning IT « Récup ») : minutes déduites du solde d'heures
    // supplémentaires (production prévue du jour). null : pas une récupération.
    int? RecupMinutes = null);

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
    int? ProductionPrevueMinutes = null,
    // v65 — Journée prévue au planning de l'agent (voir PlanningJour.Planifie).
    bool Planifie = true,
    // v65 — Heures réalisées un jour sans production prévue (samedi hors planning, renfort…).
    int? HeuresSupplementairesMinutes = null,
    // v67 — Récupération prise ce jour : minutes déduites du solde d'heures supplémentaires.
    int? RecupMinutes = null,
    // v70 — Journée en manque de pointage : heures des seuls segments pointés (arrivée → sortie,
    // retour → sortie…). Affichées, jamais comptées dans la production, l'écart ni le solde.
    int? ProductionPointeeMinutes = null)
{
    public int RetardTotalMinutes => RetardEntreeMinutes + RetardPauseMinutes;

    /// <summary>
    /// Production réalisée moins production prévue (null si l'une manque). v65 : un jour non planifié
    /// a une production prévue de 0 — l'écart est donc exactement les heures supplémentaires, et jamais
    /// un manque.
    /// </summary>
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
    int JoursProductionCalculee = 0,
    // v65 — Heures supplémentaires de la période (jours travaillés sans production prévue) et
    // nombre de ces journées. Comptées à part : elles ne gonflent pas la production prévue.
    int HeuresSupplementairesMinutes = 0,
    int JoursHeuresSupplementaires = 0,
    // v67 — Récupérations prises sur la période (minutes déduites du solde d'heures sup.) et journées.
    int RecupMinutes = 0,
    int JoursRecup = 0);

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
/// - v70 : départ pointé avant l'heure prévue d'arrivée (ex. 08:24 → 08:39 pour 09:30) = pointage
///   manquant aussi : la journée ne peut pas être finie avant d'avoir commencé ;
/// - v70 : journée en pointage manquant = seules les heures pointées sont affichées
///   (<see cref="PointageJourResult.ProductionPointeeMinutes"/>) ; elles ne comptent ni dans la
///   production, ni dans l'écart, ni dans le solde depuis le 24 ;
/// - production = départ − arrivée − pauses (08:00 → 17:00 avec 1 h de pause = 8 h).
/// </summary>
public static class PointageCalculator
{
    /// <summary>
    /// Fusion sans pointeuse connue. v65 : mêmes garde-fous que la surcharge avec pointeuse (délai
    /// plafonné, parité conservée) — un retour de pause courte n'est plus supprimé.
    /// </summary>
    public static IReadOnlyList<DateTime> FusionnerDoublesBadgeages(IEnumerable<DateTime> pointages, int doubleBadgeSecondes) =>
        FusionnerDoublesBadgeages(pointages.Select(p => (Heure: p, Pointeuse: (string?)null)), doubleBadgeSecondes);

    /// <summary>
    /// v65 — Plafond du délai de double badgeage : au-delà, deux badgeages successifs sont deux
    /// passages réels (sortie puis retour de pause) et jamais un double passage du doigt. Protège les
    /// installations qui gardent l'ancien réglage de 120 s.
    /// </summary>
    public const int PlafondDoubleBadgeSecondes = 45;

    /// <summary>
    /// v33 : double passage du doigt = même pointeuse dans le délai. Deux pointeuses différentes
    /// (ex. 6e étage puis 5e étage, 70 s plus tard) sont deux passages : les deux sont gardés.
    /// Pointeuse inconnue : même règle qu'avant (délai seul).
    /// <para>
    /// v65 : deux garde-fous, pour qu'aucun pointage valide ne soit perdu.
    /// 1. Le délai est plafonné à <paramref name="pauseMinimaleSecondes"/> (45 s par défaut) : une
    ///    pause courte — une minute — n'est plus prise pour un double passage, son pointage de retour
    ///    est conservé et la pause est comptée.
    /// 2. La fusion ne rend jamais le nombre de pointages impair : si elle le faisait, les badgeages
    ///    écartés sont rendus (le plus espacé d'abord) jusqu'à retrouver un nombre pair. Une journée
    ///    complète n'est donc plus classée « pointage manquant » à cause de la fusion, et sa production
    ///    reste calculée.
    /// </para>
    /// </summary>
    public static IReadOnlyList<DateTime> FusionnerDoublesBadgeages(
        IEnumerable<(DateTime Heure, string? Pointeuse)> pointages,
        int doubleBadgeSecondes,
        int pauseMinimaleSecondes = PlafondDoubleBadgeSecondes)
    {
        var tous = pointages.OrderBy(x => x.Heure).ToList();
        // Plafond : un double passage du doigt tient en quelques secondes ; au-delà, ce sont deux passages.
        var plafond = pauseMinimaleSecondes > 0 ? pauseMinimaleSecondes : PlafondDoubleBadgeSecondes;
        var delai = Math.Min(doubleBadgeSecondes, plafond);

        var kept = new List<(DateTime Heure, string? Pointeuse)>();
        // Badgeages écartés, avec leur écart au précédent conservé : rendus si la parité l'exige.
        var ecartes = new List<(double Ecart, (DateTime Heure, string? Pointeuse) P)>();
        foreach (var p in tous)
        {
            if (kept.Count > 0 && delai > 0)
            {
                var dernier = kept[^1];
                var memePointeuse = string.IsNullOrWhiteSpace(p.Pointeuse)
                                    || string.IsNullOrWhiteSpace(dernier.Pointeuse)
                                    || string.Equals(p.Pointeuse.Trim(), dernier.Pointeuse.Trim(), StringComparison.OrdinalIgnoreCase);
                var ecart = (p.Heure - dernier.Heure).TotalSeconds;
                if (memePointeuse && ecart < delai)
                {
                    ecartes.Add((ecart, p));
                    continue;
                }
            }
            kept.Add(p);
        }

        // Garde-fou de parité : une fusion ne doit pas rendre la journée incalculable.
        if (kept.Count % 2 == 1 && ecartes.Count > 0)
        {
            foreach (var rendu in ecartes.OrderByDescending(e => e.Ecart))
            {
                kept.Add(rendu.P);
                if (kept.Count % 2 == 0)
                    break;
            }
            kept.Sort((a, b) => a.Heure.CompareTo(b.Heure));
        }

        return kept.Select(k => k.Heure).ToList();
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

        // v65 — Journée dont on sait qu'elle n'était pas prévue au planning de l'agent (samedi hors
        // planning, jour de repos travaillé). Un planning absent (non encore publié) n'entre pas dans
        // ce cas : son traitement reste celui d'avant.
        var horsPlanning = planning is { Conge: false, Ferie: false, Planifie: false };
        var planifie = !horsPlanning;
        var prevue = planning is { Conge: false, Ferie: false } ? planning.ProductionPrevueMinutes : null;
        // Jour hors planning : aucune production attendue. La production prévue vaut 0 (et non
        // l'horaire de base ou la règle du vendredi / samedi de l'Excel) : il ne peut donc pas y avoir
        // d'heures manquantes, et les heures réalisées partent en heures supplémentaires.
        if (horsPlanning)
            prevue = 0;

        PointageJourResult Result(
            string statut,
            int pause = 0,
            int retardEntree = 0,
            int retardPause = 0,
            int? production = null,
            int? productionPointee = null) =>
            new(jour, statut, debut, pauseAutorisee, arrivee, depart, heures, pause, retardEntree, retardPause,
                production, prevue, planifie,
                // Heures supplémentaires : seulement un jour non planifié et effectivement pointé.
                // Un samedi déjà prévu en production n'en génère aucune (pas de double comptage).
                !planifie && production is int p && p > 0 ? (int?)p : null,
                // v67 : récupération déduite (jamais un jour férié ou de congé).
                planning is { Conge: false, Ferie: false } ? planning.RecupMinutes : null,
                productionPointee);

        if (planning?.Ferie == true)
            return Result(PointageJourStatuts.Ferie);
        if (planning?.Conge == true)
            return Result(PointageJourStatuts.Conge);
        // v67 — Récupération non pointée : ni absence, ni heures manquantes ; déduite du solde.
        // Pointée malgré tout : les heures réalisées partent en heures supplémentaires (jour hors planning).
        if (planning?.RecupMinutes is not null && heures.Count == 0)
            return Result(PointageJourStatuts.Recup);

        // v44 : un jour travaillé du régime n'a pas d'heure d'arrivée (elle vient du planning publié),
        // mais reste un jour travaillé : sans pointage, c'est une absence ; avec, la pause est contrôlée.
        // v65 : un jour non planifié n'est pas un jour travaillé — sans pointage il reste « Repos »
        // (jamais « SansPointage », qui signalerait une absence).
        var jourTravaille = !horsPlanning
                            && (debut is not null
                                || planning is { Source: PlanningSources.Regime }
                                || planning?.ProductionPrevueMinutes is not null);
        if (heures.Count == 0)
        {
            if (jour > aujourdhui) return Result(jourTravaille ? PointageJourStatuts.AVenir : PointageJourStatuts.Repos);
            if (!jourTravaille) return Result(PointageJourStatuts.Repos);
            return Result(jour == aujourdhui ? PointageJourStatuts.AVenir : PointageJourStatuts.SansPointage);
        }

        if (heures.Count % 2 == 1)
            return Result(jour >= aujourdhui ? PointageJourStatuts.EnCours : PointageJourStatuts.PointageManquant,
                productionPointee: jour >= aujourdhui ? null : ProductionDesSegments(pointages));

        // v70 — Départ pointé avant l'heure prévue d'arrivée : il manque des pointages (la journée ne
        // peut pas être finie avant d'avoir commencé). Même traitement qu'un nombre impair.
        if (!horsPlanning && debut is not null && pointages[^1] < jour.ToDateTime(debut.Value))
            return Result(jour >= aujourdhui ? PointageJourStatuts.EnCours : PointageJourStatuts.PointageManquant,
                productionPointee: jour >= aujourdhui ? null : ProductionDesSegments(pointages));

        var pause = 0.0;
        for (var i = 1; i + 1 < pointages.Count - 1; i += 2)
            pause += (pointages[i + 1] - pointages[i]).TotalMinutes;
        var pauseMinutes = (int)Math.Floor(pause);
        var production = (int)Math.Floor((pointages[^1] - pointages[0]).TotalMinutes - pause);

        // v65 — Journée travaillée et pointée alors qu'aucune production n'était prévue au planning
        // de l'agent (samedi hors planning, renfort, jour de repos travaillé) : heures supplémentaires.
        // Aucun retard n'est calculé (il n'y a pas d'heure d'arrivée attendue) et la production prévue
        // reste à 0, donc la journée ne crée jamais d'heures manquantes.
        if (horsPlanning)
            return Result(PointageJourStatuts.HeuresSupplementaires, pauseMinutes, production: production);

        if (debut is null)
        {
            if (!jourTravaille)
                return Result(PointageJourStatuts.SansPlanning, pauseMinutes, production: production);
            // Sans heure d'arrivée : pas de retard d'arrivée, mais le dépassement de pause compte.
            var retardPauseSeul = RetardAvecTolerance(pauseMinutes - pauseAutorisee, toleranceMinutes);
            return Result(
                retardPauseSeul > 0 ? PointageJourStatuts.Retard : PointageJourStatuts.SansPlanning,
                pauseMinutes,
                0,
                retardPauseSeul,
                production);
        }

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

    /// <summary>
    /// v70 — Heures des segments complets dans l'ordre des pointages : 1er → 2e, 3e → 4e… ; un dernier
    /// pointage sans partenaire n'ajoute rien. « On ne calcule que ce qui est pointé. »
    /// </summary>
    public static int ProductionDesSegments(IReadOnlyList<DateTime> pointages)
    {
        var total = 0.0;
        for (var i = 0; i + 1 < pointages.Count; i += 2)
            total += (pointages[i + 1] - pointages[i]).TotalMinutes;
        return (int)Math.Floor(Math.Max(0, total));
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
            // v65 : la production prévue ne compte que les journées planifiées — un samedi hors
            // planning vaut 0 et n'ajoute donc aucune heure attendue.
            list.Where(j => j.ProductionMinutes is not null).Sum(j => j.ProductionPrevueMinutes ?? 0),
            list.Count(j => j.ProductionMinutes is not null),
            // v65 : heures supplémentaires comptées à part (jamais ajoutées à la production prévue,
            // donc jamais comptées deux fois).
            list.Sum(j => j.HeuresSupplementairesMinutes ?? 0),
            list.Count(j => j.HeuresSupplementairesMinutes is not null),
            list.Sum(j => j.RecupMinutes ?? 0),
            list.Count(j => j.RecupMinutes is not null));
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
