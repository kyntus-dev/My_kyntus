namespace Conge.Application.Abstractions;

/// <summary>
/// Workflow congés dérivé de la matrice IAM (rôle RH / action conges.validation.Validate).
/// </summary>
public interface ILeaveWorkflowPolicy
{
    Task<bool> IsRhValidationRequiredAsync(CancellationToken ct = default);
}
