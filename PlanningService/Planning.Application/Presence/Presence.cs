using Planning.Application.Pointage;
using Planning.Domain.Enums;

namespace Planning.Application.Presence;

/// <summary>Code de présence (liste du service paie).</summary>
/// <param name="Deduit">Jour retiré des jours travaillés (colonnes « … (déduit) » de l'export paie).</param>
public sealed record PresenceCodeDto(string Code, string Libelle, string Famille, bool Deduit);

public static class PresenceCodes
{
    public const string Present = "P";
    public const string Formation = "FOR";
    public const string AbsenceNonAutorisee = "ABS N";
    public const string Ferie = "F";

    /// <summary>Liste du service paie, dans l'ordre de l'interface actuelle.</summary>
    public static readonly IReadOnlyList<PresenceCodeDto> Tous =
    [
        new("P", "Présent", "presence", false),
        new("FOR", "Formation", "formation", false),
        new("ABS", "Absence autorisée", "absence", false),
        new("ABS N", "Absence non autorisée", "absence", true),
        new("AM", "Arrêt maladie", "absence", true),
        new("AR", "Ne travaille plus", "absence", true),
        new("AT", "Accident travail", "absence", false),
        new("CP", "Congés payés", "conge", false),
        new("CSS", "Congés sans solde", "conge", true),
        new("DF", "Deuil familial", "conge", false),
        new("ENF", "Enfant malade", "absence", false),
        new("F", "Férié", "conge", false),
        new("MAT", "Maternité", "conge", true),
        new("PAT", "Paternité", "conge", false),
        new("REC", "Récup", "conge", false),
    ];

    public static bool EstValide(string? code) => code is not null && Tous.Any(c => c.Code == code);

    /// <summary>
    /// v62 : motif saisi qui retire l'agent du planning ce jour-là (arrêt maladie, congé, absence…).
    /// Présent, formation et férié ne retirent rien (le férié est déjà géré par le planning).
    /// </summary>
    public static bool RetireDuPlanning(string? code) =>
        code is not null && VersAbsenceType(code) is not null;

    /// <summary>v62 : type d'absence du planning correspondant à un motif de présence (null : pas une absence).</summary>
    public static AbsenceType? VersAbsenceType(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "ABS" => AbsenceType.AbsenceAutorisee,
        "ABS N" => AbsenceType.AbsenceNonAutorisee,
        "AM" => AbsenceType.ArretMaladie,
        "AR" => AbsenceType.NeTravaillePlus,
        "AT" => AbsenceType.AccidentDeTravail,
        "CP" => AbsenceType.CongesPayes,
        "CSS" => AbsenceType.CongesSansSolde,
        "DF" => AbsenceType.DeuilFamilial,
        "ENF" => AbsenceType.EnfantMalade,
        "MAT" => AbsenceType.Maternite,
        "PAT" => AbsenceType.Paternite,
        "REC" => AbsenceType.Recup,
        _ => null,
    };

    /// <summary>Code d'un congé validé, d'après son type.</summary>
    public static string DepuisConge(AbsenceType type) => type switch
    {
        AbsenceType.AbsenceAutorisee => "ABS",
        AbsenceType.AbsenceNonAutorisee => "ABS N",
        AbsenceType.ArretMaladie => "AM",
        AbsenceType.NeTravaillePlus => "AR",
        AbsenceType.AccidentDeTravail => "AT",
        AbsenceType.CongesPayes => "CP",
        AbsenceType.CongesSansSolde => "CSS",
        AbsenceType.DeuilFamilial => "DF",
        AbsenceType.EnfantMalade => "ENF",
        AbsenceType.Maternite => "MAT",
        AbsenceType.Paternite => "PAT",
        AbsenceType.Recup => "REC",
        _ => "ABS",
    };
}

