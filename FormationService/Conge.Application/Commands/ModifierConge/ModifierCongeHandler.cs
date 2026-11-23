using Conge.Application.Contracts;
using Conge.Application.Services;
using Conge.Domain.Entities;
using Conge.Domain.Enums;
using Conge.Domain.Exceptions;
using Conge.Domain.Interfaces;
using Kyntus.Messaging.Contracts;
using MediatR;

namespace Conge.Application.Commands.ModifierConge;

/// <summary>
/// v64.2 — Modifie les dates d'une demande de congé, validée comprise, avec les mêmes contrôles qu'une
/// nouvelle demande : mois interdits, quota de la cellule, chevauchement avec une autre demande
/// (celle-ci exclue), ancienneté pour un congé annuel, solde.
/// Congé validé : le solde est ajusté de l'écart (restitué puis débité dans une seule transaction)
/// et le planning est mis à jour (ancienne période rendue, nouvelle posée) ; l'employé est notifié.
/// L'opération est inscrite dans l'historique de la demande et dans le journal d'actions.
/// </summary>
public sealed class ModifierCongeHandler(
    IDemandeCongeRepository demandeRepo,
    ISoldeCongeRepository soldeRepo,
    IEmployeSnapshotRepository employeRepo,
    IUnitOfWork unitOfWork,
    ICongeEventPublisher publisher,
    CongeReglesService regles,
    CongeValidationFinalizer finalizer) : IRequestHandler<ModifierCongeCommand, ModifierCongeResult>
{
    public async Task<ModifierCongeResult> Handle(ModifierCongeCommand request, CancellationToken ct)
    {
        var demande = await demandeRepo.GetByIdAsync(request.DemandeId, ct)
            ?? throw new CongeNotFoundException(request.DemandeId);

        var employe = await employeRepo.GetByEmployeIdAsync(demande.EmployeId, ct);
        var acteur = await employeRepo.GetByEmployeIdAsync(request.ActeurId, ct);
        var acteurRole = NormaliserRole(request.ActeurRole ?? acteur?.Role);
        var statut = demande.Statut;

        var ancien = demande.Modifier(
            request.DateDebut,
            request.DateFin,
            request.ActeurId,
            acteur?.NomComplet,
            acteurRole,
            request.Commentaire);

        // Contrôles sur la nouvelle période (rien n'est enregistré si l'un d'eux échoue).
        if (demande.TypeConge == TypeConge.Annuel
            && demande.DateDebut.Date != ancien.AncienDebut.Date
            && employe is not null
            && !employe.EstEligibleCongeAnnuel(demande.DateDebut))
        {
            throw new EligibiliteException(
                demande.EmployeId,
                $"Le congé annuel ne peut commencer qu'à partir du {employe.DateEligibiliteCongeAnnuel():dd/MM/yyyy} (6 mois d'ancienneté).");
        }

        await regles.AssertHorsPeriodeInterditeAsync(demande.DateDebut, demande.DateFin, employe, ct);
        if (employe is not null)
            await regles.AssertQuotaForEmployeAsync(employe, demande.DateDebut, demande.DateFin, demande.Id, ct);

        var autres = await demandeRepo.GetByEmployeIdAsync(demande.EmployeId, ct);
        if (ChevaucheAutreDemande(autres, demande))
            throw new InvalidOperationException("Une autre demande de congé existe déjà sur cette période.");

        if (demande.TypeConge == TypeConge.Annuel)
        {
            if (statut == StatutDemande.Validee)
                await AjusterSoldeAsync(demande, ancien.AncienDebut, ancien.AncienNombreJours, ct);
            else if (demande.NombreJours > ancien.AncienNombreJours)
            {
                // Demande non validée : le solde n'est débité qu'à la validation, mais la demande
                // ne doit pas dépasser ce qui reste (même règle qu'à la création).
                var solde = await soldeRepo.GetByEmployeAndAnneeAsync(demande.EmployeId, DateTime.Today.Year, ct);
                if (solde is not null && !solde.ASufficament(demande.NombreJours))
                    throw new SoldeInsuffisantException(demande.EmployeId, demande.NombreJours, solde.SoldeRestant);
            }
        }

        demandeRepo.Update(demande);
        await unitOfWork.SaveChangesAsync(ct);

        if (statut == StatutDemande.Validee)
        {
            await publisher.PublishCongeModifieAsync(
                demande.EmployeId,
                demande.Id,
                demande.DateDebut,
                demande.DateFin,
                demande.NombreJours,
                demande.TypeConge.ToString(),
                demande.TypeExceptionnel?.ToString(),
                ancien.AncienDebut,
                ancien.AncienneFin,
                request.ActeurId,
                acteur?.NomComplet,
                ct);
        }

        await PublierJournalAsync(request, demande, employe, ancien, acteurRole, ct);

        return new ModifierCongeResult(
            demande.Id, demande.DateDebut, demande.DateFin, demande.NombreJours, ancien.AncienNombreJours);
    }

    internal static bool ChevaucheAutreDemande(IEnumerable<DemandeConge> demandes, DemandeConge demande)
        => demandes.Any(d =>
            d.Id != demande.Id
            && d.Statut is not (StatutDemande.Refusee or StatutDemande.Annulee)
            && d.DateDebut.Date <= demande.DateFin.Date
            && d.DateFin.Date >= demande.DateDebut.Date);

    /// <summary>
    /// Le congé validé avait débité le solde de l'année du congé (à défaut, l'année courante) :
    /// l'ancienne durée y est restituée, la nouvelle est débitée sur le solde de la nouvelle période.
    /// Même solde : seul l'écart est appliqué. Solde insuffisant → refus, rien n'est enregistré.
    /// </summary>
    private async Task AjusterSoldeAsync(DemandeConge demande, DateTime ancienDebut, double ancienJours, CancellationToken ct)
    {
        var anneeCourante = DateTime.Today.Year;
        var ancienSolde = await soldeRepo.GetByEmployeAndAnneeAsync(demande.EmployeId, ancienDebut.Year, ct);
        if (ancienSolde is null && ancienDebut.Year != anneeCourante)
            ancienSolde = await soldeRepo.GetByEmployeAndAnneeAsync(demande.EmployeId, anneeCourante, ct);

        var nouveauSolde = await finalizer.ResolveSoldeForDeductionAsync(demande, ct);

        if (ancienSolde is null || ReferenceEquals(ancienSolde, nouveauSolde) || ancienSolde.Id == nouveauSolde.Id)
        {
            var ecart = demande.NombreJours - ancienJours;
            if (ecart > 0)
                nouveauSolde.DeduireSolde(ecart);
            else if (ecart < 0)
                nouveauSolde.RestituerSolde(-ecart);
            soldeRepo.Update(nouveauSolde);
            return;
        }

        ancienSolde.RestituerSolde(ancienJours);
        soldeRepo.Update(ancienSolde);
        nouveauSolde.DeduireSolde(demande.NombreJours);
        soldeRepo.Update(nouveauSolde);
    }

    private async Task PublierJournalAsync(
        ModifierCongeCommand request,
        DemandeConge demande,
        EmployeSnapshot? employe,
        (DateTime AncienDebut, DateTime AncienneFin, double AncienNombreJours) ancien,
        string? acteurRole,
        CancellationToken ct)
    {
        try
        {
            var nom = employe?.NomComplet;
            await publisher.PublishJournalAsync(new JournalActionMessage
            {
                ActeurGuid = request.ActeurId,
                ActeurEmail = request.ActeurEmail,
                ActeurRole = acteurRole,
                Module = "Congés et arrêts maladie",
                Operation = "Modification de la demande de congé",
                Entite = "DemandeConge",
                EntiteId = demande.Id.ToString(),
                Element = string.IsNullOrWhiteSpace(nom) ? "Demande de congé" : $"Demande de congé — {nom}",
                AnciennesValeurs = new Dictionary<string, string?>
                {
                    ["Date de début"] = ancien.AncienDebut.ToString("dd/MM/yyyy"),
                    ["Date de fin"] = ancien.AncienneFin.ToString("dd/MM/yyyy"),
                    ["Nombre de jours"] = ancien.AncienNombreJours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                NouvellesValeurs = new Dictionary<string, string?>
                {
                    ["Date de début"] = demande.DateDebut.ToString("dd/MM/yyyy"),
                    ["Date de fin"] = demande.DateFin.ToString("dd/MM/yyyy"),
                    ["Nombre de jours"] = demande.NombreJours.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                Details = string.IsNullOrWhiteSpace(request.Commentaire) ? null : request.Commentaire.Trim(),
                Source = "Congés",
            }, ct);
        }
        catch
        {
            // Le journal ne doit jamais faire échouer une modification déjà enregistrée.
        }
    }

    /// <summary>Le rôle Super Admin n'apparaît jamais : il est présenté comme « Admin ».</summary>
    internal static string? NormaliserRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return null;
        var r = role.Trim();
        var compact = r.Replace(" ", "").Replace("_", "").Replace("-", "");
        return compact.Contains("superadmin", StringComparison.OrdinalIgnoreCase) ? "Admin" : r;
    }
}
