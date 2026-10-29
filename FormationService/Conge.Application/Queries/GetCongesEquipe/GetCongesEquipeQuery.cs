using Conge.Application.DTOs;
using Conge.Application.Queries.GetDemandesByManager;
using Conge.Domain.Interfaces;
using MediatR;

namespace Conge.Application.Queries.GetCongesEquipe;

/// <summary>Collaborateur de l'équipe, avec son rattachement (pour le filtre par cellule).</summary>
public record EquipeMembreDto(
    Guid EmployeId,
    string Nom,
    string Prenom,
    string Role,
    string? PoleId,
    string? PoleNom,
    string? CelluleId,
    string? CelluleNom);

/// <summary>Congés d'une équipe : membres du périmètre et toutes leurs demandes de l'année.</summary>
public record EquipeCongesDto(
    string Perimetre,
    int Annee,
    IReadOnlyList<EquipeMembreDto> Membres,
    IReadOnlyList<DemandeCongeDto> Demandes);

/// <param name="ManagedEmployeeIds">
/// Collaborateurs du périmètre (organisation RH) ; null = toute l'entreprise (RH / Admin).
/// </param>
public record GetCongesEquipeQuery(
    int Annee,
    IReadOnlyCollection<Guid>? ManagedEmployeeIds,
    string Perimetre) : IRequest<EquipeCongesDto>;

public sealed class GetCongesEquipeHandler(
    IDemandeCongeRepository demandes,
    IEmployeSnapshotRepository employes,
    IOrgNodeCongeRepository orgNodes)
    : IRequestHandler<GetCongesEquipeQuery, EquipeCongesDto>
{
    public async Task<EquipeCongesDto> Handle(GetCongesEquipeQuery request, CancellationToken ct)
    {
        var all = await employes.GetAllAsync(ct);
        var scope = request.ManagedEmployeeIds is null ? null : request.ManagedEmployeeIds.ToHashSet();
        var members = all
            .Where(e => scope is null || scope.Contains(e.EmployeId))
            .ToList();
        var byId = members.ToDictionary(e => e.EmployeId);

        var names = (await orgNodes.GetAllActiveAsync(ct))
            .GroupBy(n => n.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
        string? NameOf(string? id) => id is not null && names.TryGetValue(id, out var n) ? n : null;

        var membres = members
            .Select(e => new EquipeMembreDto(
                e.EmployeId,
                e.Nom,
                e.Prenom,
                e.Role,
                e.PoleId,
                NameOf(e.PoleId),
                e.CelluleId,
                NameOf(e.CelluleId)))
            .OrderBy(m => m.CelluleNom == null)
            .ThenBy(m => m.CelluleNom ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.Prenom, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Demandes qui débutent ou se terminent dans l'année (congés à cheval compris).
        var rows = (await demandes.GetByAnneeAsync(request.Annee, ct))
            .Where(d => byId.ContainsKey(d.EmployeId))
            .OrderByDescending(d => d.DateDebut)
            .Select(d => GetDemandesByManagerHandler.Map(d, byId[d.EmployeId], DecideurNom(d.SuperviseurDecideurId), DecideurNom(d.RhDecideurId)))
            .ToList();

        return new EquipeCongesDto(request.Perimetre, request.Annee, membres, rows);

        string? DecideurNom(Guid? id) =>
            id is { } g && all.FirstOrDefault(e => e.EmployeId == g) is { } s ? s.NomComplet : null;
    }
}
