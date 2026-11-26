using Conge.Application.DTOs;
using Conge.Application.Queries.GetDemandesByManager;
using Conge.Domain.Interfaces;
using MediatR;

namespace Conge.Application.Queries.GetHistoriqueRh;

/// <param name="EmployeIds">
/// v67 — Périmètre du lecteur : null = toute l'entreprise (Admin, RH, ou droit « Toute l'entreprise ») ;
/// sinon, seulement les demandes de ces collaborateurs (équipe gérée).
/// </param>
public record GetHistoriqueRhQuery(int Annee, IReadOnlyCollection<Guid>? EmployeIds = null) : IRequest<IEnumerable<DemandeCongeDto>>;

public class GetHistoriqueRhHandler : IRequestHandler<GetHistoriqueRhQuery, IEnumerable<DemandeCongeDto>>
{
    private readonly IDemandeCongeRepository _demandeRepo;
    private readonly IEmployeSnapshotRepository _employeRepo;

    public GetHistoriqueRhHandler(
        IDemandeCongeRepository demandeRepo,
        IEmployeSnapshotRepository employeRepo)
    {
        _demandeRepo = demandeRepo;
        _employeRepo = employeRepo;
    }

    public async Task<IEnumerable<DemandeCongeDto>> Handle(GetHistoriqueRhQuery request, CancellationToken ct)
    {
        var demandes = await _demandeRepo.GetByAnneeAsync(request.Annee, ct);
        var perimetre = request.EmployeIds is null ? null : request.EmployeIds.ToHashSet();
        var result = new List<DemandeCongeDto>();

        foreach (var d in demandes.Where(d => perimetre is null || perimetre.Contains(d.EmployeId)))
        {
            var employe = await _employeRepo.GetByEmployeIdAsync(d.EmployeId, ct);
            string? supNom = null;
            string? rhNom = null;
            if (d.SuperviseurDecideurId is { } sid)
                supNom = (await _employeRepo.GetByEmployeIdAsync(sid, ct))?.NomComplet;
            if (d.RhDecideurId is { } rid)
                rhNom = (await _employeRepo.GetByEmployeIdAsync(rid, ct))?.NomComplet;
            result.Add(GetDemandesByManagerHandler.Map(d, employe, supNom, rhNom));
        }

        return result.OrderByDescending(r => r.DateDemande);
    }
}
