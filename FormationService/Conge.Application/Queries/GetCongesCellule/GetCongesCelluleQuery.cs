using Conge.Domain.Enums;
using Conge.Domain.Interfaces;
using MediatR;

namespace Conge.Application.Queries.GetCongesCellule;

/// <summary>
/// Absence visible par les collègues d'une même cellule : qui, quand, où en est la demande.
/// Volontairement sans motif, sans type de congé et sans solde.
/// </summary>
public record CelluleAbsenceDto(
    Guid EmployeId,
    string Collaborateur,
    DateTime DateDebut,
    DateTime DateFin,
    double NombreJours,
    StatutDemande Statut,
    bool EstMoi);

/// <summary>Congés de ma cellule : collègues, absences de l'année et quota en vigueur.</summary>
public record CelluleCongesDto(
    string? CelluleId,
    string CelluleNom,
    int Annee,
    int Effectif,
    int? QuotaMaxAbsents,
    IReadOnlyList<CelluleAbsenceDto> Absences);

public record GetCongesCelluleQuery(Guid EmployeId, int Annee) : IRequest<CelluleCongesDto>;

public sealed class GetCongesCelluleHandler(
    IDemandeCongeRepository demandes,
    IEmployeSnapshotRepository employes,
    IOrgNodeCongeRepository orgNodes,
    IQuotaCongeServiceRepository quotas)
    : IRequestHandler<GetCongesCelluleQuery, CelluleCongesDto>
{
    /// <summary>Statuts affichés : une demande en cours bloque autant qu'un congé validé.</summary>
    private static readonly StatutDemande[] Visibles =
    {
        StatutDemande.EnAttente,
        StatutDemande.EnAttenteRh,
        StatutDemande.Validee,
    };

    public async Task<CelluleCongesDto> Handle(GetCongesCelluleQuery request, CancellationToken ct)
    {
        var moi = await employes.GetByEmployeIdAsync(request.EmployeId, ct);
        var nodeId = moi?.CelluleId;
        var scope = "Cellule";
        if (string.IsNullOrWhiteSpace(nodeId))
        {
            nodeId = moi?.OrgServiceId;
            scope = "Service";
        }

        if (moi is null || string.IsNullOrWhiteSpace(nodeId))
            return new CelluleCongesDto(null, "Aucune cellule de rattachement", request.Annee, 0, null, []);

        var nom = (await orgNodes.GetByIdAsync(nodeId, ct))?.Name
            ?? (scope == "Service" ? moi.ServiceNom : null)
            ?? "Ma cellule";

        var membres = (await employes.GetByOrgNodeIdAsync(nodeId, ct))
            .Where(e => !e.IsArchived)
            .ToList();
        var byId = membres.ToDictionary(e => e.EmployeId);

        var quota = await quotas.GetByServiceIdAsync(nodeId, ct);

        var absences = (await demandes.GetByAnneeAsync(request.Annee, ct))
            .Where(d => byId.ContainsKey(d.EmployeId) && Visibles.Contains(d.Statut))
            .OrderBy(d => d.DateDebut)
            .Select(d => new CelluleAbsenceDto(
                d.EmployeId,
                byId[d.EmployeId].NomComplet,
                d.DateDebut,
                d.DateFin,
                d.NombreJours,
                d.Statut,
                d.EmployeId == moi.EmployeId))
            .ToList();

        return new CelluleCongesDto(
            nodeId,
            nom,
            request.Annee,
            membres.Count,
            quota?.MaxAbsentsSimultanes,
            absences);
    }
}
