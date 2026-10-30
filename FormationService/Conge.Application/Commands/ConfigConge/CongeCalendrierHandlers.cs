using Conge.Application.DTOs;
using Conge.Application.Services;
using Conge.Domain.Interfaces;
using MediatR;

namespace Conge.Application.Commands.ConfigConge;

/// <summary>
/// Occupation d'un mois pour le calendrier de demande (v9) : absents par jour,
/// jours saturés par le quota de la cellule et jours des mois interdits.
/// </summary>
public record GetDisponibiliteMoisQuery(Guid EmployeId, int Annee, int Mois)
    : IRequest<CongeDisponibiliteMoisDto>;

public class GetDisponibiliteMoisHandler(
    CongeReglesService regles,
    IOrgNodeCongeRepository orgNodes) : IRequestHandler<GetDisponibiliteMoisQuery, CongeDisponibiliteMoisDto>
{
    public async Task<CongeDisponibiliteMoisDto> Handle(GetDisponibiliteMoisQuery request, CancellationToken ct)
    {
        var r = await regles.EvaluerMoisAsync(request.EmployeId, request.Annee, request.Mois, ct);
        var nom = r.NodeId is null ? null : (await orgNodes.GetByIdAsync(r.NodeId, ct))?.Name;

        return new CongeDisponibiliteMoisDto(
            r.Annee,
            r.Mois,
            r.NodeId,
            nom,
            r.Quota,
            r.Effectif,
            r.MoisInterdits,
            r.Jours
                .Select(j => new CongeJourDto(
                    j.Jour.ToString("yyyy-MM-dd"),
                    j.Absents,
                    j.Sature,
                    j.MoisInterdit,
                    j.DejaDemande))
                .ToList());
    }
}
