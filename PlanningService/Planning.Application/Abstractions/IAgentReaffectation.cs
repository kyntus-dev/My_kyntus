namespace Planning.Application.Abstractions;

public sealed record ReaffectationDto(
    long Id,
    int UserId,
    string Agent,
    int? AncienSubServiceId,
    string? Ancien,
    int? NouveauSubServiceId,
    string Nouveau,
    string? Motif,
    string Origine,
    DateTime Le,
    string? Par,
    string? AncienRole = null,
    string? NouveauRole = null);

/// <summary>
/// v26 : SubServiceId null = « hors production » (aucun service), possible seulement pour un rôle
/// hors production ; RoleId = nouveau poste (null : inchangé).
/// </summary>
/// <remarks>v28 : DepartementSupportId = structure support d'Organisation RH (ex. Formation) pour un poste hors production.</remarks>
/// <remarks>v29 : Responsabilites = nouveaux pôles / cellules / services (ids Organisation RH) d'un responsable.</remarks>
public sealed record ReaffectationRequest(
    int? SubServiceId, string? Motif, int? RoleId = null, string? DepartementSupportId = null,
    IReadOnlyList<string>? Responsabilites = null);

/// <summary>Service de destination ; PoleId = identifiant du pôle dans Organisation RH (pour le contrat).</summary>
/// <remarks>v29 : CelluleId / ServiceNodeId = identifiants Organisation RH (affectation des responsables).</remarks>
public sealed record ReaffectationServiceOptionDto(
    int Id, string Service, string? Cellule, string? Pole, string? PoleId = null, string? CelluleId = null, string? ServiceNodeId = null);

/// <summary>Responsabilités actuelles d'un responsable : niveau (Pole, Cellule, Service) et nœuds (v29).</summary>
public sealed record ResponsabilitesDto(string? Niveau, IReadOnlyList<string> Noeuds);

/// <summary>ARegulariser : poste hors production encore rattaché à un service de production (v27).</summary>
public sealed record ReaffectationAgentOptionDto(
    int Id, string Nom, int? SubServiceId, string? Service, string? Cellule, string? Role, int? RoleId = null, bool ARegulariser = false);

/// <summary>
/// Poste proposé ; Production = un service est obligatoire (pilote) ; SansService = aucun service de
/// production possible (équipe formation, RH…), v27.
/// </summary>
public sealed record ReaffectationRoleOptionDto(int Id, string Nom, bool Production, bool SansService = false);

public sealed record ReaffectationOptionsDto(
    string Perimetre,
    IReadOnlyList<ReaffectationAgentOptionDto> Agents,
    IReadOnlyList<ReaffectationServiceOptionDto> Services,
    IReadOnlyList<ReaffectationRoleOptionDto>? Roles = null);

/// <summary>Auteur d'une réaffectation et son périmètre (null : toute l'entreprise).</summary>
/// <remarks>Rh : Admin ou RH (seuls à attribuer les postes RH, Audit, Comptabilité, IT).</remarks>
public sealed record ReaffectationAuteur(int? UserId, string? Nom, IReadOnlySet<int>? SubServices, bool Admin = false, bool Rh = false);

/// <summary>Compte d'un agent du périmètre (activation / désactivation, v26).</summary>
public sealed record CompteAgentDto(
    int Id, string Nom, string? Email, string? Service, string? Cellule, string? Role, bool Actif, DateOnly? DateSortie);

public sealed record ActivationCompteRequest(bool Actif, DateOnly? DateSortie);

public class ReaffectationRefusException(string message, bool horsPerimetre = false) : Exception(message)
{
    public bool HorsPerimetre { get; } = horsPerimetre;
}

/// <summary>
/// Réaffectation d'agents d'un service à un autre (v21). Droit « rh.affectations » dans « Accès
/// par rôle » ; un responsable ne déplace qu'entre les services de son périmètre.
/// </summary>
public interface IAgentReaffectation
{
    Task<ReaffectationAuteur> ResoudreAsync(int? authUserId, string? email, bool global, CancellationToken ct = default, bool admin = false, bool rh = false);
    Task<ReaffectationOptionsDto> OptionsAsync(ReaffectationAuteur auteur, CancellationToken ct = default);
    Task<ReaffectationDto> ReaffecterAsync(ReaffectationAuteur auteur, int userId, ReaffectationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ReaffectationDto>> HistoriqueAsync(ReaffectationAuteur auteur, int? userId, int max, CancellationToken ct = default);
    /// <summary>Comptes des agents du périmètre, actifs et désactivés (v26).</summary>
    Task<IReadOnlyList<CompteAgentDto>> ComptesAsync(ReaffectationAuteur auteur, CancellationToken ct = default);
    /// <summary>Désactive ou réactive le compte d'un agent du périmètre (v26).</summary>
    Task<CompteAgentDto> ActiverCompteAsync(ReaffectationAuteur auteur, int userId, ActivationCompteRequest request, CancellationToken ct = default);
    /// <summary>Responsabilités actuelles d'un responsable dans Organisation RH (v29).</summary>
    Task<ResponsabilitesDto> ResponsabilitesAsync(int userId, CancellationToken ct = default);
}
