namespace Documentation.Domain.Entities;

public class Workflow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool AuditEnabled { get; set; }
    public bool AuditReadOnly { get; set; }
    public bool AuditLogs { get; set; }
    public bool AuditHistory { get; set; }
    public bool AuditExport { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<DocumentType> DocumentTypes { get; set; } = new List<DocumentType>();
    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
}

public class DocumentType
{
    public Guid Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? DepartmentCode { get; set; }
    public int RetentionDays { get; set; }
    public Guid? WorkflowId { get; set; }
    public bool IsMandatory { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Workflow? Workflow { get; set; }
    public ICollection<DocumentRequest> DocumentRequests { get; set; } = new List<DocumentRequest>();
    public ICollection<GeneratedDocument> GeneratedDocuments { get; set; } = new List<GeneratedDocument>();
    public ICollection<PermissionPolicy> PermissionPolicies { get; set; } = new List<PermissionPolicy>();
    public ICollection<DocumentTemplate> DocumentTemplates { get; set; } = new List<DocumentTemplate>();
}

public class WorkflowStep
{
    public Guid Id { get; set; }
    public Guid WorkflowId { get; set; }
    public int StepOrder { get; set; }
    public string StepKey { get; set; } = "";
    public string Name { get; set; } = "";
    public AppRole AssignedRole { get; set; }
    public int SlaHours { get; set; }
    public WorkflowNotificationKey NotificationKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Workflow Workflow { get; set; } = null!;
    public ICollection<WorkflowStepAction> Actions { get; set; } = new List<WorkflowStepAction>();
}

public class WorkflowStepAction
{
    public Guid WorkflowStepId { get; set; }
    public WorkflowActionKey Action { get; set; }

    public WorkflowStep Step { get; set; } = null!;
}

public class DocumentRequest
{
    public Guid Id { get; set; }

    /// <summary>Isolation multi-tenant (aligné sur <c>document_requests.tenant_id</c>).</summary>
    public string TenantId { get; set; } = "";

    public string? RequestNumber { get; set; }
    public Guid RequesterUserId { get; set; }
    public Guid? BeneficiaryUserId { get; set; }
    public Guid? DocumentTypeId { get; set; }

    /// <summary>Modèle documentaire choisi par le pilote (si la demande part d’un modèle RH).</summary>
    public Guid? DocumentTemplateId { get; set; }

