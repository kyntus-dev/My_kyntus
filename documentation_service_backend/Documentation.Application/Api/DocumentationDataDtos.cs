using System.Text.Json;

namespace Documentation.Application.Api;

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);

public sealed record DocumentTypeResponse(
    string Id,
    string Name,
    string Code,
    string Description,
    string Department,
    int RetentionDays,
    string WorkflowId,
    bool Mandatory);

public sealed record DocumentRequestResponse(
    string Id,
    string InternalId,
    string Type,
    string RequestDate,
    string Status,
    string EmployeeName,
    string? EmployeeId,
    string? RequesterUserId,
    string? BeneficiaryUserId,
    string? OrganizationalUnitId,
    /// <summary>Identifiant du type document (null si demande « autre » / personnalisée).</summary>
    string? DocumentTypeId,
    string? Reason,
    bool IsCustomType,
    IReadOnlyList<string> AllowedActions,
    string? RejectionReason,
    string? DecidedAt,
    /// <summary>Dernier document généré rattaché (GET …/generated-documents/{id}/file).</summary>
    string? GeneratedDocumentId,
    string? GeneratedAt,
    /// <summary>Chemin relatif API pour téléchargement (préfixer avec l’origine du service Documentation).</summary>
    string? DocumentUrl,
    /// <summary>Modèle choisi par le pilote (demande liée à un modèle RH).</summary>
    string? DocumentTemplateId = null,
    string? DocumentTemplateName = null,
    /// <summary>Commentaires complémentaires saisis par le pilote à la demande.</summary>
    string? ComplementaryComments = null);

public sealed record AuditLogResponse(
    string Id,
    string OccurredAt,
    string? ActorName,
    string? ActorUserId,
    string Action,
    string EntityType,
    string? EntityId,
    bool? Success,
    string? ErrorMessage,
    string? CorrelationId);

/// <summary>Résumé unité organisationnelle (pôle / cellule / département).</summary>
public sealed record OrganizationalUnitSummary(string Id, string Code, string Name, string UnitType);

/// <summary>Profil annuaire (schéma documentation.directory_users).</summary>
public sealed record DirectoryUserResponse(
    string Id,
    string Prenom,
    string Nom,
    string Email,
    string Role,
    string? ManagerId,
    string? CoachId,
    string? RpId,
    string PoleId,
    string CelluleId,
    string DepartementId,
    OrganizationalUnitSummary? Pole,
    OrganizationalUnitSummary? Cellule,
    OrganizationalUnitSummary? Departement);

public sealed record DocumentTemplateListItemResponse(
    string Id,
    string Code,
    string Name,
    string Source,
    /// <summary><c>dynamic</c> ou <c>static</c>.</summary>
    string Kind,
    bool RequiresPilotUpload,
    bool IsActive,
    string? DocumentTypeId,
    string? DocumentTypeName,
    IReadOnlyList<string> VariableNames,
    string? CurrentVersionId,
    int? CurrentVersionNumber,
    string UpdatedAt,
    string? Description,
    string? FileUrl,
    string? CreatedAt);

public sealed record DocumentTemplateGenerateResponse(
    string GeneratedDocumentId,
    string FileName,
    string StorageUri,
    string Status,
    bool NeedsRhEditorReview = false,
    IReadOnlyList<string>? MissingVariables = null);

/// <summary>Contenu éditable pour la validation RH (brouillon).</summary>
public sealed record RhGeneratedDocumentEditorResponse(
    string GeneratedDocumentId,
    string Status,
    string ContentGenerated,
    string ContentEditable,
    IReadOnlyList<string> MissingVariables);

public sealed record UpdateRhGeneratedDocumentContentRequest(string Content);

/// <summary>Lien GET temporaire (signature AWS v4) pour ouvrir un fichier modèle dans le navigateur (bucket MinIO privé).</summary>
public sealed record TemplateSourceFileUrlResponse(string Url, string ExpiresAt);

