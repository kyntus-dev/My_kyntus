using Planning.Application.DTOs;

namespace Planning.Application.Abstractions;

/// <summary>Espace Aide : guides de l'application ciblés par rôle et suivi « consulté ».</summary>
public interface IAppGuideService
{
    /// <summary>Guides actifs du rôle, avec l'état « consulté » de l'utilisateur.</summary>
    Task<MyAppGuidesDto> GetMineAsync(string? role, string userKey, CancellationToken ct = default);

    /// <summary>Marque un guide comme consulté (idempotent). Faux si le guide n'est pas visible pour ce rôle.</summary>
    Task<bool> MarkViewedAsync(int guideId, string? role, string userKey, CancellationToken ct = default);

    Task<IReadOnlyList<AppGuideAdminDto>> ListAllAsync(CancellationToken ct = default);

    Task<AppGuideAdminDto> CreateAsync(SaveAppGuideRequest request, string createdBy, CancellationToken ct = default);

    Task<AppGuideAdminDto?> UpdateAsync(int id, SaveAppGuideRequest request, CancellationToken ct = default);
}
