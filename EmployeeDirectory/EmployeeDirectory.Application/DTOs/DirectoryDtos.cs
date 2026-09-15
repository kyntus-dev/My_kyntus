namespace EmployeeDirectory.Application.Dtos;

public record EmployeeDto(
    string Id,
    string FirstName,
    string LastName,
    string Role,
    string? ParentId,
    string? ServiceId,
    string PoleId,
    string? CelluleId,
    string Email,
    string? Avatar,
    string? BusinessDepartmentId = null,
    string? BusinessDepartmentKind = null,
    string? ChefDeProjetId = null,
    string? SuperviseurId = null,
    string? ReferentTechniqueId = null,
    EmployeeHrProfileDto? HrProfile = null,
    int? IdTechnicien = null,
    string? HtelCode = null);

public record EmployeeHrProfileDto(
    DateOnly? DateNaissance,
    string? VilleNaissance,
    string? Nationalite,
    string? NumeroCarteAutoentrepreneur,
    string? Sexe,
    string? SituationFamiliale,
    int? NombreEnfants,
    string? Cin,
    string? Adresse,
    string? EmailPersonnel,
    string? Telephone1,
    string? TelephoneUrgence,
    string? RelationUrgence,
    string? Rib,
    string? ImmatriculationInterne,
    string? ImmatriculationCnss,
    DateOnly? DateEntree,
    DateOnly? DateEmbauche,
    DateOnly? DateAnciennete,
    DateOnly? DateSortie,
    DateOnly? DateEvolutionPoste,
    string? AncienPoste,
    string? AncienService,
    string? NiveauScolaire,
    string? IntitulesEtudes,
    bool EnFormation,
    DateOnly? DateDebutFormation,
    DateOnly? DateFinFormationPrevue,
    int? NiveauExpertiseMetier);

public record BusinessDepartmentDto(
    string Id,
    string Code,
    string Name,
    string Kind,
    string? ManagerEmployeeId,
    bool IsActive,
    IReadOnlyList<string> PoleIds);

public record CreateBusinessDepartmentRequest(string? Code, string Name, string Kind);
public record UpdateBusinessDepartmentRequest(string Name, string Kind, bool IsActive);
public record SetBusinessDepartmentManagerRequest(string EmployeeId);

public record OrgOverviewDto(
    IReadOnlyList<EtageNodeDto> Etages,
    IReadOnlyList<ServiceNodeDto> Services,
    IReadOnlyList<SousServiceNodeDto> SousServices,
    IReadOnlyList<EmployeeDto> Employees,
    IReadOnlyList<DepartmentDto> Departments,
    IReadOnlyList<BusinessDepartmentDto> BusinessDepartments,
    IReadOnlyList<OperationalDepartmentOverviewDto> OperationalDepartments,
    IReadOnlyList<OrgPoleOverviewDto> UnassignedPoles,
    IReadOnlyList<ManagerEtageAssignmentDto> ManagerEtage,
    IReadOnlyList<SupervisorServiceAssignmentDto> SupervisorService,
    IReadOnlyList<CoachSousServiceAssignmentDto> CoachSousService,
    IReadOnlyList<CoachPilotLinkDto> CoachPilot);

public record OperationalDepartmentOverviewDto(
    string Id,
    string Code,
    string Name,
    string? ManagerEmployeeId,
    IReadOnlyList<OrgPoleOverviewDto> Poles);

public record OrgPoleOverviewDto(
    string Id,
    string Name,
    IReadOnlyList<OrgCelluleOverviewDto> Cellules);

public record OrgCelluleOverviewDto(
    string Id,
    string Name,
    IReadOnlyList<OrgServiceOverviewDto> Services);

public record OrgServiceOverviewDto(string Id, string Name);

public record CreatePoleRequest(string Name, Guid BusinessDepartmentId);
public record AttachPoleToDepartmentRequest(Guid BusinessDepartmentId);

public record EtageNodeDto(string Id, string Name);
public record ServiceNodeDto(string Id, string Name, string EtageId);
public record SousServiceNodeDto(string Id, string Name, string ServiceId);
public record DepartmentDto(string Id, string Name, IReadOnlyList<PoleDto> Poles);
public record PoleDto(string Id, string Name, string PoleId, IReadOnlyList<CelluleDto> Cells);
public record CelluleDto(string Id, string Name, string CelluleId, IReadOnlyList<TeamDto> Services);
public record TeamDto(string Id, string Name, string ServiceId);

