using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services.EmployeeImport;

public enum EmployeeImportOrgDepth
{
    None,
    Pole,
    Cellule,
    Service
}

public static class EmployeeImportRoleSynonymRegistry
{
    private static readonly Dictionary<string, string> Synonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pilote"] = EmployeeImportRoleNames.Pilote,
        ["employee"] = EmployeeImportRoleNames.Pilote,
        ["employe"] = EmployeeImportRoleNames.Pilote,
        ["employé"] = EmployeeImportRoleNames.Pilote,
        ["coach"] = EmployeeImportRoleNames.ReferentTechnique,
        ["referenttechnique"] = EmployeeImportRoleNames.ReferentTechnique,
        ["referentechnique"] = EmployeeImportRoleNames.ReferentTechnique,
        ["référenttechnique"] = EmployeeImportRoleNames.ReferentTechnique,
        ["référent technique"] = EmployeeImportRoleNames.ReferentTechnique,
        ["referent technique"] = EmployeeImportRoleNames.ReferentTechnique,
        ["superviseur"] = EmployeeImportRoleNames.Superviseur,
        ["rp"] = EmployeeImportRoleNames.ChefDeProjet,
        ["chefdeprojet"] = EmployeeImportRoleNames.ChefDeProjet,
        ["chef de projet"] = EmployeeImportRoleNames.ChefDeProjet,
        ["rh"] = "RH",
        ["audit"] = "Audit",
        ["comptable"] = "Comptabilité",
        ["comptabilite"] = "Comptabilité",
        ["comptabilité"] = "Comptabilité",
    };

    public static bool IsImportForbiddenRoleName(string? roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return false;

        var normalized = EmployeeImportColumnMatcher.Normalize(roleName);
        return normalized == "admin"
               || normalized == "manager"
               || string.Equals(roleName.Trim(), "Admin", StringComparison.OrdinalIgnoreCase)
               || string.Equals(roleName.Trim(), "Manager", StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetForbiddenRoleMessage(string? roleName)
    {
        if (!IsImportForbiddenRoleName(roleName))
            return null;

        var normalized = EmployeeImportColumnMatcher.Normalize(roleName);
        if (normalized == "manager")
            return "Le rôle Manager ne peut pas être attribué via l'import employés.";

        return "Le rôle Admin ne peut pas être attribué via l'import employés.";
    }

    public static bool TryResolveCanonicalRole(string? raw, out string canonicalRole)
    {
        canonicalRole = string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (IsImportForbiddenRoleName(raw))
            return false;

        var trimmed = raw.Trim();
        if (Synonyms.TryGetValue(trimmed, out var synonym))
        {
            canonicalRole = synonym;
            return true;
        }

        var normalized = EmployeeImportColumnMatcher.Normalize(trimmed);
        foreach (var pair in Synonyms)
        {
            if (EmployeeImportColumnMatcher.Normalize(pair.Key) == normalized)
            {
                canonicalRole = pair.Value;
                return true;
            }
        }

        canonicalRole = trimmed;
        return true;
    }

    public static EmployeeImportOrgDepth GetOrgDepth(string? canonicalRoleName)
    {
        if (string.IsNullOrWhiteSpace(canonicalRoleName))
            return EmployeeImportOrgDepth.None;

        if (EmployeeImportRoleNames.IsChefDeProjet(canonicalRoleName))
            return EmployeeImportOrgDepth.Pole;

        if (string.Equals(canonicalRoleName, EmployeeImportRoleNames.Superviseur, StringComparison.OrdinalIgnoreCase))
            return EmployeeImportOrgDepth.Cellule;

        if (EmployeeImportRoleNames.IsReferentTechnique(canonicalRoleName) || EmployeeImportRoleNames.IsPilote(canonicalRoleName))
            return EmployeeImportOrgDepth.Service;

        var normalized = EmployeeImportColumnMatcher.Normalize(canonicalRoleName);
        if (normalized is "rh" or "audit" or "admin" or "comptabilite" or "comptable")
            return EmployeeImportOrgDepth.None;

        return EmployeeImportOrgDepth.None;
    }

    public static bool HasRequiredOrgColumns(IReadOnlyDictionary<string, string?> mapped, EmployeeImportOrgDepth depth) =>
        depth switch
        {
            EmployeeImportOrgDepth.Pole => HasValue(mapped, "pole"),
            EmployeeImportOrgDepth.Cellule => HasValue(mapped, "pole") && HasValue(mapped, "cellule"),
            EmployeeImportOrgDepth.Service => HasValue(mapped, "pole") && HasValue(mapped, "cellule") &&
                                              (HasValue(mapped, "service") || HasValue(mapped, "subService")),
            _ => true
        };

    public static string RequiredOrgColumnsMessage(EmployeeImportOrgDepth depth) =>
        depth switch
        {
            EmployeeImportOrgDepth.Pole => "Chef de projet : le Pôle est requis.",
            EmployeeImportOrgDepth.Cellule => "Superviseur : Pôle et Cellule sont requis.",
            EmployeeImportOrgDepth.Service => "Pilote / Référent technique : Pôle, Cellule et Service sont requis.",
            _ => string.Empty
        };

    public static IEnumerable<Role> ImportAllowedRoles(IEnumerable<Role> roles) =>
        roles.Where(r => !IsImportForbiddenRoleName(r.Name));

    private static bool HasValue(IReadOnlyDictionary<string, string?> mapped, string key) =>
        mapped.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
}
