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
public sealed record ReaffectationRequest(int? SubServiceId, string? Motif, int? RoleId = null);

/// <summary>Service de destination ; PoleId = identifiant du pôle dans Organisation RH (pour le contrat).</summary>
public sealed record ReaffectationServiceOptionDto(int Id, string Service, string? Cellule, string? Pole, string? PoleId = null);

public sealed record ReaffectationAgentOptionDto(
    int Id, string Nom, int? SubServiceId, string? Service, string? Cellule, string? Role, int? RoleId = null);

/// <summary>Poste proposé ; Production = un service est obligatoire (pilote).</summary>
public sealed record ReaffectationRoleOptionDto(int Id, string Nom, bool Production);

public sealed record ReaffectationOptionsDto(
    string Perimetre,
    IReadOnlyList<ReaffectationAgentOptionDto> Agents,
    IReadOnlyList<ReaffectationServiceOptionDto> Services,
    IReadOnlyList<ReaffectationRoleOptionDto>? Roles = null);

/// <summary>Auteur d'une réaffectation et son périmètre (null : toute l'entreprise).</summary>
public sealed record ReaffectationAuteur(int? UserId, string? Nom, IReadOnlySet<int>? SubServices, bool Admin = false);

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
    Task<ReaffectationAuteur> ResoudreAsync(int? authUserId, string? email, bool global, CancellationToken ct = default, bool admin = false);
    Task<ReaffectationOptionsDto> OptionsAsync(ReaffectationAuteur auteur, CancellationToken ct = default);
    Task<ReaffectationDto> ReaffecterAsync(ReaffectationAuteur auteur, int userId, ReaffectationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ReaffectationDto>> HistoriqueAsync(ReaffectationAuteur auteur, int? userId, int max, CancellationToken ct = default);
    /// <summary>Comptes des agents du périmètre, actifs et désactivés (v26).</summary>
    Task<IReadOnlyList<CompteAgentDto>> ComptesAsync(ReaffectationAuteur auteur, CancellationToken ct = default);
    /// <summary>Désactive ou réactive le compte d'un agent du périmètre (v26).</summary>
    Task<CompteAgentDto> ActiverCompteAsync(ReaffectationAuteur auteur, int userId, ActivationCompteRequest request, CancellationToken ct = default);
}
