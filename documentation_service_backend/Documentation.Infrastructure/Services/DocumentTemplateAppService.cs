using System.Text.Json;
using Documentation.Application;
using Documentation.Application.Abstractions;
using Documentation.Application.Api;
using Documentation.Application.Configuration;
using Documentation.Application.DocumentTemplates;
using Documentation.Domain.Entities;
using Documentation.Infrastructure.Persistence;
using Documentation.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Documentation.Infrastructure.Services;

public sealed class DocumentTemplateAppService(
    DocumentationDbContext db,
    IDocumentationTenantAccessor tenantAccessor,
    IDocumentationRequestContext userContext,
    IDocumentTemplateVariableMergeService variableMerge,
    ITemplateEngineService templateEngine,
    ITemplatePlaceholderNormalizationService placeholderNormalization,
    IOriginalDocxTemplateRenderService originalDocxTemplateRender,
    IDocumentTemplateManagementService templateManagement,
    IDocumentTemplateEmployeeMappingService employeeMapping,
    ITemplateBlobStorage templateBlobStorage,
    IRibValidationService ribValidation,
    IOptions<DocumentWorkflowOptions> documentWorkflowOptions,
    IDocumentWorkflowGenerationAppService workflowGeneration,
    ILogger<DocumentTemplateAppService> logger) : IDocumentTemplateAppService
{
    private const int MaxTemplateContentLength = 100_000;
    private const int MaxTemplateVariables = 100;

    public async Task<IReadOnlyList<DocumentTemplateListItemResponse>> GetDocumentTemplatesAsync(CancellationToken ct = default)
    {
        var rows = await db.DocumentTemplates.AsNoTracking()
            .OrderBy(t => t.Code)
            .Select(t => new
            {
                t.Id,
                t.Code,
                t.Name,
                t.Source,
                t.IsActive,
                t.DocumentTypeId,
                DocumentTypeName = t.DocumentType != null ? t.DocumentType.Name : null,
                t.CurrentVersionId,
                CurrentVersionNumber = t.CurrentVersion != null ? (int?)t.CurrentVersion.VersionNumber : null,
                t.UpdatedAt,
                t.Description,
                t.Kind,
                t.RequiresPilotUpload,
                FileUrl = t.CurrentVersion != null ? t.CurrentVersion.OriginalAssetUri : null,
                CreatedAt = t.CurrentVersion != null ? (DateTimeOffset?)t.CurrentVersion.CreatedAt : null,
            })
            .ToListAsync(ct);
        var templateIds = rows.Select(t => t.Id).ToArray();
        Dictionary<Guid, List<string>> variableNamesByTemplate = new();
        if (templateIds.Length > 0)
        {
            var varRows = await db.DocumentTemplateVariables.AsNoTracking()
                .Where(v => templateIds.Contains(v.TemplateId))
                .OrderBy(v => v.TemplateId)
                .ThenBy(v => v.SortOrder)
                .Select(v => new { v.TemplateId, v.VariableName })
                .ToListAsync(ct);
            foreach (var g in varRows.GroupBy(x => x.TemplateId))
                variableNamesByTemplate[g.Key] = g.Select(x => x.VariableName).ToList();
        }

        return rows.Select(t => new DocumentTemplateListItemResponse(
            t.Id.ToString(),
            t.Code,
            t.Name,
            t.Source,
            TemplateKindToApi(t.Kind),
            t.RequiresPilotUpload,
            t.IsActive,
            t.DocumentTypeId?.ToString(),
            t.DocumentTypeName,
            variableNamesByTemplate.GetValueOrDefault(t.Id, new List<string>()),
            t.CurrentVersionId?.ToString(),
            t.CurrentVersionNumber,
            t.UpdatedAt.ToString("O"),
            t.Description,
            t.FileUrl,
            t.CreatedAt?.ToString("O"))).ToList();
    }

    public async Task<DocumentTemplateDetailResponse> GetDocumentTemplateAsync(Guid id, CancellationToken ct = default)
    {
        var template = await db.DocumentTemplates
            .AsNoTracking()
            .Include(t => t.DocumentType)
            .Include(t => t.CurrentVersion)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            throw new DocumentationApiException(404, "Template introuvable.");

        var cv = template.CurrentVersion ?? await db.DocumentTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == template.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        DocumentTemplateVersionResponse? versionDto = null;
        if (cv is not null)
        {
            var varRows = await db.DocumentTemplateVariables.AsNoTracking()
                .Where(v => v.TemplateVersionId == cv.Id)
                .OrderBy(v => v.SortOrder)
                .ToListAsync(ct);
            var vars = await employeeMapping
                .EnrichAsync(varRows.Select(DocumentTemplateVariableMapper.ToResponse).ToList(), ct)
                .ConfigureAwait(false);
            versionDto = new DocumentTemplateVersionResponse(
                cv.Id.ToString(),
                cv.VersionNumber,
                cv.Status,
                SanitizeForJson(cv.StructuredContent),
                cv.OriginalAssetUri,
                cv.CreatedAt.ToString("O"),
                cv.PublishedAt?.ToString("O"),
                vars);
        }

        return new DocumentTemplateDetailResponse(
            template.Id.ToString(),
            template.Code,
            template.Name,
            template.Source,
            TemplateKindToApi(template.Kind),
            template.RequiresPilotUpload,
            template.IsActive,
            template.DocumentTypeId?.ToString(),
            template.DocumentType?.Name,
            template.UpdatedAt.ToString("O"),
            template.Description,
            versionDto);
    }

    public async Task<TemplateSourceFileUrlResponse> GetTemplateSourceFileUrlAsync(Guid id, CancellationToken ct = default)
    {
        var (cv, _) = await LoadTemplateCurrentVersionAsync(id, ct);
        if (cv is null || string.IsNullOrWhiteSpace(cv.OriginalAssetUri))
            throw new DocumentationApiException(404, "Aucun fichier source stocké pour ce modèle.");

        if (!templateBlobStorage.IsConfigured)
            throw new DocumentationApiException(400, "MinIO / S3 n'est pas configuré (DocumentTemplates:Minio).");

        var lifetime = TimeSpan.FromMinutes(15);
        var signed = templateBlobStorage.TryGetPresignedGetUrl(cv.OriginalAssetUri, lifetime);
        if (string.IsNullOrEmpty(signed))
        {
            throw new DocumentationApiException(400,
                "Impossible de signer l'URL du fichier (URI source non reconnue pour ce bucket ou autre hôte). " +
                "Vérifiez DocumentTemplates:Minio:Bucket et que l'URL en base correspond au format path-style du dépôt.");
        }

        return new TemplateSourceFileUrlResponse(signed, DateTimeOffset.UtcNow.Add(lifetime).ToString("O"));
    }

    public async Task<TemplateFileExportDto> GetTemplateSourceFileAsync(Guid id, CancellationToken ct = default)
    {
        var (cv, _) = await LoadTemplateCurrentVersionAsync(id, ct);
        if (cv is null || string.IsNullOrWhiteSpace(cv.OriginalAssetUri))
            throw new DocumentationApiException(404, "Aucun fichier source stocké pour ce modèle.");

        if (!templateBlobStorage.IsConfigured)
            throw new DocumentationApiException(400, "MinIO / S3 n'est pas configuré (DocumentTemplates:Minio).");

        var payload = await templateBlobStorage.TryReadObjectAsync(cv.OriginalAssetUri, ct);
        if (payload is null)
            throw new DocumentationApiException(404, "Fichier introuvable dans MinIO ou trop volumineux (limite 52 Mo).");

        var star = Uri.EscapeDataString(payload.FileName);
        return new TemplateFileExportDto(
            payload.Content,
            payload.ContentType,
            payload.FileName,
            new Dictionary<string, string>
            {
                ["Content-Disposition"] = $"inline; filename=\"file\"; filename*=UTF-8''{star}",
            });
    }

    public async Task<TemplateFileExportDto> GetTemplatePreviewAsync(Guid id, CancellationToken ct = default)
    {
        var (cv, _) = await LoadTemplateCurrentVersionAsync(id, ct);
        if (cv is null || string.IsNullOrWhiteSpace(cv.OriginalAssetUri))
            throw new DocumentationApiException(404, "Aucun fichier source stocké pour ce modèle.");

        if (!templateBlobStorage.IsConfigured)
            throw new DocumentationApiException(400, "MinIO / S3 n'est pas configuré (DocumentTemplates:Minio).");

        var payload = await templateBlobStorage.TryReadObjectAsync(cv.OriginalAssetUri, ct);
        if (payload is null)
            throw new DocumentationApiException(404, "Fichier introuvable dans MinIO ou trop volumineux (limite 52 Mo).");

        var fileName = payload.FileName;
        var contentType = payload.ContentType;
        var bytes = payload.Content;
        var lower = fileName.ToLowerInvariant();
        var headers = new Dictionary<string, string>();
        var star = Uri.EscapeDataString(fileName);
        headers["Content-Disposition"] = $"inline; filename=\"file\"; filename*=UTF-8''{star}";

        if (lower.EndsWith(".docx", StringComparison.Ordinal)
            || contentType.Contains("wordprocessingml", StringComparison.OrdinalIgnoreCase))
        {
            var emptyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bytes = originalDocxTemplateRender.Render(bytes, emptyValues);
            contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
            headers["X-Preview-Source"] = "original-docx";
        }
        else
        {
            headers["X-Preview-Source"] = "source-file";
        }

        return new TemplateFileExportDto(bytes, contentType, fileName, headers);
    }

    public async Task<DocumentTemplateDetailResponse> CreateDocumentTemplateAsync(
        CreateDocumentTemplateRequest body,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();
        if (string.IsNullOrWhiteSpace(body.Code) || string.IsNullOrWhiteSpace(body.Name))
            throw new DocumentationApiException(400, "Code et nom du template sont obligatoires.");
        if (!IsValidTemplateCode(body.Code))
            throw new DocumentationApiException(400, "Le code template doit contenir uniquement lettres/chiffres/souligné/tiret.");

        var normalizedSource = NormalizeSource(body.Source);
        var kind = ParseTemplateKind(body.Kind);
        if (normalizedSource is "AI_GENERATED" or "RULE_BASED")
            kind = DocumentTemplateKind.Dynamic;

        var structuredToSave = body.StructuredContent ?? "";
        IReadOnlyList<TemplateVariableInput> variablesToSave = body.Variables;

        if (kind == DocumentTemplateKind.Static)
        {
            if (string.IsNullOrWhiteSpace(body.OriginalAssetUri))
                throw new DocumentationApiException(400, "originalAssetUri est obligatoire pour un modèle statique (fichier MinIO / S3).");
            if (string.IsNullOrWhiteSpace(structuredToSave))
                structuredToSave = "{}";
            variablesToSave = Array.Empty<TemplateVariableInput>();
        }
        else
        {
            if (normalizedSource == "AI_GENERATED" && string.IsNullOrWhiteSpace(structuredToSave))
                throw new DocumentationApiException(400, "Le contenu structuré est obligatoire pour un template généré par IA.");
            if (normalizedSource == "UPLOAD" && string.IsNullOrWhiteSpace(body.OriginalAssetUri))
                throw new DocumentationApiException(400, "L'URL ou le chemin du fichier (originalAssetUri) est obligatoire pour un template uploadé.");
        }

        if (structuredToSave.Length > MaxTemplateContentLength)
            throw new DocumentationApiException(400, "Contenu template trop volumineux.");

        var now = DateTimeOffset.UtcNow;
        var template = new DocumentTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenantAccessor.ResolvedTenantId,
            Code = body.Code.Trim().ToUpperInvariant(),
            Name = body.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            Source = normalizedSource,
            Kind = kind,
            RequiresPilotUpload = body.RequiresPilotUpload,
            IsActive = true,
            DocumentTypeId = body.DocumentTypeId,
            UpdatedAt = now,
        };
        db.DocumentTemplates.Add(template);
        await db.SaveChangesAsync(ct);

        var version = await CreateTemplateVersionInternalAsync(
            template,
            structuredToSave,
            "published",
            body.OriginalAssetUri,
            variablesToSave,
            userContext.UserId,
            ct);

        template.CurrentVersionId = version.Id;
        await db.SaveChangesAsync(ct);
        return await GetDocumentTemplateAsync(template.Id, ct);
    }

    public async Task<DocumentTemplateDetailResponse> UpdateDocumentTemplateAsync(
        Guid id,
        UpdateDocumentTemplateRequest body,
        CancellationToken ct = default)
    {
        var template = await db.DocumentTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            throw new DocumentationApiException(404, "Template introuvable.");

        if (!string.IsNullOrWhiteSpace(body.Name))
            template.Name = body.Name.Trim();
        if (body.Description is not null)
            template.Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim();
        template.DocumentTypeId = body.DocumentTypeId;
        if (body.RequiresPilotUpload.HasValue)
            template.RequiresPilotUpload = body.RequiresPilotUpload.Value;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return await GetDocumentTemplateAsync(id, ct);
    }

    public async Task<DocumentTemplateDetailResponse> UpdateDocumentTemplateStatusAsync(
        Guid id,
        UpdateTemplateStatusRequest body,
        CancellationToken ct = default)
    {
        var template = await db.DocumentTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            throw new DocumentationApiException(404, "Template introuvable.");

        template.IsActive = body.IsActive;
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetDocumentTemplateAsync(id, ct);
    }

    public async Task DeleteDocumentTemplateAsync(Guid id, CancellationToken ct = default)
    {
        EnsureAuthenticated();
        if (userContext.Role is not (AppRole.Rh or AppRole.Admin))
            throw new DocumentationApiException(403, "Accès refusé.");

        var template = await db.DocumentTemplates
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            throw new DocumentationApiException(404, "Template introuvable.");

        var linkedRequests = await db.DocumentRequests
            .Where(r => r.DocumentTemplateId == id)
            .ToListAsync(ct);
        if (linkedRequests.Count > 0)
        {
            var blocked = linkedRequests
                .Where(r => r.Status is not (DocumentRequestStatus.Rejected or DocumentRequestStatus.Cancelled))
                .ToList();
            if (blocked.Count > 0)
            {
                throw new DocumentationApiException(400,
                    $"Suppression refusée : template lié à {blocked.Count} demande(s) active(s) (pending/approved/generated).");
            }
        }

        var versionIds = template.Versions.Select(v => v.Id).ToArray();
        if (versionIds.Length > 0)
        {
            var usedInGenerated = await db.GeneratedDocuments.AsNoTracking()
                .AnyAsync(g => g.TemplateVersionId.HasValue && versionIds.Contains(g.TemplateVersionId.Value), ct);
            if (usedInGenerated)
                throw new DocumentationApiException(400, "Suppression refusée : ce template possède déjà des documents générés.");
        }

        if (linkedRequests.Count > 0)
        {
            var requestIds = linkedRequests.Select(r => r.Id).ToArray();
            var fieldRows = await db.DocumentRequestFieldValues
                .Where(f => requestIds.Contains(f.DocumentRequestId))
                .ToListAsync(ct);
            if (fieldRows.Count > 0)
                db.DocumentRequestFieldValues.RemoveRange(fieldRows);

            var now = DateTimeOffset.UtcNow;
            foreach (var req in linkedRequests)
            {
                req.DocumentTemplateId = null;
                req.UpdatedAt = now;
            }
        }

        db.DocumentTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
    }

    public async Task<DocumentTemplateDetailResponse> UploadTemplateFromFileAsync(
        IFormFile file,
        string code,
        string name,
        string? description,
        Guid? documentTypeId,
        bool staticDocument,
        bool requiresPilotUpload,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();
        if (file is null || file.Length == 0)
            throw new DocumentationApiException(400, "Fichier « file » requis (multipart/form-data).");

        try
        {
            return await templateManagement.CreateFromUploadedFileAsync(
                userContext.UserId!.Value,
                code,
                name,
                description,
                documentTypeId,
                file,
                staticDocument: staticDocument,
                requiresPilotUpload: requiresPilotUpload,
                cancellationToken: ct);
        }
        catch (InvalidOperationException ex)
        {
            throw MapTemplateUploadException(ex);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            throw MapTemplateUploadException(new InvalidOperationException(ex.InnerException?.Message ?? ex.Message, ex));
        }
        catch (ArgumentException ex)
        {
            throw new DocumentationApiException(400, ex.Message);
        }
    }

    public async Task<DocumentTemplateDetailResponse> UploadTemplateFromJsonAsync(
        UploadTemplateRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.Content))
            throw new DocumentationApiException(400, "Le contenu du fichier est requis pour analyse (mode JSON).");
        if (body.Content.Length > MaxTemplateContentLength)
            throw new DocumentationApiException(400, "Fichier trop volumineux pour l'analyse V1.");

        var detected = placeholderNormalization.ExtractPlaceholders(body.Content);
        var vars = detected.Select(v => new TemplateVariableInput
        {
            Name = v.CanonicalKey,
            Type = v.Type,
            IsRequired = v.IsRequired,
            ValidationRule = v.ValidationRule,
            DisplayLabel = v.SuggestedLabel,
            FormScope = placeholderNormalization.IsDatabaseBackedKey(v.CanonicalKey) ? "db" : "hr",
            SourcePriority = placeholderNormalization.IsDatabaseBackedKey(v.CanonicalKey) ? 10 : 20,
            NormalizedName = v.NormalizedKey,
            RawPlaceholder = v.RawToken,
        }).ToList();

        var req = new CreateDocumentTemplateRequest
        {
            Code = body.Code,
            Name = body.Name,
            Description = string.IsNullOrWhiteSpace(body.Description) ? null : body.Description.Trim(),
            DocumentTypeId = body.DocumentTypeId,
            Source = "UPLOAD",
            StructuredContent = body.Content,
            OriginalAssetUri = string.IsNullOrWhiteSpace(body.FileName) ? "inline-text" : body.FileName.Trim(),
            Variables = vars,
        };

        return await CreateDocumentTemplateAsync(req, ct);
    }

    public Task<InternalEngineAnalysisResponse> AnalyzeInternalEngineTemplateAsync(InternalEngineTemplateRequest body)
    {
        EnsureAuthenticated();
        EnsureRhOrAdmin();
        if (string.IsNullOrWhiteSpace(body.StructuredContent))
            throw new DocumentationApiException(400, "structuredContent est obligatoire.");

        var placeholders = placeholderNormalization.ExtractPlaceholders(body.StructuredContent);
        var variables = placeholders.Select(p => new TemplateVariableInput
        {
            Name = p.CanonicalKey,
            Type = p.Type,
            IsRequired = p.IsRequired,
            ValidationRule = p.ValidationRule,
            DisplayLabel = p.SuggestedLabel,
            FormScope = placeholderNormalization.IsDatabaseBackedKey(p.CanonicalKey) ? "db" : "hr",
            SourcePriority = placeholderNormalization.IsDatabaseBackedKey(p.CanonicalKey) ? 10 : 20,
            NormalizedName = p.NormalizedKey,
            RawPlaceholder = p.RawToken,
        }).ToList();

        return Task.FromResult(new InternalEngineAnalysisResponse(
            body.StructuredContent,
            placeholders.Select(p => new InternalEnginePlaceholderResponse(
                p.RawToken,
                p.NormalizedKey,
                p.CanonicalKey,
                p.Status,
                p.SuggestedLabel,
                p.Type,
                p.IsRequired,
                p.ValidationRule)).ToList(),
            variables));
    }

    public async Task<DocumentTemplateDetailResponse> CreateInternalEngineTemplateAsync(
        InternalEngineTemplateRequest body,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();
        EnsureRhOrAdmin();
        if (string.IsNullOrWhiteSpace(body.StructuredContent))
            throw new DocumentationApiException(400, "structuredContent est obligatoire.");
        if (string.IsNullOrWhiteSpace(body.Name))
            throw new DocumentationApiException(400, "Le nom est obligatoire.");

        try
        {
            return await templateManagement.CreateFromInternalEngineAsync(
                userContext.UserId!.Value,
                body.Code,
                body.Name,
                body.Description,
                body.DocumentTypeId,
                body.StructuredContent,
                body.Variables,
                ct);
        }
        catch (InvalidOperationException ex)
        {
            throw MapTemplateUploadException(ex);
        }
        catch (ArgumentException ex)
        {
            throw new DocumentationApiException(400, ex.Message);
        }
    }

    public async Task<DocumentTemplateDetailResponse> GenerateTemplateFromAiAsync(
        AiGenerateTemplateRequest body,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();
        if (string.IsNullOrWhiteSpace(body.Description))
            throw new DocumentationApiException(400, "La description est obligatoire pour la génération IA.");
        if (body.Description.Length > 4000)
            throw new DocumentationApiException(400, "Description trop longue (max 4000 caractères).");

        try
        {
            return await templateManagement.CreateFromAiDescriptionAsync(
                userContext.UserId!.Value,
                body.Description,
                body.Name,
                body.Code,
                body.DocumentTypeId,
                ct);
        }
        catch (InvalidOperationException ex)
        {
            throw MapTemplateUploadException(ex);
        }
        catch (ArgumentException ex)
        {
            throw new DocumentationApiException(400, ex.Message);
        }
    }

    public async Task<DocumentTemplateDetailResponse> GenerateRuleBasedTemplateAsync(
        RuleGenerateTemplateRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.Description))
            throw new DocumentationApiException(400, "La description RH est obligatoire.");
        if (body.Description.Length > 2000)
            throw new DocumentationApiException(400, "Description trop longue.");

        var names = body.SuggestedVariables.Count == 0
            ? new[] { "nom", "prenom", "cin", "poste", "salaire", "date_embauche", "departement", "date" }
            : body.SuggestedVariables;
        var content = templateEngine.BuildRuleBasedContent(body.Description.Trim(), names);
        var vars = templateEngine.DetectVariables(content).Select(v => new TemplateVariableInput
        {
            Name = v.Name,
            Type = v.Type,
            IsRequired = v.IsRequired,
            ValidationRule = v.ValidationRule,
        }).ToList();

        var req = new CreateDocumentTemplateRequest
        {
            Code = body.Code,
            Name = body.Name,
            Description = body.Description.Trim(),
            DocumentTypeId = body.DocumentTypeId,
            Source = "RULE_BASED",
            StructuredContent = content,
            Variables = vars,
        };

        return await CreateDocumentTemplateAsync(req, ct);
    }

    public async Task<DocumentTemplateVersionResponse> CreateTemplateVersionAsync(
        Guid id,
        CreateTemplateVersionRequest body,
        CancellationToken ct = default)
    {
        logger.LogDebug(
            "CreateTemplateVersion:entry templateId={TemplateId} status={Status} contentLen={Len} vars={VarCount}",
            id,
            body.Status,
            body.StructuredContent?.Length ?? 0,
            body.Variables?.Count ?? 0);

        var template = await db.DocumentTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            throw new DocumentationApiException(404, "Template introuvable.");

        logger.LogDebug(
            "CreateTemplateVersion:template loaded templateId={TemplateId} kind={Kind} code={Code}",
            template.Id,
            template.Kind,
            template.Code);

        if (string.IsNullOrWhiteSpace(body.StructuredContent))
            throw new DocumentationApiException(400, "structuredContent est obligatoire.");
        if (body.StructuredContent.Length > MaxTemplateContentLength)
            throw new DocumentationApiException(400, "structuredContent trop volumineux.");

        var status = NormalizeVersionStatus(body.Status);
        IReadOnlyList<TemplateVariableInput> vars;
        if (template.Kind == DocumentTemplateKind.Static)
            vars = Array.Empty<TemplateVariableInput>();
        else if (body.Variables is null || body.Variables.Count == 0)
        {
            vars = templateEngine.DetectVariables(body.StructuredContent).Select(v => new TemplateVariableInput
            {
                Name = v.Name, Type = v.Type, IsRequired = v.IsRequired, ValidationRule = v.ValidationRule,
            }).ToList();
        }
        else
            vars = body.Variables;

        var mutableVars = vars.ToList();
        if (template.Kind != DocumentTemplateKind.Static && mutableVars.Count > 0)
            await employeeMapping.ApplyMatchesAsync(mutableVars, ct);

        try
        {
            var version = await CreateTemplateVersionInternalAsync(
                template,
                body.StructuredContent,
                status,
                body.OriginalAssetUri,
                mutableVars,
                userContext.UserId,
                ct);

            if (status == "published")
                template.CurrentVersionId = version.Id;
            template.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "CreateTemplateVersion:success templateId={TemplateId} versionId={VersionId} versionNumber={VersionNumber} status={Status}",
                template.Id,
                version.Id,
                version.VersionNumber,
                status);

            var versionVars = await employeeMapping.EnrichAsync(
                mutableVars.Select((v, i) => ToVariableResponse(v, i)).ToList(),
                ct);
            return MapVersionResponse(version, versionVars);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "CreateTemplateVersion:exception templateId={TemplateId} sqlState={SqlState}",
                template.Id,
                FindPostgresException(ex)?.SqlState);
            throw;
        }
    }

    public async Task<DocumentTemplateDetailResponse> PutCurrentVersionVariablesAsync(
        Guid id,
        IReadOnlyList<TemplateVariableInput> body,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();
        EnsureRhOrAdmin();
        if (body is null || body.Count == 0)
            throw new DocumentationApiException(400, "Au moins une variable est requise.");

        var template = await db.DocumentTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            throw new DocumentationApiException(404, "Template introuvable.");
        if (template.CurrentVersionId is not { } cvId)
            throw new DocumentationApiException(400, "Aucune version courante sur ce modèle.");

        var existing = await db.DocumentTemplateVariables
            .Where(v => v.TemplateVersionId == cvId)
            .ToListAsync(ct);
        if (existing.Count > 0)
            db.DocumentTemplateVariables.RemoveRange(existing);

        var rows = body.Select((v, index) => new DocumentTemplateVariable
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            TemplateVersionId = cvId,
            VariableName = v.Name.Trim(),
            VariableType = string.IsNullOrWhiteSpace(v.Type) ? "text" : v.Type.Trim().ToLowerInvariant(),
            IsRequired = v.IsRequired,
            DefaultValue = string.IsNullOrWhiteSpace(v.DefaultValue) ? null : v.DefaultValue.Trim(),
            ValidationRule = string.IsNullOrWhiteSpace(v.ValidationRule) ? null : v.ValidationRule.Trim(),
            DisplayLabel = string.IsNullOrWhiteSpace(v.DisplayLabel) ? null : v.DisplayLabel.Trim(),
            FormScope = NormalizeFormScope(v.FormScope),
            SourcePriority = v.SourcePriority ?? GuessSourcePriority(v.FormScope),
            NormalizedName = string.IsNullOrWhiteSpace(v.NormalizedName) ? null : v.NormalizedName.Trim(),
            RawPlaceholder = string.IsNullOrWhiteSpace(v.RawPlaceholder) ? null : v.RawPlaceholder.Trim(),
            SortOrder = index,
        })
            .Where(v => IsValidVariableName(v.VariableName))
            .Take(MaxTemplateVariables)
            .ToList();
        foreach (var (row, input) in rows.Zip(body.Where(v => IsValidVariableName(v.Name.Trim())).Take(MaxTemplateVariables)))
            DocumentTemplateVariableMapper.ApplyInput(row, input);
        if (rows.Count == 0)
            throw new DocumentationApiException(400, "Aucun nom de variable valide.");

        db.DocumentTemplateVariables.AddRange(rows);
        template.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetDocumentTemplateAsync(id, ct);
    }

    public async Task<IReadOnlyList<DocumentTemplateVersionResponse>> GetTemplateVersionsAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var exists = await db.DocumentTemplates.AnyAsync(t => t.Id == id, ct);
        if (!exists)
            throw new DocumentationApiException(404, "Template introuvable.");

        var versions = await db.DocumentTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == id)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);

        var versionIds = versions.Select(v => v.Id).ToArray();
        var vars = await db.DocumentTemplateVariables.AsNoTracking()
            .Where(v => v.TemplateVersionId.HasValue && versionIds.Contains(v.TemplateVersionId.Value))
            .OrderBy(v => v.SortOrder)
            .ToListAsync(ct);

        var grouped = vars.GroupBy(v => v.TemplateVersionId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var items = new List<DocumentTemplateVersionResponse>(versions.Count);
        foreach (var version in versions)
        {
            var variableResponses = grouped.GetValueOrDefault(version.Id, new List<DocumentTemplateVariable>())
                .Select(MapVariableResponse)
                .ToList();
            var enriched = await employeeMapping.EnrichAsync(variableResponses, ct);
            items.Add(MapVersionResponse(version, enriched));
        }

        return items;
    }

    public async Task<TemplateTestRunResponse> TestRunTemplateAsync(
        Guid id,
        TemplateTestRunRequest body,
        CancellationToken ct = default)
    {
        var template = await db.DocumentTemplates
            .Include(t => t.CurrentVersion)
            .Include(t => t.DocumentType)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            throw new DocumentationApiException(404, "Template introuvable.");

        var version = template.CurrentVersion ?? await db.DocumentTemplateVersions
            .Where(v => v.TemplateId == template.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (version is null)
            throw new DocumentationApiException(404, "Aucune version de modèle disponible.");

        var versionId = version.Id;
        if (template.Kind == DocumentTemplateKind.Static)
        {
            return new TemplateTestRunResponse(
                "Modèle statique : le rendu est le fichier source (aperçu / téléchargement du fichier modèle).",
                Array.Empty<string>(),
                version.OriginalAssetUri ?? $"{template.Code}-source");
        }

        var templateVariables = await db.DocumentTemplateVariables.AsNoTracking()
            .Where(v => v.TemplateVersionId == versionId)
            .OrderBy(v => v.SortOrder)
            .ToListAsync(ct);
        var requiredVariables = templateVariables
            .Where(v => v.IsRequired)
            .Select(v => v.VariableName)
            .ToList();

        var mergeResult = await variableMerge.MergeForTemplateAsync(
            body.BeneficiaryUserId,
            documentRequestId: null,
            templateVariables,
            body.SampleData,
            ct);
        var merged = mergeResult.Values;
        await variableMerge.ApplyAiRefinementAsync(
            merged,
            versionId,
            template.DocumentType?.Name ?? template.Name,
            ct).ConfigureAwait(false);

        var missing = requiredVariables
            .Where(n => !merged.TryGetValue(n, out var v) || string.IsNullOrWhiteSpace(v))
            .ToList();
        var mergedForDisplay = missing.Count > 0
            ? MergeWithMissingPlaceholders(merged, missing, documentWorkflowOptions.Value.MissingFieldPlaceholder)
            : merged;
        var rendered = templateEngine.RenderContent(version.StructuredContent, mergedForDisplay);

        return new TemplateTestRunResponse(rendered, missing, $"PREVIEW_{template.Code}.pdf");
    }

    public Task<DocumentTemplateGenerateResponse> GenerateFromTemplateAsync(
        Guid templateId,
        DocumentTemplateGenerateRequest? body,
        CancellationToken ct = default)
    {
        EnsureRhOrAdmin();
        var wf = new DocumentWorkflowRequest
        {
            TemplateId = templateId,
            DocumentRequestId = body?.DocumentRequestId,
            BeneficiaryUserId = body?.BeneficiaryUserId,
            DocumentTypeId = body?.DocumentTypeId,
            Variables = body?.Variables ?? new Dictionary<string, string>(),
        };
        return workflowGeneration.GenerateDocumentAsync(wf, ct);
    }

    public async Task<TemplateEmployeeResolutionResponse> ResolveEmployeeValuesAsync(
        Guid templateId,
        ResolveTemplateEmployeeValuesRequest body,
        CancellationToken ct = default)
    {
        EnsureAuthenticated();
        EnsureRhOrAdmin();
        var template = await db.DocumentTemplates
            .Include(t => t.CurrentVersion)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);
        if (template is null)
            throw new DocumentationApiException(404, "Template introuvable.");

        var version = template.CurrentVersion ?? await db.DocumentTemplateVersions
            .Where(v => v.TemplateId == template.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (version is null)
            throw new DocumentationApiException(400, "Aucune version de modèle n'est disponible.");

        var variables = await db.DocumentTemplateVariables.AsNoTracking()
            .Where(v => v.TemplateVersionId == version.Id)
            .OrderBy(v => v.SortOrder)
            .ToListAsync(ct);

        var detailed = await employeeMapping.ResolveAsync(
            body.BeneficiaryUserId,
            variables,
            body.CurrentValues,
            ct).ConfigureAwait(false);

        var items = variables.Select(v =>
        {
            var resolution = detailed.Resolutions.FirstOrDefault(r =>
                string.Equals(r.Name, v.VariableName, StringComparison.OrdinalIgnoreCase));
            return new TemplateVariableResolutionItem(
                v.VariableName,
                NormalizeFormScope(v.FormScope),
                v.SourceFieldKey,
                v.DisplayLabel,
                v.IsRequired,
                resolution?.FilledFromDb ?? false,
                resolution?.NeedsRhInput ?? string.Equals(NormalizeFormScope(v.FormScope), "hr", StringComparison.Ordinal),
                resolution?.UnavailableMapping ?? false,
                resolution?.DbValue,
                resolution?.ResolvedValue ?? string.Empty);
        }).ToList();

        return new TemplateEmployeeResolutionResponse(items);
    }

    private async Task<(DocumentTemplateVersion? Version, DocumentTemplate? Template)> LoadTemplateCurrentVersionAsync(
        Guid id,
        CancellationToken ct)
    {
        var template = await db.DocumentTemplates
            .AsNoTracking()
            .Include(t => t.CurrentVersion)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            throw new DocumentationApiException(404, "Template introuvable.");

        var cv = template.CurrentVersion ?? await db.DocumentTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == template.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        return (cv, template);
    }


    private async Task<DocumentTemplateVersion> CreateTemplateVersionInternalAsync(
        DocumentTemplate template,
        string structuredContent,
        string status,
        string? originalAssetUri,
        IReadOnlyList<TemplateVariableInput> variables,
        Guid? createdByUserId,
        CancellationToken ct)
    {
        var maxVersion = await db.DocumentTemplateVersions
            .Where(v => v.TemplateId == template.Id)
            .MaxAsync(v => (int?)v.VersionNumber, ct) ?? 0;

        var now = DateTimeOffset.UtcNow;
        var version = new DocumentTemplateVersion
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            TenantId = tenantAccessor.ResolvedTenantId,
            VersionNumber = maxVersion + 1,
            Status = status,
            StructuredContent = string.IsNullOrWhiteSpace(structuredContent) ? "{}" : structuredContent,
            OriginalAssetUri = string.IsNullOrWhiteSpace(originalAssetUri) ? null : originalAssetUri.Trim(),
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            PublishedAt = status == "published" ? now : null,
        };
        db.DocumentTemplateVersions.Add(version);
        await db.SaveChangesAsync(ct);

        var rows = variables.Select((v, index) => new DocumentTemplateVariable
        {
            Id = Guid.NewGuid(),
            TemplateId = template.Id,
            TemplateVersionId = version.Id,
            VariableName = v.Name.Trim(),
            VariableType = string.IsNullOrWhiteSpace(v.Type) ? "text" : v.Type.Trim().ToLowerInvariant(),
            IsRequired = v.IsRequired,
            DefaultValue = string.IsNullOrWhiteSpace(v.DefaultValue) ? null : v.DefaultValue.Trim(),
            ValidationRule = string.IsNullOrWhiteSpace(v.ValidationRule) ? null : v.ValidationRule.Trim(),
            DisplayLabel = string.IsNullOrWhiteSpace(v.DisplayLabel) ? null : v.DisplayLabel.Trim(),
            FormScope = NormalizeFormScope(v.FormScope),
            SourcePriority = v.SourcePriority ?? GuessSourcePriority(v.FormScope),
            NormalizedName = string.IsNullOrWhiteSpace(v.NormalizedName) ? null : v.NormalizedName.Trim(),
            RawPlaceholder = string.IsNullOrWhiteSpace(v.RawPlaceholder) ? null : v.RawPlaceholder.Trim(),
            SortOrder = index,
        })
            .Where(v => IsValidVariableName(v.VariableName))
            .Take(MaxTemplateVariables)
            .ToList();
        foreach (var (row, input) in rows.Zip(variables.Where(v => IsValidVariableName(v.Name.Trim())).Take(MaxTemplateVariables)))
            DocumentTemplateVariableMapper.ApplyInput(row, input);
        if (rows.Count > 0)
            db.DocumentTemplateVariables.AddRange(rows);
        await db.SaveChangesAsync(ct);
        return version;
    }

    private static Dictionary<string, string> MergeWithMissingPlaceholders(
        IReadOnlyDictionary<string, string> merged,
        IReadOnlyList<string> missingRequired,
        string? configuredPlaceholder)
    {
        if (missingRequired.Count == 0)
            return new Dictionary<string, string>(merged, StringComparer.OrdinalIgnoreCase);
        var ph = string.IsNullOrWhiteSpace(configuredPlaceholder) ? "________" : configuredPlaceholder.Trim();
        var d = new Dictionary<string, string>(merged, StringComparer.OrdinalIgnoreCase);
        foreach (var name in missingRequired)
        {
            if (!d.TryGetValue(name, out var v) || string.IsNullOrWhiteSpace(v))
                d[name] = ph;
        }

        return d;
    }

    private string? InferStrictValidationRuleByName(string variableName)
    {
        var k = (variableName ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(k))
            return null;

        if (k.Contains("cin", StringComparison.Ordinal))
            return @"^[A-Za-z]{1,2}[0-9]{6}$";
        if (k.Contains("rib", StringComparison.Ordinal) || k.Contains("compte_bancaire", StringComparison.Ordinal))
            return ribValidation.DigitsOnlyValidationPattern;
        if (k.Contains("telephone", StringComparison.Ordinal) || k.Contains("phone", StringComparison.Ordinal) || k == "tel")
            return @"^\+?[0-9]{10,15}$";
        if (k.Contains("email", StringComparison.Ordinal) || k.Contains("courriel", StringComparison.Ordinal))
            return @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
        if (k == "nom" || k == "prenom")
            return @"^[A-ZÀ-Ý][A-Za-zÀ-ÖØ-öø-ÿ'\- ]*$";
        if (k.Contains("date", StringComparison.Ordinal))
            return @"^(0[1-9]|[12][0-9]|3[01])/(0[1-9]|1[0-2])/[0-9]{4}$";

        return null;
    }

    private static DocumentationApiException MapTemplateUploadException(InvalidOperationException ex)
    {
        var msg = ex.Message;
        if (msg.Contains("existe déjà", StringComparison.OrdinalIgnoreCase))
            return new DocumentationApiException(409, msg);
        if (msg.Contains("non configuré", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Stockage MinIO", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("MinIO / S3", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("MinIO : impossible", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Échec d'envoi vers MinIO", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Impossible de joindre MinIO", StringComparison.OrdinalIgnoreCase))
            return new DocumentationApiException(503, msg);
        return new DocumentationApiException(400, msg);
    }

    private static string TemplateKindToApi(DocumentTemplateKind k) =>
        k == DocumentTemplateKind.Static ? "static" : "dynamic";

    private static DocumentTemplateKind ParseTemplateKind(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DocumentTemplateKind.Dynamic;
        return raw.Trim().ToLowerInvariant() switch
        {
            "static" => DocumentTemplateKind.Static,
            "dynamic" => DocumentTemplateKind.Dynamic,
            _ => DocumentTemplateKind.Dynamic,
        };
    }

    private static string NormalizeSource(string source)
    {
        var normalized = source.Trim().ToUpperInvariant();
        return normalized is "UPLOAD" or "RULE_BASED" or "AI_GENERATED" ? normalized : "UPLOAD";
    }

    private static string NormalizeVersionStatus(string status)
    {
        var normalized = status.Trim().ToLowerInvariant();
        return normalized is "draft" or "published" or "archived" ? normalized : "draft";
    }

    private static DocumentTemplateVersionResponse MapVersionResponse(
        DocumentTemplateVersion version,
        IReadOnlyList<DocumentTemplateVariableResponse> variables) =>
        new(
            version.Id.ToString(),
            version.VersionNumber,
            version.Status,
            SanitizeForJson(version.StructuredContent),
            version.OriginalAssetUri,
            version.CreatedAt.ToString("O"),
            version.PublishedAt?.ToString("O"),
            variables);

    private static string SanitizeForJson(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value ?? string.Empty;
        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                {
                    sb.Append(ch);
                    sb.Append(value[++i]);
                }
                continue;
            }
            if (char.IsLowSurrogate(ch))
                continue;
            sb.Append(ch);
        }
        return sb.ToString();
    }

    private static PostgresException? FindPostgresException(Exception? ex)
    {
        while (ex is not null)
        {
            if (ex is PostgresException pg)
                return pg;
            ex = ex.InnerException;
        }

        return null;
    }

    private static DocumentTemplateVariableResponse MapVariableResponse(DocumentTemplateVariable v)
    {
        var mapped = DocumentTemplateVariableMapper.ToResponse(v);
        return mapped with { FormScope = NormalizeFormScope(mapped.FormScope) };
    }

    private static DocumentTemplateVariableResponse ToVariableResponse(TemplateVariableInput v, int order) =>
        new(
            Guid.Empty.ToString(),
            v.Name,
            v.Type,
            v.IsRequired,
            v.DefaultValue,
            v.ValidationRule,
            v.DisplayLabel,
            NormalizeFormScope(v.FormScope),
            v.SourcePriority ?? GuessSourcePriority(v.FormScope),
            v.NormalizedName,
            v.RawPlaceholder,
            order)
        {
            SourceFieldKey = v.SourceFieldKey,
            MappingConfidence = v.MappingConfidence,
            MappingConfirmed = v.MappingConfirmed ?? false,
        };

    private static bool IsValidVariableName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static bool IsValidTemplateCode(string code) =>
        !string.IsNullOrWhiteSpace(code) && code.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

    private static string NormalizeFormScope(string? formScope)
    {
        var normalized = (formScope ?? "hr").Trim().ToLowerInvariant();
        return normalized is "pilot" or "hr" or "both" or "db" ? normalized : "hr";
    }

    private static int GuessSourcePriority(string? formScope) =>
        NormalizeFormScope(formScope) switch
        {
            "db" => 10,
            "pilot" => 20,
            "both" => 25,
            "hr" => 30,
            _ => 30,
        };

    private void EnsureAuthenticated()
    {
        if (!userContext.UserId.HasValue)
            throw new DocumentationApiException(401, "Authentification requise.");
    }

    private void EnsureRhOrAdmin()
    {
        if (userContext.Role is not (AppRole.Rh or AppRole.Admin))
            throw new DocumentationApiException(403, "Accès refusé.");
    }
}
