using Conge.Application.Services;
using Conge.Domain.Enums;
using Conge.Domain.Interfaces;
using MediatR;

namespace Conge.Application.Queries.GetPendingCongesRhCount;

public record GetPendingCongesRhCountQuery : IRequest<int>;

public class GetPendingCongesRhCountHandler : IRequestHandler<GetPendingCongesRhCountQuery, int>
{
    private readonly IDemandeCongeRepository _demandeRepo;
    private readonly CongeValidationFinalizer _finalizer;

    public GetPendingCongesRhCountHandler(
        IDemandeCongeRepository demandeRepo,
        CongeValidationFinalizer finalizer)
    {
        _demandeRepo = demandeRepo;
        _finalizer = finalizer;
    }

    public async Task<int> Handle(GetPendingCongesRhCountQuery request, CancellationToken ct)
    {
        var rhStep = await _finalizer.CollapsePendingRhIfDisabledAsync(ct);
        if (!rhStep)
            return 0;
        var pending = await _demandeRepo.GetByStatutAsync(StatutDemande.EnAttenteRh, ct);
        return pending.Count();
    }
}