public sealed record DocumentTemplateVersionResponse(
    string Id,
    int VersionNumber,
    string Status,
    string StructuredContent,
    string? OriginalAssetUri,
    string CreatedAt,
    string? PublishedAt,
    IReadOnlyList<DocumentTemplateVariableResponse> Variables);

public sealed record DocumentTemplateVariableResponse(
    string Id,
    string Name,
    string Type,
    bool IsRequired,
    string? DefaultValue,
    string? ValidationRule,
    string? DisplayLabel,
    string FormScope,
    int SourcePriority,
    string? NormalizedName,
    string? RawPlaceholder,
    int SortOrder)
{
    public string? SourceFieldKey { get; init; }
    public string? MappingConfidence { get; init; }
    public bool MappingConfirmed { get; init; }
    public bool MappingUnavailable { get; init; }
    public string? SuggestedFieldKey { get; init; }
    public string? SuggestedFieldLabel { get; init; }
    public double? MappingScore { get; init; }
}

public sealed record DocumentTemplateDetailResponse(
    string Id,
    string Code,
    string Name,
    string Source,
    /// <summary><c>dynamic</c> ou <c>static</c>.</summary>
    string Kind,
    bool RequiresPilotUpload,
    bool IsActive,
    string? DocumentTypeId,
    string? DocumentTypeName,
    string UpdatedAt,
    string? Description,
    DocumentTemplateVersionResponse? CurrentVersion);

public sealed class TemplateVariableInput
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "text";
    public bool IsRequired { get; set; } = true;
    public string? DefaultValue { get; set; }
    public string? ValidationRule { get; set; }
    /// <summary>Libellé affiché au pilote / RH (ex. « Date d’embauche »).</summary>
    public string? DisplayLabel { get; set; }
    public string? FormScope { get; set; }
    public int? SourcePriority { get; set; }
    public string? NormalizedName { get; set; }
    public string? RawPlaceholder { get; set; }
    public string? SourceFieldKey { get; set; }
    public string? MappingConfidence { get; set; }
    public bool? MappingConfirmed { get; set; }
}

public sealed record InternalEnginePlaceholderResponse(
    string RawToken,
    string NormalizedKey,
    string CanonicalKey,
    string Status,
    string SuggestedLabel,
    string Type,
    bool IsRequired,
    string? ValidationRule);

public sealed record InternalEngineAnalysisResponse(
    string StructuredContent,
    IReadOnlyList<InternalEnginePlaceholderResponse> Placeholders,
    IReadOnlyList<TemplateVariableInput> Variables);

public sealed class InternalEngineTemplateRequest
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public string StructuredContent { get; set; } = "";
    public IReadOnlyList<TemplateVariableInput> Variables { get; set; } = Array.Empty<TemplateVariableInput>();
}

public sealed class CreateDocumentTemplateRequest
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Description métier (optionnel).</summary>
    public string? Description { get; set; }
    public string Source { get; set; } = "UPLOAD";
    /// <summary><c>dynamic</c> (défaut) ou <c>static</c> (fichier prêt, sans variables).</summary>
    public string? Kind { get; set; }
    /// <summary>Surtout pour <c>static</c> : le pilote doit joindre un fichier.</summary>
    public bool RequiresPilotUpload { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public string StructuredContent { get; set; } = "";
    public string? OriginalAssetUri { get; set; }
    public IReadOnlyList<TemplateVariableInput> Variables { get; set; } = Array.Empty<TemplateVariableInput>();
}

public sealed class UpdateDocumentTemplateRequest
{
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public bool? RequiresPilotUpload { get; set; }
}

public sealed class UploadTemplateRequest
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public string FileName { get; set; } = "";
    public string Content { get; set; } = "";
}

public sealed class RuleGenerateTemplateRequest
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? DocumentTypeId { get; set; }
    public string Description { get; set; } = "";
    public IReadOnlyList<string> SuggestedVariables { get; set; } = Array.Empty<string>();
}

