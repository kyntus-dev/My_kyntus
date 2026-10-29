namespace Planning.Application.DTOs;

/// <summary>Guide visible par l'utilisateur connecté (espace Aide).</summary>
public sealed record AppGuideDto(
    int Id,
    string Title,
    string? Description,
    string Kind,
    int? MediaAssetId,
    string? MediaContentType,
    string? MediaFileName,
    string? ExternalUrl,
    int SortOrder,
    bool Viewed,
    DateTime? ViewedAt);

/// <summary>Parcours du rôle courant : guides actifs ciblant ce rôle et progression.</summary>
public sealed record MyAppGuidesDto(
    string Role,
    IReadOnlyList<AppGuideDto> Guides,
    int ViewedCount,
    int Total);

/// <summary>Vue d'administration (Admin / RH) : tous les guides, actifs ou archivés.</summary>
public sealed record AppGuideAdminDto(
    int Id,
    string Title,
    string? Description,
    string Kind,
    int? MediaAssetId,
    string? MediaContentType,
    string? MediaFileName,
    string? ExternalUrl,
    IReadOnlyList<string> TargetRoles,
    int SortOrder,
    bool IsActive,
    int ViewCount,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed class SaveAppGuideRequest
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Video, Document ou Link.</summary>
    public string Kind { get; set; } = "Video";

    public int? MediaAssetId { get; set; }
    public string? ExternalUrl { get; set; }

    /// <summary>Rôles ciblés ; vide = tous les rôles.</summary>
    public List<string> TargetRoles { get; set; } = [];

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
