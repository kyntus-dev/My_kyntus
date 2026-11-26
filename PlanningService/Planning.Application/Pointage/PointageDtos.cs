namespace Planning.Application.Pointage;

public sealed record PointageJourDto(
    DateOnly Jour,
    string Statut,
    string? Planning,
    string? Libelle,
    int PauseAutoriseeMinutes,
    string? Arrivee,
    string? Depart,
    IReadOnlyList<string> Pointages,
    int PauseMinutes,
    int RetardEntreeMinutes,
    int RetardPauseMinutes,
    int RetardTotalMinutes,
    int? ProductionMinutes = null,
    int? ProductionPrevueMinutes = null,
    int? EcartProductionMinutes = null,
    string? Source = null,
    // v65 — Journée prévue au planning de l'agent (planning publié, planning IT « Travail », ou jour
    // travaillé de son régime horaire). Faux : samedi ou jour de repos travaillé hors planning.
    bool Planifie = true,
    // v65 — Heures réalisées une journée non planifiée : heures supplémentaires.
    int? HeuresSupplementairesMinutes = null,
    // v67 — Récupération (planning IT « Récup ») : minutes déduites du solde d'heures sup.
    int? RecupMinutes = null,
    // v70 — Manque de pointage : heures des seuls segments pointés (affichage, hors solde).
    int? ProductionPointeeMinutes = null);

public sealed record PointageTotauxDto(
    int RetardTotalMinutes,
    int JoursEnRetard,
    int JoursAHeure,
    int JoursPointes,
    IReadOnlyList<DateOnly> JoursPointageManquant,
    IReadOnlyList<DateOnly> JoursSansPointage,
    int ProductionMinutes = 0,
    int ProductionPrevueMinutes = 0,
    int JoursProductionCalculee = 0,
    // v65 — Heures supplémentaires de la période, comptées à part de la production prévue.
    int HeuresSupplementairesMinutes = 0,
    int JoursHeuresSupplementaires = 0,
    // v67 — Récupérations de la période (minutes déduites des heures sup.) et journées.
    int RecupMinutes = 0,
    int JoursRecup = 0);

/// <summary>« Mon suivi » : pointages d'un collaborateur sur une période.</summary>
public sealed record PointageSuiviDto(
    int UserId,
    string Collaborateur,
    string? Cellule,
    IReadOnlyList<string> IdsPointeuse,
    DateOnly Du,
    DateOnly Au,
    int ToleranceMinutes,
    bool SynchronisationActive,
    DateTime? DerniereSynchro,
    PointageTotauxDto Totaux,
    IReadOnlyList<PointageJourDto> Jours,
    string? Regime = null);

/// <summary>Ligne de la vue équipe / RH : un collaborateur et ses totaux sur la période.</summary>
public sealed record PointageEquipeLigneDto(
    int UserId,
    string Collaborateur,
    string? Cellule,
    bool Relie,
    int RetardTotalMinutes,
    int JoursEnRetard,
    int JoursAHeure,
    int JoursPointes,
    int JoursPointageManquant,
    int JoursSansPointage,
    int ProductionMinutes = 0,
    int ProductionPrevueMinutes = 0,
    string? Regime = null,
    // v65 — Heures supplémentaires de la période (samedis et jours de repos travaillés hors planning).
    int HeuresSupplementairesMinutes = 0);

public sealed record PointageEquipeDto(
    string Perimetre,
    DateOnly Du,
    DateOnly Au,
    int ToleranceMinutes,
    DateTime? DerniereSynchro,
    IReadOnlyList<PointageEquipeLigneDto> Lignes);

/// <summary>Planning prévu / réalisé d'une équipe : le détail jour par jour de chaque collaborateur.</summary>
public sealed record PointageEquipeDetailDto(
    string Perimetre,
    DateOnly Du,
    DateOnly Au,
    DateTime? DerniereSynchro,
    IReadOnlyList<PointageSuiviDto> Collaborateurs);

public sealed record PointageCorrespondanceDto(
    string EmpCode,
    string? NomBioTime,
    string? DepartementBioTime,
    int? UserId,
    string? Collaborateur,
    string Statut,
    DateTime? DernierPointage,
    DateTime UpdatedAt);

public sealed record PointageCorrespondanceUpdateRequest(int? UserId, bool Ignorer);

public sealed record PointageEtatDto(
    bool Active,
    string? BaseUrl,
    DateTime? DernierEssai,
    DateTime? DerniereSynchro,
    DateTime? DerniereRelectureComplete,
    int DerniersPointagesRecus,
    string? DerniereErreur,
    DateTime? DerniereErreurAt,
    long PointagesEnBase,
    int CorrespondancesAValider,
    int CorrespondancesAuto);

/// <summary>Qui consulte : identifiants du jeton.</summary>
/// <param name="SuperAdmin">
/// v67 — Compte Super Admin : seul compte qui accède à « Heure de sortie » sans autorisation
/// compte par compte (l'Admin n'y a plus accès d'office).
/// </param>
public sealed record PointageViewer(int? AuthUserId, Guid? SubjectId, string? Email, string? Role, bool SuperAdmin = false);