    public bool IsCustomType { get; set; }
    public string? CustomTypeDescription { get; set; }
    public string? Reason { get; set; }
    public string? ComplementaryComments { get; set; }
    public DocumentRequestStatus Status { get; set; }
    public Guid? DecidedByUserId { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? RejectionReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Rattachement organisationnel au moment de la demande (département / unité).</summary>
    public Guid? OrganizationalUnitId { get; set; }

    public OrganisationUnit? OrganizationalUnit { get; set; }
    public DocumentType? DocumentType { get; set; }
    public DocumentTemplate? DocumentTemplate { get; set; }
    public ICollection<GeneratedDocument> GeneratedDocuments { get; set; } = new List<GeneratedDocument>();
    public ICollection<DocumentRequestFieldValue> FieldValues { get; set; } = new List<DocumentRequestFieldValue>();
}

/// <summary>Valeurs saisies par le pilote / RH pour une demande (formulaire dynamique par template).</summary>
public class DocumentRequestFieldValue
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public Guid DocumentRequestId { get; set; }
    public string FieldName { get; set; } = "";
    public string? FieldValue { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public DocumentRequest DocumentRequest { get; set; } = null!;
}

/// <summary>Clé API IA (OpenAI-compatible) par tenant ; une seule active à la fois (index partiel SQL).</summary>
public class AiApiKey
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public string Provider { get; set; } = "openai";
    public string? Label { get; set; }
    public string ApiKey { get; set; } = "";
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class GeneratedDocument
{
    public Guid Id { get; set; }
    public Guid? DocumentRequestId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public Guid? TemplateVersionId { get; set; }
    public string FileName { get; set; } = "";
    public string StorageUri { get; set; } = "";

    /// <summary>PDF brut lorsque MinIO n’est pas configuré (<c>bytea</c>).</summary>
    public byte[]? PdfContent { get; set; }

    public string? MimeType { get; set; }
    public long? FileSizeBytes { get; set; }
    public GeneratedDocumentStatus Status { get; set; }
    public int VersionNumber { get; set; }
    public string? ChecksumSha256 { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Rendu automatique strict (pas de valeurs inventées ; champs absents vides).</summary>
    public string? ContentGenerated { get; set; }

    /// <summary>Texte édité par le RH (brouillon) puis source du PDF après finalisation.</summary>
    public string? ContentFinal { get; set; }

    /// <summary>JSON : liste des noms de variables obligatoires vides au moment du brouillon (aide éditeur).</summary>
    public string? RhMissingVariablesJson { get; set; }

    /// <summary>
    /// JSON : fusion complète des variables au moment de la création du brouillon (identique à <c>PrepareDocumentWorkflowAsync</c> / aperçu),
    /// afin que le DOCX final et les exports utilisent les mêmes valeurs que l’écran (champs RH non persistés en base inclus).
    /// </summary>
    public string? WorkflowVariablesSnapshotJson { get; set; }

    public DocumentRequest? DocumentRequest { get; set; }
    public DocumentType? DocumentType { get; set; }
    public DocumentTemplateVersion? TemplateVersion { get; set; }
}

public class DocumentTemplate
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Description métier (optionnel), saisie RH.</summary>
    public string? Description { get; set; }
    public string Source { get; set; } = "UPLOAD";
    /// <summary>Dynamique (contenu + variables) ou statique (fichier source seul).</summary>
    public DocumentTemplateKind Kind { get; set; } = DocumentTemplateKind.Dynamic;
    /// <summary>Pour un modèle statique : le pilote doit uploader un fichier (scan, pièce jointe).</summary>
    public bool RequiresPilotUpload { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CurrentVersionId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? DocumentTypeId { get; set; }
    public DocumentType? DocumentType { get; set; }
    public DocumentTemplateVersion? CurrentVersion { get; set; }

    public ICollection<DocumentTemplateVariable> Variables { get; set; } = new List<DocumentTemplateVariable>();
    public ICollection<DocumentTemplateVersion> Versions { get; set; } = new List<DocumentTemplateVersion>();
}

public class DocumentTemplateVersion
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public string TenantId { get; set; } = "";
    public int VersionNumber { get; set; }
    public string Status { get; set; } = "draft";
    public string StructuredContent { get; set; } = "";
    public string? OriginalAssetUri { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }

    public DocumentTemplate Template { get; set; } = null!;
    public ICollection<DocumentTemplateVariable> Variables { get; set; } = new List<DocumentTemplateVariable>();
    public ICollection<GeneratedDocument> GeneratedDocuments { get; set; } = new List<GeneratedDocument>();
}

public class DocumentTemplateVariable
{
    public Guid Id { get; set; }
    public Guid TemplateId { get; set; }
    public Guid? TemplateVersionId { get; set; }
    public string VariableName { get; set; } = "";
    public string VariableType { get; set; } = "text";
    public bool IsRequired { get; set; } = true;
    public string? DefaultValue { get; set; }
    public string? ValidationRule { get; set; }
    /// <summary>Libellé formulaire RH / pilote (ex. « Numéro CIN »).</summary>
    public string? DisplayLabel { get; set; }
    public string FormScope { get; set; } = "pilot";
    public int SourcePriority { get; set; } = 0;
    public string? NormalizedName { get; set; }
    public string? RawPlaceholder { get; set; }
    /// <summary>Clé stable du catalogue <c>/users/fields</c> lorsque la variable est alimentée depuis l’employé.</summary>
    public string? SourceFieldKey { get; set; }
    /// <summary><c>high</c>, <c>medium</c> ou <c>low</c>.</summary>
    public string? MappingConfidence { get; set; }
    public bool MappingConfirmed { get; set; }
    public int SortOrder { get; set; }

