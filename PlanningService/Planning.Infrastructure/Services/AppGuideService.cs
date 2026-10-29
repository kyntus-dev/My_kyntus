using System.Globalization;
using System.Text;
using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Espace Aide : guides de l'application ciblés par rôle (vidéo, document ou lien) et
/// suivi « Marquer comme consulté » par utilisateur.
/// </summary>
public sealed class AppGuideService(AppDbContext db) : IAppGuideService
{
    private const char RoleSeparator = ';';

    public async Task<MyAppGuidesDto> GetMineAsync(string? role, string userKey, CancellationToken ct = default)
    {
        var canonicalRole = CanonicalRole(role);
        var guides = (await db.AppGuides.AsNoTracking()
                .Where(g => g.IsActive)
                .OrderBy(g => g.SortOrder)
                .ThenBy(g => g.Id)
                .ToListAsync(ct))
            .Where(g => TargetsRole(g, canonicalRole))
            .ToList();

        var guideIds = guides.Select(g => g.Id).ToList();
        var views = string.IsNullOrWhiteSpace(userKey)
            ? new Dictionary<int, DateTime>()
            : await db.AppGuideViews.AsNoTracking()
                .Where(v => v.UserKey == userKey && guideIds.Contains(v.GuideId))
                .ToDictionaryAsync(v => v.GuideId, v => v.ViewedAt, ct);
        var media = await LoadMediaAsync(guides, ct);

        var items = guides
            .Select(g =>
            {
                media.TryGetValue(g.MediaAssetId ?? 0, out var m);
                var viewed = views.TryGetValue(g.Id, out var viewedAt);
                return new AppGuideDto(
                    g.Id, g.Title, g.Description, g.Kind.ToString(),
                    g.MediaAssetId, m?.ContentType, m?.FileName, g.ExternalUrl,
                    g.SortOrder, viewed, viewed ? viewedAt : null);
            })
            .ToList();

        return new MyAppGuidesDto(
            KyntusRoleNames.NormalizePlanningRole(role),
            items,
            items.Count(i => i.Viewed),
            items.Count);
    }

