using Documentation.Application.Abstractions;
using Documentation.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Documentation.Infrastructure.Persistence;

public class DocumentationDbContext : DbContext
{
    /// <summary>Noms qualifiés des types ENUM PostgreSQL (évite la lecture en Int32 par défaut EF).</summary>
    private const string TAppRole = "documentation.app_role";
    private const string TDocumentRequestStatus = "documentation.document_request_status";
    private const string TGeneratedDocumentStatus = "documentation.generated_document_status";
    private const string TWorkflowNotificationKey = "documentation.workflow_notification_key";
    private const string TWorkflowActionKey = "documentation.workflow_action_key";
    private const string TStorageType = "documentation.storage_type";

    private readonly IDocumentationTenantAccessor _tenantAccessor;

    /// <summary>
    /// Utilisé par les query filters — doit être une propriété du DbContext (évaluation par instance).
    /// Doit rester non vide : les filtres multi-tenant s’appuient sur <see cref="IDocumentationTenantAccessor.ResolvedTenantId"/>.
    /// </summary>
    internal string CurrentTenantIdForFilter
    {
        get
        {
            var id = _tenantAccessor.ResolvedTenantId;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException(
                    "TenantId non résolu pour les filtres EF : vérifier Documentation:DefaultTenantId et DocumentationTenantAccessor.");
            }

            return id;
        }
    }

    public DocumentationDbContext(
        DbContextOptions<DocumentationDbContext> options,
        IDocumentationTenantAccessor tenantAccessor)
        : base(options)
    {
        _tenantAccessor = tenantAccessor;
    }

    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<WorkflowStepAction> WorkflowStepActions => Set<WorkflowStepAction>();
    public DbSet<DocumentRequest> DocumentRequests => Set<DocumentRequest>();
    public DbSet<GeneratedDocument> GeneratedDocuments => Set<GeneratedDocument>();
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<DocumentTemplateVersion> DocumentTemplateVersions => Set<DocumentTemplateVersion>();
    public DbSet<DocumentTemplateVariable> DocumentTemplateVariables => Set<DocumentTemplateVariable>();
    public DbSet<PermissionPolicy> PermissionPolicies => Set<PermissionPolicy>();
    public DbSet<DmsGeneralConfiguration> DmsGeneralConfigurations => Set<DmsGeneralConfiguration>();
    public DbSet<DmsStorageConfiguration> DmsStorageConfigurations => Set<DmsStorageConfiguration>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DirectoryUser> DirectoryUsers => Set<DirectoryUser>();
    public DbSet<OrganisationUnit> OrganisationUnits => Set<OrganisationUnit>();
    public DbSet<DocumentRequestSequence> DocumentRequestSequences => Set<DocumentRequestSequence>();
    public DbSet<AiApiKey> AiApiKeys => Set<AiApiKey>();
    public DbSet<DocumentRequestFieldValue> DocumentRequestFieldValues => Set<DocumentRequestFieldValue>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantAccessor.ResolvedTenantId;
        foreach (var entry in ChangeTracker.Entries<DocumentRequest>())
        {
            if (entry.State == EntityState.Added && string.IsNullOrEmpty(entry.Entity.TenantId))
                entry.Entity.TenantId = tenantId;
        }

        foreach (var entry in ChangeTracker.Entries<AuditLog>())
        {
            if (entry.State == EntityState.Added && string.IsNullOrEmpty(entry.Entity.TenantId))
                entry.Entity.TenantId = tenantId;
        }

        foreach (var entry in ChangeTracker.Entries<DocumentTemplate>())
        {
            if (entry.State == EntityState.Added && string.IsNullOrEmpty(entry.Entity.TenantId))
                entry.Entity.TenantId = tenantId;
        }

        foreach (var entry in ChangeTracker.Entries<DocumentTemplateVersion>())
        {
            if (entry.State == EntityState.Added && string.IsNullOrEmpty(entry.Entity.TenantId))
                entry.Entity.TenantId = tenantId;
        }

        foreach (var entry in ChangeTracker.Entries<AiApiKey>())
        {
            if (entry.State == EntityState.Added && string.IsNullOrEmpty(entry.Entity.TenantId))
                entry.Entity.TenantId = tenantId;
        }

        foreach (var entry in ChangeTracker.Entries<DocumentRequestFieldValue>())
        {
            if (entry.State == EntityState.Added && string.IsNullOrEmpty(entry.Entity.TenantId))
                entry.Entity.TenantId = tenantId;
            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Entity.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("documentation");

        modelBuilder.HasPostgresEnum<DocumentRequestStatus>(schema: "documentation");
        modelBuilder.HasPostgresEnum<GeneratedDocumentStatus>(schema: "documentation");
        modelBuilder.HasPostgresEnum<WorkflowNotificationKey>(schema: "documentation");
        modelBuilder.HasPostgresEnum<WorkflowActionKey>(schema: "documentation");
        modelBuilder.HasPostgresEnum<AppRole>(schema: "documentation");
        modelBuilder.HasPostgresEnum<StorageType>(schema: "documentation");
        modelBuilder.HasPostgresEnum<DocumentTemplateKind>(schema: "documentation");

        modelBuilder.Entity<DocumentRequest>().HasQueryFilter(dr => dr.TenantId == CurrentTenantIdForFilter);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(a => a.TenantId == CurrentTenantIdForFilter);
        modelBuilder.Entity<DirectoryUser>().HasQueryFilter(u => u.TenantId == CurrentTenantIdForFilter);
        modelBuilder.Entity<OrganisationUnit>().HasQueryFilter(ou => ou.TenantId == CurrentTenantIdForFilter);
        modelBuilder.Entity<DocumentTemplate>().HasQueryFilter(t => t.TenantId == CurrentTenantIdForFilter);
        modelBuilder.Entity<DocumentTemplateVersion>().HasQueryFilter(v => v.TenantId == CurrentTenantIdForFilter);
        modelBuilder.Entity<AiApiKey>().HasQueryFilter(k => k.TenantId == CurrentTenantIdForFilter);
        modelBuilder.Entity<DocumentRequestFieldValue>().HasQueryFilter(f => f.TenantId == CurrentTenantIdForFilter);

        modelBuilder.Entity<Workflow>(e =>
        {
            e.ToTable("workflows");
            e.Property(x => x.Name).HasMaxLength(255);
        });

        modelBuilder.Entity<DocumentType>(e =>
        {
            e.ToTable("document_types");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(255);
            e.Property(x => x.DepartmentCode).HasMaxLength(128);
            e.HasOne(x => x.Workflow)
                .WithMany(w => w.DocumentTypes)
                .HasForeignKey(x => x.WorkflowId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WorkflowStep>(e =>
        {
            e.ToTable("workflow_steps");
            e.Property(x => x.AssignedRole).HasColumnType(TAppRole);
            e.Property(x => x.NotificationKey).HasColumnType(TWorkflowNotificationKey);
            e.Property(x => x.StepKey).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(255);
            e.HasOne(x => x.Workflow)
                .WithMany(w => w.Steps)
                .HasForeignKey(x => x.WorkflowId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.WorkflowId);
        });

        modelBuilder.Entity<WorkflowStepAction>(e =>
        {
            e.ToTable("workflow_step_actions");
            e.Property(x => x.Action).HasColumnType(TWorkflowActionKey);
            e.HasKey(x => new { x.WorkflowStepId, x.Action });
            e.HasOne(x => x.Step)
                .WithMany(s => s.Actions)
                .HasForeignKey(x => x.WorkflowStepId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentRequest>(e =>
        {
            e.ToTable("document_requests");
            e.Property(x => x.Status).HasColumnType(TDocumentRequestStatus);
            e.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            e.HasIndex(x => x.RequesterUserId);
            e.HasIndex(x => x.BeneficiaryUserId);
            e.Property(x => x.RequestNumber).HasMaxLength(32);
            e.HasIndex(x => new { x.TenantId, x.RequestNumber })
                .IsUnique()
                .HasFilter("request_number IS NOT NULL");
            e.HasOne(x => x.DocumentType)
                .WithMany(t => t.DocumentRequests)
                .HasForeignKey(x => x.DocumentTypeId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.DocumentTemplate)
                .WithMany()
                .HasForeignKey(x => x.DocumentTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.OrganizationalUnit)
                .WithMany()
                .HasForeignKey(x => x.OrganizationalUnitId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DocumentRequestFieldValue>(e =>
        {
            e.ToTable("document_request_field_values");
            e.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
            e.Property(x => x.FieldName).HasMaxLength(128).IsRequired();
            e.Property(x => x.FieldValue).HasColumnType("text");
            e.HasIndex(x => new { x.DocumentRequestId, x.FieldName }).IsUnique();
            e.HasOne(x => x.DocumentRequest)
                .WithMany(r => r.FieldValues)
                .HasForeignKey(x => x.DocumentRequestId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiApiKey>(e =>
        {
            e.ToTable("ai_api_keys");
            e.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Provider).HasMaxLength(32).IsRequired();
            e.Property(x => x.Label).HasMaxLength(128);
            e.Property(x => x.ApiKey).HasColumnType("text").IsRequired();
        });

        modelBuilder.Entity<GeneratedDocument>(e =>
        {
            e.ToTable("generated_documents");
            e.Property(x => x.Status).HasColumnType(TGeneratedDocumentStatus);
            e.Property(x => x.FileName).HasMaxLength(512);
            e.Property(x => x.MimeType).HasMaxLength(128);
            e.Property(x => x.PdfContent).HasColumnType("bytea");
            e.Property(x => x.ChecksumSha256).HasMaxLength(64);
            e.Property(x => x.ContentGenerated).HasColumnType("text");
            e.Property(x => x.ContentFinal).HasColumnType("text");
            e.Property(x => x.RhMissingVariablesJson).HasColumnName("rh_missing_variables").HasColumnType("text");
            e.Property(x => x.WorkflowVariablesSnapshotJson)
                .HasColumnName("workflow_variables_snapshot")
                .HasColumnType("jsonb");
            e.HasOne(x => x.DocumentRequest)
                .WithMany(r => r.GeneratedDocuments)
                .HasForeignKey(x => x.DocumentRequestId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.DocumentType)
                .WithMany(t => t.GeneratedDocuments)
                .HasForeignKey(x => x.DocumentTypeId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.TemplateVersion)
                .WithMany(v => v.GeneratedDocuments)
                .HasForeignKey(x => x.TemplateVersionId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.DocumentRequestId, x.CreatedAt });
        });

        modelBuilder.Entity<DocumentTemplate>(e =>
        {
            e.ToTable("document_templates");
            e.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(255);
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.Source).HasMaxLength(32);
            e.Property(x => x.Kind).HasColumnType("documentation.document_template_kind");
            e.HasIndex(x => x.TenantId);
            e.HasOne(x => x.DocumentType)
                .WithMany(t => t.DocumentTemplates)
                .HasForeignKey(x => x.DocumentTypeId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CurrentVersion)
                .WithMany()
                .HasForeignKey(x => x.CurrentVersionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DocumentTemplateVersion>(e =>
        {
            e.ToTable("document_template_versions");
            e.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
            e.Property(x => x.StructuredContent).HasColumnType("jsonb");
            e.Property(x => x.OriginalAssetUri).HasColumnType("text");
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => new { x.TemplateId, x.VersionNumber }).IsUnique();
            e.HasOne(x => x.Template)
                .WithMany(t => t.Versions)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentTemplateVariable>(e =>
        {
            e.ToTable("document_template_variables");
            e.Property(x => x.VariableName).HasMaxLength(128);
            e.Property(x => x.VariableType).HasMaxLength(16).IsRequired();
            e.Property(x => x.DefaultValue).HasColumnType("text");
            e.Property(x => x.ValidationRule).HasColumnType("text");
            e.Property(x => x.DisplayLabel).HasMaxLength(255);
            e.Property(x => x.FormScope).HasMaxLength(16).IsRequired();
            e.Property(x => x.NormalizedName).HasMaxLength(128);
            e.Property(x => x.RawPlaceholder).HasMaxLength(255);
            e.Property(x => x.SourceFieldKey).HasMaxLength(128);
            e.Property(x => x.MappingConfidence).HasMaxLength(16);
            e.HasOne(x => x.Template)
                .WithMany(t => t.Variables)
                .HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TemplateId);
            e.HasOne(x => x.TemplateVersion)
                .WithMany(v => v.Variables)
                .HasForeignKey(x => x.TemplateVersionId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TemplateVersionId, x.VariableName })
                .IsUnique()
                .HasFilter("template_version_id IS NOT NULL");
        });

        modelBuilder.Entity<PermissionPolicy>(e =>
        {
            e.ToTable("permission_policies");
            e.Property(x => x.Role).HasColumnType(TAppRole);
            e.Property(x => x.DepartmentCode).HasMaxLength(128);
            e.HasOne(x => x.DocumentType)
                .WithMany(t => t.PermissionPolicies)
                .HasForeignKey(x => x.DocumentTypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DmsGeneralConfiguration>(e =>
        {
            e.ToTable("dms_general_configuration");
            e.Property(x => x.SystemName).HasMaxLength(255);
            e.Property(x => x.DefaultLanguage).HasMaxLength(16);
            e.Property(x => x.DefaultTimezone).HasMaxLength(64);
            e.Property(x => x.NumberingPattern).HasMaxLength(128);
        });

        modelBuilder.Entity<DmsStorageConfiguration>(e =>
        {
            e.ToTable("dms_storage_configuration");
            e.Property(x => x.StorageType).HasColumnType(TStorageType);
            e.Property(x => x.BucketName).HasMaxLength(255);
            e.Property(x => x.Region).HasMaxLength(64);
            e.Property(x => x.AccessKeyReference).HasMaxLength(512);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.TenantId);
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.EntityType).HasMaxLength(64);
            e.Property(x => x.Details).HasColumnType("jsonb");
            e.Property(x => x.RequestNumber).HasMaxLength(32);
        });

        modelBuilder.Entity<OrganisationUnit>(e =>
        {
            e.ToTable("organisation_units");
            e.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
            e.Property(x => x.UnitType).HasMaxLength(32);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(255);
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasOne<OrganisationUnit>()
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DirectoryUser>(e =>
        {
            e.ToTable("directory_users");
            e.Property(x => x.Role).HasColumnType(TAppRole);
            e.Property(x => x.TenantId).HasMaxLength(64);
            e.Property(x => x.Prenom).HasMaxLength(128);
            e.Property(x => x.Nom).HasMaxLength(128);
            e.Property(x => x.Email).HasMaxLength(255);
            e.Property(x => x.Cin).HasMaxLength(32);
            e.Property(x => x.Rib).HasMaxLength(64);
            e.Property(x => x.ImmatriculationCnss).HasMaxLength(32);
            e.HasIndex(x => x.TenantId);
            e.HasIndex(x => x.ManagerId);
            e.HasIndex(x => x.CoachId);
            e.HasIndex(x => x.RpId);
            e.HasIndex(x => x.PoleId);
            e.HasIndex(x => x.CelluleId);
            e.HasIndex(x => x.DepartementId);
            e.HasOne<DirectoryUser>()
                .WithMany()
                .HasForeignKey(x => x.ManagerId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne<DirectoryUser>()
                .WithMany()
                .HasForeignKey(x => x.CoachId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne<DirectoryUser>()
                .WithMany()
                .HasForeignKey(x => x.RpId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne<OrganisationUnit>()
                .WithMany()
                .HasForeignKey(x => x.PoleId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrganisationUnit>()
                .WithMany()
                .HasForeignKey(x => x.CelluleId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrganisationUnit>()
                .WithMany()
                .HasForeignKey(x => x.DepartementId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<DocumentRequestSequence>(e =>
        {
            e.ToTable("document_request_sequences");
            e.HasKey(x => new { x.TenantId, x.Year });
            e.Property(x => x.TenantId).HasMaxLength(64);
        });
    }
}
