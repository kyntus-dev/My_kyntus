using Conge.Domain.Enums;
using System;

namespace Conge.Application.DTOs;

public record DemandeCongeDecisionDto(
    Guid Id,
    Guid ActeurId,
    string ActeurNom,
    string ActeurRole,
    string Action,
    StatutDemande StatutAvant,
    StatutDemande StatutApres,
    string? Commentaire,
    DateTime At
);

public record DemandeCongeDto(
    Guid Id,
    Guid EmployeId,
    Guid ManagerId,
    TypeConge TypeConge,
    TypeCongeExceptionnel? TypeExceptionnel,
    DateTime DateDebut,
    DateTime DateFin,
    double NombreJours,
    StatutDemande Statut,
    string? Motif,
    string? CommentaireManager,
    DateTime DateDemande,
    DateTime? DateDecision,
    string? NomEmploye,
    string? PrenomEmploye,
    string? CommentaireRh = null,
    DateTime? DateValidationSuperviseur = null,
    Guid? SuperviseurDecideurId = null,
    Guid? RhDecideurId = null,
    string? SuperviseurDecideurNom = null,
    string? RhDecideurNom = null,
    string? ValidationNodeId = null,
    IReadOnlyList<DemandeCongeDecisionDto>? Decisions = null
);

public record SoldeCongeDto(
    Guid EmployeId,
    int Annee,
    double SoldeInitial,
    double SoldeUtilise,
    double SoldeRestant,
    DateTime? DateEligibilite = null
);

public record EmployeSnapshotDto(
    Guid EmployeId,
    string Nom,
    string Prenom,
    string Email,
    Guid ManagerId,
    Guid ServiceId,
    string ServiceNom,
    DateTime DateEmbauche,
    int AncienneteAnnees,
    bool EstEligible
);

public record SuiviEquipeDto(
    Guid EmployeId,
    string NomComplet,
    string ServiceNom,
    int DemandesEnAttente,
    int DemandesValidees,
    double SoldeRestant
);

public record PeriodesInterditesDto(
    IReadOnlyList<int> Mois,
    DateTime UpdatedAt,
    string ScopeKind = "General",
    string ScopeId = "",
    bool IsOverride = false,
    string InheritedFrom = "General",
    string? ScopeLabel = null);

public record PeriodesInterditesScopesDto(
    PeriodesInterditesDto General,
    IReadOnlyList<PeriodesInterditesDto> Overrides);

public record QuotaCongeServiceDto(
    string ServiceId,
    string ServiceNom,
    int? MaxAbsentsSimultanes,
    int Effectif,
    string ScopeKind = "Service");

public record CongeDisponibiliteDto(
    bool Ok,
    string? Motif,
    IReadOnlyList<int> MoisInterdits,
    IReadOnlyList<string> JoursSatures);