public record ManagerEtageAssignmentDto(string Id, string UserId, string EtageId);
public record SupervisorServiceAssignmentDto(string Id, string UserId, string CelluleId, string ServiceId);
public record CoachSousServiceAssignmentDto(string Id, string UserId, string ServiceId, string SousServiceId);
public record CoachPilotLinkDto(string Id, string CoachUserId, string PilotUserId);

public record RevokedStructuralRoleDto(string Role, string NodeId, string? NodeLabel, string? DepartmentCode);

public record NodeIncumbentRevokedDto(string EmployeeId, string Kind, string NodeId);

public record StructuralRoleAssignmentResult(
    IReadOnlyList<RevokedStructuralRoleDto> Revoked,
    IReadOnlyList<NodeIncumbentRevokedDto> RevokedOnNode,
    string? AddedEmployeeId);

/// <summary>Résultat d'une synchronisation exacte multi-nœuds pour un employé/kind.</summary>
public record StructuralAssignmentsReconcileResult(
    string Kind,
    string EmployeeId,
    IReadOnlyList<string> NodeIds,
    string PrimaryNodeId,
    IReadOnlyList<string> AddedNodeIds,
    IReadOnlyList<string> RemovedNodeIds,
    IReadOnlyList<RevokedStructuralRoleDto> RevokedOtherKinds,
    IReadOnlyList<NodeIncumbentRevokedDto> RevokedOnNode);

public record CreateEmployeeRequest(
    Guid? EmployeeId,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? ServiceId,
    Guid? ParentId,
    DateTime? HireDate,
    Guid? BusinessDepartmentId = null,
    Guid? ChefDeProjetId = null,
    Guid? SuperviseurId = null,
    Guid? ReferentTechniqueId = null,
    EmployeeHrProfileDto? HrProfile = null,
    int? IdTechnicien = null);

public record BulkCreateEmployeesRequest(IReadOnlyList<CreateEmployeeRequest> Items);

public record BulkCreateEmployeeResult(string Email, bool Success, Guid? EmployeeId, string? Error);

public record UpdateEmployeeRequest(
    string FirstName,
    string LastName,
    string Email,
    string Role,
    string? ServiceId,
    Guid? ParentId,
    bool IsActive,
    DateTime? HireDate,
    Guid? BusinessDepartmentId = null,
    Guid? ChefDeProjetId = null,
    Guid? SuperviseurId = null,
    Guid? ReferentTechniqueId = null,
    EmployeeHrProfileDto? HrProfile = null,
    int? IdTechnicien = null);

/// <summary>
/// Détache un employé d'un périmètre organisationnel (pole / cellule / service).
/// Level : service | cellule | pole | all.
/// </summary>
public record ClearOrgPlacementRequest(string? Level = null, string? NodeId = null);

public record OrgAssignmentAsOfDto(
    DateTime AsOf,
    IReadOnlyList<ActiveAssignmentDto> Assignments);

public record ActiveAssignmentDto(
    string Kind,
    string NodeId,
    string NodeLevel,
    string EmployeeId,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo);

public record AssignmentHistoryEntryDto(
    string Kind,
    string NodeId,
    string? PreviousEmployeeId,
    string? NewEmployeeId,
    DateTime ChangedAt,
    string? ChangeReason);

public record PilotRotationHistoryEntryDto(
    string ServiceId,
    string ServiceName,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo,
    int? DurationDays,
    string? ChangeReason,
    bool IsOverride);

public record PilotRotationSummaryDto(
    Guid EmployeeId,
    string FirstName,
    string LastName,
    string Email,
    int RotationCount,
    string? CurrentServiceId,
    string? CurrentServiceName,
    DateTime? FirstEffectiveFrom,
    DateTime? LastEffectiveFrom,
    IReadOnlyList<PilotRotationHistoryEntryDto> Segments);

public record PilotRotationEligibilityDto(
    bool Eligible,
    bool IsSameService,
    string? CurrentServiceId,
    string? CurrentServiceName,
    DateTime? CurrentSince,
    DateTime? EligibleAt,
    int DaysRemaining);

public record RebacSubtreeDto(string EmployeeId, IReadOnlyList<string> DescendantIds);
public record RebacManagedNodesDto(string EmployeeId, string Kind, IReadOnlyList<string> NodeIds);

public record ResponsibleEmployeeDto(
    string EmployeeId,
    string FirstName,
    string LastName,
    string Email,
    string Kind,
    string NodeId);

public record ManagedNodeDto(
    string Kind,
    string NodeId,
    string NodeLevel,
    string? Label);

public record CanActOnResultDto(bool Allowed, string ActorId, string TargetEmployeeId);

