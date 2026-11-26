using System.Globalization;
using Planning.Domain.Entities;

namespace Planning.Application.PlanningIt;

/// <summary>Case d'un jour. <see cref="Ferie"/> (v31) : nom du jour férié français, sinon null.</summary>
public sealed record PlanningItJourDto(DateOnly Jour, string? Statut, string? Arrivee, DateTime? UpdatedAt, string? ModifiePar, string? Ferie = null);

public sealed record PlanningItMembreDto(
    int UserId,
    string Collaborateur,
    string? Role,
    bool EstMoi,
    bool Modifiable,
    IReadOnlyList<PlanningItJourDto> Jours,
    string? Commentaire);

public sealed record PlanningItSemaineDto(
    DateOnly Du,
    DateOnly Au,
    bool PeutModifierTout,
    DateTime GenereLe,
    IReadOnlyList<PlanningItMembreDto> Membres);

/// <summary>Saisie d'une case : statut (Travail / Repos / Conge / Recup) et heure d'arrivée ; statut null = effacer.</summary>
public sealed record PlanningItCaseRequest(int UserId, DateOnly Jour, string? Statut, string? Arrivee);

public sealed record PlanningItCommentaireRequest(int UserId, DateOnly Semaine, string? Texte);

/// <summary>Droits du lecteur sur le planning IT (matrice « Accès par rôle »).</summary>
public sealed record PlanningItDroits(bool Voir, bool SaisirSoi, bool ModifierTout);

/// <summary>
/// v67 — Solde d'heures supplémentaires d'un membre IT sur l'année : acquises (journées travaillées
/// hors planning), récupérées (journées « Récup » du planning IT, y compris celles déjà planifiées) et
/// restantes (acquises − récupérées ; négatif = récupéré plus qu'acquis).
/// </summary>
public sealed record PlanningItSoldeDto(
    int UserId,
    int AcquisesMinutes,
    int RecupereesMinutes,
    int RestantesMinutes,
    int JoursRecup,
    int RecupPlanifieesMinutes,
    // v71 — Solde d'heures cumulé depuis DebutSoldeHeures, jusqu'à aujourd'hui : écarts de production
    // des journées calculées (heures sup. comprises) moins les Récup prises. Jamais remis à zéro le 24 :
    // il se garde jusqu'à sa récupération (Récup, ou sortie plus tôt un autre jour).
    int SoldePeriodeMinutes = 0,
    // Récup déjà planifiées après aujourd'hui (le solde les déduira le jour venu).
    int RecupPlanifieesPeriodeMinutes = 0,
    DateOnly? PeriodeDu = null);

public sealed record PlanningItSoldesDto(int Annee, DateOnly Du, DateOnly Au, IReadOnlyList<PlanningItSoldeDto> Soldes);

public interface IPlanningItService
{
    /// <summary>
    /// v67 — Soldes d'heures supplémentaires de l'année : la ligne du lecteur ; toutes les lignes avec
    /// « Modifier tout » (corriger toutes les lignes).
    /// </summary>
    Task<PlanningItSoldesDto> GetSoldesAsync(int? viewerUserId, PlanningItDroits droits, int annee, CancellationToken ct = default);

    Task<PlanningItSemaineDto> GetSemaineAsync(int? viewerUserId, PlanningItDroits droits, DateOnly jour, CancellationToken ct = default);
    Task SaisirAsync(int? viewerUserId, PlanningItDroits droits, PlanningItCaseRequest request, CancellationToken ct = default);
    Task CommenterAsync(int? viewerUserId, PlanningItDroits droits, PlanningItCommentaireRequest request, CancellationToken ct = default);
}

/// <summary>Règles du planning IT, sans base de données (testées unitairement).</summary>
public static class PlanningItRegles
{
    /// <summary>Lundi de la semaine d'un jour.</summary>
    public static DateOnly Lundi(DateOnly jour) => jour.AddDays(-(((int)jour.DayOfWeek + 6) % 7));

