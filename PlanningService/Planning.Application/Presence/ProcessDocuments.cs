namespace Planning.Application.Presence;

/// <summary>v48 — Bibliothèque « Process Bytel » : documents partagés par l'encadrement.</summary>
public sealed record ProcessDocumentDto(
    long Id,
    string Dossier,
    string Titre,
    string NomFichier,
    string ContentType,
    long Taille,
    string? Commentaire,
    bool PourTous,
    IReadOnlyList<string> Activites,
    string? Auteur,
    DateTime CreeLe);

/// <summary>Contenu d'un document (téléchargement).</summary>
public sealed record ProcessDocumentContenuDto(string NomFichier, string ContentType, byte[] Contenu);

/// <summary>
/// Liste vue par le demandeur. <c>PeutDeposer</c> ouvre le dépôt et la suppression ;
/// <c>MonPole</c> est le pôle qui sert au filtre de visibilité (null = sans pôle).
/// </summary>
public sealed record ProcessBibliothequeDto(
    IReadOnlyList<string> Dossiers,
    IReadOnlyList<ProcessDocumentDto> Documents,
    bool PeutDeposer,
    string? MonPole);

/// <summary>Destinataires d'un dépôt : tous, ou les activités choisies (avec leurs pôles).</summary>
public sealed record ProcessVisibiliteRequest(bool PourTous, List<string> Activites, List<string> Poles);

public static class ProcessDocuments
{
    public const string DossierParDefaut = "ITs";
    public const long TailleMax = 25 * 1024 * 1024;
    public const int MaxDossier = 80;
    public const int MaxTitre = 200;

    public static string Separateur => "|";

    public static IReadOnlyList<string> Decouper(string? valeurs) =>
        (valeurs ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    public static string Joindre(IEnumerable<string>? valeurs) =>
        string.Join('|', (valeurs ?? Enumerable.Empty<string>())
            .Select(v => (v ?? string.Empty).Trim().Replace("|", "/"))
            .Where(v => v.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase));

    /// <summary>Nom de dossier rangé : « ITs » par défaut, jamais vide.</summary>
    public static string NormaliserDossier(string? dossier)
    {
        var value = (dossier ?? string.Empty).Trim();
        if (value.Length == 0) return DossierParDefaut;
        return value.Length > MaxDossier ? value[..MaxDossier].Trim() : value;
    }
}

/// <summary>v48 — Bibliothèque « Process Bytel » (dépôt par l'encadrement, lecture par les agents).</summary>
public interface IProcessDocumentService
{
    /// <summary>Documents visibles par le demandeur. <paramref name="peutDeposer"/> : il voit tout.</summary>
    Task<ProcessBibliothequeDto> ListerAsync(PresenceViewer viewer, bool peutDeposer, CancellationToken ct = default);

    Task<ProcessDocumentDto> AjouterAsync(
        PresenceViewer viewer,
        string? dossier,
        string? titre,
        string nomFichier,
        string? contentType,
        byte[] contenu,
        string? commentaire,
        ProcessVisibiliteRequest visibilite,
        CancellationToken ct = default);

    Task<ProcessDocumentContenuDto> TelechargerAsync(PresenceViewer viewer, long id, bool peutDeposer, CancellationToken ct = default);

    Task SupprimerAsync(PresenceViewer viewer, long id, CancellationToken ct = default);
}
