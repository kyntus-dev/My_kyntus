namespace Kyntus.Iam;

public sealed record PolicyRequest(
    Guid SubjectId,
    string Role,
    string Action,
    string ResourceType,
    string? ResourceId = null,
    IReadOnlyDictionary<string, string>? Context = null);

public sealed record PolicyDecision(bool Allowed, string? Reason = null);

public interface IPolicyEvaluator
{
    Task<PolicyDecision> EvaluateAsync(PolicyRequest request, CancellationToken ct = default);
}

public interface IRebacClient
{
    Task<bool> IsDescendantAsync(Guid viewerId, Guid targetId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetManagedNodeIdsAsync(Guid employeeId, string kind, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(Guid actorId, CancellationToken ct = default);
    Task<bool> CanActOnAsync(Guid actorId, Guid targetEmployeeId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetResponsibleIdsAsync(string kind, string nodeId, CancellationToken ct = default);
}

public interface IPermissionCatalog
{
    Task<bool> RoleHasActionAsync(string role, string action, string scope, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetEffectivePermissionKeysAsync(string role, CancellationToken ct = default);
}

/// <summary>
/// Exceptions de droits par compte (v19), au-dessus des droits du rôle.
/// null : pas d'exception ; true : action accordée au compte ; false : action retirée au compte.
/// </summary>
public interface ISubjectPermissionOverrides
{
    Task<bool?> GetOverrideAsync(Guid subjectId, string action, CancellationToken ct = default);
}
