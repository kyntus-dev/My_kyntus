using System.Text.Json;
using Formation.Application.DTOs;
using Formation.Domain.Entities;
using Formation.Domain.Enums;
using Formation.Infrastructure.Persistence;
using MassTransit;
using Kyntus.Caching;
using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Formation.Infrastructure.Services;

public sealed class LearningCatalogService(
    FormationDbContext db,
    IPublishEndpoint publish,
    ICacheService cache,
    ILogger<LearningCatalogService> logger)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = null };

    public async Task<IReadOnlyList<TrainingCatalogItemDto>> ListAsync(
        string? category,
        bool includeArchived,
        CancellationToken ct)
    {
        // Catalogue publié (défaut admin) — cache partagé.
        if (!includeArchived && string.IsNullOrWhiteSpace(category))
        {
            return await cache.GetOrCreateAsync(
                KyntusCacheKeys.FormationCatalog(),
                token => ListUncachedAsync(category: null, includeArchived: false, token),
                KyntusCacheTtl.Referential,
                ct);
        }

        return await ListUncachedAsync(category, includeArchived, ct);
    }

    private async Task<IReadOnlyList<TrainingCatalogItemDto>> ListUncachedAsync(
        string? category,
        bool includeArchived,
        CancellationToken ct)
    {
        var q = db.TrainingCatalogItems.AsNoTracking().AsQueryable();
        if (!includeArchived)
            q = q.Where(x => x.Status != CatalogItemStatus.Archived && x.IsActive);
        if (!string.IsNullOrWhiteSpace(category))
            q = q.Where(x => x.Category == category.Trim());

        var items = await q.OrderByDescending(x => x.UpdatedAt).ToListAsync(ct);
        return await MapListAsync(items, includeTree: false, ct);
    }

    private Task InvalidatePublishedCatalogCacheAsync(CancellationToken ct = default) =>
        cache.RemoveAsync(KyntusCacheKeys.FormationCatalog(), ct);

    public async Task<TrainingCatalogItemDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var item = await db.TrainingCatalogItems.AsNoTracking()
            .Include(x => x.AudienceRules)
            .Include(x => x.Modules).ThenInclude(m => m.Lessons).ThenInclude(l => l.Resources)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return null;
        var list = await MapListAsync([item], includeTree: true, ct);
        return list.FirstOrDefault();
    }

    public async Task<TrainingCatalogItemDto> CreateAsync(UpsertTrainingCatalogItemRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("Le titre est obligatoire.");

        var item = new TrainingCatalogItem
        {
            Title = request.Title.Trim(),
            Description = request.Description?.Trim() ?? "",
            Category = request.Category?.Trim() ?? "",
            DefaultGateMode = request.DefaultGateMode,
            AudienceMatchMode = request.AudienceMatchMode,
            CreatedByUserId = request.CreatedByUserId,
            Status = CatalogItemStatus.Draft,
            IsActive = true,
            SelfServiceEnabled = request.SelfServiceEnabled,
            DueMode = request.DueMode,
            DueDate = request.DueMode == CatalogDueMode.Absolute ? request.DueDate : null,
            DueInDays = request.DueMode == CatalogDueMode.RelativeDays ? request.DueInDays : null,
            DefaultQuizTemplateId = request.DefaultQuizTemplateId,
        };
        db.TrainingCatalogItems.Add(item);
        // Empty audience rule = public.
        db.TrainingCatalogAudienceRules.Add(new TrainingCatalogAudienceRule { CatalogItemId = item.Id });
        await db.SaveChangesAsync(ct);
        await InvalidatePublishedCatalogCacheAsync(ct);
        return (await GetAsync(item.Id, ct))!;
    }

    public async Task<TrainingCatalogItemDto> UpdateAsync(Guid id, UpsertTrainingCatalogItemRequest request, CancellationToken ct)
    {
        var item = await db.TrainingCatalogItems.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Formation catalogue introuvable.");
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("Le titre est obligatoire.");
        if (item.Status == CatalogItemStatus.Archived)
            throw new InvalidOperationException("Impossible de modifier une formation archivée.");

        item.Title = request.Title.Trim();
        item.Description = request.Description?.Trim() ?? "";
        item.Category = request.Category?.Trim() ?? "";
        item.DefaultGateMode = request.DefaultGateMode;
        item.AudienceMatchMode = request.AudienceMatchMode;
        item.SelfServiceEnabled = request.SelfServiceEnabled;
        item.DueMode = request.DueMode;
        item.DueDate = request.DueMode == CatalogDueMode.Absolute ? request.DueDate : null;
        item.DueInDays = request.DueMode == CatalogDueMode.RelativeDays ? request.DueInDays : null;
        item.DefaultQuizTemplateId = request.DefaultQuizTemplateId;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await InvalidatePublishedCatalogCacheAsync(ct);
        return (await GetAsync(item.Id, ct))!;
    }

    public async Task<TrainingCatalogItemDto> PublishAsync(Guid id, CancellationToken ct)
    {
        var item = await db.TrainingCatalogItems.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Formation catalogue introuvable.");
        item.Status = CatalogItemStatus.Published;
        item.IsActive = true;
        item.PublishedAt = DateTime.UtcNow;
        item.ArchivedAt = null;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await InvalidatePublishedCatalogCacheAsync(ct);
        await NotifyAudienceCatalogAvailableAsync(item.Id, item.Title, ct);
        return (await GetAsync(item.Id, ct))!;
    }

    public async Task<TrainingCatalogItemDto> ArchiveAsync(Guid id, CancellationToken ct)
    {
        var item = await db.TrainingCatalogItems.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Formation catalogue introuvable.");
        item.Status = CatalogItemStatus.Archived;
        item.IsActive = false;
        item.ArchivedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await InvalidatePublishedCatalogCacheAsync(ct);
        return (await GetAsync(item.Id, ct))!;
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var linked = await db.TrainingSessions.AnyAsync(s => s.CatalogItemId == id, ct);
        if (linked)
            throw new InvalidOperationException("Des sessions sont liées — archivez la formation au lieu de la supprimer.");

        var item = await db.TrainingCatalogItems
            .Include(x => x.Modules).ThenInclude(m => m.Lessons).ThenInclude(l => l.Resources)
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InvalidOperationException("Formation catalogue introuvable.");

        foreach (var res in item.Modules.SelectMany(m => m.Lessons).SelectMany(l => l.Resources))
            TryDeleteFile(res.StoragePath);

        db.TrainingCatalogItems.Remove(item);
        await db.SaveChangesAsync(ct);
        await InvalidatePublishedCatalogCacheAsync(ct);
    }

    public async Task<TrainingCatalogAudienceDto> UpsertAudienceAsync(
        Guid catalogItemId,
        UpsertTrainingCatalogAudienceRequest request,
        CancellationToken ct)
    {
        var item = await db.TrainingCatalogItems
            .Include(x => x.AudienceRules)
            .FirstOrDefaultAsync(x => x.Id == catalogItemId, ct)
            ?? throw new InvalidOperationException("Formation catalogue introuvable.");

        item.AudienceMatchMode = request.MatchMode;
        item.UpdatedAt = DateTime.UtcNow;

        var rule = item.AudienceRules.FirstOrDefault();
        if (rule is null)
        {
            rule = new TrainingCatalogAudienceRule { CatalogItemId = catalogItemId };
            db.TrainingCatalogAudienceRules.Add(rule);
        }

        rule.RolesJson = JsonSerializer.Serialize(NormalizeStrings(request.Roles), JsonOpts);
        rule.StructureKeysJson = JsonSerializer.Serialize(NormalizeStrings(request.StructureKeys), JsonOpts);
        rule.UserIdsJson = JsonSerializer.Serialize(
            request.UserIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [],
            JsonOpts);

        await db.SaveChangesAsync(ct);
        var beneficiaries = await ResolveAudienceAsync(item.Id, ct);
        if (item.Status == CatalogItemStatus.Published)
            await NotifyAudienceCatalogAvailableAsync(item.Id, item.Title, ct, beneficiaries);
        return new TrainingCatalogAudienceDto(
            item.AudienceMatchMode,
            ParseStringList(rule.RolesJson),
            ParseStringList(rule.StructureKeysJson),
            ParseGuidList(rule.UserIdsJson),
            beneficiaries.Count);
    }

    public async Task<TrainingModuleDto> UpsertModuleAsync(
        Guid catalogItemId,
        Guid? moduleId,
        UpsertTrainingModuleRequest request,
        CancellationToken ct)
    {
        _ = await db.TrainingCatalogItems.FirstOrDefaultAsync(x => x.Id == catalogItemId, ct)
            ?? throw new InvalidOperationException("Formation catalogue introuvable.");
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("Le titre du module est obligatoire.");

        TrainingModule module;
        if (moduleId is Guid mid)
        {
            module = await db.TrainingModules.FirstOrDefaultAsync(m => m.Id == mid && m.CatalogItemId == catalogItemId, ct)
                ?? throw new InvalidOperationException("Module introuvable.");
            module.Title = request.Title.Trim();
            module.Description = request.Description?.Trim() ?? "";
            module.SortOrder = request.SortOrder;
        }
        else
        {
            module = new TrainingModule
            {
                CatalogItemId = catalogItemId,
                Title = request.Title.Trim(),
                Description = request.Description?.Trim() ?? "",
                SortOrder = request.SortOrder,
            };
            db.TrainingModules.Add(module);
        }

        await TouchCatalogAsync(catalogItemId, ct);
        await db.SaveChangesAsync(ct);
        return await MapModuleAsync(module.Id, ct);
    }

    public async Task DeleteModuleAsync(Guid catalogItemId, Guid moduleId, CancellationToken ct)
    {
        var module = await db.TrainingModules
            .Include(m => m.Lessons).ThenInclude(l => l.Resources)
            .FirstOrDefaultAsync(m => m.Id == moduleId && m.CatalogItemId == catalogItemId, ct)
            ?? throw new InvalidOperationException("Module introuvable.");
        foreach (var res in module.Lessons.SelectMany(l => l.Resources))
            TryDeleteFile(res.StoragePath);
        db.TrainingModules.Remove(module);
        await TouchCatalogAsync(catalogItemId, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Remplace l'arbre modules/leçons/ressources en une transaction.
    /// Les fichiers locaux déjà en base sont conservés s'ils sont présents par Id ; les nouveaux fichiers s'uploadent ensuite.
    /// </summary>
    public async Task<ReplaceCatalogStructureResponse> ReplaceStructureAsync(
        Guid catalogItemId,
        ReplaceCatalogStructureRequest request,
        CancellationToken ct)
    {
        var item = await db.TrainingCatalogItems
            .Include(c => c.Modules).ThenInclude(m => m.Lessons).ThenInclude(l => l.Resources)
            .FirstOrDefaultAsync(c => c.Id == catalogItemId, ct)
            ?? throw new InvalidOperationException("Formation catalogue introuvable.");
        if (item.Status == CatalogItemStatus.Archived)
            throw new InvalidOperationException("Impossible de modifier une formation archivée.");

        var modulesReq = request.Modules ?? Array.Empty<StructureModuleRequest>();
        var keepModuleIds = modulesReq.Where(m => m.Id is Guid).Select(m => m.Id!.Value).ToHashSet();
        var keepLessonIds = modulesReq.SelectMany(m => m.Lessons ?? Array.Empty<StructureLessonRequest>())
            .Where(l => l.Id is Guid).Select(l => l.Id!.Value).ToHashSet();
        var keepResourceIds = modulesReq.SelectMany(m => m.Lessons ?? Array.Empty<StructureLessonRequest>())
            .SelectMany(l => l.Resources ?? Array.Empty<StructureResourceRequest>())
            .Where(r => r.Id is Guid).Select(r => r.Id!.Value).ToHashSet();

        foreach (var module in item.Modules.Where(m => !keepModuleIds.Contains(m.Id)).ToList())
        {
            foreach (var res in module.Lessons.SelectMany(l => l.Resources))
                TryDeleteFile(res.StoragePath);
            db.TrainingModules.Remove(module);
        }

        foreach (var module in item.Modules.Where(m => keepModuleIds.Contains(m.Id)).ToList())
        {
            foreach (var lesson in module.Lessons.Where(l => !keepLessonIds.Contains(l.Id)).ToList())
            {
                foreach (var res in lesson.Resources)
                    TryDeleteFile(res.StoragePath);
                db.TrainingLessons.Remove(lesson);
            }

            foreach (var lesson in module.Lessons.Where(l => keepLessonIds.Contains(l.Id)).ToList())
            {
                foreach (var res in lesson.Resources.Where(r => !keepResourceIds.Contains(r.Id)).ToList())
                {
                    TryDeleteFile(res.StoragePath);
                    db.TrainingResources.Remove(res);
                }
            }
        }

        var moduleResults = new List<StructureModuleResultDto>();

        foreach (var modReq in modulesReq.OrderBy(m => m.SortOrder))
        {
            if (string.IsNullOrWhiteSpace(modReq.Title))
                throw new InvalidOperationException("Le titre du module est obligatoire.");

            TrainingModule module;
            if (modReq.Id is Guid mid)
            {
                module = item.Modules.FirstOrDefault(m => m.Id == mid)
                    ?? throw new InvalidOperationException($"Module {mid} introuvable.");
                module.Title = modReq.Title.Trim();
                module.Description = modReq.Description?.Trim() ?? "";
                module.SortOrder = modReq.SortOrder;
            }
            else
            {
                module = new TrainingModule
                {
                    CatalogItemId = catalogItemId,
                    Title = modReq.Title.Trim(),
                    Description = modReq.Description?.Trim() ?? "",
                    SortOrder = modReq.SortOrder,
                };
                db.TrainingModules.Add(module);
            }

            var lessonResults = new List<StructureLessonResultDto>();
            foreach (var lesReq in (modReq.Lessons ?? Array.Empty<StructureLessonRequest>()).OrderBy(l => l.SortOrder))
            {
                if (string.IsNullOrWhiteSpace(lesReq.Title))
                    throw new InvalidOperationException("Le titre de la leçon est obligatoire.");

                TrainingLesson lesson;
                if (lesReq.Id is Guid lid)
                {
                    lesson = module.Lessons.FirstOrDefault(l => l.Id == lid)
                        ?? await db.TrainingLessons.Include(l => l.Resources)
                            .FirstOrDefaultAsync(l => l.Id == lid && l.ModuleId == module.Id, ct)
                        ?? throw new InvalidOperationException($"Leçon {lid} introuvable.");
                    lesson.Title = lesReq.Title.Trim();
                    lesson.Description = lesReq.Description?.Trim() ?? "";
                    lesson.SortOrder = lesReq.SortOrder;
                    lesson.IsRequired = lesReq.IsRequired;
                    if (lesson.ModuleId != module.Id)
                        lesson.ModuleId = module.Id;
                }
                else
                {
                    lesson = new TrainingLesson
                    {
                        ModuleId = module.Id,
                        Title = lesReq.Title.Trim(),
                        Description = lesReq.Description?.Trim() ?? "",
                        SortOrder = lesReq.SortOrder,
                        IsRequired = lesReq.IsRequired,
                    };
                    db.TrainingLessons.Add(lesson);
                }

                var resourceResults = new List<StructureResourceResultDto>();
                foreach (var resReq in (lesReq.Resources ?? Array.Empty<StructureResourceRequest>()).OrderBy(r => r.SortOrder))
                {
                    if (string.IsNullOrWhiteSpace(resReq.Title))
                        throw new InvalidOperationException("Le titre de la ressource est obligatoire.");

                    TrainingResource resource;
                    if (resReq.Id is Guid rid)
                    {
                        resource = lesson.Resources.FirstOrDefault(r => r.Id == rid)
                            ?? await db.TrainingResources.FirstOrDefaultAsync(r => r.Id == rid && r.LessonId == lesson.Id, ct)
                            ?? throw new InvalidOperationException($"Ressource {rid} introuvable.");
                        resource.Type = resReq.Type;
                        resource.Title = resReq.Title.Trim();
                        resource.SortOrder = resReq.SortOrder;
                        resource.DurationMinutes = resReq.DurationMinutes;
                        // Ne pas écraser un fichier stocké avec une URL vide.
                        if (string.IsNullOrWhiteSpace(resource.StoragePath))
                        {
                            resource.Url = resReq.Url?.Trim();
                            resource.TextContent = resReq.TextContent;
                        }
                        else if (!string.IsNullOrWhiteSpace(resReq.Url))
                        {
                            resource.Url = resReq.Url.Trim();
                        }
                        if (resReq.Type == TrainingResourceType.Text)
                            resource.TextContent = resReq.TextContent;
                    }
                    else
                    {
                        resource = new TrainingResource
                        {
                            LessonId = lesson.Id,
                            Type = resReq.Type,
                            Title = resReq.Title.Trim(),
                            Url = resReq.Url?.Trim(),
                            TextContent = resReq.TextContent,
                            SortOrder = resReq.SortOrder,
                            DurationMinutes = resReq.DurationMinutes,
                        };
                        db.TrainingResources.Add(resource);
                    }

                    resourceResults.Add(new StructureResourceResultDto(
                        string.IsNullOrWhiteSpace(resReq.ClientKey) ? resource.Id.ToString() : resReq.ClientKey,
                        resource.Id));
                }

                lessonResults.Add(new StructureLessonResultDto(
                    string.IsNullOrWhiteSpace(lesReq.ClientKey) ? lesson.Id.ToString() : lesReq.ClientKey,
                    lesson.Id,
                    resourceResults));
            }

            moduleResults.Add(new StructureModuleResultDto(
                string.IsNullOrWhiteSpace(modReq.ClientKey) ? module.Id.ToString() : modReq.ClientKey,
                module.Id,
                lessonResults));
        }

        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        // Assurer que les nouveaux modules/leçons ont bien leurs Id après SaveChanges.
        return new ReplaceCatalogStructureResponse(catalogItemId, moduleResults);
    }

    public async Task<TrainingLessonDto> UpsertLessonAsync(
        Guid moduleId,
        Guid? lessonId,
        UpsertTrainingLessonRequest request,
        CancellationToken ct)
    {
        var module = await db.TrainingModules.FirstOrDefaultAsync(m => m.Id == moduleId, ct)
            ?? throw new InvalidOperationException("Module introuvable.");
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("Le titre de la leçon est obligatoire.");

        TrainingLesson lesson;
        if (lessonId is Guid lid)
        {
            lesson = await db.TrainingLessons.FirstOrDefaultAsync(l => l.Id == lid && l.ModuleId == moduleId, ct)
                ?? throw new InvalidOperationException("Leçon introuvable.");
            lesson.Title = request.Title.Trim();
            lesson.Description = request.Description?.Trim() ?? "";
            lesson.SortOrder = request.SortOrder;
            lesson.IsRequired = request.IsRequired;
        }
        else
        {
            lesson = new TrainingLesson
            {
                ModuleId = moduleId,
                Title = request.Title.Trim(),
                Description = request.Description?.Trim() ?? "",
                SortOrder = request.SortOrder,
                IsRequired = request.IsRequired,
            };
            db.TrainingLessons.Add(lesson);
        }

        await TouchCatalogAsync(module.CatalogItemId, ct);
        await db.SaveChangesAsync(ct);
        return await MapLessonAsync(lesson.Id, ct);
    }

    public async Task DeleteLessonAsync(Guid moduleId, Guid lessonId, CancellationToken ct)
    {
        var module = await db.TrainingModules.FirstOrDefaultAsync(m => m.Id == moduleId, ct)
            ?? throw new InvalidOperationException("Module introuvable.");
        var lesson = await db.TrainingLessons
            .Include(l => l.Resources)
            .FirstOrDefaultAsync(l => l.Id == lessonId && l.ModuleId == moduleId, ct)
            ?? throw new InvalidOperationException("Leçon introuvable.");
        foreach (var res in lesson.Resources)
            TryDeleteFile(res.StoragePath);
        db.TrainingLessons.Remove(lesson);
        await TouchCatalogAsync(module.CatalogItemId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TrainingResourceDto> UpsertResourceAsync(
        Guid lessonId,
        Guid? resourceId,
        UpsertTrainingResourceRequest request,
        CancellationToken ct)
    {
        var lesson = await db.TrainingLessons.Include(l => l.Module)
            .FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            ?? throw new InvalidOperationException("Leçon introuvable.");
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("Le titre de la ressource est obligatoire.");

        TrainingResource resource;
        if (resourceId is Guid rid)
        {
            resource = await db.TrainingResources.FirstOrDefaultAsync(r => r.Id == rid && r.LessonId == lessonId, ct)
                ?? throw new InvalidOperationException("Ressource introuvable.");
            resource.Type = request.Type;
            resource.Title = request.Title.Trim();
            resource.Url = request.Url?.Trim();
            resource.TextContent = request.TextContent;
            resource.SortOrder = request.SortOrder;
            resource.DurationMinutes = request.DurationMinutes;
        }
        else
        {
            resource = new TrainingResource
            {
                LessonId = lessonId,
                Type = request.Type,
                Title = request.Title.Trim(),
                Url = request.Url?.Trim(),
                TextContent = request.TextContent,
                SortOrder = request.SortOrder,
                DurationMinutes = request.DurationMinutes,
            };
            db.TrainingResources.Add(resource);
        }

        await TouchCatalogAsync(lesson.Module!.CatalogItemId, ct);
        await db.SaveChangesAsync(ct);
        return MapResource(resource);
    }

    public async Task<TrainingResourceDto> UploadResourceFileAsync(
        Guid lessonId,
        string fileName,
        string contentType,
        Stream content,
        string? rootPath,
        TrainingResourceType type,
        string? title,
        CancellationToken ct,
        int? sortOrder = null)
    {
        var lesson = await db.TrainingLessons.Include(l => l.Module)
            .FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            ?? throw new InvalidOperationException("Leçon introuvable.");

        var root = string.IsNullOrWhiteSpace(rootPath)
            ? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "learning")
            : rootPath;
        Directory.CreateDirectory(root);

        var safeName = $"{lessonId:N}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Path.GetFileName(fileName)}";
        var fullPath = Path.Combine(root, safeName);
        await using (var fs = File.Create(fullPath))
            await content.CopyToAsync(fs, ct);

        int order;
        if (sortOrder is int so)
        {
            order = so;
        }
        else
        {
            var maxOrder = await db.TrainingResources
                .Where(r => r.LessonId == lessonId)
                .Select(r => (int?)r.SortOrder)
                .MaxAsync(ct) ?? -1;
            order = maxOrder + 1;
        }

        var resource = new TrainingResource
        {
            LessonId = lessonId,
            Type = type,
            Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(fileName) : title.Trim(),
            StoragePath = fullPath,
            FileName = fileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            SortOrder = order,
            Url = $"/api/formations/catalog/resources/file/{Guid.Empty}", // patched after save
        };
        db.TrainingResources.Add(resource);
        await TouchCatalogAsync(lesson.Module!.CatalogItemId, ct);
        await db.SaveChangesAsync(ct);

        resource.Url = $"/api/formations/catalog/resources/file/{resource.Id}";
        await db.SaveChangesAsync(ct);
        return MapResource(resource);
    }

    /// <summary>Métadonnées + chemin disque pour streaming HTTP (range).</summary>
    public async Task<(TrainingResource Resource, string FullPath, long Length, DateTime LastWriteUtc)?> GetResourceFileInfoAsync(
        Guid resourceId,
        CancellationToken ct)
    {
        var resource = await db.TrainingResources.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == resourceId, ct);
        if (resource is null || string.IsNullOrWhiteSpace(resource.StoragePath) || !File.Exists(resource.StoragePath))
            return null;
        var info = new FileInfo(resource.StoragePath);
        return (resource, resource.StoragePath, info.Length, info.LastWriteTimeUtc);
    }

    [Obsolete("Prefer GetResourceFileInfoAsync for streaming.")]
    public async Task<(TrainingResource Resource, byte[] Bytes)?> GetResourceFileAsync(Guid resourceId, CancellationToken ct)
    {
        var info = await GetResourceFileInfoAsync(resourceId, ct);
        if (info is null) return null;
        var bytes = await File.ReadAllBytesAsync(info.Value.FullPath, ct);
        return (info.Value.Resource, bytes);
    }

    public async Task DeleteResourceAsync(Guid lessonId, Guid resourceId, CancellationToken ct)
    {
        var lesson = await db.TrainingLessons.Include(l => l.Module)
            .FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            ?? throw new InvalidOperationException("Leçon introuvable.");
        var resource = await db.TrainingResources.FirstOrDefaultAsync(r => r.Id == resourceId && r.LessonId == lessonId, ct)
            ?? throw new InvalidOperationException("Ressource introuvable.");
        TryDeleteFile(resource.StoragePath);
        db.TrainingResources.Remove(resource);
        await TouchCatalogAsync(lesson.Module!.CatalogItemId, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TrainingSessionDto> LinkSessionCatalogAsync(
        Guid sessionId,
        LinkSessionCatalogRequest request,
        CancellationToken ct)
    {
        var session = await db.TrainingSessions.Include(s => s.Assignments)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session introuvable.");

        if (request.CatalogItemId is Guid catalogId)
        {
            var catalog = await db.TrainingCatalogItems.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == catalogId, ct)
                ?? throw new InvalidOperationException("Formation catalogue introuvable.");
            if (catalog.Status == CatalogItemStatus.Archived)
                throw new InvalidOperationException("Impossible de lier une formation archivée.");

            session.CatalogItemId = catalogId;
            session.LearningGateMode = request.LearningGateMode ?? catalog.DefaultGateMode;
            session.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            if (request.AssignAudience)
                await AssignAudienceToSessionAsync(sessionId, catalogId, ct);
        }
        else
        {
            session.CatalogItemId = null;
            session.LearningGateMode = request.LearningGateMode;
            session.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        var mapped = await db.TrainingSessions.AsNoTracking()
            .FirstAsync(s => s.Id == sessionId, ct);
        var count = await db.TrainingAssignments.CountAsync(a => a.SessionId == sessionId, ct);
        var quiz = await db.TrainingQuizzes.AsNoTracking()
            .FirstOrDefaultAsync(q => q.SessionId == sessionId, ct);
        var hasReport = await db.TrainingSessionReports.AnyAsync(r => r.SessionId == sessionId, ct);
        return TrainingSessionDtoMapper.ToDto(
            mapped,
            count,
            hasReport,
            quiz?.Id,
            quiz?.Status.ToString());
    }

    public async Task<int> AssignAudienceToSessionAsync(Guid sessionId, Guid catalogItemId, CancellationToken ct)
    {
        var session = await db.TrainingSessions.Include(s => s.Assignments)
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session introuvable.");

        var beneficiaries = await ResolveAudienceAsync(catalogItemId, ct);
        if (beneficiaries.Count == 0)
        {
            logger.LogInformation("Audience vide pour catalogue {CatalogId} — aucune affectation.", catalogItemId);
            return 0;
        }

        var remaining = Math.Max(0, session.Capacity - session.Assignments.Count);
        var toAssign = beneficiaries
            .Where(b => session.Assignments.All(a => a.EmployeeId != b.EmployeId))
            .Take(remaining)
            .Select(b => new AssignTrainingEmployeeItem
            {
                EmployeeId = b.EmployeId,
                EmployeeName = $"{b.Prenom} {b.Nom}".Trim(),
            })
            .ToList();

        if (toAssign.Count == 0) return 0;

        var newly = new List<(Guid EmployeeId, string EmployeeName)>();
        foreach (var item in toAssign)
        {
            db.TrainingAssignments.Add(new TrainingAssignment
            {
                SessionId = sessionId,
                EmployeeId = item.EmployeeId,
                EmployeeName = item.EmployeeName,
            });
            newly.Add((item.EmployeeId, item.EmployeeName));
        }

        session.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        foreach (var (employeeId, employeeName) in newly)
        {
            await publish.Publish(new TrainingSessionAssignedMessage
            {
                SessionId = sessionId,
                Title = session.Title,
                PlannedStart = session.PlannedStart,
                EmployeeId = employeeId,
                EmployeeName = employeeName,
                AssignedAt = DateTime.UtcNow,
            }, ct);
        }

        return newly.Count;
    }

    public async Task EnsureCanAccessCatalogAsync(
        Guid catalogItemId,
        Guid employeeId,
        CancellationToken ct,
        string? email = null)
    {
        var aliases = await FormationEmployeeIdentity.ResolveAliasesAsync(db, employeeId, email, ct);
        await EnsureCanAccessCatalogForAliasesAsync(catalogItemId, aliases, ct);
    }

    private async Task EnsureCanAccessCatalogForAliasesAsync(
        Guid catalogItemId,
        IReadOnlyCollection<Guid> aliases,
        CancellationToken ct)
    {
        var beneficiaries = await ResolveAudienceAsync(catalogItemId, ct);
        // Empty audience = public.
        var rule = await db.TrainingCatalogAudienceRules.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CatalogItemId == catalogItemId, ct);
        var roles = ParseStringList(rule?.RolesJson);
        var structures = ParseStringList(rule?.StructureKeysJson);
        var users = ParseGuidList(rule?.UserIdsJson);
        if (roles.Count == 0 && structures.Count == 0 && users.Count == 0)
            return;

        if (beneficiaries.All(b => !aliases.Contains(b.EmployeId)))
            throw new InvalidOperationException("Vous n'êtes pas dans l'audience de cette formation.");
    }

    public async Task<TrainingCatalogEnrollment> EnsureEnrollmentAsync(
        Guid catalogItemId,
        Guid employeeId,
        CatalogEnrollmentSource source,
        Guid? sessionId,
        Guid? assignmentId,
        CancellationToken ct,
        IReadOnlyCollection<Guid>? aliases = null)
    {
        var matchIds = aliases is { Count: > 0 }
            ? aliases
            : (IReadOnlyCollection<Guid>)new[] { employeeId };

        var existing = await db.TrainingCatalogEnrollments
            .FirstOrDefaultAsync(e => e.CatalogItemId == catalogItemId && matchIds.Contains(e.EmployeeId), ct);
        if (existing is not null)
        {
            if (source == CatalogEnrollmentSource.Session)
            {
                existing.SessionId ??= sessionId;
                existing.AssignmentId ??= assignmentId;
                if (existing.Source == CatalogEnrollmentSource.SelfService)
                    existing.Source = CatalogEnrollmentSource.Session;
                existing.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
            }

            return existing;
        }

        var catalog = await db.TrainingCatalogItems.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == catalogItemId, ct)
            ?? throw new InvalidOperationException("Formation catalogue introuvable.");

        var now = DateTime.UtcNow;
        var enrollment = new TrainingCatalogEnrollment
        {
            CatalogItemId = catalogItemId,
            EmployeeId = employeeId,
            Source = source,
            SessionId = sessionId,
            AssignmentId = assignmentId,
            DueAt = ComputeDueAt(catalog, now),
            Status = CatalogEnrollmentStatus.NotStarted,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.TrainingCatalogEnrollments.Add(enrollment);
        await db.SaveChangesAsync(ct);
        return enrollment;
    }

    public async Task<IReadOnlyList<MySelfServiceCatalogItemDto>> ListMySelfServiceCatalogAsync(
        Guid employeeId,
        CancellationToken ct,
        string? email = null)
    {
        var aliases = await FormationEmployeeIdentity.ResolveAliasesAsync(db, employeeId, email, ct);
        if (aliases.Count == 0)
            return [];

        var candidates = await db.TrainingCatalogItems.AsNoTracking()
            .Where(c => c.Status == CatalogItemStatus.Published
                        && c.IsActive
                        && c.SelfServiceEnabled)
            .OrderByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);

        var result = new List<MySelfServiceCatalogItemDto>();
        foreach (var item in candidates)
        {
            try
            {
                await EnsureCanAccessCatalogForAliasesAsync(item.Id, aliases, ct);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            var enrollment = await db.TrainingCatalogEnrollments.AsNoTracking()
                .FirstOrDefaultAsync(e => e.CatalogItemId == item.Id && aliases.Contains(e.EmployeeId), ct);

            var requiredLessonIds = await (
                from l in db.TrainingLessons.AsNoTracking()
                join m in db.TrainingModules.AsNoTracking() on l.ModuleId equals m.Id
                where m.CatalogItemId == item.Id && l.IsRequired
                select l.Id).ToListAsync(ct);

            var done = 0;
            if (enrollment is not null && requiredLessonIds.Count > 0)
            {
                done = await db.TrainingLessonProgresses.AsNoTracking()
                    .CountAsync(p => p.EnrollmentId == enrollment.Id
                                     && requiredLessonIds.Contains(p.LessonId)
                                     && p.CompletedAt != null, ct);
            }

            var percent = requiredLessonIds.Count == 0
                ? 100m
                : Math.Round((decimal)done / requiredLessonIds.Count * 100m, 1);

            var status = enrollment?.Status ?? CatalogEnrollmentStatus.NotStarted;
            if (enrollment?.DueAt is DateTime due
                && status != CatalogEnrollmentStatus.Completed
                && due < DateTime.UtcNow)
                status = CatalogEnrollmentStatus.Overdue;

            result.Add(new MySelfServiceCatalogItemDto(
                item.Id,
                item.Title,
                item.Description,
                item.Category,
                enrollment?.Id ?? Guid.Empty,
                status,
                enrollment?.DueAt ?? ComputeDueAt(item, DateTime.UtcNow),
                percent,
                requiredLessonIds.Count,
                done,
                enrollment?.StartedAt,
                enrollment?.CompletedAt));
        }

        return result;
    }

    public async Task<CatalogPlayerDto> GetPlayerByCatalogAsync(
        Guid catalogItemId,
        Guid employeeId,
        CancellationToken ct,
        string? email = null)
    {
        var aliases = await FormationEmployeeIdentity.ResolveAliasesAsync(db, employeeId, email, ct);
        await EnsureCanAccessCatalogForAliasesAsync(catalogItemId, aliases, ct);

        var catalog = await db.TrainingCatalogItems.AsNoTracking()
            .Include(c => c.Modules).ThenInclude(m => m.Lessons).ThenInclude(l => l.Resources)
            .FirstOrDefaultAsync(c => c.Id == catalogItemId, ct)
            ?? throw new InvalidOperationException("Formation catalogue introuvable.");

        if (!catalog.SelfServiceEnabled)
            throw new InvalidOperationException("Cette formation n'est pas disponible en libre accès.");

        if (catalog.Status != CatalogItemStatus.Published)
            throw new InvalidOperationException("Cette formation n'est pas publiée.");

        var canonicalId = FormationEmployeeIdentity.PreferCanonicalEmployeeId(aliases, employeeId);
        var enrollment = await EnsureEnrollmentAsync(
            catalogItemId, canonicalId, CatalogEnrollmentSource.SelfService, null, null, ct, aliases);

        return await BuildPlayerDtoAsync(
            catalog,
            enrollment,
            sessionId: null,
            assignment: null,
            gate: catalog.DefaultGateMode,
            ct);
    }

    public async Task<CatalogPlayerDto> GetPlayerAsync(
        Guid sessionId,
        Guid employeeId,
        CancellationToken ct,
        string? email = null)
    {
        var aliases = await FormationEmployeeIdentity.ResolveAliasesAsync(db, employeeId, email, ct);

        var session = await db.TrainingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session introuvable.");
        if (session.CatalogItemId is null)
            throw new InvalidOperationException("Cette session n'a pas de contenu e-learning lié.");

        var assignment = await db.TrainingAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.SessionId == sessionId && aliases.Contains(a.EmployeeId), ct)
            ?? throw new InvalidOperationException("Vous n'êtes pas affecté à cette séance.");

        await EnsureCanAccessCatalogForAliasesAsync(session.CatalogItemId.Value, aliases, ct);

        var catalog = await db.TrainingCatalogItems.AsNoTracking()
            .Include(c => c.Modules).ThenInclude(m => m.Lessons).ThenInclude(l => l.Resources)
            .FirstOrDefaultAsync(c => c.Id == session.CatalogItemId, ct)
            ?? throw new InvalidOperationException("Formation catalogue introuvable.");

        var enrollment = await EnsureEnrollmentAsync(
            session.CatalogItemId.Value,
            assignment.EmployeeId,
            CatalogEnrollmentSource.Session,
            session.Id,
            assignment.Id,
            ct,
            aliases);

        var gate = session.LearningGateMode ?? catalog.DefaultGateMode;
        return await BuildPlayerDtoAsync(catalog, enrollment, session.Id, assignment, gate, ct);
    }

    public async Task<TrainingLessonDto> CompleteLessonByCatalogAsync(
        Guid catalogItemId,
        Guid lessonId,
        CompleteLessonRequest request,
        CancellationToken ct,
        string? email = null)
    {
        var aliases = await FormationEmployeeIdentity.ResolveAliasesAsync(db, request.EmployeeId, email, ct);
        await EnsureCanAccessCatalogForAliasesAsync(catalogItemId, aliases, ct);

        var catalog = await db.TrainingCatalogItems.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == catalogItemId, ct)
            ?? throw new InvalidOperationException("Formation catalogue introuvable.");
        if (!catalog.SelfServiceEnabled)
            throw new InvalidOperationException("Cette formation n'est pas disponible en libre accès.");

        var lesson = await db.TrainingLessons.AsNoTracking()
            .Include(l => l.Module)
            .FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            ?? throw new InvalidOperationException("Leçon introuvable.");
        if (lesson.Module!.CatalogItemId != catalogItemId)
            throw new InvalidOperationException("Leçon hors catalogue.");

        var canonicalId = FormationEmployeeIdentity.PreferCanonicalEmployeeId(aliases, request.EmployeeId);
        var enrollment = await EnsureEnrollmentAsync(
            catalogItemId, canonicalId, CatalogEnrollmentSource.SelfService, null, null, ct, aliases);

        return await CompleteLessonForEnrollmentAsync(enrollment, lessonId, request.LastResourceId, ct);
    }

    public async Task<TrainingLessonDto> CompleteLessonAsync(
        Guid sessionId,
        Guid lessonId,
        CompleteLessonRequest request,
        CancellationToken ct,
        string? email = null)
    {
        var aliases = await FormationEmployeeIdentity.ResolveAliasesAsync(db, request.EmployeeId, email, ct);

        var session = await db.TrainingSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct)
            ?? throw new InvalidOperationException("Session introuvable.");
        if (session.CatalogItemId is null)
            throw new InvalidOperationException("Session sans catalogue.");

        var assignment = await db.TrainingAssignments
            .FirstOrDefaultAsync(a => a.SessionId == sessionId && aliases.Contains(a.EmployeeId), ct)
            ?? throw new InvalidOperationException("Affectation introuvable.");

        var lesson = await db.TrainingLessons.AsNoTracking()
            .Include(l => l.Module)
            .FirstOrDefaultAsync(l => l.Id == lessonId, ct)
            ?? throw new InvalidOperationException("Leçon introuvable.");
        if (lesson.Module!.CatalogItemId != session.CatalogItemId)
            throw new InvalidOperationException("Leçon hors catalogue de la session.");

        var enrollment = await EnsureEnrollmentAsync(
            session.CatalogItemId.Value,
            assignment.EmployeeId,
            CatalogEnrollmentSource.Session,
            session.Id,
            assignment.Id,
            ct,
            aliases);

        return await CompleteLessonForEnrollmentAsync(enrollment, lessonId, request.LastResourceId, ct);
    }

    public async Task<(bool CanTake, string? Reason)> EvaluateQuizGateAsync(
        TrainingSession session,
        TrainingAssignment assignment,
        Guid? catalogItemId,
        CancellationToken ct)
    {
        var gate = session.LearningGateMode
            ?? (catalogItemId is null ? LearningGateMode.Attendance : LearningGateMode.Content);

        var present = assignment.Status == TrainingAssignmentStatus.Completed;
        var contentOk = true;
        if (gate is LearningGateMode.Content or LearningGateMode.Both)
        {
            if (catalogItemId is null)
                contentOk = true;
            else
            {
                var requiredLessonIds = await (
                    from l in db.TrainingLessons.AsNoTracking()
                    join m in db.TrainingModules.AsNoTracking() on l.ModuleId equals m.Id
                    where m.CatalogItemId == catalogItemId && l.IsRequired
                    select l.Id).ToListAsync(ct);

                if (requiredLessonIds.Count > 0)
                {
                    var enrollment = await db.TrainingCatalogEnrollments.AsNoTracking()
                        .FirstOrDefaultAsync(e =>
                            e.CatalogItemId == catalogItemId && e.EmployeeId == assignment.EmployeeId, ct);

                    var done = enrollment is null
                        ? 0
                        : await db.TrainingLessonProgresses.AsNoTracking()
                            .CountAsync(p => p.EnrollmentId == enrollment.Id
                                             && requiredLessonIds.Contains(p.LessonId)
                                             && p.CompletedAt != null, ct);
                    contentOk = done >= requiredLessonIds.Count;
                }
            }
        }

        return gate switch
        {
            LearningGateMode.Attendance when !present =>
                (false, "Présence requise avant de passer le quiz."),
            LearningGateMode.Content when !contentOk =>
                (false, "Terminez les leçons obligatoires avant de passer le quiz."),
            LearningGateMode.Both when !present =>
                (false, "Présence requise avant de passer le quiz."),
            LearningGateMode.Both when !contentOk =>
                (false, "Terminez les leçons obligatoires avant de passer le quiz."),
            _ => (true, null),
        };
    }

    private async Task<CatalogPlayerDto> BuildPlayerDtoAsync(
        TrainingCatalogItem catalog,
        TrainingCatalogEnrollment enrollment,
        Guid? sessionId,
        TrainingAssignment? assignment,
        LearningGateMode gate,
        CancellationToken ct)
    {
        var progress = await db.TrainingLessonProgresses.AsNoTracking()
            .Where(p => p.EnrollmentId == enrollment.Id)
            .ToDictionaryAsync(p => p.LessonId, ct);

        var modules = catalog.Modules.OrderBy(m => m.SortOrder).Select(m => new TrainingModuleDto(
            m.Id,
            m.CatalogItemId,
            m.Title,
            m.Description,
            m.SortOrder,
            m.Lessons.OrderBy(l => l.SortOrder).Select(l =>
            {
                progress.TryGetValue(l.Id, out var p);
                return new TrainingLessonDto(
                    l.Id,
                    l.ModuleId,
                    l.Title,
                    l.Description,
                    l.SortOrder,
                    l.IsRequired,
                    l.Resources.OrderBy(r => r.SortOrder).Select(MapResource).ToList(),
                    p?.CompletedAt is not null,
                    p?.ProgressPercent ?? 0m);
            }).ToList())).ToList();

        var required = modules.SelectMany(m => m.Lessons).Where(l => l.IsRequired).ToList();
        var done = required.Count(l => l.IsCompleted);
        var percent = required.Count == 0 ? 100m : Math.Round((decimal)done / required.Count * 100m, 1);

        bool canTake;
        string? reason;
        var hasQuiz = catalog.DefaultQuizTemplateId is Guid || sessionId is not null;
        if (assignment is not null && sessionId is Guid sid)
        {
            var session = await db.TrainingSessions.AsNoTracking()
                .FirstAsync(s => s.Id == sid, ct);
            (canTake, reason) = await EvaluateQuizGateAsync(session, assignment, catalog.Id, ct);
        }
        else if (catalog.DefaultQuizTemplateId is Guid)
        {
            canTake = percent >= 100m;
            reason = canTake ? null : "Terminez les leçons obligatoires avant de passer le quiz.";
            if (canTake)
            {
                var catalogQuiz = await db.TrainingQuizzes.AsNoTracking()
                    .FirstOrDefaultAsync(q => q.CatalogItemId == catalog.Id, ct);
                if (catalogQuiz is not null && !catalogQuiz.AllowMultipleAttempts)
                {
                    var alreadyTaken = await db.TrainingQuizAttempts.AsNoTracking()
                        .AnyAsync(a => a.QuizId == catalogQuiz.Id && a.AssignmentId == enrollment.Id, ct);
                    if (alreadyTaken)
                    {
                        canTake = false;
                        reason = "Vous avez déjà passé ce quiz.";
                    }
                }
            }
        }
        else
        {
            canTake = false;
            reason = hasQuiz ? null : "Aucun quiz associé à cette formation.";
        }

        var status = enrollment.Status;
        if (status != CatalogEnrollmentStatus.Completed
            && enrollment.DueAt is DateTime due
            && due < DateTime.UtcNow)
            status = CatalogEnrollmentStatus.Overdue;

        return new CatalogPlayerDto(
            catalog.Id,
            sessionId,
            assignment?.Id ?? enrollment.AssignmentId,
            enrollment.Id,
            catalog.Title,
            catalog.Description,
            catalog.Category,
            gate,
            percent,
            required.Count,
            done,
            canTake,
            reason,
            modules,
            enrollment.DueAt,
            status,
            catalog.DefaultQuizTemplateId);
    }

    private async Task<TrainingLessonDto> CompleteLessonForEnrollmentAsync(
        TrainingCatalogEnrollment enrollment,
        Guid lessonId,
        Guid? lastResourceId,
        CancellationToken ct)
    {
        var progress = await db.TrainingLessonProgresses
            .FirstOrDefaultAsync(p => p.EnrollmentId == enrollment.Id && p.LessonId == lessonId, ct);
        if (progress is null)
        {
            progress = new TrainingLessonProgress
            {
                EnrollmentId = enrollment.Id,
                LessonId = lessonId,
                StartedAt = DateTime.UtcNow,
            };
            db.TrainingLessonProgresses.Add(progress);
        }

        progress.LastResourceId = lastResourceId;
        progress.ProgressPercent = 100m;
        progress.CompletedAt = DateTime.UtcNow;

        var tracked = await db.TrainingCatalogEnrollments
            .FirstAsync(e => e.Id == enrollment.Id, ct);
        tracked.StartedAt ??= DateTime.UtcNow;
        tracked.UpdatedAt = DateTime.UtcNow;
        if (tracked.Status == CatalogEnrollmentStatus.NotStarted
            || tracked.Status == CatalogEnrollmentStatus.Overdue)
            tracked.Status = CatalogEnrollmentStatus.InProgress;

        var requiredLessonIds = await (
            from l in db.TrainingLessons.AsNoTracking()
            join m in db.TrainingModules.AsNoTracking() on l.ModuleId equals m.Id
            where m.CatalogItemId == tracked.CatalogItemId && l.IsRequired
            select l.Id).ToListAsync(ct);

        if (requiredLessonIds.Count > 0)
        {
            var doneIds = await db.TrainingLessonProgresses
                .Where(p => p.EnrollmentId == tracked.Id
                            && requiredLessonIds.Contains(p.LessonId)
                            && p.CompletedAt != null)
                .Select(p => p.LessonId)
                .ToListAsync(ct);
            if (!doneIds.Contains(lessonId) && requiredLessonIds.Contains(lessonId))
                doneIds.Add(lessonId);

            if (doneIds.Count >= requiredLessonIds.Count)
            {
                tracked.Status = CatalogEnrollmentStatus.Completed;
                tracked.CompletedAt ??= DateTime.UtcNow;
            }
        }
        else
        {
            tracked.Status = CatalogEnrollmentStatus.Completed;
            tracked.CompletedAt ??= DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
        return await MapLessonAsync(lessonId, ct, tracked.Id);
    }

    private static DateTime? ComputeDueAt(TrainingCatalogItem catalog, DateTime fromUtc) =>
        catalog.DueMode switch
        {
            CatalogDueMode.Absolute => catalog.DueDate,
            CatalogDueMode.RelativeDays when catalog.DueInDays is int days && days >= 0
                => fromUtc.AddDays(days),
            _ => null,
        };

    public async Task<LearningQuizStatsDto> GetLearningStatsAsync(Guid? catalogItemId, CancellationToken ct)
    {
        var catalogs = await db.TrainingCatalogItems.AsNoTracking()
            .CountAsync(c => c.Status != CatalogItemStatus.Archived, ct);
        var sessionsQuery = db.TrainingSessions.AsNoTracking()
            .Where(s => s.CatalogItemId != null);
        if (catalogItemId is Guid cidFilter)
            sessionsQuery = sessionsQuery.Where(s => s.CatalogItemId == cidFilter);

        var sessions = await sessionsQuery
            .Select(s => new { s.Id, s.Title, s.CatalogItemId })
            .ToListAsync(ct);
        var sessionIds = sessions.Select(s => s.Id).ToList();
        var quizzes = await db.TrainingQuizzes.AsNoTracking()
            .Include(q => q.Questions)
            .Where(q => q.SessionId != null && sessionIds.Contains(q.SessionId.Value))
            .ToListAsync(ct);
        var quizIds = quizzes.Select(q => q.Id).ToList();
        var attempts = await db.TrainingQuizAttempts.AsNoTracking()
            .Where(a => quizIds.Contains(a.QuizId) && a.FinalScore != null)
            .ToListAsync(ct);

        var categories = await db.TrainingCatalogItems.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Category, ct);

        double avg = attempts.Count == 0 ? 0 : Math.Round(attempts.Average(a => (double)(a.FinalScore ?? 0)), 1);
        double best = attempts.Count == 0 ? 0 : Math.Round(attempts.Max(a => (double)(a.FinalScore ?? 0)), 1);
        var graded = attempts.Where(a => a.IsGraded && a.Passed != null).ToList();
        double passRate = graded.Count == 0
            ? 0
            : Math.Round(graded.Count(a => a.Passed == true) * 100.0 / graded.Count, 1);

        var bySession = sessions.Select(s =>
        {
            var quiz = quizzes.FirstOrDefault(q => q.SessionId == s.Id);
            var atts = quiz is null ? [] : attempts.Where(a => a.QuizId == quiz.Id).ToList();
            var g = atts.Where(a => a.IsGraded && a.Passed != null).ToList();
            string? cat = null;
            if (s.CatalogItemId is Guid cid)
                categories.TryGetValue(cid, out cat);
            return new LearningQuizStatsBySessionDto(
                s.Id,
                s.CatalogItemId,
                s.Title,
                cat,
                quiz?.Questions.Count ?? 0,
                atts.Count,
                atts.Count == 0 ? 0 : Math.Round(atts.Average(a => (double)(a.FinalScore ?? 0)), 1),
                atts.Count == 0 ? 0 : Math.Round(atts.Max(a => (double)(a.FinalScore ?? 0)), 1),
                g.Count == 0 ? 0 : Math.Round(g.Count(a => a.Passed == true) * 100.0 / g.Count, 1));
        }).ToList();

        return new LearningQuizStatsDto(
            catalogs,
            sessions.Count,
            quizzes.Sum(q => q.Questions.Count),
            attempts.Count,
            avg,
            best,
            passRate,
            bySession);
    }

    public async Task<IReadOnlyList<LearningQuizResultExportRowDto>> ExportResultsAsync(
        Guid? sessionId,
        Guid? catalogItemId,
        CancellationToken ct)
    {
        var q = db.TrainingQuizAttempts.AsNoTracking().AsQueryable();
        if (sessionId is Guid sid)
        {
            var quiz = await db.TrainingQuizzes.AsNoTracking()
                .FirstOrDefaultAsync(x => x.SessionId == sid, ct);
            if (quiz is null) return [];
            q = q.Where(a => a.QuizId == quiz.Id);
        }
        else if (catalogItemId is Guid cid)
        {
            var sessionIdsForCatalog = await db.TrainingSessions.AsNoTracking()
                .Where(s => s.CatalogItemId == cid)
                .Select(s => s.Id)
                .ToListAsync(ct);
            var quizIdsForCatalog = await db.TrainingQuizzes.AsNoTracking()
                .Where(x => x.SessionId != null && sessionIdsForCatalog.Contains(x.SessionId.Value))
                .Select(x => x.Id)
                .ToListAsync(ct);
            q = q.Where(a => quizIdsForCatalog.Contains(a.QuizId));
        }

        var attempts = await q.OrderByDescending(a => a.SubmittedAt).ToListAsync(ct);
        var assignmentIds = attempts.Select(a => a.AssignmentId).Distinct().ToList();
        var assignments = await db.TrainingAssignments.AsNoTracking()
            .Where(a => assignmentIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, ct);
        var sessionIds = assignments.Values.Select(a => a.SessionId).Distinct().ToList();
        var sessions = await db.TrainingSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        var employeeIds = attempts.Select(a => a.EmployeeId).Distinct().ToList();
        var employees = await db.EmployeAnnuaires.AsNoTracking()
            .Where(e => employeeIds.Contains(e.EmployeId))
            .ToDictionaryAsync(e => e.EmployeId, ct);

        return attempts.Select(a =>
        {
            assignments.TryGetValue(a.AssignmentId, out var asg);
            sessions.TryGetValue(asg?.SessionId ?? Guid.Empty, out var sess);
            employees.TryGetValue(a.EmployeeId, out var emp);
            return new LearningQuizResultExportRowDto(
                asg?.EmployeeName ?? (emp is null ? "" : $"{emp.Prenom} {emp.Nom}".Trim()),
                emp?.Email ?? "",
                emp?.Role ?? "",
                emp?.StructureKey ?? "",
                sess?.Id,
                sess?.Title ?? "",
                sess?.CatalogItemId,
                a.FinalScore,
                a.Passed,
                a.AttemptNumber,
                a.SubmittedAt);
        }).ToList();
    }

    public async Task<IReadOnlyList<EmployeAnnuaire>> ResolveAudienceAsync(Guid catalogItemId, CancellationToken ct)
    {
        var item = await db.TrainingCatalogItems.AsNoTracking()
            .Include(c => c.AudienceRules)
            .FirstOrDefaultAsync(c => c.Id == catalogItemId, ct);
        if (item is null) return [];

        var rule = item.AudienceRules.FirstOrDefault();
        var roles = ParseStringList(rule?.RolesJson);
        var structures = ParseStringList(rule?.StructureKeysJson);
        var users = ParseGuidList(rule?.UserIdsJson);

        // Public: no filters.
        if (roles.Count == 0 && structures.Count == 0 && users.Count == 0)
            return await db.EmployeAnnuaires.AsNoTracking().ToListAsync(ct);

        var all = await db.EmployeAnnuaires.AsNoTracking().ToListAsync(ct);
        return all.Where(e => AudienceResolver.Matches(e, roles, structures, users, item.AudienceMatchMode)).ToList();
    }

    private async Task NotifyAudienceCatalogAvailableAsync(
        Guid catalogItemId,
        string catalogTitle,
        CancellationToken ct,
        IReadOnlyList<EmployeAnnuaire>? beneficiaries = null)
    {
        try
        {
            beneficiaries ??= await ResolveAudienceAsync(catalogItemId, ct);
            var title = string.IsNullOrWhiteSpace(catalogTitle) ? "une formation" : catalogTitle.Trim();
            var now = DateTime.UtcNow;
            foreach (var emp in beneficiaries)
            {
                if (emp.EmployeId == Guid.Empty) continue;
                await publish.Publish(new CatalogFormationAvailableMessage
                {
                    CatalogItemId = catalogItemId,
                    CatalogTitle = title,
                    EmployeeId = emp.EmployeId,
                    EmployeeName = $"{emp.Prenom} {emp.Nom}".Trim(),
                    PublishedAt = now,
                }, ct);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Échec notification audience pour catalogue {CatalogItemId}.", catalogItemId);
        }
    }

    private async Task<IReadOnlyList<TrainingCatalogItemDto>> MapListAsync(
        IReadOnlyList<TrainingCatalogItem> items,
        bool includeTree,
        CancellationToken ct)
    {
        var ids = items.Select(i => i.Id).ToList();
        var modules = await db.TrainingModules.AsNoTracking()
            .Where(m => ids.Contains(m.CatalogItemId))
            .ToListAsync(ct);
        var moduleIds = modules.Select(m => m.Id).ToList();
        var lessons = await db.TrainingLessons.AsNoTracking()
            .Where(l => moduleIds.Contains(l.ModuleId))
            .ToListAsync(ct);
        var lessonIds = lessons.Select(l => l.Id).ToList();
        var resources = await db.TrainingResources.AsNoTracking()
            .Where(r => lessonIds.Contains(r.LessonId))
            .ToListAsync(ct);
        var rules = await db.TrainingCatalogAudienceRules.AsNoTracking()
            .Where(r => ids.Contains(r.CatalogItemId))
            .ToListAsync(ct);

        var result = new List<TrainingCatalogItemDto>();
        foreach (var item in items)
        {
            var itemModules = modules.Where(m => m.CatalogItemId == item.Id).ToList();
            var itemLessons = lessons.Where(l => itemModules.Any(m => m.Id == l.ModuleId)).ToList();
            var itemResources = resources.Where(r => itemLessons.Any(l => l.Id == r.LessonId)).ToList();
            var rule = rules.FirstOrDefault(r => r.CatalogItemId == item.Id);
            var audience = new TrainingCatalogAudienceDto(
                item.AudienceMatchMode,
                ParseStringList(rule?.RolesJson),
                ParseStringList(rule?.StructureKeysJson),
                ParseGuidList(rule?.UserIdsJson),
                0);

            IReadOnlyList<TrainingModuleDto>? tree = null;
            if (includeTree)
            {
                tree = itemModules.OrderBy(m => m.SortOrder).Select(m => new TrainingModuleDto(
                    m.Id,
                    m.CatalogItemId,
                    m.Title,
                    m.Description,
                    m.SortOrder,
                    itemLessons.Where(l => l.ModuleId == m.Id).OrderBy(l => l.SortOrder).Select(l =>
                        new TrainingLessonDto(
                            l.Id,
                            l.ModuleId,
                            l.Title,
                            l.Description,
                            l.SortOrder,
                            l.IsRequired,
                            itemResources.Where(r => r.LessonId == l.Id).OrderBy(r => r.SortOrder)
                                .Select(MapResource).ToList())).ToList())).ToList();
            }

            result.Add(new TrainingCatalogItemDto(
                item.Id,
                item.Title,
                item.Description,
                item.Category,
                item.Status,
                item.IsActive,
                item.DefaultGateMode,
                item.AudienceMatchMode,
                item.CreatedAt,
                item.UpdatedAt,
                item.PublishedAt,
                item.ArchivedAt,
                itemModules.Count,
                itemLessons.Count,
                itemResources.Count,
                audience,
                tree,
                item.SelfServiceEnabled,
                item.DueMode,
                item.DueDate,
                item.DueInDays,
                item.DefaultQuizTemplateId));
        }

        return result;
    }

    private async Task<TrainingModuleDto> MapModuleAsync(Guid moduleId, CancellationToken ct)
    {
        var m = await db.TrainingModules.AsNoTracking()
            .Include(x => x.Lessons).ThenInclude(l => l.Resources)
            .FirstAsync(x => x.Id == moduleId, ct);
        return new TrainingModuleDto(
            m.Id,
            m.CatalogItemId,
            m.Title,
            m.Description,
            m.SortOrder,
            m.Lessons.OrderBy(l => l.SortOrder).Select(l => new TrainingLessonDto(
                l.Id,
                l.ModuleId,
                l.Title,
                l.Description,
                l.SortOrder,
                l.IsRequired,
                l.Resources.OrderBy(r => r.SortOrder).Select(MapResource).ToList())).ToList());
    }

    private async Task<TrainingLessonDto> MapLessonAsync(Guid lessonId, CancellationToken ct, Guid? enrollmentId = null)
    {
        var l = await db.TrainingLessons.AsNoTracking()
            .Include(x => x.Resources)
            .FirstAsync(x => x.Id == lessonId, ct);
        bool completed = false;
        decimal percent = 0m;
        if (enrollmentId is Guid eid)
        {
            var p = await db.TrainingLessonProgresses.AsNoTracking()
                .FirstOrDefaultAsync(x => x.EnrollmentId == eid && x.LessonId == lessonId, ct);
            completed = p?.CompletedAt is not null;
            percent = p?.ProgressPercent ?? 0m;
        }

        return new TrainingLessonDto(
            l.Id,
            l.ModuleId,
            l.Title,
            l.Description,
            l.SortOrder,
            l.IsRequired,
            l.Resources.OrderBy(r => r.SortOrder).Select(MapResource).ToList(),
            completed,
            percent);
    }

    private static TrainingResourceDto MapResource(TrainingResource r) =>
        new(
            r.Id,
            r.LessonId,
            r.Type,
            r.Title,
            r.Url,
            r.ContentType,
            r.FileName,
            r.TextContent,
            r.SortOrder,
            r.DurationMinutes,
            string.IsNullOrWhiteSpace(r.StoragePath) ? null : $"/api/formations/catalog/resources/file/{r.Id}");

    private async Task TouchCatalogAsync(Guid catalogItemId, CancellationToken ct)
    {
        var item = await db.TrainingCatalogItems.FirstOrDefaultAsync(c => c.Id == catalogItemId, ct);
        if (item is null) return;
        item.UpdatedAt = DateTime.UtcNow;
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        try { File.Delete(path); } catch { /* ignore */ }
    }

    private static List<string> NormalizeStrings(IEnumerable<string>? values) =>
        (values ?? []).Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch { return []; }
    }

    private static List<Guid> ParseGuidList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<Guid>>(json) ?? []; }
        catch { return []; }
    }
}
