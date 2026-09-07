namespace Prime.Infrastructure.Services;

/// <summary>Diffusion des livrables PRIME : pilote attend la fin ; comptabilité après validation RH pour la synthèse.</summary>
public static class PrimeFicheDistributionAccess
{
    public static bool RoleMustWaitForPrimeDistribution(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        var r = role.Trim();
        return string.Equals(r, "Pilote", StringComparison.Ordinal);
    }

    public static bool IsComptabiliteRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        var r = role.Trim();
        return string.Equals(r, "Comptabilité", StringComparison.Ordinal) ||
               string.Equals(r, "Comptable", StringComparison.Ordinal);
    }

    /// <summary>Fiche fusionnée / export individuel : seul le pilote attend la diffusion complète.</summary>
    public static bool CanAccessMergedFicheLivrable(string? role, bool poolDistributionFullyUnlocked) =>
        !RoleMustWaitForPrimeDistribution(role) || poolDistributionFullyUnlocked;

    /// <summary>
    /// Synthèse globale Excel : superviseur et validateurs dès que le fichier existe ;
    /// comptabilité après validation RH ; pilote après diffusion complète.
    /// </summary>
    public static bool CanDownloadGlobalPoolSynthesis(
        string? role,
        bool legacyManagerRhUnlocked,
        bool poolDistributionFullyUnlocked,
        bool hasApprovedLines = false)
    {
        if (string.IsNullOrWhiteSpace(role)) return false;
        var r = role.Trim();
        if (string.Equals(r, "Pilote", StringComparison.Ordinal))
            return poolDistributionFullyUnlocked;
        // Comptabilité : l'export des primes validées (par les deux workflows) est disponible
        // dès qu'au moins une ligne est approuvée, sans attendre la fin de tout le périmètre.
        if (IsComptabiliteRole(r))
            return legacyManagerRhUnlocked || poolDistributionFullyUnlocked || hasApprovedLines;
        if (string.Equals(r, "Superviseur", StringComparison.Ordinal) ||
            string.Equals(r, "Admin", StringComparison.Ordinal) ||
            string.Equals(r, "RH", StringComparison.Ordinal) ||
            string.Equals(r, "Manager", StringComparison.Ordinal) ||
            PrimeFicheValidationRoles.IsOperationalApprover(r))
            return true;
        return false;
    }
}
