namespace Planning.Application.Abstractions;

public sealed record ReaffectationDto(
    long Id,
    int UserId,
    string Agent,
    int? AncienSubServiceId,
    string? Ancien,
    int NouveauSubServiceId,
    string Nouveau,
    string? Motif,
    string Origine,
    DateTime Le,
    string? Par);

public sealed record ReaffectationRequest(int SubServiceId, string? Motif);

public sealed record ReaffectationServiceOptionDto(int Id, string Service, string? Cellule, string? Pole);

public sealed record ReaffectationAgentOptionDto(int Id, string Nom, int? SubServiceId, string? Service, string? Cellule, string? Role);

public sealed record ReaffectationOptionsDto(
    string Perimetre,
    IReadOnlyList<ReaffectationAgentOptionDto> Agents,
    IReadOnlyList<ReaffectationServiceOptionDto> Services);

/// <summary>Auteur d'une réaffectation et son périmètre (null : toute l'entreprise).</summary>
public sealed record ReaffectationAuteur(int? UserId, string? Nom, IReadOnlySet<int>? SubServices);

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
    Task<ReaffectationAuteur> ResoudreAsync(int? authUserId, string? email, bool global, CancellationToken ct = default);
    Task<ReaffectationOptionsDto> OptionsAsync(ReaffectationAuteur auteur, CancellationToken ct = default);
    Task<ReaffectationDto> ReaffecterAsync(ReaffectationAuteur auteur, int userId, ReaffectationRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ReaffectationDto>> HistoriqueAsync(ReaffectationAuteur auteur, int? userId, int max, CancellationToken ct = default);
}
