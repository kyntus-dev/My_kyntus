using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Documentation.Application.DocumentTemplates;
using Documentation.Application.Api;
using Documentation.Application.Abstractions;
using Documentation.Infrastructure.Context;
using Documentation.Infrastructure.Persistence;
using Documentation.Domain.Entities;
using Documentation.Infrastructure.DocxParsing;
using Documentation.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Documentation.Infrastructure.Services;

/// <summary>Création de templates RH : upload MinIO / S3 ou génération IA.</summary>
public sealed class DocumentTemplateManagementService(
    DocumentationDbContext db,
    IDocumentationTenantAccessor tenantAccessor,
    ITemplateBlobStorage blobStorage,
    IAiTemplateContentGenerator aiGenerator,
    ITemplateEngineService templateEngine,
    ITemplatePlaceholderNormalizationService placeholderNormalization,
    IDocumentTemplateEmployeeMappingService employeeMapping,
    ILogger<DocumentTemplateManagementService> logger) : IDocumentTemplateManagementService
{
    private const int MaxTemplateContentLength = 100_000;
    private const int MaxTemplateVariables = 100;

    public async Task<DocumentTemplateDetailResponse> CreateFromUploadedFileAsync(
        Guid userId,
        string code,
        string name,
        string? description,
        Guid? documentTypeId,
        IFormFile file,
        bool staticDocument = false,
        bool requiresPilotUpload = false,
        CancellationToken cancellationToken = default)
    {
        if (!blobStorage.IsConfigured)
            throw new InvalidOperationException("MinIO / S3 non configuré : renseignez DocumentTemplates:Minio dans appsettings.");

        if (file.Length == 0)
            throw new ArgumentException("Fichier vide.", nameof(file));
        if (file.Length > 50 * 1024 * 1024)
            throw new ArgumentException("Fichier trop volumineux (max 50 Mo).", nameof(file));

        var tenant = tenantAccessor.ResolvedTenantId;

        var safeName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "template.bin";

        var objectKey = $"{tenant}/templates/{Guid.NewGuid():N}-{safeName}";
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType;

        byte[] fileBytes;
        await using (var gather = new MemoryStream())
        {
            await using (var src = file.OpenReadStream())
                await src.CopyToAsync(gather, cancellationToken).ConfigureAwait(false);
            fileBytes = gather.ToArray();
        }

        // Flux dédié à l’upload S3 (le SDK peut fermer ou consommer le flux) et flux séparé pour l’analyse DOCX / texte.
        string fileUrl;
        await using (var uploadStream = new MemoryStream(fileBytes, writable: false))
        {
            fileUrl = await blobStorage.PutTemplateObjectAsync(objectKey, uploadStream, contentType, cancellationToken)
                .ConfigureAwait(false);
        }

        if (staticDocument)
        {
            var structuredJsonStatic = JsonSerializer.Serialize(new
            {
                format = "static",
                fileUrl,
                fileName = safeName,
                mime = contentType,
            });
            if (string.IsNullOrWhiteSpace(code))
                throw new ArgumentException("Le code template est obligatoire pour un document statique.", nameof(code));
            var staticCode = code.Trim().ToUpperInvariant();
            if (!IsValidTemplateCode(staticCode))
                throw new ArgumentException("Code template invalide.", nameof(code));
            if (await db.DocumentTemplates.AnyAsync(t => t.TenantId == tenant && t.Code == staticCode, cancellationToken)
                    .ConfigureAwait(false))
                throw new InvalidOperationException("Ce code template existe déjà.");
            var staticDisplayName = string.IsNullOrWhiteSpace(name)
                ? Path.GetFileNameWithoutExtension(safeName)
                : name.Trim();
            var staticDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
            return await PersistTemplateAsync(
                tenant,
                userId,
                staticCode,
                staticDisplayName,
                staticDescription,
                "UPLOAD",
                documentTypeId,
                structuredJsonStatic,
                fileUrl,
                Array.Empty<TemplateVariableInput>(),
                DocumentTemplateKind.Static,
                requiresPilotUpload,
                cancellationToken).ConfigureAwait(false);
        }

        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        string bodyForVariables;
        string structuredJson;
        if (ext == ".docx")
        {
            try
            {
                using var docxStream = new MemoryStream(fileBytes, writable: false);
                var plain = DocxPlainTextExtractor.Extract(docxStream);
                docxStream.Position = 0;
                var legacyMarkers = DocxPlainTextExtractor.ExtractLegacyMarkers(docxStream);
                if (legacyMarkers.Count > 0)
                {
                    logger.LogInformation(
                        "DOCX upload detected legacy markers {LegacyMarkerCount} {LegacyMarkerList}",
                        legacyMarkers.Count,
                        string.Join(',', legacyMarkers));
                }

                bodyForVariables = plain;
                structuredJson = JsonSerializer.Serialize(new
                {
                    format = "uploaded_docx",
                    bodyText = plain.Length > MaxTemplateContentLength ? plain[..MaxTemplateContentLength] : plain,
                    fileUrl,
                    fileName = safeName,
                    legacyMarkers,
                });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Extraction DOCX échouée pour {File}", safeName);
                bodyForVariables = "";
                structuredJson = JsonSerializer.Serialize(new
                {
                    format = "uploaded_binary",
                    fileUrl,
                    fileName = safeName,
                    note = "Extraction automatique impossible — éditez le contenu ou les variables manuellement.",
                });
            }
        }
        else if (ext is ".txt" or ".html" or ".htm")
        {
            using var textStream = new MemoryStream(fileBytes, writable: false);
            using var reader = new StreamReader(textStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            var text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            bodyForVariables = text;
            structuredJson = text.Length > MaxTemplateContentLength
                ? JsonSerializer.Serialize(new { format = "uploaded_text", bodyText = text[..MaxTemplateContentLength], fileUrl, fileName = safeName })
                : JsonSerializer.Serialize(new { format = "uploaded_text", bodyText = text, fileUrl, fileName = safeName });
        }
        else
        {
            bodyForVariables = "";
            structuredJson = JsonSerializer.Serialize(new
            {
                format = "uploaded_binary",
                fileUrl,
                fileName = safeName,
                mime = contentType,
                note = "Fichier binaire stocké. Définissez les variables ou convertissez en DOCX pour détection automatique.",
            });
        }

        var resolvedDisplayName = !string.IsNullOrWhiteSpace(name)
            ? name.Trim()
            : Path.GetFileNameWithoutExtension(safeName);

        var resolvedDescription = !string.IsNullOrWhiteSpace(description)
            ? description.Trim()
            : null;

        string normalizedCode;
        if (!string.IsNullOrWhiteSpace(code))
        {
            normalizedCode = code.Trim().ToUpperInvariant();
            if (!IsValidTemplateCode(normalizedCode))
                throw new ArgumentException("Code template invalide.", nameof(code));
            if (await db.DocumentTemplates.AnyAsync(t => t.TenantId == tenant && t.Code == normalizedCode, cancellationToken)
                    .ConfigureAwait(false))
                throw new InvalidOperationException("Ce code template existe déjà.");
        }
        else
        {
            var preferred = SanitizeTemplateCodeForAllocation(resolvedDisplayName);
            normalizedCode = await AllocateUniqueCodePreferringAsync(tenant, preferred, cancellationToken)
                .ConfigureAwait(false);
        }

        var normalized = placeholderNormalization.ExtractPlaceholders(bodyForVariables);
        var vars = normalized
            .Select(v => new TemplateVariableInput
            {
                Name = v.CanonicalKey,
                Type = v.Type,
                IsRequired = v.IsRequired,
                ValidationRule = v.ValidationRule,
                DefaultValue = null,
                DisplayLabel = v.SuggestedLabel,
                FormScope = placeholderNormalization.IsDatabaseBackedKey(v.CanonicalKey) ? "db" : "hr",
                SourcePriority = placeholderNormalization.IsDatabaseBackedKey(v.CanonicalKey) ? 10 : 20,
                NormalizedName = v.NormalizedKey,
                RawPlaceholder = v.RawToken,
            })
            .ToList();

        logger.LogInformation(
            "Internal engine detected {VarCount} placeholders for uploaded template {Name}",
            vars.Count,
            resolvedDisplayName);

        return await PersistTemplateAsync(
            tenant,
            userId,
            normalizedCode,
            resolvedDisplayName,
            resolvedDescription,
            "UPLOAD",
            documentTypeId,
            structuredJson,
            fileUrl,
            vars,
            DocumentTemplateKind.Dynamic,
            requiresPilotUpload: false,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DocumentTemplateDetailResponse> CreateFromInternalEngineAsync(
        Guid userId,
        string code,
        string name,
        string? description,
        Guid? documentTypeId,
        string structuredContent,
        IReadOnlyList<TemplateVariableInput>? variables,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(structuredContent))
            throw new ArgumentException("Le contenu du template est obligatoire.", nameof(structuredContent));

        var tenant = tenantAccessor.ResolvedTenantId;
        var normalizedCode = await AllocateUniqueCodeAsync(tenant, code, cancellationToken).ConfigureAwait(false);
        var placeholders = placeholderNormalization.ExtractPlaceholders(structuredContent);
        var sourceVariables = variables?.Count > 0
            ? variables
            : placeholders.Select(p => new TemplateVariableInput
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

        return await PersistTemplateAsync(
            tenant,
            userId,
            normalizedCode,
            string.IsNullOrWhiteSpace(name) ? normalizedCode : name.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            "INTERNAL_ENGINE",
            documentTypeId,
            structuredContent,
            originalAssetUri: null,
            sourceVariables,
            DocumentTemplateKind.Dynamic,
            requiresPilotUpload: false,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Alloue un code unique en privilégiant une suggestion IA (sinon code aléatoire <c>TPL-…</c>).</summary>
    private async Task<string> AllocateUniqueCodePreferringAsync(string tenant, string? preferredSanitized, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(preferredSanitized))
        {
            var c = preferredSanitized.Trim().ToUpperInvariant();
            if (IsValidTemplateCode(c)
                && !await db.DocumentTemplates.AnyAsync(t => t.TenantId == tenant && t.Code == c, ct).ConfigureAwait(false))
                return c;
        }

        return await AllocateUniqueCodeAsync(tenant, null, ct).ConfigureAwait(false);
    }

    /// <summary>Nettoie le code suggéré par l’IA pour respecter les caractères autorisés (max 20).</summary>
    [ExcludeFromCodeCoverage]
    private static string? SanitizeTemplateCodeForAllocation(string? suggested)
    {
        if (string.IsNullOrWhiteSpace(suggested))
            return null;
        var sb = new StringBuilder();
        foreach (var ch in suggested.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch is '-' or '_')
                sb.Append(ch);
        }

        var s = sb.ToString().Trim('-', '_');
        if (s.Length == 0)
            return null;
        return s.Length > 20 ? s[..20] : s;
    }

    [ExcludeFromCodeCoverage]
    private static string AttachAiVariableDescriptionsToStructuredJson(
        string structuredJson,
        IReadOnlyList<DetectedTemplateVariable> merged)
    {
        try
        {
            if (JsonNode.Parse(structuredJson) is not JsonObject root)
                return structuredJson;
            var descObj = new JsonObject();
            foreach (var v in merged)
            {
                if (string.IsNullOrWhiteSpace(v.Description))
                    continue;
                descObj[v.Name] = v.Description!.Trim();
            }

            if (descObj.Count > 0)
                root["aiVariableDescriptions"] = descObj;
            return root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch
        {
            return structuredJson;
        }
    }

    public async Task<DocumentTemplateDetailResponse> CreateFromAiDescriptionAsync(
        Guid userId,
        string description,
        string? name,
        string? code,
        Guid? documentTypeId,
        CancellationToken cancellationToken = default)
    {
        if (!await aiGenerator.IsAvailableAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidOperationException(
                "API IA non configurée : clé active (admin) ou DocumentTemplates:Ai:ApiKey.");

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description obligatoire pour la génération IA.", nameof(description));
        if (description.Length > 4000)
            throw new ArgumentException("Description trop longue (max 4000 caractères).", nameof(description));

        var tenant = tenantAccessor.ResolvedTenantId;
        var json = await aiGenerator.GenerateStructuredTemplateJsonAsync(description, cancellationToken).ConfigureAwait(false);
        if (json.Length > MaxTemplateContentLength)
            throw new InvalidOperationException("Réponse IA trop volumineuse.");

        var normalizedCode = await AllocateUniqueCodeAsync(tenant, code, cancellationToken).ConfigureAwait(false);
        var displayName = string.IsNullOrWhiteSpace(name)
            ? (description.Trim().Length > 120 ? description.Trim()[..120] + "…" : description.Trim())
            : name.Trim();

        var vars = templateEngine.DetectVariables(json)
            .Select(v => new TemplateVariableInput
            {
                Name = v.Name,
                Type = v.Type,
                IsRequired = v.IsRequired,
                ValidationRule = v.ValidationRule,
                DisplayLabel = null,
            }).ToList();

        return await PersistTemplateAsync(
            tenant,
            userId,
            normalizedCode,
            displayName,
            description.Trim(),
            "AI_GENERATED",
            documentTypeId,
            json,
            originalAssetUri: null,
            vars,
            DocumentTemplateKind.Dynamic,
            requiresPilotUpload: false,
            cancellationToken).ConfigureAwait(false);
    }

    [ExcludeFromCodeCoverage]
    private async Task<string> AllocateUniqueCodeAsync(string tenant, string? requested, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var c = requested.Trim().ToUpperInvariant();
            if (!IsValidTemplateCode(c))
                throw new ArgumentException("Code template invalide.");
            if (await db.DocumentTemplates.AnyAsync(t => t.TenantId == tenant && t.Code == c, ct).ConfigureAwait(false))
                throw new InvalidOperationException("Ce code template existe déjà.");
            return c;
        }

        for (var i = 0; i < 30; i++)
        {
            var c = $"TPL-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
            if (!await db.DocumentTemplates.AnyAsync(t => t.TenantId == tenant && t.Code == c, ct).ConfigureAwait(false))
                return c;
        }

        throw new InvalidOperationException("Impossible d'allouer un code template unique.");
    }

    private async Task<DocumentTemplateDetailResponse> PersistTemplateAsync(
        string tenant,
        Guid userId,
        string code,
        string name,
        string? description,
        string source,
        Guid? documentTypeId,
        string structuredContent,
        string? originalAssetUri,
        IReadOnlyList<TemplateVariableInput> variables,
        DocumentTemplateKind kind,
        bool requiresPilotUpload,
        CancellationToken ct)
    {
        if (structuredContent.Length > MaxTemplateContentLength)
        {
            logger.LogWarning(
                "Structured content truncated from {OriginalLength} to {MaxLength} for template code {Code}",
                structuredContent.Length,
                MaxTemplateContentLength,
                code);
            structuredContent = structuredContent[..MaxTemplateContentLength];
        }

        var detectedForValidation = variables
            .Select(v => new DetectedTemplateVariable(
                v.Name,
                string.IsNullOrWhiteSpace(v.Type) ? "text" : v.Type.Trim().ToLowerInvariant(),
                v.IsRequired,
                string.IsNullOrWhiteSpace(v.ValidationRule) ? null : v.ValidationRule.Trim(),
                null))
            .ToList();
        var emptyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _ = templateEngine.ValidateValues(detectedForValidation, emptyValues);
        var requiredCount = variables.Count(v => v.IsRequired);
        if (requiredCount > 20)
        {
            logger.LogWarning(
                "Template has high required variable count {RequiredCount} for code {Code}",
                requiredCount,
                code);
        }

        var now = DateTimeOffset.UtcNow;
        var mutableVariables = variables.ToList();
        if (kind != DocumentTemplateKind.Static && mutableVariables.Count > 0)
            await employeeMapping.ApplyMatchesAsync(mutableVariables, ct).ConfigureAwait(false);

        var template = new DocumentTemplate
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            Code = code,
            Name = name,
            Description = description,
            Source = source,
            Kind = kind,
            RequiresPilotUpload = requiresPilotUpload,
            IsActive = true,
            DocumentTypeId = documentTypeId,
            UpdatedAt = now,
        };
        db.DocumentTemplates.Add(template);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var version = await CreateTemplateVersionInternalAsync(
            template,
            structuredContent,
            "published",
            originalAssetUri,
            kind == DocumentTemplateKind.Static ? Array.Empty<TemplateVariableInput>() : mutableVariables,
            userId,
            ct).ConfigureAwait(false);

        template.CurrentVersionId = version.Id;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var detail = await MapDetailAsync(template.Id, ct).ConfigureAwait(false);
        return detail ?? throw new InvalidOperationException("Template introuvable après création.");
    }

    private async Task<DocumentTemplateVersion> CreateTemplateVersionInternalAsync(
        DocumentTemplate template,
        string structuredContent,
        string status,
        string? originalAssetUri,
        IReadOnlyList<TemplateVariableInput> variables,
        Guid createdByUserId,
        CancellationToken ct)
    {
        var maxVersion = await db.DocumentTemplateVersions
            .Where(v => v.TemplateId == template.Id)
            .MaxAsync(v => (int?)v.VersionNumber, ct).ConfigureAwait(false) ?? 0;

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
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var rows = variables
            .Where(v => IsValidVariableName(v.Name))
            .Select((v, index) => new DocumentTemplateVariable
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
                FormScope = string.IsNullOrWhiteSpace(v.FormScope) ? "hr" : v.FormScope.Trim().ToLowerInvariant(),
                SourcePriority = v.SourcePriority ?? 20,
                NormalizedName = string.IsNullOrWhiteSpace(v.NormalizedName) ? null : v.NormalizedName.Trim(),
                RawPlaceholder = string.IsNullOrWhiteSpace(v.RawPlaceholder) ? null : v.RawPlaceholder.Trim(),
                SortOrder = index,
            })
            .Take(MaxTemplateVariables)
            .ToList();
        foreach (var (row, input) in rows.Zip(
                     variables.Where(v => IsValidVariableName(v.Name)).Take(MaxTemplateVariables)))
            DocumentTemplateVariableMapper.ApplyInput(row, input);
        if (rows.Count > 0)
            db.DocumentTemplateVariables.AddRange(rows);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return version;
    }

    private async Task<DocumentTemplateDetailResponse?> MapDetailAsync(Guid id, CancellationToken ct)
    {
        var template = await db.DocumentTemplates
            .AsNoTracking()
            .Include(t => t.DocumentType)
            .Include(t => t.CurrentVersion)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            .ConfigureAwait(false);
        if (template is null)
            return null;

        DocumentTemplateVersionResponse? versionDto = null;
        if (template.CurrentVersionId.HasValue)
        {
            var varRows = await db.DocumentTemplateVariables.AsNoTracking()
                .Where(v => v.TemplateVersionId == template.CurrentVersionId.Value)
                .OrderBy(v => v.SortOrder)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            var vars = await employeeMapping
                .EnrichAsync(varRows.Select(DocumentTemplateVariableMapper.ToResponse).ToList(), ct)
                .ConfigureAwait(false);
            var cv = template.CurrentVersion;
            if (cv is not null)
            {
                versionDto = new DocumentTemplateVersionResponse(
                    cv.Id.ToString(),
                    cv.VersionNumber,
                    cv.Status,
                    cv.StructuredContent,
                    cv.OriginalAssetUri,
                    cv.CreatedAt.ToString("O"),
                    cv.PublishedAt?.ToString("O"),
                    vars);
            }
        }

        return new DocumentTemplateDetailResponse(
            template.Id.ToString(),
            template.Code,
            template.Name,
            template.Source,
            template.Kind == DocumentTemplateKind.Static ? "static" : "dynamic",
            template.RequiresPilotUpload,
            template.IsActive,
            template.DocumentTypeId?.ToString(),
            template.DocumentType?.Name,
            template.UpdatedAt.ToString("O"),
            template.Description,
            versionDto);
    }

    private static bool IsValidVariableName(string name) =>
        !string.IsNullOrWhiteSpace(name) && name.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static bool IsValidTemplateCode(string code) =>
        !string.IsNullOrWhiteSpace(code) && code.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');
}
