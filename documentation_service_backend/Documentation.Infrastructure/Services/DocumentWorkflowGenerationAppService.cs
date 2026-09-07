using System.Globalization;
using System.Text.Json;
using Documentation.Application;
using Documentation.Application.Abstractions;
using Documentation.Application.Api;
using Documentation.Application.Configuration;
using Documentation.Domain.Entities;
using Documentation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Documentation.Infrastructure.Services;

public sealed class DocumentWorkflowGenerationAppService(
    DocumentationDbContext db,
    IDocumentationTenantAccessor tenantAccessor,
    IDocumentationRequestContext userContext,
    IDocumentTemplateVariableMergeService variableMerge,
    ITemplateEngineService templateEngine,
    IOriginalDocxTemplateRenderService originalDocxTemplateRender,
    IPdfExportService pdfExport,
    ITemplateBlobStorage templateBlobStorage,
    IOptions<DocumentWorkflowOptions> documentWorkflowOptions,
    IRibValidationService ribValidation,
    ILogger<DocumentWorkflowGenerationAppService> logger) : IDocumentWorkflowGenerationAppService
{
    public async Task<TemplateFileExportDto> PreviewDocumentAsync(DocumentWorkflowRequest req, CancellationToken ct = default)
    {
        EnsureRhOrAdmin();
        var prep = await PrepareDocumentWorkflowAsync(req, ct);
        var wfOpts = documentWorkflowOptions.Value;

        if (prep.MissingRequired.Count > 0 && !wfOpts.RequireRhEditorReview)
        {
            throw new DocumentationApiException(400,
                "Variables obligatoires manquantes.",
                new
                {
                    missingVariables = prep.MissingRequired,
                    invalidVariables = prep.InvalidFormat,
                });
        }

        if (prep.InvalidFormat.Count > 0)
        {
            throw new DocumentationApiException(400,
                "Formats invalides détectés. Corrigez les champs avant de continuer.",
                new
                {
                    missingVariables = prep.MissingRequired,
                    invalidVariables = prep.InvalidFormat,
                });
        }

        var headers = new Dictionary<string, string>
        {
            ["Cache-Control"] = "no-store, no-cache, must-revalidate",
            ["Pragma"] = "no-cache",
        };

        if (prep.Template.Kind != DocumentTemplateKind.Static)
        {
            var filled = Math.Max(0, prep.RequiredVariableCount - prep.MissingRequired.Count);
            var pct = prep.RequiredVariableCount > 0
                ? (int)Math.Round(100.0 * filled / prep.RequiredVariableCount)
                : 100;
            headers["X-Document-Required-Total"] = prep.RequiredVariableCount.ToString(CultureInfo.InvariantCulture);
            headers["X-Document-Missing-Count"] = prep.MissingRequired.Count.ToString(CultureInfo.InvariantCulture);
            headers["X-Document-Filled-Count"] = filled.ToString(CultureInfo.InvariantCulture);
            headers["X-Document-Filled-Percent"] = pct.ToString(CultureInfo.InvariantCulture);
            headers["X-Document-Missing-Variables"] = string.Join(',', prep.MissingRequired);
            headers["X-Document-Invalid-Count"] = prep.InvalidFormat.Count.ToString(CultureInfo.InvariantCulture);
        }

        if (prep.Template.Kind == DocumentTemplateKind.Static)
        {
            var payload = await templateBlobStorage.TryReadObjectAsync(prep.Version.OriginalAssetUri, ct);
            if (payload is null)
                throw new DocumentationApiException(404, "Fichier modèle statique introuvable ou stockage indisponible.");
            var star = Uri.EscapeDataString(payload.FileName);
            headers["Content-Disposition"] = $"inline; filename=\"file\"; filename*=UTF-8''{star}";
            return new TemplateFileExportDto(payload.Content, payload.ContentType, payload.FileName, headers);
        }

        var mergedForPreview = prep.Merged;
        if (prep.MissingRequired.Count > 0)
        {
            mergedForPreview = MergeWithMissingPlaceholders(
                prep.Merged,
                prep.MissingRequired,
                wfOpts.MissingFieldPlaceholder);
        }

        var originalDocx = await templateBlobStorage.TryReadObjectAsync(prep.Version.OriginalAssetUri, ct);
        if (DocxTemplatePayloadInspector.IsWordProcessingOpenXml(originalDocx) && originalDocx is { Content.Length: > 0 })
        {
            var docxBytes = originalDocxTemplateRender.Render(originalDocx.Content, mergedForPreview);
            var safeCode = string.IsNullOrWhiteSpace(prep.Template.Code) ? "DOC" : prep.Template.Code.Trim();
            var docxFileName = $"PREVIEW_{safeCode}_{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.docx";
            headers["X-Preview-Source"] = "original-docx";
            return new TemplateFileExportDto(
                docxBytes,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                docxFileName,
                headers);
        }

        var rendered = templateEngine.RenderContent(prep.Version.StructuredContent, mergedForPreview);
        var (fileName, pdfBytes) = pdfExport.BuildPdf(
            prep.Template.Code,
            tenantAccessor.ResolvedTenantId,
            rendered,
            prep.TitleFallback);
        return new TemplateFileExportDto(pdfBytes, "application/pdf", fileName, headers);
    }

    public async Task<DocumentTemplateGenerateResponse> GenerateDocumentAsync(DocumentWorkflowRequest req, CancellationToken ct = default)
    {
        EnsureRhOrAdmin();
        var prep = await PrepareDocumentWorkflowAsync(req, ct);
        if (prep.Template.Kind != DocumentTemplateKind.Static &&
            (prep.MissingRequired.Count > 0 || prep.InvalidFormat.Count > 0))
        {
            throw new DocumentationApiException(400,
                "Données manquantes ou invalides : génération bloquée tant que les champs ne sont pas corrigés.",
                new
                {
                    missingVariables = prep.MissingRequired,
                    invalidVariables = prep.InvalidFormat,
                });
        }

        return await CompleteDocumentGenerationAsync(req, prep, ct);
    }

    public async Task<DocumentTemplateGenerateResponse> UploadReadyDocumentAsync(UploadReadyDocumentCommand dto, CancellationToken ct = default)
    {
        EnsureRhOrAdmin();
        if (dto.FileBytes.Length == 0)
            throw new DocumentationApiException(400, "Fichier « file » requis.");
        if (dto.FileBytes.LongLength > 50 * 1024 * 1024)
            throw new DocumentationApiException(400, "Fichier trop volumineux (max 50 Mo).");

        DocumentRequest? linkedRequest = null;
        if (dto.DocumentRequestId is { } requestId && requestId != Guid.Empty)
        {
            linkedRequest = await db.DocumentRequests.FirstOrDefaultAsync(r => r.Id == requestId, ct);
            if (linkedRequest is null)
                throw new DocumentationApiException(404, "Demande introuvable.");
            if (!string.Equals(linkedRequest.TenantId?.Trim(), tenantAccessor.ResolvedTenantId.Trim(), StringComparison.Ordinal))
                throw new DocumentationApiException(403, "Accès refusé.");
            if (linkedRequest.Status != DocumentRequestStatus.Approved)
                throw new DocumentationApiException(400, "La demande doit être approuvée avant l'upload du document final.");
        }

        var ownerId = dto.BeneficiaryUserId
            ?? linkedRequest?.BeneficiaryUserId
            ?? linkedRequest?.RequesterUserId
            ?? userContext.UserId!.Value;
        var documentTypeId = dto.DocumentTypeId ?? linkedRequest?.DocumentTypeId;
        var now = DateTimeOffset.UtcNow;
        var genId = Guid.NewGuid();
        var fileName = string.IsNullOrWhiteSpace(dto.FileName)
            ? $"document_pret_{now:yyyyMMdd_HHmmss}"
            : Path.GetFileName(dto.FileName);

        string storageUri;
        if (templateBlobStorage.IsConfigured)
        {
            await using var stream = new MemoryStream(dto.FileBytes);
            var key = $"{tenantAccessor.ResolvedTenantId.TrimEnd('/')}/generated/{genId:N}/{Uri.EscapeDataString(fileName)}";
            storageUri = await templateBlobStorage.PutTemplateObjectAsync(key, stream, dto.ContentType, ct);
        }
        else
        {
            storageUri = $"inline://generated/{genId:N}/{Uri.EscapeDataString(fileName)}";
        }

        var gen = new GeneratedDocument
        {
            Id = genId,
            DocumentRequestId = linkedRequest?.Id,
            OwnerUserId = ownerId,
            DocumentTypeId = documentTypeId,
            TemplateVersionId = null,
            FileName = fileName,
            StorageUri = storageUri,
            PdfContent = templateBlobStorage.IsConfigured ? null : dto.FileBytes,
            MimeType = string.IsNullOrWhiteSpace(dto.ContentType) ? "application/octet-stream" : dto.ContentType,
            FileSizeBytes = dto.FileBytes.LongLength,
            Status = GeneratedDocumentStatus.Generated,
            VersionNumber = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GeneratedDocuments.Add(gen);

        if (linkedRequest is not null)
        {
            linkedRequest.Status = DocumentRequestStatus.Generated;
            linkedRequest.UpdatedAt = now;
            var auditDetails = JsonSerializer.Serialize(new
            {
                generatedDocumentId = gen.Id.ToString("D"),
                fileName,
                uploadReady = true,
            });
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantAccessor.ResolvedTenantId,
                OccurredAt = now,
                ActorUserId = userContext.UserId,
                Action = "DOCUMENT_UPLOADED_READY",
                EntityType = "document_request",
                EntityId = linkedRequest.Id,
                Details = auditDetails,
                Success = true,
                RequestNumber = linkedRequest.RequestNumber,
            });
        }

        await db.SaveChangesAsync(ct);
        return new DocumentTemplateGenerateResponse(gen.Id.ToString("D"), fileName, storageUri, gen.Status.ToString());
    }

    private sealed class DocumentWorkflowPreparation
    {
        public DocumentTemplate Template { get; private init; } = null!;
        public DocumentTemplateVersion Version { get; private init; } = null!;
        public Dictionary<string, string> Merged { get; private init; } = null!;
        public IReadOnlyList<string> MissingRequired { get; private init; } = Array.Empty<string>();
        public IReadOnlyList<string> InvalidFormat { get; private init; } = Array.Empty<string>();
        public int RequiredVariableCount { get; private init; }
        public DocumentRequest? LinkedRequest { get; private init; }
        public string TitleFallback { get; private init; } = "";

        public static DocumentWorkflowPreparation Ok(
            DocumentTemplate template,
            DocumentTemplateVersion version,
            Dictionary<string, string> merged,
            IReadOnlyList<string> missingRequired,
            IReadOnlyList<string> invalidFormat,
            int requiredVariableCount,
            DocumentRequest? linkedRequest,
            string titleFallback) => new()
        {
            Template = template,
            Version = version,
            Merged = merged,
            MissingRequired = missingRequired,
            InvalidFormat = invalidFormat,
            RequiredVariableCount = requiredVariableCount,
            LinkedRequest = linkedRequest,
            TitleFallback = titleFallback,
        };
    }

    private async Task<DocumentWorkflowPreparation> PrepareDocumentWorkflowAsync(
        DocumentWorkflowRequest req,
        CancellationToken ct)
    {
        EnsureAuthenticated();

        if (req.TemplateId == Guid.Empty)
            throw new DocumentationApiException(400, "templateId est obligatoire.");

        var template = await db.DocumentTemplates
            .Include(t => t.DocumentType)
            .Include(t => t.CurrentVersion)
            .FirstOrDefaultAsync(t => t.Id == req.TemplateId, ct);
        if (template is null)
            throw new DocumentationApiException(404, "Template introuvable.");
        if (!template.IsActive)
            throw new DocumentationApiException(400, "Ce template est inactif.");

        var version = template.CurrentVersion;
        if (version is null)
        {
            version = await db.DocumentTemplateVersions
                .Where(v => v.TemplateId == template.Id)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefaultAsync(ct);
        }

        if (version is null)
        {
            throw new DocumentationApiException(400,
                "Aucune version de modèle n'est disponible. Publiez une version ou réimportez le fichier.");
        }

        DocumentRequest? linkedRequest = null;
        if (req.DocumentRequestId is { } linkRequestId && linkRequestId != Guid.Empty)
        {
            linkedRequest = await db.DocumentRequests.FirstOrDefaultAsync(r => r.Id == linkRequestId, ct);
            if (linkedRequest is null)
                throw new DocumentationApiException(400, "Demande introuvable.");
            if (linkedRequest.Status != DocumentRequestStatus.Approved)
                throw new DocumentationApiException(400, "La demande doit être approuvée avant génération du document.");
        }

        var templateVariables = await db.DocumentTemplateVariables.AsNoTracking()
            .Where(v => v.TemplateVersionId == version.Id)
            .OrderBy(v => v.SortOrder)
            .ToListAsync(ct);
        var mergeResult = await variableMerge.MergeForTemplateAsync(
            req.BeneficiaryUserId,
            req.DocumentRequestId,
            templateVariables,
            req.Variables,
            ct);
        var merged = mergeResult.Values;
        var versionId = version.Id;
        var titleFallback = template.DocumentType?.Name ?? template.Name;
        if (string.IsNullOrWhiteSpace(titleFallback))
            titleFallback = template.Code;

        var requiredVariableCount = 0;
        IReadOnlyList<string> missing;
        IReadOnlyList<string> invalid;
        if (template.Kind == DocumentTemplateKind.Static)
        {
            missing = Array.Empty<string>();
            invalid = Array.Empty<string>();
        }
        else
        {
            var variableRows = templateVariables
                .Select(v => new { v.VariableName, v.VariableType, v.IsRequired, v.ValidationRule })
                .ToList();
            requiredVariableCount = variableRows.Count(v => v.IsRequired);
            var specs = variableRows
                .Where(v => !string.IsNullOrWhiteSpace(v.VariableName))
                .Select(v => new DetectedTemplateVariable(
                    v.VariableName.Trim(),
                    string.IsNullOrWhiteSpace(v.VariableType) ? "text" : v.VariableType.Trim().ToLowerInvariant(),
                    v.IsRequired,
                    string.IsNullOrWhiteSpace(v.ValidationRule)
                        ? InferStrictValidationRuleByName(v.VariableName)
                        : v.ValidationRule.Trim()))
                .ToList();
            if (specs.Count == 0)
            {
                missing = Array.Empty<string>();
                invalid = Array.Empty<string>();
            }
            else
            {
                var validation = templateEngine.ValidateVariables(specs, merged);
                missing = validation.MissingRequired;
                invalid = validation.InvalidFormat;
            }
        }

        return DocumentWorkflowPreparation.Ok(
            template,
            version,
            merged,
            missing,
            invalid,
            requiredVariableCount,
            linkedRequest,
            titleFallback);
    }

    private async Task<DocumentTemplateGenerateResponse> CompleteDocumentGenerationAsync(
        DocumentWorkflowRequest req,
        DocumentWorkflowPreparation prep,
        CancellationToken ct)
    {
        if (prep.Template.Kind == DocumentTemplateKind.Static)
            return await CompleteStaticDocumentGenerationAsync(req, prep, ct);

        if (prep.Template.Kind == DocumentTemplateKind.Dynamic)
            return await CompleteRhDraftDocumentGenerationAsync(req, prep, ct);

        var template = prep.Template;
        var version = prep.Version;
        var merged = prep.Merged;
        var linkedRequest = prep.LinkedRequest;

        var typeId = template.DocumentTypeId ?? req.DocumentTypeId;
        if (typeId == Guid.Empty)
            typeId = null;

        var rendered = templateEngine.RenderContent(version.StructuredContent, merged);
        var now = DateTimeOffset.UtcNow;
        var genId = Guid.NewGuid();
        var (fileName, pdfBytes) = pdfExport.BuildPdf(
            template.Code,
            tenantAccessor.ResolvedTenantId,
            rendered,
            prep.TitleFallback);
        string storageUri;
        if (templateBlobStorage.IsConfigured)
        {
            await using var stream = new MemoryStream(pdfBytes);
            var key = $"{tenantAccessor.ResolvedTenantId.TrimEnd('/')}/generated/{genId:N}/{fileName}";
            storageUri = await templateBlobStorage.PutTemplateObjectAsync(key, stream, "application/pdf", ct);
        }
        else
        {
            storageUri = $"inline://generated/{genId:N}/{Uri.EscapeDataString(fileName)}";
        }

        var gen = new GeneratedDocument
        {
            Id = genId,
            DocumentRequestId = req.DocumentRequestId,
            OwnerUserId = userContext.UserId!.Value,
            DocumentTypeId = typeId,
            TemplateVersionId = version.Id,
            FileName = fileName,
            StorageUri = storageUri,
            PdfContent = templateBlobStorage.IsConfigured ? null : pdfBytes,
            MimeType = "application/pdf",
            FileSizeBytes = pdfBytes.LongLength,
            Status = GeneratedDocumentStatus.Generated,
            VersionNumber = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GeneratedDocuments.Add(gen);
        if (linkedRequest is not null)
        {
            linkedRequest.Status = DocumentRequestStatus.Generated;
            linkedRequest.UpdatedAt = now;
            var auditDetails = JsonSerializer.Serialize(new
            {
                generatedDocumentId = gen.Id.ToString("D"),
                fileName,
                templateCode = template.Code,
                templateId = template.Id.ToString("D"),
            });
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantAccessor.ResolvedTenantId,
                OccurredAt = now,
                ActorUserId = userContext.UserId,
                Action = "DOCUMENT_GENERATED",
                EntityType = "document_request",
                EntityId = linkedRequest.Id,
                Details = auditDetails,
                Success = true,
                RequestNumber = linkedRequest.RequestNumber,
            });
        }

        await db.SaveChangesAsync(ct);
        return new DocumentTemplateGenerateResponse(gen.Id.ToString(), fileName, storageUri, gen.Status.ToString());
    }

    private async Task<DocumentTemplateGenerateResponse> CompleteStaticDocumentGenerationAsync(
        DocumentWorkflowRequest req,
        DocumentWorkflowPreparation prep,
        CancellationToken ct)
    {
        var template = prep.Template;
        var version = prep.Version;
        var linkedRequest = prep.LinkedRequest;

        if (string.IsNullOrWhiteSpace(version.OriginalAssetUri))
            throw new DocumentationApiException(400, "Modèle statique sans fichier source.");

        var payload = await templateBlobStorage.TryReadObjectAsync(version.OriginalAssetUri, ct);
        if (payload is null)
            throw new DocumentationApiException(400, "Impossible de lire le fichier modèle (MinIO / taille).");

        var typeId = template.DocumentTypeId ?? req.DocumentTypeId;
        if (typeId == Guid.Empty)
            typeId = null;

        var now = DateTimeOffset.UtcNow;
        var genId = Guid.NewGuid();
        var fileName = string.IsNullOrWhiteSpace(payload.FileName) ? $"{template.Code}_document" : payload.FileName;
        string storageUri;
        if (templateBlobStorage.IsConfigured)
        {
            await using var stream = new MemoryStream(payload.Content);
            var key = $"{tenantAccessor.ResolvedTenantId.TrimEnd('/')}/generated/{genId:N}/{Uri.EscapeDataString(fileName)}";
            storageUri = await templateBlobStorage.PutTemplateObjectAsync(key, stream, payload.ContentType, ct);
        }
        else
        {
            storageUri = $"inline://generated/{genId:N}/{Uri.EscapeDataString(fileName)}";
        }

        var gen = new GeneratedDocument
        {
            Id = genId,
            DocumentRequestId = req.DocumentRequestId,
            OwnerUserId = userContext.UserId!.Value,
            DocumentTypeId = typeId,
            TemplateVersionId = version.Id,
            FileName = fileName,
            StorageUri = storageUri,
            PdfContent = templateBlobStorage.IsConfigured ? null : payload.Content,
            MimeType = payload.ContentType,
            FileSizeBytes = payload.Content.LongLength,
            Status = GeneratedDocumentStatus.Generated,
            VersionNumber = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GeneratedDocuments.Add(gen);
        if (linkedRequest is not null)
        {
            linkedRequest.Status = DocumentRequestStatus.Generated;
            linkedRequest.UpdatedAt = now;
            var auditDetails = JsonSerializer.Serialize(new
            {
                generatedDocumentId = gen.Id.ToString("D"),
                fileName,
                templateCode = template.Code,
                templateId = template.Id.ToString("D"),
                staticTemplate = true,
            });
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantAccessor.ResolvedTenantId,
                OccurredAt = now,
                ActorUserId = userContext.UserId,
                Action = "DOCUMENT_GENERATED",
                EntityType = "document_request",
                EntityId = linkedRequest.Id,
                Details = auditDetails,
                Success = true,
                RequestNumber = linkedRequest.RequestNumber,
            });
        }

        await db.SaveChangesAsync(ct);
        return new DocumentTemplateGenerateResponse(gen.Id.ToString(), fileName, storageUri, gen.Status.ToString());
    }

    private async Task<DocumentTemplateGenerateResponse> CompleteRhDraftDocumentGenerationAsync(
        DocumentWorkflowRequest req,
        DocumentWorkflowPreparation prep,
        CancellationToken ct)
    {
        var template = prep.Template;
        var version = prep.Version;
        var merged = prep.Merged;
        var linkedRequest = prep.LinkedRequest;
        var wfOpts = documentWorkflowOptions.Value;

        var structural = templateEngine.ListStructuralResidualsAfterRender(version.StructuredContent, merged);
        if (structural.Count > 0)
        {
            logger.LogWarning(
                "Génération bloquée : marqueurs résiduels sur le modèle {TemplateCode} : {Residuals}",
                template.Code,
                string.Join(", ", structural));
            throw new DocumentationApiException(400,
                "Le document contient encore des marqueurs non remplis ((X), {{variable}}, masques date, tirets, etc.). Complétez les données ou le modèle avant de générer.",
                new { structuralResiduals = structural });
        }

        var contentGeneratedStrict = templateEngine.RenderContent(version.StructuredContent, merged);

        var missingPh = string.IsNullOrWhiteSpace(wfOpts.MissingFieldPlaceholder)
            ? "________"
            : wfOpts.MissingFieldPlaceholder.Trim();
        var displayMerged = new Dictionary<string, string>(merged, StringComparer.OrdinalIgnoreCase);
        if (wfOpts.MarkMissingFieldsInRhDraft)
        {
            foreach (var name in prep.MissingRequired)
            {
                if (!displayMerged.TryGetValue(name, out var v) || string.IsNullOrWhiteSpace(v))
                    displayMerged[name] = missingPh;
            }
        }

        var initialRhText = wfOpts.MarkMissingFieldsInRhDraft
            ? templateEngine.RenderContent(version.StructuredContent, displayMerged)
            : contentGeneratedStrict;

        var typeId = template.DocumentTypeId ?? req.DocumentTypeId;
        if (typeId == Guid.Empty)
            typeId = null;

        var now = DateTimeOffset.UtcNow;
        var genId = Guid.NewGuid();
        var draftLabel = $"{template.Code}_brouillon";

        var gen = new GeneratedDocument
        {
            Id = genId,
            DocumentRequestId = req.DocumentRequestId,
            OwnerUserId = userContext.UserId!.Value,
            DocumentTypeId = typeId,
            TemplateVersionId = version.Id,
            FileName = $"{draftLabel}.pdf",
            StorageUri = string.Empty,
            PdfContent = null,
            MimeType = null,
            FileSizeBytes = null,
            Status = GeneratedDocumentStatus.DraftPendingRhReview,
            VersionNumber = 1,
            ContentGenerated = contentGeneratedStrict,
            ContentFinal = initialRhText,
            RhMissingVariablesJson = prep.MissingRequired.Count > 0
                ? JsonSerializer.Serialize(prep.MissingRequired)
                : null,
            WorkflowVariablesSnapshotJson = JsonSerializer.Serialize(prep.Merged),
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.GeneratedDocuments.Add(gen);

        if (linkedRequest is not null)
        {
            var auditDetails = JsonSerializer.Serialize(new
            {
                generatedDocumentId = gen.Id.ToString("D"),
                templateCode = template.Code,
                templateId = template.Id.ToString("D"),
                draftPendingRh = true,
                missingVariables = prep.MissingRequired,
            });
            db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantAccessor.ResolvedTenantId,
                OccurredAt = now,
                ActorUserId = userContext.UserId,
                Action = "DOCUMENT_DRAFT_CREATED",
                EntityType = "document_request",
                EntityId = linkedRequest.Id,
                Details = auditDetails,
                Success = true,
                RequestNumber = linkedRequest.RequestNumber,
            });
        }

        await db.SaveChangesAsync(ct);

        return new DocumentTemplateGenerateResponse(
            gen.Id.ToString("D"),
            gen.FileName,
            string.Empty,
            gen.Status.ToString(),
            true,
            prep.MissingRequired);
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

    private void EnsureAuthenticated()
    {
        if (!userContext.UserId.HasValue)
            throw new DocumentationApiException(401, "Authentification requise.");
    }

    private void EnsureRhOrAdmin()
    {
        EnsureAuthenticated();
        if (userContext.Role is not (AppRole.Rh or AppRole.Admin))
            throw new DocumentationApiException(403, "Accès refusé.");
    }
}
