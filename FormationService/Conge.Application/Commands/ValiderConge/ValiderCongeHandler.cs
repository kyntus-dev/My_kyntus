using Conge.Application.Abstractions;
using Conge.Application.Services;
using Conge.Domain.Enums;
using Conge.Domain.Exceptions;
using Conge.Domain.Interfaces;
using MediatR;

namespace Conge.Application.Commands.ValiderConge;

public class ValiderCongeSuperviseurHandler : IRequestHandler<ValiderCongeSuperviseurCommand, bool>
{
    private readonly IDemandeCongeRepository _demandeRepo;
    private readonly IEmployeSnapshotRepository _employeRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CongeReglesService _regles;
    private readonly ILeaveWorkflowPolicy _workflow;
    private readonly CongeValidationFinalizer _finalizer;

    public ValiderCongeSuperviseurHandler(
        IDemandeCongeRepository demandeRepo,
        IEmployeSnapshotRepository employeRepo,
        IUnitOfWork unitOfWork,
        CongeReglesService regles,
        ILeaveWorkflowPolicy workflow,
        CongeValidationFinalizer finalizer)
    {
        _demandeRepo = demandeRepo;
        _employeRepo = employeRepo;
        _unitOfWork = unitOfWork;
        _regles = regles;
        _workflow = workflow;
        _finalizer = finalizer;
    }

    public async Task<bool> Handle(ValiderCongeSuperviseurCommand request, CancellationToken ct)
    {
        var demande = await _demandeRepo.GetByIdAsync(request.DemandeId, ct)
            ?? throw new CongeNotFoundException(request.DemandeId);

        if (demande.Statut != StatutDemande.EnAttente)
            throw new InvalidOperationException("Seules les demandes en attente superviseur peuvent être validées ici.");

        var employe = await _employeRepo.GetByEmployeIdAsync(demande.EmployeId, ct)
            ?? throw new EmployeNotFoundException(demande.EmployeId);

        await _regles.AssertHorsPeriodeInterditeAsync(demande.DateDebut, demande.DateFin, employe, ct);
        await _regles.AssertQuotaForEmployeAsync(
            employe, demande.DateDebut, demande.DateFin, demande.Id, ct);

        var etapeRh = await _workflow.IsRhValidationRequiredAsync(ct);
        var acteur = await _employeRepo.GetByEmployeIdAsync(request.SuperviseurId, ct);
        var acteurNom = acteur?.NomComplet;
        demande.ValiderParSuperviseur(
            request.SuperviseurId,
            request.Commentaire,
            acteurNom,
            acteur?.Role ?? "Superviseur",
            etapeRh);

        if (!etapeRh)
        {
            await _finalizer.PersistAndPublishAsync(demande, request.SuperviseurId, acteurNom, ct);
            return true;
        }

        _demandeRepo.Update(demande);
        await _unitOfWork.SaveChangesAsync(ct);
        return true;
    }
}

public class ValiderCongeRhHandler : IRequestHandler<ValiderCongeRhCommand, bool>
{
    private readonly IDemandeCongeRepository _demandeRepo;
    private readonly IEmployeSnapshotRepository _employeRepo;
    private readonly CongeReglesService _regles;
    private readonly CongeValidationFinalizer _finalizer;

    public ValiderCongeRhHandler(
        IDemandeCongeRepository demandeRepo,
        IEmployeSnapshotRepository employeRepo,
        CongeReglesService regles,
        CongeValidationFinalizer finalizer)
    {
        _demandeRepo = demandeRepo;
        _employeRepo = employeRepo;
        _regles = regles;
        _finalizer = finalizer;
    }

    public async Task<bool> Handle(ValiderCongeRhCommand request, CancellationToken ct)
    {
        var demande = await _demandeRepo.GetByIdAsync(request.DemandeId, ct)
            ?? throw new CongeNotFoundException(request.DemandeId);

        if (demande.Statut != StatutDemande.EnAttenteRh)
            throw new InvalidOperationException("Seules les demandes en attente RH peuvent être validées ici.");

        var employe = await _employeRepo.GetByEmployeIdAsync(demande.EmployeId, ct)
            ?? throw new EmployeNotFoundException(demande.EmployeId);

        await _regles.AssertHorsPeriodeInterditeAsync(demande.DateDebut, demande.DateFin, employe, ct);
        await _regles.AssertQuotaForEmployeAsync(
            employe, demande.DateDebut, demande.DateFin, demande.Id, ct);

        var acteur = await _employeRepo.GetByEmployeIdAsync(request.RhId, ct);
        var acteurNom = acteur?.NomComplet;
        demande.ValiderParRh(
            request.RhId,
            request.Commentaire,
            acteurNom,
            acteur?.Role ?? "RH");

        await _finalizer.PersistAndPublishAsync(demande, request.RhId, acteurNom, ct);
        return true;
    }
}

/// <summary>
/// Compat route PUT /valider : superviseur si EnAttente, RH si EnAttenteRh.
/// </summary>
public class ValiderCongeHandler : IRequestHandler<ValiderCongeCommand, bool>
{
    private readonly IMediator _mediator;
    private readonly IDemandeCongeRepository _demandeRepo;

    public ValiderCongeHandler(IMediator mediator, IDemandeCongeRepository demandeRepo)
    {
        _mediator = mediator;
        _demandeRepo = demandeRepo;
    }

    public async Task<bool> Handle(ValiderCongeCommand request, CancellationToken ct)
    {
        var demande = await _demandeRepo.GetByIdAsync(request.DemandeId, ct)
            ?? throw new CongeNotFoundException(request.DemandeId);

        return demande.Statut switch
        {
            StatutDemande.EnAttente => await _mediator.Send(
                new ValiderCongeSuperviseurCommand(request.DemandeId, request.ManagerId, request.Commentaire), ct),
            StatutDemande.EnAttenteRh => await _mediator.Send(
                new ValiderCongeRhCommand(request.DemandeId, request.ManagerId, request.Commentaire), ct),
            _ => throw new InvalidOperationException($"Impossible de valider une demande avec le statut '{demande.Statut}'.")
        };
    }
}
