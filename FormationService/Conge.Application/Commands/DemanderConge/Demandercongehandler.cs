using Conge.Application.Abstractions;
using Conge.Application.Contracts;
using Conge.Application.Services;
using Conge.Domain.Entities;
using Conge.Domain.Enums;
using Conge.Domain.Events;
using Conge.Domain.Exceptions;
using Conge.Domain.Interfaces;
using Kyntus.Messaging.Contracts;
using MediatR;

namespace Conge.Application.Commands.DemanderConge;

public class DemanderCongeHandler : IRequestHandler<DemanderCongeCommand, Guid>
{
    private readonly IDemandeCongeRepository _demandeRepo;
    private readonly ISoldeCongeRepository _soldeRepo;
    private readonly IEmployeSnapshotRepository _employeRepo;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICongeEventPublisher _eventPublisher;
    private readonly CongeReglesService _regles;
    private readonly IEmployeSnapshotEnsure _ensure;
    private readonly ILeaveWorkflowPolicy _workflow;
    private readonly CongeValidationFinalizer _finalizer;

    public DemanderCongeHandler(
        IDemandeCongeRepository demandeRepo,
        ISoldeCongeRepository soldeRepo,
        IEmployeSnapshotRepository employeRepo,
        IUnitOfWork unitOfWork,
        ICongeEventPublisher eventPublisher,
        CongeReglesService regles,
        IEmployeSnapshotEnsure ensure,
        ILeaveWorkflowPolicy workflow,
        CongeValidationFinalizer finalizer)
    {
        _demandeRepo = demandeRepo;
        _soldeRepo = soldeRepo;
        _employeRepo = employeRepo;
        _unitOfWork = unitOfWork;
        _eventPublisher = eventPublisher;
        _regles = regles;
        _ensure = ensure;
        _workflow = workflow;
        _finalizer = finalizer;
    }

    public async Task<Guid> Handle(DemanderCongeCommand request, CancellationToken ct)
    {
        var employe = await _ensure.EnsureAsync(
                new EmployeSnapshotEnsureRequest(
                    request.EmployeId,
                    Email: null,
                    Role: null,
                    ActorSubjectId: null,
                    AllowJwtStub: false),
                ct)
            ?? await _employeRepo.GetByEmployeIdAsync(request.EmployeId, ct)
            ?? throw new EmployeNotFoundException(request.EmployeId);

        Guid validateurId;
        var statutInitial = StatutDemande.EnAttente;

        if (KyntusRoleNames.IsSuperviseur(employe.Role))
        {
            var adminRh = await _employeRepo.GetAdminOuRhAsync(ct)
                ?? throw new EmployeNotFoundException(Guid.Empty);
            validateurId = adminRh.EmployeId;
            statutInitial = StatutDemande.EnAttenteRh;
        }
        else
        {
            // Compat : premier responsable connu (ManagerId snapshot).
            validateurId = employe.ManagerId;
        }

        var (validationNodeId, validationNodeLevel) = ResolveValidationNode(employe);

        var dateFinEffective = request.TypeConge == TypeConge.Exceptionnel && request.TypeExceptionnel.HasValue
            ? request.DateDebut.AddDays(PolitiqueConge.GetDureeExceptionnelle(request.TypeExceptionnel.Value) - 1)
            : request.DateFin;

        await _regles.AssertHorsPeriodeInterditeAsync(request.DateDebut, dateFinEffective, employe, ct);
        await _regles.AssertQuotaForEmployeAsync(
            employe, request.DateDebut, dateFinEffective, null, ct);

        var chevauchement = await _demandeRepo.ExistsCongeEnChevauchementAsync(
            request.EmployeId, request.DateDebut, dateFinEffective, ct);
        if (chevauchement)
            throw new InvalidOperationException(
                "Une demande de congé existe déjà sur cette période.");

        DemandeConge demande;

        if (request.TypeConge == TypeConge.Annuel)
        {
            var solde = await _soldeRepo.GetByEmployeAndAnneeAsync(
                request.EmployeId, DateTime.Today.Year, ct)
                ?? throw new SoldeNotFoundException(request.EmployeId, DateTime.Today.Year);

            demande = DemandeConge.CreerCongeAnnuel(
                request.EmployeId,
                validateurId,
                request.DateDebut,
                request.DateFin,
                solde,
                employe,
                request.Motif,
                statutInitial,
                validationNodeId,
                validationNodeLevel);
        }
        else if (request.TypeConge == TypeConge.Exceptionnel)
        {
            if (!request.TypeExceptionnel.HasValue)
                throw new ArgumentException(
                    "Le type exceptionnel est obligatoire pour un congé exceptionnel.");

            demande = DemandeConge.CreerCongeExceptionnel(
                request.EmployeId,
                validateurId,
                request.TypeExceptionnel.Value,
                request.DateDebut,
                request.Motif,
                statutInitial,
                validationNodeId,
                validationNodeLevel);
        }
        else
        {
            throw new NotSupportedException(
                $"Type de congé '{request.TypeConge}' non supporté via cette commande.");
        }

        await _demandeRepo.AddAsync(demande, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        if (statutInitial == StatutDemande.EnAttenteRh
            && !await _workflow.IsRhValidationRequiredAsync(ct))
        {
            demande.FinaliserSansEtapeRh(employe.EmployeId, employe.NomComplet, employe.Role);
            await _finalizer.PersistAndPublishAsync(demande, employe.EmployeId, employe.NomComplet, ct);
            return demande.Id;
        }

        foreach (var domainEvent in demande.DomainEvents)
        {
            if (domainEvent is CongeDemandeEvent)
                await _eventPublisher.PublishCongeDemandeAsync(
                    demande.EmployeId,
                    demande.Id,
                    demande.ManagerId,
                    demande.DateDebut,
                    demande.DateFin,
                    ct);
        }
        demande.ClearDomainEvents();
        return demande.Id;
    }

    /// <summary>Cellule préférée, sinon service Directory / legacy Guid.</summary>
    internal static (string? NodeId, string? Level) ResolveValidationNode(EmployeSnapshot employe)
    {
        if (!string.IsNullOrWhiteSpace(employe.CelluleId))
            return (employe.CelluleId, "Cellule");
        if (!string.IsNullOrWhiteSpace(employe.OrgServiceId))
            return (employe.OrgServiceId, "Service");
        if (employe.ServiceId != Guid.Empty)
            return (employe.ServiceId.ToString(), "Service");
        if (!string.IsNullOrWhiteSpace(employe.PoleId))
            return (employe.PoleId, "Pole");
        return (null, null);
    }
}
