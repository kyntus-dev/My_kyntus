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

    Task<IReadOnlyList<PointageCorrespondanceDto>> GetCorrespondancesAsync(string? statut, CancellationToken ct = default);

    Task<PointageCorrespondanceDto?> UpdateCorrespondanceAsync(string empCode, PointageCorrespondanceUpdateRequest request, PointageViewer viewer, CancellationToken ct = default);

    /// <summary>Valide d'un coup tous les rapprochements automatiques par le nom.</summary>
    Task<int> ValiderCorrespondancesAutoAsync(PointageViewer viewer, CancellationToken ct = default);

    Task<PointageEtatDto> GetEtatAsync(CancellationToken ct = default);
}
