using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Application.Org;
using EmployeeDirectory.Domain.Enums;

namespace EmployeeDirectory.Application.Abstractions;

public interface IDirectoryReadService
{
    Task<IReadOnlyList<EmployeeDto>> GetEmployeesAsync(string? role, string? poleId, CancellationToken ct = default);
    /// <summary>Liste paginée des employés actifs (évite de charger toute la table sous charge).</summary>
    Task<(IReadOnlyList<EmployeeDto> Items, int Total)> GetEmployeesPagedAsync(
        string? role, string? poleId, int skip, int take, CancellationToken ct = default);
    Task<EmployeeDto?> GetEmployeeByIdAsync(Guid id, CancellationToken ct = default);
    Task<EmployeeDto?> GetEmployeeByEmailAsync(string email, CancellationToken ct = default) =>
        Task.FromResult<EmployeeDto?>(null);
    Task<OrgOverviewDto> GetOrgOverviewAsync(CancellationToken ct = default);
    Task<OrgAssignmentAsOfDto> GetAssignmentsAsOfAsync(DateTime asOf, CancellationToken ct = default);
    Task<IReadOnlyList<AssignmentHistoryEntryDto>> GetAssignmentHistoryAsync(Guid employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetEmployeeIdsWithAssignmentsAfterAsync(
        IReadOnlyList<Guid> employeeIds,
        DateTime after,
        CancellationToken ct = default);
    Task<bool> IsDescendantAsync(Guid viewerId, Guid targetId, CancellationToken ct = default);
    Task<RebacSubtreeDto> GetSubtreeAsync(Guid employeeId, CancellationToken ct = default);
    Task<RebacManagedNodesDto> GetManagedNodesAsync(Guid employeeId, string kind, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessDepartmentDto>> GetBusinessDepartmentsAsync(CancellationToken ct = default);
    Task<BusinessDepartmentDto?> GetBusinessDepartmentByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> IsEmailUsedAsync(string email, Guid? excludeEmployeeId = null, CancellationToken ct = default);
}

/// <summary>Résolution multi-responsables depuis OrgAssignment (source d'autorité).</summary>
public interface IOrgResponsibilityResolver
{
    Task<IReadOnlyList<ResponsibleEmployeeDto>> GetResponsiblesAsync(string kind, string nodeId, CancellationToken ct = default);
    Task<IReadOnlyList<ManagedNodeDto>> GetManagedNodesDetailedAsync(Guid employeeId, string? kind = null, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(Guid actorId, CancellationToken ct = default);
    Task<bool> CanActOnAsync(Guid actorId, Guid targetEmployeeId, CancellationToken ct = default);
    Task<bool> CanActOnNodeAsync(Guid actorId, string kind, string nodeId, CancellationToken ct = default);
}

public interface IDirectoryWriteService
{
    Task<EmployeeDto> CreateEmployeeAsync(CreateEmployeeRequest request, Guid? changedBy, CancellationToken ct = default);
    Task<EmployeeDto?> UpdateEmployeeAsync(Guid id, UpdateEmployeeRequest request, Guid? changedBy, CancellationToken ct = default);
    Task<EmployeeDto?> ClearOrgPlacementAsync(Guid id, ClearOrgPlacementRequest request, Guid? changedBy, CancellationToken ct = default);
    Task<bool> DeleteEmployeeAsync(Guid id, Guid? changedBy, CancellationToken ct = default);
    Task<StructuralRoleAssignmentResult> AssignStructureRoleAsync(
        string kind,
        string nodeId,
        Guid employeeId,
        Guid? changedBy,
        string? reason,
        IReadOnlyList<Guid>? revokeEmployeeIds = null,
        bool forceTenureOverride = false,
        CancellationToken ct = default);

    /// <summary>
    /// Synchronise exactement les nœuds managés d'un employé pour un kind structurel
    /// (ChefDeProjet / Superviseur / ReferentTechnique) dans une seule transaction.
    /// </summary>
    Task<StructuralAssignmentsReconcileResult> ReconcileEmployeeStructuralAssignmentsAsync(
        string kind,
        Guid employeeId,
        IReadOnlyList<string> nodeIds,
        string primaryNodeId,
        Guid? changedBy,
        string? reason,
        CancellationToken ct = default);

    Task ClearStructureRoleAsync(string kind, string nodeId, Guid? changedBy, string? reason, CancellationToken ct = default);
    Task<bool> RemoveStructurePilotAsync(string serviceId, Guid employeeId, Guid? changedBy, string? reason, CancellationToken ct = default);
    Task<bool> RemoveStructureAssignmentAsync(string kind, string nodeId, Guid employeeId, Guid? changedBy, string? reason, CancellationToken ct = default);

    /// <summary>
    /// Clôture les doublons actifs (Kind, NodeId) en gardant le titulaire le plus récent.
    /// Retourne le nombre de lignes clôturées.
    /// </summary>
    Task<int> DeduplicateActiveNodeIncumbentsAsync(Guid? changedBy = null, CancellationToken ct = default);

    /// <summary>
    /// Annule les affectations org créées/fermées dans une fenêtre (un import),
    /// sans toucher aux rotations postérieures ni aux nœuds (pôles/cellules/services).
    /// </summary>
    Task<UndoImportAssignmentWindowResult> UndoImportAssignmentWindowAsync(
        UndoImportAssignmentWindowRequest request,
        Guid? changedBy,
        CancellationToken ct = default);

    Task<string> CreatePoleAsync(string name, Guid businessDepartmentId, CancellationToken ct = default);
    Task<bool> AttachPoleToBusinessDepartmentAsync(string poleId, Guid businessDepartmentId, CancellationToken ct = default);
    Task<string> CreateCelluleAsync(string poleId, string name, CancellationToken ct = default);
    Task<string> CreateServiceAsync(string celluleId, string name, CancellationToken ct = default);
    Task<bool> RenameOrgNodeAsync(OrgNodeLevel level, string nodeId, string name, CancellationToken ct = default);
    Task<bool> DeleteOrgNodeAsync(OrgNodeLevel level, string nodeId, CancellationToken ct = default);
    Task<bool> SetAuthSubjectIdAsync(Guid employeeId, Guid authSubjectId, CancellationToken ct = default);
    Task<BusinessDepartmentDto> CreateBusinessDepartmentAsync(CreateBusinessDepartmentRequest request, CancellationToken ct = default);
    Task<BusinessDepartmentDto?> UpdateBusinessDepartmentAsync(Guid id, UpdateBusinessDepartmentRequest request, CancellationToken ct = default);
    Task<bool> DeleteBusinessDepartmentAsync(Guid id, CancellationToken ct = default);
    Task<bool> AssignPoleToBusinessDepartmentAsync(Guid departmentId, string poleId, CancellationToken ct = default);
    Task<bool> RemovePoleFromBusinessDepartmentAsync(Guid departmentId, string poleId, CancellationToken ct = default);
    Task<StructuralRoleAssignmentResult> SetBusinessDepartmentManagerAsync(Guid departmentId, Guid employeeId, Guid? changedBy = null, string? reason = null, CancellationToken ct = default);
    Task<bool> ClearBusinessDepartmentManagerAsync(Guid departmentId, CancellationToken ct = default);
}

public interface IDirectoryReconciliationService
{
    Task<DirectoryReconcileVerifyDto> VerifyAsync(CancellationToken ct = default);
    Task<DirectoryReconcileReportDto> ReconcileAsync(CancellationToken ct = default);
}

public interface IIamReadService
{
    Task<EffectivePermissionsDto> GetEffectivePermissionsAsync(Guid subjectId, string role, CancellationToken ct = default);
    Task<bool> EvaluateAsync(Guid subjectId, string role, string action, string resourceType, string? resourceId, CancellationToken ct = default);
}

public interface IIamAdminService
{
    Task<IamCatalogDto> GetCatalogAsync(CancellationToken ct = default);
    Task<IReadOnlyList<IamPermissionRowDto>> ListAsync(string? role, CancellationToken ct = default);
    Task<IamMatrixDto> GetMatrixAsync(string role, string? scope = null, CancellationToken ct = default);
    Task<IamPermissionRowDto> UpsertAsync(UpsertIamPermissionRequest request, CancellationToken ct = default);
    Task<int> UpsertBulkAsync(IReadOnlyList<UpsertIamPermissionRequest> items, CancellationToken ct = default);
    Task SeedDefaultsIfEmptyAsync(CancellationToken ct = default);
}
