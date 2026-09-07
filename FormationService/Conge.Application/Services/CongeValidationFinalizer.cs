using Conge.Application.Abstractions;
using Conge.Application.Contracts;
using Conge.Domain.Entities;
using Conge.Domain.Enums;
using Conge.Domain.Interfaces;

namespace Conge.Application.Services;

public sealed class CongeValidationFinalizer(
    IDemandeCongeRepository demandeRepo,
    ISoldeCongeRepository soldeRepo,
    IEmployeSnapshotRepository employeRepo,
    IUnitOfWork unitOfWork,
    ICongeEventPublisher publisher,
    ILeaveWorkflowPolicy workflow)
{
    public async Task<bool> CollapsePendingRhIfDisabledAsync(CancellationToken ct)
    {
        if (await workflow.IsRhValidationRequiredAsync(ct))
            return true;

        var pending = (await demandeRepo.GetByStatutAsync(StatutDemande.EnAttenteRh, ct)).ToList();
        foreach (var demande in pending)
        {
            try
            {
                var validateurId = demande.SuperviseurDecideurId ?? demande.EmployeId;
                var acteur = await employeRepo.GetByEmployeIdAsync(validateurId, ct);
                demande.FinaliserSansEtapeRh(
                    validateurId,
                    acteur?.NomComplet,
                    acteur?.Role ?? "Superviseur");
                await PersistAndPublishAsync(demande, validateurId, acteur?.NomComplet, ct);
            }
            catch
            {
                // Une demande (ex. solde année future) ne doit pas 404 toute la file RH.
            }
        }

        return false;
    }

    public async Task PersistAndPublishAsync(
        DemandeConge demande,
        Guid validateurId,
        string? validateurNom,
        CancellationToken ct)
    {
        if (demande.TypeConge == TypeConge.Annuel)
        {
            var solde = await ResolveSoldeForDeductionAsync(demande, ct);
            solde.DeduireSolde(demande.NombreJours);
            soldeRepo.Update(solde);
        }

        demandeRepo.Update(demande);
        await unitOfWork.SaveChangesAsync(ct);

        await publisher.PublishCongeValideAsync(
            demande.EmployeId,
            demande.Id,
            demande.DateDebut,
            demande.DateFin,
            demande.NombreJours,
            demande.TypeConge.ToString(),
            demande.TypeExceptionnel?.ToString(),
            validateurId,
            validateurNom,
            ct);

        demande.ClearDomainEvents();
    }

    /// <summary>
    /// La demande réserve le solde de l'année civile courante, même si le congé
    /// commence l'année suivante. On débite d'abord l'année du congé, sinon
    /// l'année courante, sinon on initialise l'année du congé.
    /// </summary>
    internal async Task<SoldeConge> ResolveSoldeForDeductionAsync(
        DemandeConge demande,
        CancellationToken ct)
    {
        var leaveYear = demande.DateDebut.Year;
        var currentYear = DateTime.Today.Year;

        var solde = await soldeRepo.GetByEmployeAndAnneeAsync(demande.EmployeId, leaveYear, ct);
        if (solde is not null)
            return solde;

        if (leaveYear != currentYear)
        {
            solde = await soldeRepo.GetByEmployeAndAnneeAsync(demande.EmployeId, currentYear, ct);
            if (solde is not null)
                return solde;
        }

        var employe = await employeRepo.GetByEmployeIdAsync(demande.EmployeId, ct);
        var jours = PolitiqueConge.CalculerSoldeAnnuel(
            employe?.GetAncienneteAnnees() ?? 0,
            employe?.EstMineur ?? false);
        solde = SoldeConge.Initialiser(demande.EmployeId, jours, leaveYear);
        await soldeRepo.AddAsync(solde, ct);
        return solde;
    }
}
