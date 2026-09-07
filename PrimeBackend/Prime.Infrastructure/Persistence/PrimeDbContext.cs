using Kyntus.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Prime.Domain.Entities;

namespace Prime.Infrastructure.Persistence;

public class PrimeDbContext(DbContextOptions<PrimeDbContext> options) : DbContext(options)
{
    public DbSet<PoleEntity> Poles => Set<PoleEntity>();
    public DbSet<CelluleEntity> Cellules => Set<CelluleEntity>();
    public DbSet<ServiceEntity> Services => Set<ServiceEntity>();
    public DbSet<EmployeeEntity> Employees => Set<EmployeeEntity>();
    public DbSet<SupervisorPrimeFicheEntity> SupervisorPrimeFiches => Set<SupervisorPrimeFicheEntity>();
    public DbSet<ServicePrimeIndicatorEntity> ServicePrimeIndicators => Set<ServicePrimeIndicatorEntity>();
    public DbSet<EmployeeServiceIndicatorPonderationEntity> EmployeeServiceIndicatorPonderations =>
        Set<EmployeeServiceIndicatorPonderationEntity>();
    public DbSet<ServicePoleLinePonderationEntity> ServicePoleLinePonderations => Set<ServicePoleLinePonderationEntity>();
    public DbSet<CommonLinePonderationEntity> CommonLinePonderations => Set<CommonLinePonderationEntity>();
    public DbSet<SupervisorCellulePrimeDraft> SupervisorCellulePrimeDrafts => Set<SupervisorCellulePrimeDraft>();
    public DbSet<EmployeePrimeServiceFiche> EmployeePrimeServiceFiches => Set<EmployeePrimeServiceFiche>();
    public DbSet<PrimeHistoricalFicheEntity> PrimeHistoricalFiches => Set<PrimeHistoricalFicheEntity>();
    public DbSet<EmployeePrimeFicheValidationHistory> EmployeePrimeFicheValidationHistories =>
        Set<EmployeePrimeFicheValidationHistory>();
    // ---- Phase 1.3 : Administration ----
    public DbSet<RbacPermission> RbacPermissions => Set<RbacPermission>();
    public DbSet<WorkflowStepConfig> WorkflowSteps => Set<WorkflowStepConfig>();
    public DbSet<WorkflowGlobalConfig> WorkflowGlobalConfigs => Set<WorkflowGlobalConfig>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Anomaly> Anomalies => Set<Anomaly>();
    public DbSet<GlobalPoolWorkflowStep> GlobalPoolWorkflowSteps => Set<GlobalPoolWorkflowStep>();
    public DbSet<GlobalPoolApprovalEntity> GlobalPoolApprovals => Set<GlobalPoolApprovalEntity>();
    public DbSet<GlobalPoolScopeSynthesisEntity> GlobalPoolScopeSyntheses => Set<GlobalPoolScopeSynthesisEntity>();
    public DbSet<GlobalPoolSynthesisLineEntity> GlobalPoolSynthesisLines => Set<GlobalPoolSynthesisLineEntity>();
    public DbSet<PrimeAbsenceSanctionConfigEntity> PrimeAbsenceSanctionConfigs => Set<PrimeAbsenceSanctionConfigEntity>();
    public DbSet<GlobalPoolSynthesisLineHistoryEntity> GlobalPoolSynthesisLineHistories =>
        Set<GlobalPoolSynthesisLineHistoryEntity>();
    public DbSet<BusinessDepartmentEntity> BusinessDepartments => Set<BusinessDepartmentEntity>();
    public DbSet<BusinessDepartmentPoleEntity> BusinessDepartmentPoles => Set<BusinessDepartmentPoleEntity>();
    public DbSet<AllowanceTypeEntity> AllowanceTypes => Set<AllowanceTypeEntity>();
    public DbSet<AllowanceTypeDepartmentEntity> AllowanceTypeDepartments => Set<AllowanceTypeDepartmentEntity>();
    public DbSet<AllowanceRequestEntity> AllowanceRequests => Set<AllowanceRequestEntity>();
    public DbSet<AllowanceRequestHistoryEntity> AllowanceRequestHistories => Set<AllowanceRequestHistoryEntity>();
    public DbSet<AllowanceWorkflowStepEntity> AllowanceWorkflowSteps => Set<AllowanceWorkflowStepEntity>();
    public DbSet<AllowanceRuleEntity> AllowanceRules => Set<AllowanceRuleEntity>();
    public DbSet<AllowanceNoBonusMarkerEntity> AllowanceNoBonusMarkers => Set<AllowanceNoBonusMarkerEntity>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PoleEntity>(e =>
        {
            e.ToTable("prime_pole");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.HasMany(x => x.Cellules)
                .WithOne(x => x.Pole)
                .HasForeignKey(x => x.PoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CelluleEntity>(e =>
        {
            e.ToTable("prime_cellule");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
            e.HasMany(x => x.Services)
                .WithOne(x => x.Cellule)
                .HasForeignKey(x => x.CelluleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServiceEntity>(e =>
        {
            e.ToTable("prime_service");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256);
        });

        modelBuilder.Entity<SupervisorPrimeFicheEntity>(e =>
        {
            e.ToTable("prime_supervisor_fiche");
            e.HasKey(x => x.Id);
            e.Property(x => x.SupervisorUserId).HasMaxLength(128);
            e.Property(x => x.CelluleId).HasMaxLength(128);
            e.Property(x => x.Period).HasMaxLength(16);
            e.Property(x => x.TemplateId).HasMaxLength(128);
            e.Property(x => x.TemplateDisplayName).HasMaxLength(512);
            e.Property(x => x.Status).HasMaxLength(32);
            e.HasIndex(x => new { x.SupervisorUserId, x.Period });
        });

        modelBuilder.Entity<EmployeeEntity>(e =>
        {
            e.ToTable("prime_employee");
            e.HasKey(x => x.Id);
            e.Property(x => x.FirstName).HasMaxLength(128);
            e.Property(x => x.LastName).HasMaxLength(128);
            e.Property(x => x.Role).HasMaxLength(64);
            e.Property(x => x.Email).HasMaxLength(256);
            e.HasIndex(x => x.ServiceId);
            e.HasIndex(x => x.PoleId);
            e.HasIndex(x => x.CelluleId);
            e.Property(x => x.CelluleId).IsRequired(false);
            e.Property(x => x.ServiceId).IsRequired(false);
            e.Property(x => x.BusinessDepartmentId).HasMaxLength(64).IsRequired(false);
            e.Property(x => x.BusinessDepartmentKind).HasMaxLength(32).IsRequired(false);
            e.Property(x => x.ReferentTechniqueId).HasMaxLength(128).IsRequired(false);
            e.Property(x => x.ChefDeProjetId).HasMaxLength(128).IsRequired(false);
            e.Property(x => x.SuperviseurId).HasMaxLength(128).IsRequired(false);
            e.HasOne<ServiceEntity>()
                .WithMany()
                .HasForeignKey(x => x.ServiceId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ServicePrimeIndicatorEntity>(e =>
        {
            e.ToTable("prime_service_prime_indicator");
            e.HasKey(x => x.Id);
            e.Property(x => x.ServiceId).HasMaxLength(128);
            e.Property(x => x.Label).HasMaxLength(512);
            e.Property(x => x.PonderationPrimePct).HasPrecision(9, 4);
            e.Property(x => x.PonderationChallengePct).HasPrecision(9, 4);
            e.Property(x => x.TemplateStableId).HasMaxLength(256);
            e.HasIndex(x => x.ServiceId);
            e.HasIndex(x => new { x.ServiceId, x.SortOrder });
            e.HasOne(x => x.Service)
                .WithMany(x => x.PrimeIndicators)
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmployeeServiceIndicatorPonderationEntity>(e =>
        {
            e.ToTable("prime_employee_service_indicator_ponderation");
            e.HasKey(x => x.Id);
            e.Property(x => x.EmployeeId).HasMaxLength(128).IsRequired();
            e.Property(x => x.PonderationPrimePct).HasPrecision(9, 4);
            e.Property(x => x.PonderationChallengePct).HasPrecision(9, 4);
            e.HasIndex(x => new { x.EmployeeId, x.ServicePrimeIndicatorId }).IsUnique();
            e.HasIndex(x => x.ServicePrimeIndicatorId);
            e.HasOne(x => x.Indicator)
                .WithMany(x => x.EmployeePonderations)
                .HasForeignKey(x => x.ServicePrimeIndicatorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServicePoleLinePonderationEntity>(e =>
        {
            e.ToTable("prime_service_pole_line_ponderation");
            e.HasKey(x => x.Id);
            e.Property(x => x.ServiceId).HasMaxLength(128);
            e.Property(x => x.TemplateStableId).HasMaxLength(256);
            e.Property(x => x.Label).HasMaxLength(512);
            e.Property(x => x.PonderationPrimePct).HasPrecision(9, 4);
            e.Property(x => x.PonderationChallengePct).HasPrecision(9, 4);
            e.HasIndex(x => x.ServiceId);
            e.HasIndex(x => new { x.ServiceId, x.TemplateStableId }).IsUnique();
            e.HasIndex(x => new { x.ServiceId, x.SortOrder });
            e.HasOne(x => x.Service)
                .WithMany(x => x.PoleLinePonderations)
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommonLinePonderationEntity>(e =>
        {
            e.ToTable("prime_common_line_ponderation");
            e.HasKey(x => x.Id);
            e.Property(x => x.ScopeType).HasMaxLength(16).IsRequired();
            e.Property(x => x.ScopeId).HasMaxLength(128).IsRequired();
            e.Property(x => x.TemplateId).HasMaxLength(128).IsRequired();
            e.Property(x => x.TemplateStableId).HasMaxLength(256).IsRequired();
            e.Property(x => x.Label).HasMaxLength(512);
            e.Property(x => x.Contract).HasMaxLength(32);
            e.Property(x => x.PonderationPrimePct).HasPrecision(9, 4);
            e.Property(x => x.PonderationChallengePct).HasPrecision(9, 4);
            e.Property(x => x.CreatedBy).HasMaxLength(128);
            e.HasIndex(x => new { x.ScopeType, x.ScopeId, x.TemplateId, x.TemplateStableId, x.EffectiveFrom });
            e.HasIndex(x => new { x.ScopeType, x.ScopeId, x.TemplateStableId, x.EffectiveTo });
        });

        modelBuilder.Entity<SupervisorCellulePrimeDraft>(e =>
        {
            e.ToTable("prime_supervisor_cellule_prime_draft");
            e.HasKey(x => x.Id);
            e.Property(x => x.SupervisorUserId).HasMaxLength(128);
            e.Property(x => x.RootPoleId).HasMaxLength(128);
            e.Property(x => x.CelluleId).HasMaxLength(128);
            e.Property(x => x.Period).HasMaxLength(16);
            e.Property(x => x.TemplateId).HasMaxLength(128);
            e.Property(x => x.TemplateDisplayName).HasMaxLength(512);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.GlobalPoolFileName).HasMaxLength(512);
            e.Property(x => x.GlobalPoolUploadedByUserId).HasMaxLength(128);
            e.Property(x => x.GlobalPoolManagerApprovedByUserId).HasMaxLength(128);
            e.Property(x => x.GlobalPoolRhApprovedByUserId).HasMaxLength(128);
            e.Property(x => x.GlobalPoolComptaAckByUserId).HasMaxLength(128);
            e.HasIndex(x => new { x.SupervisorUserId, x.Period });
            e.HasIndex(x => new { x.SupervisorUserId, x.RootPoleId, x.Period }).IsUnique();
            e.HasIndex(x => new { x.SupervisorUserId, x.CelluleId, x.Period, x.TemplateId });
            e.HasOne<PoleEntity>()
                .WithMany()
                .HasForeignKey(x => x.RootPoleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<EmployeePrimeServiceFiche>(e =>
        {
            e.ToTable("prime_employee_prime_service_fiche");
            e.HasKey(x => x.Id);
            e.Property(x => x.SupervisorUserId).HasMaxLength(128);
            e.Property(x => x.EmployeeId).HasMaxLength(128);
            e.Property(x => x.ServiceId).HasMaxLength(128);
            e.Property(x => x.CelluleId).HasMaxLength(128);
            e.Property(x => x.Period).HasMaxLength(16);
            e.Property(x => x.FillingStatus).HasMaxLength(32);
            // ---- Phase 1.1 : workflow validation ----
            e.Property(x => x.ValidationStatus).HasMaxLength(64).IsRequired().HasDefaultValue("AwaitingData");
            e.Property(x => x.LastApproverUserId).HasMaxLength(128);
            e.Property(x => x.RejectedByUserId).HasMaxLength(128);
            e.Property(x => x.RejectionReason).HasMaxLength(2048);
            e.Property(x => x.PrimeAmount).HasPrecision(12, 2);
            e.Property(x => x.ChallengeAmount).HasPrecision(12, 2);
            e.Property(x => x.TotalAmount).HasPrecision(12, 2);
            e.Property(x => x.DetailGridPreviewSheetName).HasMaxLength(256);
            e.Property(x => x.TemplateVersionRef).HasMaxLength(256);
            e.Property(x => x.PonderationsSnapshotJson);
            e.HasIndex(x => new { x.ServiceId, x.Period });
            e.HasIndex(x => new { x.SupervisorUserId, x.Period });
            e.HasIndex(x => new { x.EmployeeId, x.Period }).IsUnique();
            e.HasIndex(x => x.ValidationStatus);
            e.HasOne(x => x.CellulePrimeDraft)
                .WithMany(x => x.EmployeeFiches)
                .HasForeignKey(x => x.CellulePrimeDraftId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EmployeePrimeFicheValidationHistory>(e =>
        {
            e.ToTable("prime_employee_fiche_validation_history");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(32).IsRequired();
            e.Property(x => x.FromStatus).HasMaxLength(64).IsRequired();
            e.Property(x => x.ToStatus).HasMaxLength(64).IsRequired();
            e.Property(x => x.ActorUserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.ActorRole).HasMaxLength(64).IsRequired();
            e.Property(x => x.ActorDisplayName).HasMaxLength(256);
            e.Property(x => x.Comment).HasMaxLength(2048);
            e.Property(x => x.PrimeAmount).HasPrecision(12, 2);
            e.Property(x => x.ChallengeAmount).HasPrecision(12, 2);
            e.Property(x => x.TotalAmount).HasPrecision(12, 2);
            e.HasIndex(x => new { x.FicheId, x.At });
            e.HasOne(x => x.Fiche)
                .WithMany(x => x.ValidationHistory)
                .HasForeignKey(x => x.FicheId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PrimeHistoricalFicheEntity>(e =>
        {
            e.ToTable("prime_historical_fiche");
            e.HasKey(x => x.Id);
            e.Property(x => x.Period).HasMaxLength(16).IsRequired();
            e.Property(x => x.CelluleId).HasMaxLength(128).IsRequired();
            e.Property(x => x.ServiceId).HasMaxLength(128);
            e.Property(x => x.RootPoleId).HasMaxLength(128).IsRequired();
            e.Property(x => x.SupervisorUserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.EmployeeExternalName).HasMaxLength(512).IsRequired();
            e.Property(x => x.EmployeeId).HasMaxLength(128);
            e.Property(x => x.DetailGridPreviewSheetName).HasMaxLength(256);
            e.Property(x => x.OriginFileName).HasMaxLength(512);
            e.Property(x => x.Source).HasMaxLength(32).IsRequired();
            e.Property(x => x.ImportedByUserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.PrimeAmount).HasPrecision(12, 2);
            e.Property(x => x.ChallengeAmount).HasPrecision(12, 2);
            e.Property(x => x.TotalAmount).HasPrecision(12, 2);
            e.HasIndex(x => new { x.SupervisorUserId, x.Period });
            e.HasIndex(x => new { x.CelluleId, x.Period });
        });

        // ---- Phase 1.3 : Administration ----
        modelBuilder.Entity<RbacPermission>(e =>
        {
            e.ToTable("prime_rbac_permission");
            e.HasKey(x => x.Id);
            e.Property(x => x.Role).HasMaxLength(64).IsRequired();
            e.Property(x => x.Action).HasMaxLength(32).IsRequired();
            e.Property(x => x.Scope).HasMaxLength(32).IsRequired();
            e.HasIndex(x => new { x.Role, x.Action, x.Scope }).IsUnique();
        });

        modelBuilder.Entity<WorkflowStepConfig>(e =>
        {
            e.ToTable("prime_workflow_step");
            e.HasKey(x => x.Id);
            e.Property(x => x.ApproverRole).HasMaxLength(64).IsRequired();
            e.Property(x => x.FromStatus).HasMaxLength(64).IsRequired();
            e.Property(x => x.ToStatus).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.SortOrder);
            e.HasIndex(x => new { x.FromStatus, x.ApproverRole, x.ToStatus }).IsUnique();
        });

        modelBuilder.Entity<GlobalPoolWorkflowStep>(e =>
        {
            e.ToTable("prime_global_pool_workflow_step");
            e.HasKey(x => x.Id);
            e.Property(x => x.ApproverRole).HasMaxLength(64).IsRequired();
            e.HasIndex(x => x.SortOrder);
        });

        modelBuilder.Entity<GlobalPoolScopeSynthesisEntity>(e =>
        {
            e.ToTable("prime_global_pool_scope_synthesis");
            e.HasKey(x => x.Id);
            e.Property(x => x.Period).HasMaxLength(16).IsRequired();
            e.Property(x => x.ScopeType).HasMaxLength(16).IsRequired();
            e.Property(x => x.ScopeId).HasMaxLength(128).IsRequired();
            e.Property(x => x.ScopeDisplayName).HasMaxLength(512);
            e.Property(x => x.FileName).HasMaxLength(512);
            e.Property(x => x.GeneratedByUserId).HasMaxLength(128);
            e.Property(x => x.ManagerApprovedByUserId).HasMaxLength(128);
            e.Property(x => x.RhApprovedByUserId).HasMaxLength(128);
            e.Property(x => x.ComptaAckByUserId).HasMaxLength(128);
            e.HasIndex(x => new { x.Period, x.ScopeType, x.ScopeId }).IsUnique();
            e.HasMany(x => x.Lines)
                .WithOne(x => x.ScopeSynthesis)
                .HasForeignKey(x => x.ScopeSynthesisId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.GlobalPoolApprovals)
                .WithOne(x => x.ScopeSynthesis)
                .HasForeignKey(x => x.ScopeSynthesisId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GlobalPoolSynthesisLineEntity>(e =>
        {
            e.ToTable("prime_global_pool_synthesis_line");
            e.HasKey(x => x.Id);
            e.Property(x => x.EmployeeId).HasMaxLength(128).IsRequired();
            e.Property(x => x.ServiceId).HasMaxLength(128).IsRequired();
            e.Property(x => x.LineStatus).HasMaxLength(32).IsRequired();
            e.Property(x => x.RhDecision).HasMaxLength(32).IsRequired();
            e.Property(x => x.RhDecidedByUserId).HasMaxLength(128);
            e.Property(x => x.RhRejectionReason).HasMaxLength(2048);
            e.Property(x => x.ManagerDecision).HasMaxLength(32).IsRequired();
            e.Property(x => x.ManagerDecidedByUserId).HasMaxLength(128);
            e.Property(x => x.ManagerRejectionReason).HasMaxLength(2048);
            e.Property(x => x.RejectedByUserId).HasMaxLength(128);
            e.Property(x => x.RejectedByRole).HasMaxLength(64);
            e.Property(x => x.RejectionReason).HasMaxLength(2048);
            e.Property(x => x.PaymentStatus).HasMaxLength(32).IsRequired();
            e.Property(x => x.PaidByUserId).HasMaxLength(128);
            e.Property(x => x.PaymentReference).HasMaxLength(256);
            e.Property(x => x.PrimeAmount).HasPrecision(12, 2);
            e.Property(x => x.ChallengeAmount).HasPrecision(12, 2);
            e.Property(x => x.TotalAmount).HasPrecision(12, 2);
            e.Property(x => x.SanctionAmount).HasPrecision(12, 2);
            e.Property(x => x.RegularizationAmount).HasPrecision(12, 2);
            e.Property(x => x.NetPayableAmount).HasPrecision(12, 2);
            e.Property(x => x.RegularizationUpdatedByUserId).HasMaxLength(128);
            e.HasIndex(x => new { x.ScopeSynthesisId, x.FicheId }).IsUnique();
        });

        modelBuilder.Entity<PrimeAbsenceSanctionConfigEntity>(e =>
        {
            e.ToTable("prime_absence_sanction_config");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.UpdatedByUserId).HasMaxLength(128);
        });

        modelBuilder.Entity<GlobalPoolSynthesisLineHistoryEntity>(e =>
        {
            e.ToTable("prime_global_pool_synthesis_line_history");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(32).IsRequired();
            e.Property(x => x.ActorUserId).HasMaxLength(128).IsRequired();
            e.Property(x => x.ActorRole).HasMaxLength(64).IsRequired();
            e.Property(x => x.Comment).HasMaxLength(2048);
            e.HasIndex(x => new { x.LineId, x.At });
            e.HasOne(x => x.Line)
                .WithMany(x => x.History)
                .HasForeignKey(x => x.LineId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GlobalPoolApprovalEntity>(e =>
        {
            e.ToTable("prime_global_pool_approval");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasMaxLength(128).IsRequired();
            e.HasIndex(x => new { x.DraftId, x.StepId })
                .IsUnique()
                .HasFilter("\"DraftId\" IS NOT NULL");
            e.HasIndex(x => new { x.ScopeSynthesisId, x.StepId })
                .IsUnique()
                .HasFilter("\"ScopeSynthesisId\" IS NOT NULL");
            e.HasOne(x => x.Step)
                .WithMany()
                .HasForeignKey(x => x.StepId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Draft)
                .WithMany()
                .HasForeignKey(x => x.DraftId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowGlobalConfig>(e =>
        {
            e.ToTable("prime_workflow_global_config");
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("prime_audit_log");
            e.HasKey(x => x.Id);
            e.Property(x => x.UserId).HasMaxLength(128);
            e.Property(x => x.UserDisplayName).HasMaxLength(256);
            e.Property(x => x.Role).HasMaxLength(64);
            e.Property(x => x.Action).HasMaxLength(64);
            e.Property(x => x.EntityType).HasMaxLength(128);
            e.Property(x => x.EntityId).HasMaxLength(128);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasIndex(x => x.At);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.UserId);
        });

        modelBuilder.Entity<Anomaly>(e =>
        {
            e.ToTable("prime_anomaly");
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(64).IsRequired();
            e.Property(x => x.Severity).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Description).HasMaxLength(2048);
            e.Property(x => x.TargetEntityType).HasMaxLength(128);
            e.Property(x => x.TargetEntityId).HasMaxLength(128);
            e.Property(x => x.Period).HasMaxLength(16);
            e.Property(x => x.ServiceId).HasMaxLength(128);
            e.Property(x => x.CelluleId).HasMaxLength(128);
            e.Property(x => x.PoleId).HasMaxLength(128);
            e.Property(x => x.ResolvedByUserId).HasMaxLength(128);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Type);
            e.HasIndex(x => new { x.TargetEntityType, x.TargetEntityId });
            e.HasIndex(x => new { x.Period, x.ServiceId });
        });

        modelBuilder.Entity<BusinessDepartmentEntity>(e =>
        {
            e.ToTable("prime_business_department");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(64);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.ManagerEmployeeId).HasMaxLength(128);
        });

        modelBuilder.Entity<BusinessDepartmentPoleEntity>(e =>
        {
            e.ToTable("prime_business_department_pole");
            e.HasKey(x => x.Id);
            e.Property(x => x.BusinessDepartmentId).HasMaxLength(64);
            e.Property(x => x.PoleId).HasMaxLength(64);
            e.HasOne(x => x.BusinessDepartment).WithMany(x => x.PoleAssignments).HasForeignKey(x => x.BusinessDepartmentId);
        });

        modelBuilder.Entity<AllowanceTypeEntity>(e =>
        {
            e.ToTable("prime_allowance_type");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(64);
            e.Property(x => x.Label).HasMaxLength(256);
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.CalculationMode).HasMaxLength(32);
            e.Property(x => x.ApplicableDepartmentKinds).HasMaxLength(64);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<AllowanceTypeDepartmentEntity>(e =>
        {
            e.ToTable("prime_allowance_type_department");
            e.HasKey(x => x.Id);
            e.Property(x => x.BusinessDepartmentId).HasMaxLength(64);
            e.HasOne(x => x.AllowanceType).WithMany(x => x.DepartmentLinks).HasForeignKey(x => x.AllowanceTypeId);
        });

        modelBuilder.Entity<AllowanceRequestEntity>(e =>
        {
            e.ToTable("prime_allowance_request");
            e.HasKey(x => x.Id);
            e.Property(x => x.EmployeeId).HasMaxLength(128);
            e.Property(x => x.BusinessDepartmentId).HasMaxLength(64);
            e.Property(x => x.Period).HasMaxLength(16);
            e.Property(x => x.Currency).HasMaxLength(8);
            e.Property(x => x.Reason).HasMaxLength(2048);
            e.Property(x => x.Source).HasMaxLength(32);
            e.Property(x => x.Status).HasMaxLength(32);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => new { x.Period, x.BusinessDepartmentId });
            e.HasIndex(x => new { x.EmployeeId, x.Period });
            e.HasOne(x => x.AllowanceType).WithMany().HasForeignKey(x => x.AllowanceTypeId);
        });

        modelBuilder.Entity<AllowanceRequestHistoryEntity>(e =>
        {
            e.ToTable("prime_allowance_request_history");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(32);
            e.Property(x => x.FromStatus).HasMaxLength(32);
            e.Property(x => x.ToStatus).HasMaxLength(32);
            e.Property(x => x.ActorUserId).HasMaxLength(128);
            e.Property(x => x.ActorRole).HasMaxLength(64);
            e.HasOne(x => x.AllowanceRequest).WithMany(x => x.History).HasForeignKey(x => x.AllowanceRequestId);
        });

        modelBuilder.Entity<AllowanceWorkflowStepEntity>(e =>
        {
            e.ToTable("prime_allowance_workflow_step");
            e.HasKey(x => x.Id);
            e.Property(x => x.ApproverRole).HasMaxLength(64);
        });

        modelBuilder.Entity<AllowanceRuleEntity>(e =>
        {
            e.ToTable("prime_allowance_rule");
            e.HasKey(x => x.Id);
            e.Property(x => x.BusinessDepartmentId).HasMaxLength(64);
            e.Property(x => x.DataSource).HasMaxLength(64);
            e.HasOne(x => x.AllowanceType).WithMany().HasForeignKey(x => x.AllowanceTypeId);
        });

        modelBuilder.Entity<AllowanceNoBonusMarkerEntity>(e =>
        {
            e.ToTable("prime_allowance_no_bonus_marker");
            e.HasKey(x => x.Id);
            e.Property(x => x.EmployeeId).HasMaxLength(128);
            e.Property(x => x.BusinessDepartmentId).HasMaxLength(64);
            e.Property(x => x.Period).HasMaxLength(16);
            e.Property(x => x.MarkedByUserId).HasMaxLength(128);
            e.Property(x => x.Comment).HasMaxLength(512);
            e.HasIndex(x => new { x.EmployeeId, x.Period }).IsUnique();
        });

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.MessageType).HasMaxLength(512);
            e.HasIndex(x => x.ProcessedAt);
        });
    }
}
