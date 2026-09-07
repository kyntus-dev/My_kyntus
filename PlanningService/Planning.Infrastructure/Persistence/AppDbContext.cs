using Kyntus.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Persistence;
using Planning.Domain.Entities;

namespace Planning.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // ── DbSets existants ──
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<Floor> Floors { get; set; } = null!;
    public DbSet<Service> Services { get; set; } = null!;
    public DbSet<Shift> Shifts { get; set; } = null!;
    public DbSet<ShiftAssignment> ShiftAssignments { get; set; } = null!;
    public DbSet<SubService> SubServices { get; set; } = null!;
    public DbSet<WeeklyPlanning> WeeklyPlannings { get; set; } = null!;
    public DbSet<WeeklyShiftConfig> WeeklyShiftConfigs { get; set; } = null!;
    public DbSet<Declaration> Declarations { get; set; } = null!;
    public DbSet<UserSubService> UserSubServices { get; set; } = null!;
    public DbSet<SaturdayGroup> SaturdayGroups { get; set; } = null!;
    public DbSet<Contract> Contracts { get; set; } = null!;
    public DbSet<ContractNotification> ContractNotifications { get; set; } = null!;
    public DbSet<PlanningNotification> PlanningNotifications { get; set; } = null!;
    public DbSet<Conge> Conges { get; set; } = null!;
    public DbSet<PlanningComment> PlanningComments { get; set; } = null!;
    public DbSet<SaturdayHistory> SaturdayHistories => Set<SaturdayHistory>();
    public DbSet<SubServiceShiftConfig> SubServiceShiftConfigs { get; set; } = null!;
    public DbSet<ShiftModeProfile> ShiftModeProfiles { get; set; } = null!;
    public DbSet<WeeklyCellShiftModePlan> WeeklyCellShiftModePlans { get; set; } = null!;
    public DbSet<WeeklyEmployeeShiftMode> WeeklyEmployeeShiftModes { get; set; } = null!;
    public DbSet<PlanningConsultation> PlanningConsultations { get; set; } = null!;
    public DbSet<PlanningAutoGenerateSettings> PlanningAutoGenerateSettings { get; set; } = null!;
    public DbSet<PlanningChangeRequest> PlanningChangeRequests { get; set; } = null!;
    public DbSet<PlanningExceptionalRequest> PlanningExceptionalRequests { get; set; } = null!;
    public DbSet<PlanningReinforcementRequest> PlanningReinforcementRequests { get; set; } = null!;
    public DbSet<PlanningReinforcementVolunteer> PlanningReinforcementVolunteers { get; set; } = null!;
    public DbSet<Reclamation> Reclamations { get; set; } = null!;
    public DbSet<ReclamationHistorique> ReclamationHistoriques { get; set; } = null!;
    public DbSet<Proposition> Propositions { get; set; } = null!;
    public DbSet<PropositionHistorique> PropositionHistoriques { get; set; } = null!;
    // ✅ NOUVEAU — Newsletter
    public DbSet<Newsletter> Newsletters { get; set; } = null!;
    public DbSet<NewsletterCampaign> NewsletterCampaigns { get; set; } = null!;
  
    public DbSet<CampaignAnalytics> CampaignAnalytics { get; set; } = null!;
    public DbSet<MediaAsset> MediaAssets { get; set; } = null!;
    public DbSet<TicketComment> TicketComments { get; set; } = null!;

    // 🆕 Ajouter le DbSet
    public DbSet<UserManagedService> UserManagedServices { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages { get; set; } = null!;

    public DbSet<EmployeeImportFieldConfig> EmployeeImportFieldConfigs { get; set; } = null!;
    public DbSet<UserCustomFieldValue> UserCustomFieldValues { get; set; } = null!;
    public DbSet<EmployeeImportJob> EmployeeImportJobs { get; set; } = null!;
    public DbSet<EmployeeImportJobLine> EmployeeImportJobLines { get; set; } = null!;
    public DbSet<EmployeeImportSession> EmployeeImportSessions { get; set; } = null!;
    public DbSet<UserHrProfile> UserHrProfiles { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── User ──
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();
        modelBuilder.Entity<User>()
            .Property(u => u.HtelCode)
            .HasMaxLength(128);
        modelBuilder.Entity<User>()
            .Property(u => u.HtelTechnicienName)
            .HasMaxLength(256);
        modelBuilder.Entity<User>()
            .HasIndex(u => u.IdTechnicien)
            .IsUnique()
            .HasFilter("\"IdTechnicien\" IS NOT NULL");

        // ── Role ──
        modelBuilder.Entity<Role>()
            .HasIndex(r => r.Name)
            .IsUnique();

        modelBuilder.Entity<Floor>()
            .HasIndex(f => f.PrimePoleId)
            .IsUnique()
            .HasFilter("\"PrimePoleId\" IS NOT NULL");

        modelBuilder.Entity<Service>()
            .HasIndex(s => s.PrimeCelluleId)
            .IsUnique()
            .HasFilter("\"PrimeCelluleId\" IS NOT NULL");

        modelBuilder.Entity<SubService>()
            .HasIndex(s => s.PrimeServiceId)
            .IsUnique()
            .HasFilter("\"PrimeServiceId\" IS NOT NULL");

        // ── Service ──
        modelBuilder.Entity<Service>()
            .HasIndex(s => s.Code)
            .IsUnique();

        // ── SubService ──
        modelBuilder.Entity<SubService>()
            .HasIndex(s => s.Code)
            .IsUnique();

        // ── UserSubService ──
        modelBuilder.Entity<UserSubService>()
            .HasKey(us => new { us.UserId, us.SubServiceId });

        modelBuilder.Entity<UserSubService>()
            .HasOne(us => us.User)
            .WithMany(u => u.ManagedSubServices)
            .HasForeignKey(us => us.UserId);

        modelBuilder.Entity<UserSubService>()
            .HasOne(us => us.SubService)
            .WithMany(s => s.Managers)
            .HasForeignKey(us => us.SubServiceId);

        // ── UserManagedService ──
        modelBuilder.Entity<UserManagedService>()
            .HasKey(us => new { us.UserId, us.ServiceId });

        modelBuilder.Entity<UserManagedService>()
            .HasOne(us => us.User)
            .WithMany(u => u.ManagedServices)
            .HasForeignKey(us => us.UserId);

        modelBuilder.Entity<UserManagedService>()
            .HasOne(us => us.Service)
            .WithMany(s => s.ManagedByUsers)
            .HasForeignKey(us => us.ServiceId);
        // ── ShiftAssignment ──
        modelBuilder.Entity<ShiftAssignment>()
            .HasIndex(sa => new { sa.UserId, sa.AssignedDate })
            .IsUnique();

        modelBuilder.Entity<ShiftAssignment>()
            .HasOne(a => a.User)
            .WithMany(u => u.ShiftAssignments)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ShiftAssignment>()
            .HasOne(a => a.WeeklyPlanning)
            .WithMany(p => p.ShiftAssignments)
            .HasForeignKey(a => a.WeeklyPlanningId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ShiftAssignment>()
            .HasOne(a => a.SubServiceShiftConfig)
            .WithMany()
            .HasForeignKey(a => a.SubServiceShiftConfigId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<ShiftAssignment>()
            .HasOne(a => a.ShiftModeProfile)
            .WithMany()
            .HasForeignKey(a => a.ShiftModeProfileId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // ── ShiftModeProfile ──
        modelBuilder.Entity<ShiftModeProfile>(entity =>
        {
            entity.Property(e => e.Title).IsRequired().HasMaxLength(100);
            entity.HasIndex(e => new { e.SubServiceId, e.Title });
            entity.HasOne(e => e.SubService)
                .WithMany(s => s.ShiftModeProfiles)
                .HasForeignKey(e => e.SubServiceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WeeklyCellShiftModePlan>(entity =>
        {
            entity.Property(e => e.WeekCode).IsRequired().HasMaxLength(16);
            entity.HasIndex(e => new { e.SubServiceId, e.WeekCode }).IsUnique();
            entity.HasOne(e => e.SubService)
                .WithMany()
                .HasForeignKey(e => e.SubServiceId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ValidatedBy)
                .WithMany()
                .HasForeignKey(e => e.ValidatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<WeeklyEmployeeShiftMode>(entity =>
        {
            entity.HasIndex(e => new { e.WeeklyCellShiftModePlanId, e.UserId }).IsUnique();
            entity.HasOne(e => e.Plan)
                .WithMany(p => p.EmployeeModes)
                .HasForeignKey(e => e.WeeklyCellShiftModePlanId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.ShiftModeProfile)
                .WithMany()
                .HasForeignKey(e => e.ShiftModeProfileId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── WeeklyPlanning ──
        modelBuilder.Entity<WeeklyPlanning>()
            .HasOne(p => p.Validator)
            .WithMany()
            .HasForeignKey(p => p.ValidatedBy)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<WeeklyPlanning>()
            .HasIndex(p => new { p.WeekCode, p.SubServiceId })
            .IsUnique();

        // ── Declaration ──
        modelBuilder.Entity<Declaration>()
            .HasOne(d => d.User)
            .WithMany(u => u.Declarations)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Declaration>()
            .HasOne(d => d.Resolver)
            .WithMany()
            .HasForeignKey(d => d.ResolverId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Reclamation>().HasMany(r => r.Historique)
        .WithOne(h => h.Reclamation)
        .HasForeignKey(h => h.ReclamationId)
        .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Proposition>().HasMany(p => p.Historique)
            .WithOne(h => h.Proposition)
            .HasForeignKey(h => h.PropositionId)
            .OnDelete(DeleteBehavior.Cascade);

        // ── Conge ──
        modelBuilder.Entity<Conge>(entity =>
        {
            entity.HasOne(c => c.User)
                  .WithMany()
                  .HasForeignKey(c => c.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(c => new { c.UserId, c.StartDate, c.EndDate });
            entity.HasIndex(c => c.SourceDemandeId);
        });

        // ── SubServiceShiftConfig ──
        modelBuilder.Entity<SubServiceShiftConfig>(entity =>
        {
            entity.Property(e => e.Label).IsRequired().HasMaxLength(50);
            entity.Property(e => e.WeekCode).HasMaxLength(10);
            entity.Property(e => e.BreakSlotsJson).HasMaxLength(128);
            entity.Property(e => e.Percentage).HasPrecision(5, 2);
            entity.Property(e => e.ShiftKind).HasConversion<int>();
            entity.Ignore(e => e.EndTime);
            entity.HasOne(e => e.SubService)
                  .WithMany()
                  .HasForeignKey(e => e.SubServiceId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ShiftModeProfile)
                  .WithMany(p => p.ShiftConfigs)
                  .HasForeignKey(e => e.ShiftModeProfileId)
                  .OnDelete(DeleteBehavior.SetNull)
                  .IsRequired(false);

            // Template: unique (SubServiceId, Mode, Saturday, Label) when IsTemplate
            entity.HasIndex(e => new { e.SubServiceId, e.ShiftModeProfileId, e.AppliesToSaturday, e.Label })
                .IsUnique()
                .HasFilter("\"IsTemplate\" = TRUE");

            // Snapshot: unique (SubServiceId, WeekCode, Mode, Saturday, Label) when not template
            entity.HasIndex(e => new { e.SubServiceId, e.WeekCode, e.ShiftModeProfileId, e.AppliesToSaturday, e.Label })
                .IsUnique()
                .HasFilter("\"IsTemplate\" = FALSE AND \"WeekCode\" IS NOT NULL");
        });

        modelBuilder.Entity<PlanningConsultation>(entity =>
        {
            entity.HasIndex(e => new { e.PlanningId, e.UserId }).IsUnique();
            entity.HasOne(e => e.Planning)
                .WithMany()
                .HasForeignKey(e => e.PlanningId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PlanningAutoGenerateSettings>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasMaxLength(32);
            entity.Property(e => e.TimeZone).HasMaxLength(64);
            entity.Property(e => e.Target).HasMaxLength(32);
            entity.Property(e => e.LastRunWeekCode).HasMaxLength(16);
            entity.Property(e => e.LastValidationReminderWeekCode).HasMaxLength(16);
            entity.Property(e => e.SenderFromEmail).HasMaxLength(256);
            entity.Property(e => e.SenderDisplayName).HasMaxLength(128);
            entity.Property(e => e.SmtpHost).HasMaxLength(256);
            entity.Property(e => e.SmtpUser).HasMaxLength(256);
            entity.Property(e => e.SmtpPassword).HasMaxLength(512);
            entity.Property(e => e.MailSendMode).HasMaxLength(16);
            entity.Property(e => e.MailApiProvider).HasMaxLength(32);
            entity.Property(e => e.MailApiKey).HasMaxLength(512);
        });

        modelBuilder.Entity<PlanningChangeRequest>(entity =>
        {
            entity.Property(e => e.WeekCode).IsRequired().HasMaxLength(16);
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.RejectionReason).HasMaxLength(1000);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.HasIndex(e => new { e.RequesterUserId, e.WeekCode });
            entity.HasIndex(e => new { e.Status, e.WeekCode });
            entity.HasOne(e => e.Requester)
                .WithMany()
                .HasForeignKey(e => e.RequesterUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CurrentAssignment)
                .WithMany()
                .HasForeignKey(e => e.CurrentAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.ProposedSwapUser)
                .WithMany()
                .HasForeignKey(e => e.ProposedSwapUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.ProcessedBy)
                .WithMany()
                .HasForeignKey(e => e.ProcessedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.SupervisorProcessedBy)
                .WithMany()
                .HasForeignKey(e => e.SupervisorProcessedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PlanningExceptionalRequest>(entity =>
        {
            entity.Property(e => e.WeekCode).IsRequired().HasMaxLength(16);
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.RejectionReason).HasMaxLength(1000);
            entity.Property(e => e.JustificationFileName).HasMaxLength(260);
            entity.Property(e => e.JustificationContentType).HasMaxLength(128);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.HasIndex(e => new { e.WeekCode, e.SubServiceId, e.Status });
            entity.HasIndex(e => new { e.RequesterUserId, e.CreatedAt });
            entity.HasIndex(e => new { e.RequesterUserId, e.RequestedDate })
                .IsUnique()
                .HasFilter("\"Status\" IN (0, 1, 2)");
            entity.HasOne(e => e.Requester)
                .WithMany()
                .HasForeignKey(e => e.RequesterUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SubService)
                .WithMany()
                .HasForeignKey(e => e.SubServiceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.RequestedShiftTemplate)
                .WithMany()
                .HasForeignKey(e => e.RequestedShiftTemplateId)
                .OnDelete(DeleteBehavior.SetNull)
                .IsRequired(false);
            entity.HasOne(e => e.SupervisorProcessedBy)
                .WithMany()
                .HasForeignKey(e => e.SupervisorProcessedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.RhProcessedBy)
                .WithMany()
                .HasForeignKey(e => e.RhProcessedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.ProcessedBy)
                .WithMany()
                .HasForeignKey(e => e.ProcessedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PlanningReinforcementRequest>(entity =>
        {
            entity.Property(e => e.WeekCode).IsRequired().HasMaxLength(16);
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(1000);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.HasIndex(e => new { e.SubServiceId, e.SaturdayDate, e.Status });
            entity.HasIndex(e => new { e.WeekCode, e.SubServiceId });
            entity.HasOne(e => e.SubService)
                .WithMany()
                .HasForeignKey(e => e.SubServiceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.CreatedBy)
                .WithMany()
                .HasForeignKey(e => e.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.WeeklyPlanning)
                .WithMany()
                .HasForeignKey(e => e.WeeklyPlanningId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<PlanningReinforcementVolunteer>(entity =>
        {
            entity.Property(e => e.Status).HasConversion<int>();
            entity.HasIndex(e => new { e.RequestId, e.UserId }).IsUnique();
            entity.HasOne(e => e.Request)
                .WithMany(r => r.Volunteers)
                .HasForeignKey(e => e.RequestId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.SelectedShiftConfig)
                .WithMany()
                .HasForeignKey(e => e.SelectedShiftConfigId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── PlanningComment ──
        modelBuilder.Entity<PlanningComment>(entity =>
        {
            entity.HasIndex(e => new { e.WeeklyPlanningId, e.UserId }).IsUnique();
            entity.Property(e => e.Comment).IsRequired().HasMaxLength(500);
            entity.HasOne(e => e.WeeklyPlanning)
                  .WithMany()
                  .HasForeignKey(e => e.WeeklyPlanningId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User)
                  .WithMany()
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        // ✅ NOUVEAU — Newsletter ──────────────────────────────────────────────



        // ✅ CONFIGURATION CORRIGÉE — Newsletter
        modelBuilder.Entity<NewsletterCampaign>()
            .HasOne(c => c.Newsletter)
            .WithMany(n => n.Campaigns)
            .HasForeignKey(c => c.NewsletterId)
            .OnDelete(DeleteBehavior.Restrict);

        // ✅ À UTILISER
        modelBuilder.Entity<CampaignAnalytics>()
            .HasOne(a => a.Campaign)
            .WithMany(c => c.Analytics)
            .HasForeignKey(a => a.CampaignId);

        // On indique juste que UserId est requis, sans créer de relation complexe 
        // si ApplicationUser n'est pas géré par ce DbContext précis.
        modelBuilder.Entity<CampaignAnalytics>()
            .Property(a => a.UserId)
            .IsRequired();

        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.MessageType).HasMaxLength(512);
            e.HasIndex(x => x.ProcessedAt);
        });

        // Table créée via PlanningSchemaPatches (DDL idempotent), pas via migration.
        modelBuilder.Entity<PlanningNotification>(e =>
        {
            e.ToTable("PlanningNotifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.WeekCode).HasMaxLength(64);
            e.Property(x => x.SubServiceName).HasMaxLength(200);
            e.HasIndex(x => x.AuthUserId);
        });

        modelBuilder.Entity<EmployeeImportFieldConfig>(e =>
        {
            e.HasIndex(x => x.FieldKey).IsUnique();
            e.Property(x => x.AliasesJson).HasColumnType("jsonb");
            e.Property(x => x.DataType).HasMaxLength(32).HasDefaultValue("text");
        });

        modelBuilder.Entity<UserCustomFieldValue>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.FieldKey }).IsUnique();
            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.FieldKey).HasMaxLength(128);
        });

        modelBuilder.Entity<EmployeeImportJob>(e =>
        {
            e.HasMany(j => j.Lines)
                .WithOne(l => l.Job)
                .HasForeignKey(l => l.JobId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(j => j.FileContent).HasColumnType("bytea");
            e.Property(j => j.ContentType).HasMaxLength(128);
            e.Property(j => j.Status).HasMaxLength(32);
            e.Property(j => j.ErrorMessage).HasMaxLength(2000);
        });

        modelBuilder.Entity<EmployeeImportJobLine>(e =>
        {
            e.HasIndex(l => new { l.JobId, l.LineNumber });
        });

        modelBuilder.Entity<EmployeeImportSession>(e =>
        {
            e.HasIndex(s => s.ExpiresAt);
            e.Property(s => s.FileContent).HasColumnType("bytea");
            e.Property(s => s.ContentType).HasMaxLength(128);
        });

        modelBuilder.Entity<UserHrProfile>(e =>
        {
            e.ToTable("user_hr_profiles");
            e.HasKey(x => x.UserId);
            e.HasOne(x => x.User)
                .WithOne()
                .HasForeignKey<UserHrProfile>(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaAsset>(e =>
        {
            e.ToTable("MediaAssets");
            e.HasKey(x => x.Id);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.ContentType).HasMaxLength(128);
            e.Property(x => x.StoragePath).HasMaxLength(512);
            e.Property(x => x.UploadedByUserId).HasMaxLength(128);
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
        });

        modelBuilder.Entity<TicketComment>(e =>
        {
            e.ToTable("TicketComments");
            e.HasKey(x => x.Id);
            e.Property(x => x.AuthorId).HasMaxLength(128);
            e.Property(x => x.AuthorNom).HasMaxLength(200);
            e.Property(x => x.Text).HasMaxLength(4000);
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
        });
    }
}