/// <summary>Règles du tableau des présences (fonctions pures, testées).</summary>
public static class PresenceRegles
{
    /// <summary>
    /// Code calculé d'une journée, sans saisie du responsable :
    /// congé validé → son code ; férié → F ; en formation → FOR ; pointée → P ;
    /// journée planifiée passée sans pointage → ABS N (à requalifier si besoin) ; sinon vide.
    /// </summary>
    public static string? CodeAuto(PointageJourDto jour, string? codeConge, bool enFormation)
    {
        // v64 : hors contrat (avant l'entrée, après la sortie) : ni présence ni absence calculée.
        if (jour.Statut == PointageJourStatuts.HorsContrat) return null;
        if (codeConge is not null) return codeConge;
        if (jour.Statut == PointageJourStatuts.Ferie) return "F";
        if (jour.Statut == PointageJourStatuts.Conge) return "CP";
        if (enFormation && (jour.Pointages.Count > 0 || jour.Planning is not null)) return PresenceCodes.Formation;
        if (jour.Pointages.Count > 0) return PresenceCodes.Present;
        if (jour.Statut == PointageJourStatuts.SansPointage) return PresenceCodes.AbsenceNonAutorisee;
        return null;
    }

    /// <summary>Retard retenu : arrivée et pauses, hors parts autorisées ; seulement un jour de présence.</summary>
    public static int RetardRetenu(string? code, int retardEntree, int retardPause, bool entreeAutorisee, bool pauseAutorisee) =>
        code == PresenceCodes.Present
            ? (entreeAutorisee ? 0 : retardEntree) + (pauseAutorisee ? 0 : retardPause)
            : 0;

    /// <summary>Retard autorisé (information) : parts couvertes par une autorisation.</summary>
    public static int RetardAutorise(string? code, int retardEntree, int retardPause, bool entreeAutorisee, bool pauseAutorisee) =>
        code == PresenceCodes.Present
            ? (entreeAutorisee ? retardEntree : 0) + (pauseAutorisee ? retardPause : 0)
            : 0;

    /// <summary>Bloc des heures supplémentaires : seules les tranches complètes de 30 minutes comptent.</summary>
    public const int BlocHeuresSupMinutes = 30;

    /// <summary>Limite entre heures supplémentaires de jour et de nuit.</summary>
    public static readonly TimeOnly LimiteHeuresSupNuit = new(21, 0);

    /// <summary>
    /// v61 : heures supplémentaires d'une journée, d'après les pointages et le planning.
    /// Jour ouvré : sortie après la fin prévue (début du planning + production prévue + pause autorisée),
    /// partagée entre avant 21h et après 21h. Dimanche ou férié : tout le temps travaillé.
    /// Chaque part est arrondie à la tranche de 30 minutes inférieure. Rien sans journée complète
    /// (nombre pair de pointages), ni un jour d'absence ou de congé.
    /// </summary>
    public static HeuresSupJour HeuresSupplementaires(
        DateOnly jour,
        string statut,
        string? code,
        string? planning,
        int pauseAutoriseeMinutes,
        int? productionPrevueMinutes,
        IReadOnlyList<string> pointages)
    {
        if (code is not (null or PresenceCodes.Present or PresenceCodes.Formation or "F"))
            return HeuresSupJour.Aucune;
        var heures = new List<TimeOnly>(pointages.Count);
        foreach (var p in pointages)
            if (TimeOnly.TryParse(p, System.Globalization.CultureInfo.InvariantCulture, out var t))
                heures.Add(t);
        heures.Sort();
        if (heures.Count < 2 || heures.Count % 2 == 1
            || statut is PointageJourStatuts.PointageManquant or PointageJourStatuts.EnCours)
            return HeuresSupJour.Aucune;

        var dimancheOuFerie = jour.DayOfWeek == DayOfWeek.Sunday
                              || statut == PointageJourStatuts.Ferie
                              || JoursFeries.Nom(jour) is not null;
        if (dimancheOuFerie)
        {
            var travaille = 0.0;
            for (var i = 0; i + 1 < heures.Count; i += 2)
                travaille += (heures[i + 1] - heures[i]).TotalMinutes;
            return new HeuresSupJour(null, 0, 0, Bloc(travaille));
        }

        if (productionPrevueMinutes is not int prevue
            || !TimeOnly.TryParse(planning, System.Globalization.CultureInfo.InvariantCulture, out var debut))
            return HeuresSupJour.Aucune;
        var finMinutes = debut.Hour * 60 + debut.Minute + prevue + Math.Max(0, pauseAutoriseeMinutes);
        if (finMinutes >= 24 * 60)
            return HeuresSupJour.Aucune;
        // Calcul en minutes depuis minuit (la soustraction de TimeOnly reboucle sur 24 h).
        var departMinutes = heures[^1].Hour * 60 + heures[^1].Minute;
        var limiteMinutes = LimiteHeuresSupNuit.Hour * 60 + LimiteHeuresSupNuit.Minute;
        var avant = Math.Max(0, Math.Min(departMinutes, limiteMinutes) - finMinutes);
        var apres = Math.Max(0, departMinutes - Math.Max(finMinutes, limiteMinutes));
        var fin = new TimeOnly(finMinutes / 60, finMinutes % 60);
        return new HeuresSupJour(fin.ToString("HH:mm"), Bloc(avant), Bloc(apres), 0);
    }

