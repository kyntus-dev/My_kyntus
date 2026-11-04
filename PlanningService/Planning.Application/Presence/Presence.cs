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
    bool AbsencePartielle = false);

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
    string? Pilote = null); // « NOM Prénom » (colonne Pilote de l'export paie)

public sealed record PresenceGrilleDto(
    string Perimetre,
    DateOnly Du,
    DateOnly Au,
    int ToleranceMinutes,
    DateTime? DerniereSynchro,
    bool PeutModifier,
    IReadOnlyList<PresenceCodeDto> Codes,
    IReadOnlyList<PresenceAgentDto> Agents);

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

public sealed record PresenceViewer(int? AuthUserId, Guid? SubjectId, string? Email, string? Role, bool Global);

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
