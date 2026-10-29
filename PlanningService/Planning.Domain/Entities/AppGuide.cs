namespace Planning.Domain.Entities;

public enum AppGuideKind
{
    Video = 0,
    Document = 1,
    Link = 2,
}

/// <summary>
/// Guide de l'application (espace Aide) : un contenu de prise en main ciblé par rôle.
/// Le fichier (vidéo / document) est un <see cref="MediaAsset"/> rattaché au guide ;
/// un lien externe est aussi possible.
/// </summary>
public class AppGuide
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public AppGuideKind Kind { get; set; } = AppGuideKind.Video;
    public int? MediaAssetId { get; set; }
    public string? ExternalUrl { get; set; }

    /// <summary>Rôles Planning canoniques séparés par « ; ». Vide = tous les rôles.</summary>
    public string TargetRoles { get; set; } = string.Empty;

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Trace « Marquer comme consulté » (une ligne par utilisateur et par guide).</summary>
public class AppGuideView
{
    public int Id { get; set; }
    public int GuideId { get; set; }

    /// <summary>Identifiant stable de l'utilisateur (sujet JWT).</summary>
    public string UserKey { get; set; } = string.Empty;

    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
}
