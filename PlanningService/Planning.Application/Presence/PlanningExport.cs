namespace Planning.Application.Presence;

/// <summary>Une journée du planning RH publié, pour l'export Excel.</summary>
public sealed record PlanningExportJourDto(
    DateOnly Jour,
    string? Libelle,
    string? Debut,
    string? Fin,
    int Minutes,
    bool Conge,
    bool Ferie,
    bool Renfort);

public sealed record PlanningExportAgentDto(
    int UserId,
    string Nom,
    string? Pole,
    string? Cellule,
    string? Service,
    IReadOnlyList<PlanningExportJourDto> Jours);

public sealed record PlanningExportDto(DateOnly Du, DateOnly Au, string Perimetre, IReadOnlyList<PlanningExportAgentDto> Agents);

/// <summary>Export du planning RH publié sur une période (périmètre du demandeur ; RH / Admin : tous).</summary>
public interface IPlanningExportService
{
    /// <summary>93 jours au plus.</summary>
    Task<PlanningExportDto> GetPlanningRhAsync(PresenceViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default);
}
