using Conge.Application.DTOs;
using Conge.Application.Services;
using Conge.Domain.Enums;
using Conge.Domain.Interfaces;
using Kyntus.Iam;
using MediatR;

namespace Conge.Application.Queries.GetDemandesByManager;

public class GetDemandesByManagerHandler
    : IRequestHandler<GetDemandesByManagerQuery, IEnumerable<DemandeCongeDto>>
{
    private readonly IDemandeCongeRepository _demandeRepo;
    private readonly IEmployeSnapshotRepository _employeRepo;
    private readonly IRebacClient? _rebac;
    private readonly CongeValidationFinalizer _finalizer;

    public GetDemandesByManagerHandler(
        IDemandeCongeRepository demandeRepo,
        IEmployeSnapshotRepository employeRepo,
        CongeValidationFinalizer finalizer,
        IRebacClient? rebac = null)
    {
        _demandeRepo = demandeRepo;
        _employeRepo = employeRepo;
        _finalizer = finalizer;
        _rebac = rebac;
    }

    public async Task<IEnumerable<DemandeCongeDto>> Handle(
        GetDemandesByManagerQuery request,
        CancellationToken ct)
    {
        var manager = await _employeRepo.GetByEmployeIdAsync(request.ManagerId, ct);
        if (manager is null)
            return Array.Empty<DemandeCongeDto>();

        IEnumerable<Domain.Entities.DemandeConge> demandes;
        if (IsRhOrAdmin(manager.Role))
        {
            var rhStep = await _finalizer.CollapsePendingRhIfDisabledAsync(ct);
            if (rhStep)
            {
                demandes = await _demandeRepo.GetByStatutAsync(StatutDemande.EnAttenteRh, ct);
            }
            else
            {
                var year = DateTime.UtcNow.Year;
                var validees = await _demandeRepo.GetByStatutAsync(StatutDemande.Validee, ct);
                demandes = validees.Where(d =>
                    d.DateDemande.Year >= year - 1 || d.DateDebut.Year >= year);
            }
        }
        else
        {
            IReadOnlyList<string>? managedNodes = null;
            if (_rebac is not null)
            {
                try
                {
                    // Kind = OrgAssignmentKind (Superviseur → cellules, ReferentTechnique → services, …).
                    var superviseurNodes = await _rebac.GetManagedNodeIdsAsync(request.ManagerId, "Superviseur", ct);
                    var referentNodes = await _rebac.GetManagedNodeIdsAsync(request.ManagerId, "ReferentTechnique", ct);
                    managedNodes = superviseurNodes.Concat(referentNodes)
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch
                {
                    managedNodes = null;
                }
            }

            var all = await _demandeRepo.GetByManagerIdAsync(
                request.ManagerId,
                managedNodes is { Count: > 0 } ? managedNodes : null,
                ct);
            demandes = all.Where(d => d.Statut == StatutDemande.EnAttente);
        }

        var result = new List<DemandeCongeDto>();
        foreach (var d in demandes)
        {
            var employe = await _employeRepo.GetByEmployeIdAsync(d.EmployeId, ct);
            string? supNom = null;
            string? rhNom = null;
            if (d.SuperviseurDecideurId is { } sid)
            {
                var s = await _employeRepo.GetByEmployeIdAsync(sid, ct);
                supNom = s?.NomComplet;
            }
            if (d.RhDecideurId is { } rid)
            {
                var r = await _employeRepo.GetByEmployeIdAsync(rid, ct);
                rhNom = r?.NomComplet;
            }

            result.Add(Map(d, employe, supNom, rhNom));
        }

        return result.OrderByDescending(r => r.DateDemande);
    }

    internal static DemandeCongeDto Map(
        Domain.Entities.DemandeConge d,
        Domain.Entities.EmployeSnapshot? employe,
        string? superviseurDecideurNom = null,
        string? rhDecideurNom = null)
        => new(
            d.Id,
            d.EmployeId,
            d.ManagerId,
            d.TypeConge,
            d.TypeExceptionnel,
            d.DateDebut,
            d.DateFin,
            d.NombreJours,
            d.Statut,
            d.Motif,
            d.CommentaireManager,
            d.DateDemande,
            d.DateDecision,
            employe?.Nom,
            employe?.Prenom,
            d.CommentaireRh,
            d.DateValidationSuperviseur,
            d.SuperviseurDecideurId,
            d.RhDecideurId,
            superviseurDecideurNom,
            rhDecideurNom,
            d.ValidationNodeId,
            d.Decisions.Count == 0
                ? null
                : d.Decisions
                    .OrderBy(x => x.At)
                    .Select(x => new DemandeCongeDecisionDto(
                        x.Id,
                        x.ActeurId,
                        x.ActeurNom,
                        x.ActeurRole,
                        x.Action,
                        x.StatutAvant,
                        x.StatutApres,
                        x.Commentaire,
                        x.At))
                    .ToList());

    private static bool IsRhOrAdmin(string? role)
    {
        var r = role?.Trim() ?? string.Empty;
        return r.Equals("RH", StringComparison.OrdinalIgnoreCase)
            || r.Equals("Admin", StringComparison.OrdinalIgnoreCase);
    }
}
