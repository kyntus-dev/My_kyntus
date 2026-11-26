namespace Planning.Application.Abstractions;

/// <summary>v64 — Événement tracé explicitement (génération, import, export, droits d'accès…).</summary>
public sealed record JournalEvenement(
    string Module,
    string Operation,
    string Element,
    string Entite = "",
    string? EntiteId = null,
    int? CibleUserId = null,
    IReadOnlyDictionary<string, string?>? Anciennes = null,
    IReadOnlyDictionary<string, string?>? Nouvelles = null,
    string? Details = null,
    string? Source = null);

/// <summary>
/// v64 — Auteur fourni explicitement (événement reçu d'un autre service). v67 : <paramref name="SuperAdmin"/>
/// — action du Super Admin, jamais inscrite.
/// </summary>
public sealed record JournalAuteurExterne(Guid? Guid, string? Email, string? Role, bool SuperAdmin = false);

public sealed record JournalFiltre(
    DateOnly? Du,
    DateOnly? Au,
    string? Module,
    string? Operation,
    int? ActeurId,
    string? ActeurRole,
    int? CibleUserId,
    string? Recherche,
    int Page = 1,
    int Taille = 50);

public sealed record JournalLigneDto(
    long Id,
    DateTime Horodatage,
    int? ActeurId,
    string ActeurNom,
    string? ActeurRole,
    string Module,
    string Operation,
    string Entite,
    string? EntiteId,
    string Element,
    int? CibleUserId,
    IReadOnlyDictionary<string, string?>? AnciennesValeurs,
    IReadOnlyDictionary<string, string?>? NouvellesValeurs,
    string? Details,
    string? Source);

public sealed record JournalActeurOptionDto(int? Id, string Nom, string? Role);

public sealed record JournalFiltresDto(
    IReadOnlyList<string> Modules,
    IReadOnlyList<string> Operations,
    IReadOnlyList<string> Roles,
    IReadOnlyList<JournalActeurOptionDto> Acteurs);

public sealed record JournalPageDto(int Total, int Page, int Taille, IReadOnlyList<JournalLigneDto> Lignes);

public interface IJournalActionsService
{
    /// <summary>Trace un événement (jamais d'exception : une erreur du journal est seulement consignée).</summary>
    Task EnregistrerAsync(JournalEvenement evenement, JournalAuteurExterne? auteur = null, CancellationToken ct = default);

    Task<JournalPageDto> RechercherAsync(JournalFiltre filtre, CancellationToken ct = default);

    Task<JournalFiltresDto> FiltresAsync(CancellationToken ct = default);

    /// <summary>Export CSV (séparateur « ; », UTF-8 avec BOM) des lignes du filtre, 20 000 au plus.</summary>
    Task<byte[]> ExporterCsvAsync(JournalFiltre filtre, CancellationToken ct = default);
}
