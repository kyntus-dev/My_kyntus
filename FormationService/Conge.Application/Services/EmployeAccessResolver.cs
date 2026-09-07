using Conge.Domain.Entities;

namespace Conge.Application.Services;

/// <summary>
/// JWT sub / Guid Planning / Guid Directory peuvent diverger.
/// Canonicalise l’accès « soi-même » sans ouvrir le dossier d’un autre employé.
/// </summary>
public static class EmployeAccessResolver
{
    public static bool IsSelf(
        Guid requestedId,
        Guid actorId,
        string? email,
        EmployeSnapshot? requestedSnap,
        EmployeSnapshot? actorSnap)
    {
        if (requestedId == actorId)
            return true;

        if (requestedSnap is not null
            && !string.IsNullOrWhiteSpace(email)
            && string.Equals(requestedSnap.Email, email, StringComparison.OrdinalIgnoreCase))
            return true;

        if (actorSnap is not null && requestedId == actorSnap.EmployeId)
            return true;

        // Guid Planning inconnu du snapshot, mais l’acteur est identifié par e-mail.
        if (requestedSnap is null
            && actorSnap is not null
            && !string.IsNullOrWhiteSpace(email)
            && string.Equals(actorSnap.Email, email, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    public static Guid CanonicalId(
        Guid requestedId,
        EmployeSnapshot? requestedSnap,
        EmployeSnapshot? actorSnap)
        => requestedSnap?.EmployeId ?? actorSnap?.EmployeId ?? requestedId;
}