    public DocumentTemplate Template { get; set; } = null!;
    public DocumentTemplateVersion? TemplateVersion { get; set; }
}

public class PermissionPolicy
{
    public Guid Id { get; set; }
    public AppRole Role { get; set; }
    public Guid? DocumentTypeId { get; set; }
    public string? DepartmentCode { get; set; }
    public bool CanRead { get; set; }
    public bool CanCreate { get; set; }
    public bool CanUpdate { get; set; }
    public bool CanDelete { get; set; }
    public bool CanValidate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public DocumentType? DocumentType { get; set; }
}

public class DmsGeneralConfiguration
{
    public Guid Id { get; set; }
    public string SystemName { get; set; } = "";
    public string DefaultLanguage { get; set; } = "";
    public string DefaultTimezone { get; set; } = "";
    public int MaxFileSizeMB { get; set; }
    public string AllowedFileTypes { get; set; } = "";
    public bool VersioningEnabled { get; set; }
    public int RetentionDaysDefault { get; set; }
    public bool DocumentsMandatoryByType { get; set; }
    public bool AutoNumberingEnabled { get; set; }
    public string NumberingPattern { get; set; } = "";
    public bool EncryptionEnabled { get; set; }
    public bool ExternalSharingEnabled { get; set; }
    public bool ElectronicSignatureEnabled { get; set; }
    public bool EmailOnUpload { get; set; }
    public bool EmailOnValidation { get; set; }
    public bool EmailOnRejection { get; set; }
    public bool ReminderExpiredEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class DmsStorageConfiguration
{
    public Guid Id { get; set; }
    public StorageType StorageType { get; set; }
    public string? ApiUrl { get; set; }
    public string? BucketName { get; set; }
    public string? Region { get; set; }
    public string? AccessKeyReference { get; set; }
    public bool BackupEnabled { get; set; }
    public bool CompressionEnabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class AuditLog
{
    public Guid Id { get; set; }

    /// <summary>Isolation multi-tenant (aligné sur <c>audit_logs.tenant_id</c>).</summary>
    public string TenantId { get; set; } = "";

    public DateTimeOffset OccurredAt { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public Guid? CorrelationId { get; set; }
    public System.Net.IPAddress? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Details { get; set; }
    public bool? Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Optionnel — enrichissement post 004 (cible métier).</summary>
    public Guid? TargetUserId { get; set; }

    /// <summary>Optionnel — traçabilité lisible (numéro REQ).</summary>
    public string? RequestNumber { get; set; }
}

/// <summary>Unité organisationnelle : pôle → cellule → département (table <c>organisation_units</c>).</summary>
public class OrganisationUnit
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public Guid? ParentId { get; set; }

    /// <summary><c>POLE</c>, <c>CELLULE</c> ou <c>DEPARTEMENT</c> (schéma SQL).</summary>
    public string UnitType { get; set; } = "";

    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Annuaire utilisateurs (données réelles — table <c>directory_users</c>).</summary>
public class DirectoryUser
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = "";
    public string Prenom { get; set; } = "";
    public string Nom { get; set; } = "";
    public string Email { get; set; } = "";
    public AppRole Role { get; set; }

    /// <summary>Rôle coach : manager d’agence (hiérarchie métier).</summary>
    public Guid? ManagerId { get; set; }

    /// <summary>Rôle pilote : coach référent.</summary>
    public Guid? CoachId { get; set; }

    /// <summary>Rôle manager : RP de tutelle (chaîne métier Manager → RP).</summary>
    public Guid? RpId { get; set; }

    public Guid PoleId { get; set; }
    public Guid CelluleId { get; set; }
    public Guid DepartementId { get; set; }

    public string? Cin { get; set; }
    public string? Rib { get; set; }
    public string? ImmatriculationCnss { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class DocumentRequestSequence
{
    public string TenantId { get; set; } = "";
    public int Year { get; set; }
    public int LastValue { get; set; }
}
