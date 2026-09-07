namespace Auth.Application;

/// <summary>
/// Les rôles Planning (import / annuaire) ne correspondent pas aux noms ni aux IDs Auth.
/// Ne jamais réutiliser le RoleId Planning côté Auth.
/// </summary>
public static class PlanningRoleToAuthRoleMapper
{
    public static string? MapToAuthRoleName(string? planningRoleName)
    {
        if (string.IsNullOrWhiteSpace(planningRoleName))
            return null;

        var key = Normalize(planningRoleName);
        return key switch
        {
            "pilote" or "employee" or "employe" => "Employee",
            "rh" => "RH",
            "referenttechnique" or "coach" => "Coach",
            "chefdeprojet" or "rp" => "RP",
            "superviseur" => "Superviseur",
            "manager" => "Manager",
            "admin" => "Admin",
            "audit" => "Audit",
            "equipeformation" or "formateur" => "Formateur",
            "qualiticien" => "Qualiticien",
            "comptable" or "comptabilite" => "Comptabilité",
            _ => planningRoleName.Trim(),
        };
    }

    static string Normalize(string value) =>
        new string(value.Trim().ToLowerInvariant()
            .Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray())
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);
}