    public async Task<bool> MarkViewedAsync(int guideId, string? role, string userKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userKey))
            return false;

        var guide = await db.AppGuides.AsNoTracking().FirstOrDefaultAsync(g => g.Id == guideId && g.IsActive, ct);
        if (guide is null || !TargetsRole(guide, CanonicalRole(role)))
            return false;

        if (await db.AppGuideViews.AnyAsync(v => v.GuideId == guideId && v.UserKey == userKey, ct))
            return true;

        db.AppGuideViews.Add(new AppGuideView { GuideId = guideId, UserKey = userKey, ViewedAt = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Double clic simultané : l'index unique garantit une seule ligne, l'état final est correct.
        }

        return true;
    }

    public async Task<IReadOnlyList<AppGuideAdminDto>> ListAllAsync(CancellationToken ct = default)
    {
        var guides = await db.AppGuides.AsNoTracking()
            .OrderBy(g => g.SortOrder)
            .ThenBy(g => g.Id)
            .ToListAsync(ct);
        var counts = await db.AppGuideViews.AsNoTracking()
            .GroupBy(v => v.GuideId)
            .Select(g => new { GuideId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.GuideId, x => x.Count, ct);
        var media = await LoadMediaAsync(guides, ct);

        return guides
            .Select(g => ToAdminDto(g, media, counts.TryGetValue(g.Id, out var c) ? c : 0))
            .ToList();
    }

    public async Task<AppGuideAdminDto> CreateAsync(SaveAppGuideRequest request, string createdBy, CancellationToken ct = default)
    {
        var guide = new AppGuide { CreatedBy = createdBy ?? string.Empty, CreatedAt = DateTime.UtcNow };
        await ApplyAsync(guide, request, ct);
        db.AppGuides.Add(guide);
        await db.SaveChangesAsync(ct);
        await AttachMediaAsync(guide, ct);
        return await GetAdminDtoAsync(guide.Id, ct);
    }

    public async Task<AppGuideAdminDto?> UpdateAsync(int id, SaveAppGuideRequest request, CancellationToken ct = default)
    {
        var guide = await db.AppGuides.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (guide is null)
            return null;

        await ApplyAsync(guide, request, ct);
        guide.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await AttachMediaAsync(guide, ct);
        return await GetAdminDtoAsync(guide.Id, ct);
    }

    private async Task ApplyAsync(AppGuide guide, SaveAppGuideRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0)
            throw new ArgumentException("Le titre est requis.");
        if (title.Length > 200)
            throw new ArgumentException("Le titre ne doit pas dépasser 200 caractères.");

        if (!Enum.TryParse<AppGuideKind>(request.Kind?.Trim(), true, out var kind) || !Enum.IsDefined(kind))
            throw new ArgumentException("Type de guide invalide (Video, Document ou Link).");

        var url = string.IsNullOrWhiteSpace(request.ExternalUrl) ? null : request.ExternalUrl.Trim();
        if (url is not null
            && (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException("Le lien doit être une adresse http(s) complète.");
        }

        int? mediaId = kind == AppGuideKind.Link ? null : request.MediaAssetId;
        if (kind == AppGuideKind.Link && url is null)
            throw new ArgumentException("Un guide de type lien nécessite une adresse.");
        if (kind != AppGuideKind.Link && mediaId is null && url is null)
            throw new ArgumentException("Ajoutez un fichier ou un lien pour ce guide.");

        if (mediaId is not null)
        {
            var media = await db.MediaAssets.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mediaId, ct)
                ?? throw new ArgumentException("Fichier introuvable : rechargez-le.");
            if (kind == AppGuideKind.Video && media.Kind != MediaKind.Video)
                throw new ArgumentException("Le fichier choisi n'est pas une vidéo (mp4, webm ou mov).");
            if (media.OwnerType != MediaOwnerType.Orphan
                && !(media.OwnerType == MediaOwnerType.AppGuide && media.OwnerId == guide.Id && guide.Id != 0))
            {
                throw new ArgumentException("Ce fichier est déjà utilisé ailleurs : rechargez-le pour ce guide.");
            }
        }

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description is { Length: > 2000 })
            throw new ArgumentException("La description ne doit pas dépasser 2000 caractères.");

        guide.Title = title;
        guide.Description = description;
        guide.Kind = kind;
        guide.MediaAssetId = mediaId;
        guide.ExternalUrl = url;
        guide.TargetRoles = SerializeRoles(request.TargetRoles);
        guide.SortOrder = request.SortOrder;
        guide.IsActive = request.IsActive;
    }

    private async Task AttachMediaAsync(AppGuide guide, CancellationToken ct)
    {
        if (guide.MediaAssetId is null)
            return;
        var media = await db.MediaAssets.FirstOrDefaultAsync(m => m.Id == guide.MediaAssetId, ct);
        if (media is null || (media.OwnerType == MediaOwnerType.AppGuide && media.OwnerId == guide.Id))
            return;
        media.OwnerType = MediaOwnerType.AppGuide;
        media.OwnerId = guide.Id;
        await db.SaveChangesAsync(ct);
    }

    private async Task<AppGuideAdminDto> GetAdminDtoAsync(int id, CancellationToken ct)
    {
        var guide = await db.AppGuides.AsNoTracking().FirstAsync(g => g.Id == id, ct);
        var media = await LoadMediaAsync([guide], ct);
        var count = await db.AppGuideViews.CountAsync(v => v.GuideId == id, ct);
        return ToAdminDto(guide, media, count);
    }

    private async Task<Dictionary<int, MediaAsset>> LoadMediaAsync(IReadOnlyCollection<AppGuide> guides, CancellationToken ct)
    {
        var ids = guides.Where(g => g.MediaAssetId.HasValue).Select(g => g.MediaAssetId!.Value).Distinct().ToList();
        if (ids.Count == 0)
            return new Dictionary<int, MediaAsset>();
        return await db.MediaAssets.AsNoTracking().Where(m => ids.Contains(m.Id)).ToDictionaryAsync(m => m.Id, ct);
    }

    private static AppGuideAdminDto ToAdminDto(AppGuide g, IReadOnlyDictionary<int, MediaAsset> media, int viewCount)
    {
        media.TryGetValue(g.MediaAssetId ?? 0, out var m);
        return new AppGuideAdminDto(
            g.Id, g.Title, g.Description, g.Kind.ToString(), g.MediaAssetId, m?.ContentType, m?.FileName,
            g.ExternalUrl, ParseRoles(g.TargetRoles), g.SortOrder, g.IsActive, viewCount, g.CreatedAt, g.UpdatedAt);
    }

    private static bool TargetsRole(AppGuide guide, string canonicalRole)
    {
        var roles = ParseRoles(guide.TargetRoles);
        return roles.Count == 0 || roles.Any(r => CanonicalRole(r) == canonicalRole);
    }

    private static string SerializeRoles(IEnumerable<string>? roles) =>
        string.Join(RoleSeparator, (roles ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => KyntusRoleNames.NormalizePlanningRole(r.Trim()))
            .DistinctBy(CanonicalRole)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ParseRoles(string? stored) =>
        (stored ?? string.Empty)
            .Split(RoleSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    /// <summary>Clé de comparaison : rôle normalisé, sans accents ni casse (« Référent technique » = « referent technique »).</summary>
    private static string CanonicalRole(string? role)
    {
        var normalized = KyntusRoleNames.NormalizePlanningRole(role).Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
