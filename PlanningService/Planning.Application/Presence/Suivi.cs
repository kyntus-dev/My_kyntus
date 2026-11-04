namespace Planning.Application.Presence;

/// <summary>Listes proposées dans les formulaires (avertissements, éléments variables, dossier).</summary>
public static class SuiviCatalogues
{
    public static readonly IReadOnlyList<string> TypesAvertissement =
    [
        "Avertissement verbal",
        "Avertissement écrit",
        "Erreur de production",
        "Comportement",
        "Sanction",
        "Mise à pied",
        "Autre",
    ];

    /// <summary>Types de l'export paie actuel ; « (à valoriser) » = montant fixé par le service paie.</summary>
    public static readonly IReadOnlyList<string> TypesEvp =
    [
        "Heures Supp. (à valoriser)",
        "Samedis (à valoriser)",
        "Challenges (à valoriser)",
        "Prime",
        "Indemnité",
        "Rappel de salaire",
        "Retenue",
        "Avance sur salaire",
        "Autre",
    ];

    public static readonly IReadOnlyList<string> CategoriesDossier =
    [
        "Contrat",
        "Avenant",
        "Pièce d'identité",
        "RIB",
        "CNSS",
        "Diplôme / attestation",
        "Justificatif d'absence",
        "Courrier / avertissement",
        "Autre",
    ];

    public const long TailleMaxDocument = 10 * 1024 * 1024;
}

public sealed record AvertissementDto(
    long Id, int UserId, string Agent, DateOnly Date, string Type, string Commentaire, string? Auteur, DateTime CreatedAt);

public sealed record AvertissementRequest(int UserId, DateOnly Date, string Type, string Commentaire);

public sealed record EvpDto(
    long Id, int UserId, string Agent, DateOnly Date, string Type, decimal Montant, string Commentaire, string? Auteur, DateTime CreatedAt);

public sealed record EvpRequest(int UserId, DateOnly Date, string Type, decimal Montant, string Commentaire);

public sealed record DossierDocumentDto(
    long Id, string Categorie, string NomFichier, string ContentType, long Taille, string? Commentaire, string? Auteur, DateTime CreatedAt);

/// <summary>Informations RH déjà présentes dans la fiche employé (lecture).</summary>
public sealed record FicheRhDto(
    string Nom,
    string Email,
    string? Role,
    string? Cellule,
    string Type,
    string? Matricule,
    string? Cin,
    string? Cnss,
    string? Telephone,
    string? Adresse,
    string? Rib,
    DateOnly? DateEntree,
    DateOnly? DateEmbauche,
    DateOnly? DateSortie,
    bool EnFormation);

public sealed record DossierDto(int UserId, FicheRhDto Fiche, IReadOnlyList<DossierDocumentDto> Documents);

/// <summary>Agent du périmètre (listes de choix des formulaires).</summary>
public sealed record PresenceAgentRefDto(int UserId, string Nom, string? Pole, string? PoleId, string? Cellule, string Type);

public sealed record DocumentFichier(string NomFichier, string ContentType, byte[] Contenu);

public interface IPresenceSuiviService
{
    Task<IReadOnlyList<PresenceAgentRefDto>> ListerAgentsAsync(PresenceViewer viewer, CancellationToken ct = default);

    Task<IReadOnlyList<AvertissementDto>> ListerAvertissementsAsync(PresenceViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default);
    Task<AvertissementDto> AjouterAvertissementAsync(PresenceViewer viewer, AvertissementRequest request, CancellationToken ct = default);
    Task SupprimerAvertissementAsync(PresenceViewer viewer, long id, CancellationToken ct = default);

    Task<IReadOnlyList<EvpDto>> ListerEvpAsync(PresenceViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default);
    Task<EvpDto> AjouterEvpAsync(PresenceViewer viewer, EvpRequest request, CancellationToken ct = default);
    Task<EvpDto> ModifierEvpAsync(PresenceViewer viewer, long id, EvpRequest request, CancellationToken ct = default);
    Task SupprimerEvpAsync(PresenceViewer viewer, long id, CancellationToken ct = default);

    Task<DossierDto> GetDossierAsync(PresenceViewer viewer, int userId, CancellationToken ct = default);
    Task<DossierDocumentDto> AjouterDocumentAsync(
        PresenceViewer viewer, int userId, string categorie, string nomFichier, string contentType, byte[] contenu, string? commentaire,
        CancellationToken ct = default);
    Task<DocumentFichier> GetDocumentAsync(PresenceViewer viewer, long id, CancellationToken ct = default);
    Task SupprimerDocumentAsync(PresenceViewer viewer, long id, CancellationToken ct = default);
}
