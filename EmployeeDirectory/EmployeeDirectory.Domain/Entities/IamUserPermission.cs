namespace EmployeeDirectory.Domain.Entities;

/// <summary>
/// Exception de droit pour un compte (v19) : le compte hérite des droits de son rôle, sauf les
/// actions accordées (IsAllowed = true) ou retirées (IsAllowed = false) ici.
/// Action = « écran.Verbe » (ex. « presences.View »).
/// </summary>
public class IamUserPermission
{
    public Guid Id { get; set; }
    public Guid SubjectId { get; set; }
    public string Action { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}
