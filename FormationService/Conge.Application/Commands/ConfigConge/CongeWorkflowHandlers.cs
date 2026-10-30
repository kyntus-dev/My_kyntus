using Conge.Application.DTOs;
using Conge.Application.Services;
using Conge.Domain.Enums;
using Conge.Domain.Interfaces;
using MediatR;

namespace Conge.Application.Commands.ConfigConge;

/// <summary>Circuit de validation des congés (v9) : lecture et bascule.</summary>
public record GetCongeWorkflowQuery : IRequest<CongeWorkflowDto>;

/// <summary>
/// Bascule du circuit. Passer à « superviseur seul » finalise les demandes déjà
/// validées par un superviseur et restées en attente RH (décision déjà prise).
/// </summary>
public record UpdateCongeWorkflowCommand(bool EtapeRhRequise, Guid? UpdatedBy = null)
    : IRequest<CongeWorkflowDto>;

public class GetCongeWorkflowHandler(
    ICongeWorkflowConfigRepository repo,
    IDemandeCongeRepository demandes) : IRequestHandler<GetCongeWorkflowQuery, CongeWorkflowDto>
{
    public async Task<CongeWorkflowDto> Handle(GetCongeWorkflowQuery request, CancellationToken ct)
    {
        var config = await repo.GetOrCreateAsync(ct);
        var attente = (await demandes.GetByStatutAsync(StatutDemande.EnAttenteRh, ct)).Count();
        return new CongeWorkflowDto(config.EtapeRhRequise, config.UpdatedAt, config.UpdatedBy, attente);
    }
}

public class UpdateCongeWorkflowHandler(
    ICongeWorkflowConfigRepository repo,
    IDemandeCongeRepository demandes,
    IUnitOfWork unitOfWork,
    CongeValidationFinalizer finalizer) : IRequestHandler<UpdateCongeWorkflowCommand, CongeWorkflowDto>
{
    public async Task<CongeWorkflowDto> Handle(UpdateCongeWorkflowCommand request, CancellationToken ct)
    {
        var config = await repo.GetOrCreateAsync(ct);
        config.Definir(request.EtapeRhRequise, request.UpdatedBy);
        repo.Update(config);
        await unitOfWork.SaveChangesAsync(ct);

        if (!request.EtapeRhRequise)
            await finalizer.CollapsePendingRhIfDisabledAsync(ct);

        var attente = (await demandes.GetByStatutAsync(StatutDemande.EnAttenteRh, ct)).Count();
        return new CongeWorkflowDto(config.EtapeRhRequise, config.UpdatedAt, config.UpdatedBy, attente);
    }
}
