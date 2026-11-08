namespace Planning.Application.Presence;

/// <summary>Listes proposées dans les formulaires (avertissements, éléments variables, dossier).</summary>
public static class SuiviCatalogues
{
    /// <summary>Suite donnée (« Pénalité seule » : le montant du référentiel, sans autre mesure).</summary>
    public static readonly IReadOnlyList<string> TypesAvertissement =
    [
        "Pénalité seule",
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

/// <summary>Manquement saisi. <see cref="Code"/> : code du référentiel (v30) ; null pour les anciennes saisies.</summary>
public sealed record AvertissementDto(
    long Id, int UserId, string Agent, DateOnly Date, string Type, string Commentaire, string? Auteur, DateTime CreatedAt,
    string? Code = null);

public sealed record AvertissementRequest(int UserId, DateOnly Date, string Type, string Commentaire, string? Code = null);

public sealed record EvpDto(
    long Id, int UserId, string Agent, DateOnly Date, string Type, decimal Montant, string Commentaire, string? Auteur, DateTime CreatedAt);

public sealed record EvpRequest(int UserId, DateOnly Date, string Type, decimal Montant, string Commentaire);

/// <summary>v31 : catégories des types ajoutés à la main.</summary>
public static class CategoriesType
{
    public const string Avertissement = "avertissement";
    public const string Evp = "evp";

    public static string? Normaliser(string? categorie)
    {
        var c = (categorie ?? string.Empty).Trim().ToLowerInvariant();
        return c is Avertissement or Evp ? c : null;
    }
}

/// <summary>v31 : type ajouté à la main (supprimable).</summary>
public sealed record TypePersonnaliseDto(long Id, string Categorie, string Libelle, string? Auteur);

public sealed record TypePersonnaliseRequest(string Categorie, string Libelle);

/// <summary>Listes des formulaires : types prévus suivis des types ajoutés à la main.</summary>
public sealed record SuiviCataloguesDto(
    IReadOnlyList<string> Avertissements,
    IReadOnlyList<string> Evp,
    IReadOnlyList<string> Dossier,
    long TailleMaxDocument,
    IReadOnlyList<ManquementDto> Manquements,
    IReadOnlyList<TypePersonnaliseDto> TypesPersonnalises);

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
    Task<SuiviCataloguesDto> CataloguesAsync(CancellationToken ct = default);
    Task<SuiviCataloguesDto> AjouterTypeAsync(PresenceViewer viewer, TypePersonnaliseRequest request, CancellationToken ct = default);
    Task<SuiviCataloguesDto> SupprimerTypeAsync(PresenceViewer viewer, long id, CancellationToken ct = default);

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