    /// <summary>
    /// Contrôle d'une saisie ; retourne le message d'erreur ou null.
    /// Un membre ne modifie que sa propre ligne ; « Modifier tout » (Admin) corrige toutes les lignes.
    /// </summary>
    public static string? Controler(int? viewerUserId, PlanningItDroits droits, PlanningItCaseRequest r)
    {
        if (!droits.ModifierTout && !(droits.SaisirSoi && viewerUserId == r.UserId))
            return "Vous ne pouvez modifier que vos propres horaires.";
        if (r.Statut is null)
            return null;
        if (!PlanningItStatuts.EstValide(r.Statut))
            return "État invalide (Travail, Repos, Conge ou Recup).";
        if (r.Statut == PlanningItStatuts.Travail && LireHeure(r.Arrivee) is null)
            return "Indiquez l'heure d'arrivée (hh:mm).";
        return null;
    }

    /// <summary>
    /// v67 — Solde d'un membre à partir de son suivi de pointage sur l'année : heures sup. acquises
    /// (journées pointées hors planning), récupérations (planning IT « Récup ») déduites ;
    /// <paramref name="aujourdhui"/> sépare les récupérations prises de celles déjà planifiées.
    /// </summary>
    public static PlanningItSoldeDto Solde(
        int userId,
        IEnumerable<Planning.Application.Pointage.PointageJourDto> jours,
        DateOnly aujourdhui,
        DateOnly? anneeDu = null,
        DateOnly? anneeAu = null)
    {
        var tous = jours.ToList();
        // Compteurs de l'année (inchangés) ; les jours lus en plus pour la période (fin décembre) n'y entrent pas.
        var liste = tous.Where(j => (anneeDu is null || j.Jour >= anneeDu) && (anneeAu is null || j.Jour <= anneeAu)).ToList();
        var acquises = liste.Sum(j => j.HeuresSupplementairesMinutes ?? 0);
        var recup = liste.Where(j => j.RecupMinutes is not null).ToList();
        var recuperees = recup.Sum(j => j.RecupMinutes!.Value);
        var planifiees = recup.Where(j => j.Jour > aujourdhui).Sum(j => j.RecupMinutes!.Value);

        // v71 — Solde d'heures cumulé (même règle que « Mon suivi » et « Heure de sortie ») : depuis
        // DebutSoldeHeures, sans remise à zéro le 24.
        var du = DebutSoldeHeures;
        var periode = tous.Where(j => j.Jour >= du && j.Jour <= aujourdhui).ToList();
        var soldePeriode = periode.Sum(j => j.EcartProductionMinutes ?? 0) - periode.Sum(j => j.RecupMinutes ?? 0);
        var planifieesPeriode = tous.Where(j => j.Jour > aujourdhui).Sum(j => j.RecupMinutes ?? 0);
        return new PlanningItSoldeDto(userId, acquises, recuperees, acquises - recuperees, recup.Count, planifiees,
            soldePeriode, planifieesPeriode, du);
    }

    /// <summary>
    /// v71 — Début du solde d'heures : il se cumule depuis cette date et n'est jamais remis à zéro le 24.
    /// Même date côté écran : DEBUT_SOLDE_HEURES (heure-sortie.util.ts).
    /// </summary>
    public static readonly DateOnly DebutSoldeHeures = new(2026, 10, 24);

    /// <summary>v70 — Début de la période de paie (le 24) qui contient <paramref name="jour"/>.</summary>
    public static DateOnly DebutPeriodePaie(DateOnly jour) =>
        jour.Day >= 24 ? new DateOnly(jour.Year, jour.Month, 24) : new DateOnly(jour.Year, jour.Month, 24).AddMonths(-1);

    public static TimeOnly? LireHeure(string? value) =>
        TimeOnly.TryParseExact((value ?? string.Empty).Trim(), ["H:mm", "HH:mm", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t
            : null;
}