    private static int Bloc(double minutes) =>
        (int)Math.Floor(minutes / BlocHeuresSupMinutes) * BlocHeuresSupMinutes;
}

public sealed record PresenceJourDto(
    DateOnly Jour,
    string? Code,
    bool Auto, // true : code calculé (pointage, congé) ; false : saisi par un responsable
    string? CodeAuto,
    string Statut,
    string? Planning,
    string? Arrivee,
    string? Depart,
    IReadOnlyList<string> Pointages,
    int RetardEntreeMinutes,
    int RetardPauseMinutes,
    bool RetardAutorise,
    bool RetardPauseAutorise,
    string? MotifAutorisation,
    int RetardRetenuMinutes,
    int RetardAutoriseMinutes,
    bool ManquePointage,
    string? Commentaire,
    string? ModifiePar,
    DateTime? ModifieLe,
    bool AbsencePartielle = false,
    // v61 : heures supplémentaires détectées au pointage (au-delà de la fin prévue, par blocs de 30 min).
    string? FinPrevue = null,
    int HsAvant21Minutes = 0,
    int HsApres21Minutes = 0,
    int HsDimancheFerieMinutes = 0);

/// <summary>v61 : heures supplémentaires d'une journée, en minutes (multiples de 30).</summary>
public sealed record HeuresSupJour(string? FinPrevue, int Avant21Minutes, int Apres21Minutes, int DimancheFerieMinutes)
{
    public static readonly HeuresSupJour Aucune = new(null, 0, 0, 0);
}

public sealed record PresenceTotauxDto(
    int Presents,
    int Formation,
    IReadOnlyDictionary<string, int> Absences,
    int RetardRetenuMinutes,
    int RetardAutoriseMinutes,
    int JoursEnRetard,
    int ManquesPointage,
    int AbsencesNonQualifiees);

public sealed record PresenceAgentDto(
    int UserId,
    string Nom,
    string Type,
    string? Pole,
    string? PoleId,
    string? Cellule,
    string? CelluleId,
    string? Service,
    DateOnly? DateEntree,
    DateOnly? DateSortie,
    IReadOnlyList<PresenceJourDto> Jours,
    PresenceTotauxDto Totaux,
    DateOnly? FormationDu = null,
    DateOnly? FormationAu = null,
    string? Pilote = null, // « NOM Prénom » (colonne Pilote de l'export paie)
    // v30 : case « En formation » cochée (agent hors paie tant qu'il n'est pas en production).
    // FormationAu = dernier jour de formation une fois terminée (null tant qu'elle continue).
    bool EnFormation = false,
    // v46 : rôle du compte (les superviseurs et référents techniques du périmètre d'un chef de projet).
    string? Role = null,
    // v48 : « contractuel » / « non-contractuel » (type de formation choisi sur la fiche).
    string? TypeFormation = null,
    // v61 : superviseur / référent technique : pôles (ids et noms) et cellules des agents visibles qui le
    // désignent, plus son propre placement. Sert aux listes filtrées par activité, pôle et cellule.
    IReadOnlyList<string>? PoleIdsEncadres = null,
    IReadOnlyList<string>? PolesEncadres = null,
    IReadOnlyList<string>? CellulesEncadrees = null,
    // v63.3 : aucun identifiant de pointeuse relié à ce compte (ses journées restent vides tant
    // qu'aucun motif n'est saisi) — liaison à faire dans Utilisateurs › Liaisons HTEL.
    bool PointeuseReliee = true);

