using Prime.Domain.Entities;

namespace Prime.Infrastructure.Services;

/// <summary>
/// Résolution du rôle acteur synthèse globale (RH puis Comptabilité).
/// </summary>
public static class PrimeGlobalPoolActorResolver
{
    public const string ManagerStepGoneMessage =
        "L'étape Manager n'existe plus : la synthèse est validée par la RH, puis la Comptabilité.";

    public static bool IsPoolStakeholderRole(string? role) =>
        role is "RH" or "Comptable" or "Comptabilité" or "Admin";

    public static bool IsOperationalDepartmentManager(Employee emp) =>
        string.Equals(emp.BusinessDepartmentKind, "Operational", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rôle effectif pour la synthèse globale, ou null si non autorisé.
    /// </summary>
    public static string? ResolveActingRole(
        Employee emp,
        string realRole,
        string declaredRole,
        bool managesOperationalDepartment = false)
    {
        _ = (emp, managesOperationalDepartment);
        if (IsPoolStakeholderRole(realRole))
        {
            if (string.Equals(realRole, "Admin", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(declaredRole))
            {
                var declared = declaredRole.Trim();
                return IsPoolStakeholderRole(declared) ? declared : "Admin";
            }
            return realRole;
        }

        return null;
    }
}
