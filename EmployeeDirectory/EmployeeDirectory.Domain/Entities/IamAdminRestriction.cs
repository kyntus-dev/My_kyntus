namespace EmployeeDirectory.Domain.Entities;

/// <summary>
/// Restriction posée par le Super Admin sur un administrateur (v60). Un administrateur a tous les
/// droits ; une ligne retire (IsAllowed = false) ou rend (IsAllowed = true) une fonctionnalité
/// « écran.Verbe ». SubjectId = Guid.Empty : s'applique à tous les administrateurs ; la ligne propre
/// au compte l'emporte. Table nouvelle, vide au départ : aucun administrateur n'est restreint.
/// </summary>
public class IamAdminRestriction
{
    public Guid Id { get; set; }
    public Guid SubjectId { get; set; }
    public string Action { get; set; } = string.Empty;
    public bool IsAllowed { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? UpdatedBy { get; set; }
}