public sealed record PresenceGrilleDto(
    string Perimetre,
    DateOnly Du,
    DateOnly Au,
    int ToleranceMinutes,
    DateTime? DerniereSynchro,
    bool PeutModifier,
    IReadOnlyList<PresenceCodeDto> Codes,
    IReadOnlyList<PresenceAgentDto> Agents,
    // v46 : superviseurs et référents techniques ne peuvent pas autoriser un retard ni un dépassement de pause.
    bool PeutAutoriserRetard = true,
    // v61 : rubrique « Présences des superviseurs et référents » : listes dédiées (Consulter) et saisie (Agir).
    bool EncadrementDedie = false,
    bool EncadrementModifiable = false,
    // v61 : false = périmètre limité : les filtres ne proposent que les contrats, pôles et cellules de ce périmètre.
    bool ToutesActivites = false,
    // v62 : listes « Superviseurs » et « Référents techniques » proposées (RH, Admin et portée globale
    // compris), selon « Présences des superviseurs et référents › Consulter ».
    bool FiltreEncadrement = false);

/// <summary>Saisie sur une journée (ou une période si <see cref="Au"/> est renseigné).</summary>
public sealed record PresenceSaisieRequest(
    int UserId,
    DateOnly Jour,
    DateOnly? Au,
    string? Code, // code imposé ; vide = revenir au calcul du pointage
    bool RetardAutorise,
    bool RetardPauseAutorise,
    string? MotifAutorisation,
    string? Commentaire,
    bool AbsencePartielle = false);

/// <param name="EncadrementVoir">v61 : droit « Présences des superviseurs et référents › Consulter » (null : règle v46, chef de projet).</param>
/// <param name="EncadrementAgir">v61 : droit « … › Modifier » (null : règle v46, chef de projet).</param>
public sealed record PresenceViewer(
    int? AuthUserId, Guid? SubjectId, string? Email, string? Role, bool Global,
    bool? EncadrementVoir = null, bool? EncadrementAgir = null);

/// <summary>Demandeur résolu : son compte Planning et les agents qu'il peut traiter (null = tous).</summary>
public sealed record PresenceAuteur(int? UserId, string? Nom, IReadOnlySet<int>? Agents)
{
    public bool PeutTraiter(int userId) => Agents is null || Agents.Contains(userId);
}

public interface IPresenceService
{
    /// <summary>Tableau des présences du périmètre du responsable (tous pour RH / Admin).</summary>
    Task<PresenceGrilleDto?> GetGrilleAsync(PresenceViewer viewer, DateOnly du, DateOnly au, bool peutModifier, CancellationToken ct = default);

    /// <summary>Enregistre une saisie ; renvoie la ligne de l'agent recalculée sur la période affichée.</summary>
    Task<PresenceAgentDto> SaisirAsync(PresenceViewer viewer, PresenceSaisieRequest request, DateOnly grilleDu, DateOnly grilleAu, CancellationToken ct = default);

    /// <summary>Compte du demandeur et agents de son périmètre (null = toute l'entreprise).</summary>
    Task<PresenceAuteur> ResoudreAsync(PresenceViewer viewer, CancellationToken ct = default);
}

/// <summary>Refus métier (hors périmètre, code inconnu…) : 403 / 400 avec ce message.</summary>
public sealed class PresenceRefusException(string message, bool horsPerimetre = false) : Exception(message)
{
    public bool HorsPerimetre { get; } = horsPerimetre;
}

/// <summary>
/// v58 — État de validation du tableau de paie d'une période : qui a validé et quand.
/// Visible par tous ceux qui consultent le tableau.
/// </summary>
public sealed record PaieValidationDto(
    DateOnly Du,
    DateOnly Au,
    bool Validee,
    string? ValideParNom,
    DateTime? ValideLe,
    string? Commentaire);
