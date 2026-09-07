using Kyntus.Messaging.Contracts;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Règles d'accès pour PATCH niveau contractuel (superviseur / référent).
/// Extraite pour rester testable sans le graphe UserService.
/// </summary>
public static class ContractualLevelAccess
{
    public const string DeniedMessage = "Vous n'avez pas le droit de modifier le niveau de cet employé.";
    public const string ActorNotFoundMessage = "Profil planning introuvable pour le compte connecté.";
    public const string RoleDeniedMessage =
        "Seuls RH, Admin, Superviseur et Référent technique peuvent modifier le niveau contractuel.";

    public static bool IsPrivileged(string? role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);

    public static bool IsAllowedOperationalRole(string? role) =>
        KyntusRoleNames.IsSuperviseur(role) || KyntusRoleNames.IsReferentTechnique(role);

    /// <summary>
    /// AuthUserId puis email JWT, puis Guid <c>sub</c>.
    /// </summary>
    public static bool MatchesActor(
        Guid userGuid,
        string? userEmail,
        int? userAuthUserId,
        Guid subjectId,
        string? jwtEmail,
        int? jwtAuthUserId)
    {
        if (jwtAuthUserId is > 0 && userAuthUserId == jwtAuthUserId)
            return true;

        if (!string.IsNullOrWhiteSpace(jwtEmail)
            && !string.IsNullOrWhiteSpace(userEmail)
            && string.Equals(userEmail.Trim(), jwtEmail.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        return subjectId != Guid.Empty && userGuid == subjectId;
    }

    /// <summary>
    /// Directory a répondu oui → autoriser. Directory down → fallback local. Directory a dit non → refuser.
    /// </summary>
    public static bool AllowUpdate(bool rebacAllowed, bool rebacUnavailable, bool localFallbackAllowed)
    {
        if (rebacAllowed)
            return true;
        if (rebacUnavailable)
            return localFallbackAllowed;
        return false;
    }
}
