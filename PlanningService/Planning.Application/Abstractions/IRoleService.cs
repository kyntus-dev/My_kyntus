using Planning.Application.DTOs;

namespace Planning.Application.Abstractions;

public interface IRoleService
{
    Task<IReadOnlyList<RoleListItemDto>> GetActiveRolesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<RoleAdminDto>> GetRolesAdminAsync(CancellationToken ct = default);

    /// <summary>Nouveau rôle, sans aucun droit : l'Admin les coche dans « Accès par rôle ».</summary>
    Task<RoleAdminDto> CreerAsync(RoleSaisieRequest request, CancellationToken ct = default);

    /// <summary>Description modifiable pour tous ; nom modifiable pour les rôles créés par l'Admin.</summary>
    Task<RoleAdminDto> ModifierAsync(int id, RoleSaisieRequest request, CancellationToken ct = default);

    /// <summary>Bascule les comptes vers <paramref name="remplacementId"/> (fiche + connexion), puis supprime.</summary>
    Task<RoleSuppressionResultDto> SupprimerAsync(int id, int? remplacementId, CancellationToken ct = default);
}

/// <summary>Règle métier refusée (nom en double, rôle système…) : renvoyée en 409 avec son message.</summary>
public sealed class RoleRegleException(string message) : Exception(message);