/// <summary>Génération de contenu structuré via API IA (OpenAI-compatible).</summary>
public sealed class AiGenerateTemplateRequest
{
    /// <summary>Description métier obligatoire — base du prompt.</summary>
    public string Description { get; set; } = "";
    public string? Name { get; set; }
    public string? Code { get; set; }
    public Guid? DocumentTypeId { get; set; }
}

public sealed class CreateTemplateVersionRequest
{
    public string StructuredContent { get; set; } = "";
    public string Status { get; set; } = "draft";
    public string? OriginalAssetUri { get; set; }
    public IReadOnlyList<TemplateVariableInput> Variables { get; set; } = Array.Empty<TemplateVariableInput>();
}

public sealed class UpdateTemplateStatusRequest
{
    public bool IsActive { get; set; }
}

public sealed class TemplateTestRunRequest
{
    /// <summary>Collaborateur (pilote) — fusion annuaire côté serveur pour l’aperçu.</summary>
    public Guid? BeneficiaryUserId { get; set; }

    public IReadOnlyDictionary<string, string> SampleData { get; set; } = new Dictionary<string, string>();
}

public sealed record TemplateTestRunResponse(
    string RenderedContent,
    IReadOnlyList<string> MissingVariables,
    string PreviewFileName);

public sealed class DocumentTemplateGenerateRequest
{
    public Guid? DocumentRequestId { get; set; }

    /// <summary>Collaborateur concerné — complète les variables depuis <c>directory_users</c> si absentes du corps.</summary>
    public Guid? BeneficiaryUserId { get; set; }

    public Guid? DocumentTypeId { get; set; }
    public IReadOnlyDictionary<string, string> Variables { get; set; } = new Dictionary<string, string>();
}

/// <summary>Workflow RH : aperçu PDF temporaire puis génération finale (même moteur PDF que la persistance).</summary>
public sealed class DocumentWorkflowRequest
{
    public Guid TemplateId { get; set; }

    public Guid? DocumentRequestId { get; set; }

    /// <summary>Collaborateur — fusion annuaire comme <see cref="DocumentTemplateGenerateRequest"/>.</summary>
    public Guid? BeneficiaryUserId { get; set; }

    public Guid? DocumentTypeId { get; set; }

    public IReadOnlyDictionary<string, string> Variables { get; set; } = new Dictionary<string, string>();
}

public sealed class ResolveTemplateEmployeeValuesRequest
{
    public Guid BeneficiaryUserId { get; set; }
    public Dictionary<string, string>? CurrentValues { get; set; }
}

public sealed record TemplateVariableResolutionItem(
    string Name,
    string FormScope,
    string? SourceFieldKey,
    string? DisplayLabel,
    bool IsRequired,
    bool FilledFromDb,
    bool NeedsRhInput,
    bool UnavailableMapping,
    string? DbValue,
    string ResolvedValue);

public sealed record TemplateEmployeeResolutionResponse(
    IReadOnlyList<TemplateVariableResolutionItem> Variables);

/// <summary>Entrée unique : template brut + données base + formulaire (génération IA directe, sans moteur local).</summary>
public sealed class AiDirectDocumentFillRequest
{
    public string Template { get; set; } = "";

    public JsonElement? DbData { get; set; }

    public JsonElement? FormData { get; set; }

    /// <summary>Titre pour l’en-tête du PDF en mode aperçu.</summary>
    public string? DocumentTitle { get; set; }
}

public sealed record AiDirectDocumentFillResponse(
    string Status,
    string? Document,
    IReadOnlyList<string>? Reasons,
    bool ModelReportedMissingData,
    /// <summary>Message utilisateur (ex. génération IA directe dans l’UI).</summary>
    string? Message = null);

/// <summary>Export PDF/DOCX à partir du texte déjà généré par l’IA (sans second appel modèle).</summary>
public sealed class AiDirectRenderRequest
{
    public string Document { get; set; } = "";

    public string Format { get; set; } = "pdf";

    public string? Title { get; set; }
}
