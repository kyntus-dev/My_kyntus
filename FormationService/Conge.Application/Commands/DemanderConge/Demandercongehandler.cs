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
    private readonly LeaveRoutingService _routing;

    public DemanderCongeHandler(
        IDemandeCongeRepository demandeRepo,
        ISoldeCongeRepository soldeRepo,
        IEmployeSnapshotRepository employeRepo,
        IUnitOfWork unitOfWork,
        ICongeEventPublisher eventPublisher,
        CongeReglesService regles,
        IEmployeSnapshotEnsure ensure,
        LeaveRoutingService routing)
    {
        _demandeRepo = demandeRepo;
        _soldeRepo = soldeRepo;
        _employeRepo = employeRepo;
        _unitOfWork = unitOfWork;
        _eventPublisher = eventPublisher;
        _regles = regles;
        _ensure = ensure;
        _routing = routing;
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

        // v9 : la demande part au superviseur de la cellule ; à défaut (congé d'un
        // responsable, cellule sans superviseur actif) elle est adressée au service RH.
        var routage = await _routing.ResolveAsync(employe, ct);
        var validateurId = routage.ValidateurId != Guid.Empty ? routage.ValidateurId : employe.ManagerId;
        var statutInitial = routage.DecisionRh ? StatutDemande.EnAttenteRh : StatutDemande.EnAttente;
        if (routage.DecisionRh && routage.ValidateurId == Guid.Empty)
            throw new EmployeNotFoundException(Guid.Empty);

        var (validationNodeId, validationNodeLevel) = (routage.NodeId, routage.NodeLevel);

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

        // Une demande adressée au service RH attend sa décision : elle n'est jamais
        // auto-validée, même quand l'étape RH du circuit standard est désactivée.
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

    /// <summary>Cellule préférée, sinon service Directory / legacy Guid (voir <see cref="LeaveRoutingService"/>).</summary>
    internal static (string? NodeId, string? Level) ResolveValidationNode(EmployeSnapshot employe)
        => LeaveRoutingService.ResolveValidationNode(employe);
}