public record EffectivePermissionsDto(
    string SubjectId,
    string Role,
    IReadOnlyList<string> Permissions);

public record IamScreenDto(string Id, string Label, string Module, IReadOnlyList<string> Verbs);

public record IamCatalogDto(
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Verbs,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<IamScreenDto> Screens);

public record IamPermissionRowDto(Guid Id, string Role, string Action, string Scope, bool IsAllowed);

public record UpsertIamPermissionRequest(string Role, string Action, string Scope, bool IsAllowed);

public record UpsertIamPermissionsBulkRequest(IReadOnlyList<UpsertIamPermissionRequest> Items);

public record IamMatrixRowDto(
    string Capability,
    string Label,
    string Module,
    string Scope,
    IReadOnlyDictionary<string, bool> Verbs);

public record IamMatrixDto(string Role, string PrimaryScope, IReadOnlyList<IamMatrixRowDto> Rows);

public record LeaveWorkflowDto(bool RhValidationRequired);

public record DirectoryReconcileVerifyDto(
    int DirectoryActiveEmployees,
    int DirectoryInactiveEmployees,
    int OrgPoles,
    int OrgCellules,
    int OrgServices,
    int EmployeesWithUnmappedOrgRefs,
    int? PlanningActiveUsers,
    int? PrimeEmployeeCount,
    int? OrphansInPlanningNotDirectory,
    int? OrphansInPrimeNotDirectory,
    bool Ok);

public record DirectoryReconcileReportDto(
    int MergedDuplicates,
    int OrgNodesBackfilled,
    int EmployeesImportedFromPrime,
    int EmployeesRepublished,
    int OrphansPlanning,
    int OrphansPrime,
    int OrgGapsFixed,
    DirectoryReconcileVerifyDto Verify,
    int DuplicateNodeIncumbentsClosed = 0);

// ─── HTEL techniciens (source de vérité externe) ───

public record HtelTechnicienDto(int IdTechnicien, string Technicien, int Actif, string Code);

public record HtelLinkedEmployeeDto(
    string EmployeeId,
    string FirstName,
    string LastName,
    string Email,
    int IdTechnicien,
    string? HtelCode,
    string? HtelTechnicienName)
{
    public bool IsActive { get; init; }
    public bool ExistsInHtel { get; init; }
    public int? HtelActif { get; init; }
    public string? SourceCode { get; init; }
    public bool IdentityNeedsReview { get; init; }
    public bool MirrorNeedsSync { get; init; }
}

public record HtelOrphanTechnicienDto(int IdTechnicien, string Technicien, int Actif, string Code);

public record HtelAmbiguousMatchDto(
    int IdTechnicien,
    string Technicien,
    string Code,
    IReadOnlyList<HtelEmployeeCandidateDto> Candidates)
{
    public int Actif { get; init; }
    public string? LinkedEmployeeId { get; init; }
}

public record HtelEmployeeCandidateDto(
    string EmployeeId,
    string FirstName,
    string LastName,
    string Email)
{
    public bool IsActive { get; init; }
    public int? IdTechnicien { get; init; }
}

public record HtelUnlinkedEmployeeDto(
    string EmployeeId,
    string FirstName,
    string LastName,
    string Email)
{
    public bool IsActive { get; init; }
}

public record HtelLiaisonsReportDto(
    IReadOnlyList<HtelLinkedEmployeeDto> Linked,
    IReadOnlyList<HtelOrphanTechnicienDto> OrphansHtel,
    IReadOnlyList<HtelAmbiguousMatchDto> Ambiguous,
    IReadOnlyList<HtelUnlinkedEmployeeDto> UnlinkedEmployees)
{
    public DateTime CheckedAtUtc { get; init; }
    public IReadOnlyList<HtelMissingEmployeeDto> MissingFromHtel { get; init; } = [];
}

public record HtelMissingEmployeeDto(
    string EmployeeId, string FirstName, string LastName, string Email,
    bool IsActive, int? IdTechnicien, string Reason);

// Les valeurs attendues empêchent d'appliquer une comparaison devenue obsolète.
public record HtelIdentityReviewRequest(
    Guid EmployeeId, int IdTechnicien,
    string ExpectedFirstName, string ExpectedLastName, string ExpectedTechnicien,
    string FirstName, string LastName);

public record HtelSyncReportDto(
    int TechniciensFetched,
    int LinkedUpdated,
    int NewlyLinked,
    int OrphansHtel,
    int Ambiguous,
    int UnlinkedEmployees);

public record HtelLinkRequest(Guid EmployeeId, int IdTechnicien);
public record HtelUnlinkRequest(Guid EmployeeId);
