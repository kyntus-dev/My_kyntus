namespace EmployeeDirectory.Application.Dtos;

/// <summary>
/// Niveau de périmètre métier : activité (département de production), pôle ou cellule.
/// « Mixte » quand un même responsable cumule des périmètres de niveaux différents.
/// </summary>
public static class OrgScopeLevels
{
    public const string Activite = "Activite";
    public const string Pole = "Pole";
    public const string Cellule = "Cellule";
    public const string Mixte = "Mixte";
}

public record OrgScopeReconcileResult(
    string Kind,
    string EmployeeId,
    string ScopeLevel,
    IReadOnlyList<string> ScopeIds,
    string PrimaryScopeId,
    StructuralAssignmentsReconcileResult Assignments);

/// <summary>
/// Ligne du rapport de couverture : pour un responsable et un périmètre (activité ou cellule),
/// combien de nœuds il détient sur le total du périmètre.
/// </summary>
public record OrgScopeCoverageRowDto(
    string EmployeeId,
    string EmployeeName,
    string EmployeeEmail,
    string Role,
    string Kind,
    string ScopeLevel,
    string ScopeId,
    string ScopeLabel,
    int HeldNodes,
    int TotalNodes,
    bool IsFullCoverage);
