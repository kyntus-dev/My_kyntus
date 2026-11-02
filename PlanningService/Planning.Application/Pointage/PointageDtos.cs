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
    string? Source = null);

public sealed record PointageTotauxDto(
    int RetardTotalMinutes,
    int JoursEnRetard,
    int JoursAHeure,
    int JoursPointes,
    IReadOnlyList<DateOnly> JoursPointageManquant,
    IReadOnlyList<DateOnly> JoursSansPointage,
    int ProductionMinutes = 0,
    int ProductionPrevueMinutes = 0,
    int JoursProductionCalculee = 0);

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
    string? Regime = null);

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
public sealed record PointageViewer(int? AuthUserId, Guid? SubjectId, string? Email, string? Role);
