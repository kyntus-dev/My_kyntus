namespace Planning.Application.Pointage;

/// <summary>Synchronisation ZKBio Time → My-Kyntus (lecture seule côté ZKBio Time).</summary>
public interface IPointageSyncService
{
    /// <summary>Lit les nouveaux pointages ; retourne le nombre de pointages ajoutés.</summary>
    Task<int> SynchroniserAsync(CancellationToken ct = default);
}

/// <summary>Consultation des pointages calculés et gestion des correspondances.</summary>
public interface IPointageQueryService
{
    Task<PointageSuiviDto?> GetMonSuiviAsync(PointageViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default);

    /// <summary>Suivi d'un collaborateur (superviseur dans son périmètre, RH, Admin) ; null si interdit ou inconnu.</summary>
    Task<PointageSuiviDto?> GetSuiviCollaborateurAsync(PointageViewer viewer, int userId, DateOnly du, DateOnly au, CancellationToken ct = default);

    Task<PointageEquipeDto?> GetEquipeAsync(PointageViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default);

    /// <summary>Planning prévu / réalisé : journées de chaque collaborateur du périmètre.</summary>
    Task<PointageEquipeDetailDto?> GetEquipeDetailAsync(PointageViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default);

    /// <summary>
    /// Journées calculées de collaborateurs donnés, sans contrôle de périmètre : l'appelant (tableau
    /// des présences) a déjà vérifié que ces collaborateurs relèvent du demandeur.
    /// </summary>
    Task<IReadOnlyList<PointageSuiviDto>> CalculerPourAsync(IReadOnlyList<int> userIds, DateOnly du, DateOnly au, CancellationToken ct = default);

    /// <summary>Journée en cours du collaborateur connecté (page « Heure de sortie », tableau de bord).</summary>
    Task<PointageAujourdhuiDto?> GetAujourdhuiAsync(PointageViewer viewer, CancellationToken ct = default);

    Task<IReadOnlyList<PointageCorrespondanceDto>> GetCorrespondancesAsync(string? statut, CancellationToken ct = default);

    Task<PointageCorrespondanceDto?> UpdateCorrespondanceAsync(string empCode, PointageCorrespondanceUpdateRequest request, PointageViewer viewer, CancellationToken ct = default);

    /// <summary>Valide d'un coup tous les rapprochements automatiques par le nom.</summary>
    Task<int> ValiderCorrespondancesAutoAsync(PointageViewer viewer, CancellationToken ct = default);

    Task<PointageEtatDto> GetEtatAsync(CancellationToken ct = default);
}

/// <summary>Régimes horaires : gestion par l'Admin et résolution par collaborateur.</summary>
public interface IRegimeHoraireService
{
    Task<IReadOnlyList<RegimeHoraireDto>> GetAllAsync(CancellationToken ct = default);
    Task<RegimeHoraireDto> CreateAsync(RegimeHoraireRequest request, int? by, CancellationToken ct = default);
    Task<RegimeHoraireDto?> UpdateAsync(int id, RegimeHoraireRequest request, int? by, CancellationToken ct = default);
    /// <summary>Supprime un régime sans affectation ; refusé (InvalidOperationException) sinon.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken ct = default);
    /// <summary>Affecte (ou réaffecte) une cible à un régime.</summary>
    Task<RegimeHoraireDto?> AffecterAsync(int regimeId, RegimeAffectationRequest request, int? by, CancellationToken ct = default);
    Task<bool> RetirerAffectationAsync(int affectationId, CancellationToken ct = default);
    Task<IReadOnlyList<RegimeCibleDto>> GetCiblesAsync(string cible, CancellationToken ct = default);
    /// <summary>Comptes actifs et leur accès à « Heure de sortie ».</summary>
    Task<IReadOnlyList<HeureSortieAccesDto>> GetAccesHeureSortieAsync(CancellationToken ct = default);
    /// <summary>Donne ou retire l'accès à une liste de comptes ; retourne le nombre de comptes modifiés.</summary>
    Task<int> SetAccesHeureSortieAsync(HeureSortieAccesRequest request, int? by, CancellationToken ct = default);
    Task<bool> ADroitHeureSortieAsync(int userId, CancellationToken ct = default);
    /// <summary>Régime effectif de chaque collaborateur demandé (absent : aucun régime).</summary>
    Task<IReadOnlyDictionary<int, RegimeEffectif>> ResoudreAsync(IReadOnlyCollection<int> userIds, CancellationToken ct = default);
}
