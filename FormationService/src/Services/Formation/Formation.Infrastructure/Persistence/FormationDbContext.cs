using Formation.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Formation.Infrastructure.Persistence;

public class FormationDbContext : DbContext
{
    public FormationDbContext(DbContextOptions<FormationDbContext> options) : base(options) { }

    public DbSet<FormationEntity> Formations => Set<FormationEntity>();
    public DbSet<Inscription> Inscriptions => Set<Inscription>();
    public DbSet<Certification> Certifications => Set<Certification>();
    public DbSet<EmployeAnnuaire> EmployeAnnuaires => Set<EmployeAnnuaire>();
    public DbSet<TrainingProgram> TrainingPrograms => Set<TrainingProgram>();
    public DbSet<TrainingSession> TrainingSessions => Set<TrainingSession>();
    public DbSet<TrainingAssignment> TrainingAssignments => Set<TrainingAssignment>();
    public DbSet<InitialTrainingPath> InitialTrainingPaths => Set<InitialTrainingPath>();
    public DbSet<InitialTrainingQuizResult> InitialTrainingQuizResults => Set<InitialTrainingQuizResult>();
    public DbSet<FormationDocumentDefinition> FormationDocumentDefinitions => Set<FormationDocumentDefinition>();
    public DbSet<FormationDocumentChecklistItem> FormationDocumentChecklistItems => Set<FormationDocumentChecklistItem>();
    public DbSet<TrainingSessionReport> TrainingSessionReports => Set<TrainingSessionReport>();
    public DbSet<TrainingQuiz> TrainingQuizzes => Set<TrainingQuiz>();
    public DbSet<TrainingQuizQuestion> TrainingQuizQuestions => Set<TrainingQuizQuestion>();
    public DbSet<TrainingQuizAttempt> TrainingQuizAttempts => Set<TrainingQuizAttempt>();
    public DbSet<TrainingQuizTemplate> TrainingQuizTemplates => Set<TrainingQuizTemplate>();
    public DbSet<TrainingQuizTemplateQuestion> TrainingQuizTemplateQuestions => Set<TrainingQuizTemplateQuestion>();
    public DbSet<TrainingCatalogItem> TrainingCatalogItems => Set<TrainingCatalogItem>();
    public DbSet<TrainingModule> TrainingModules => Set<TrainingModule>();
    public DbSet<TrainingLesson> TrainingLessons => Set<TrainingLesson>();
    public DbSet<TrainingResource> TrainingResources => Set<TrainingResource>();
    public DbSet<TrainingCatalogAudienceRule> TrainingCatalogAudienceRules => Set<TrainingCatalogAudienceRule>();
    public DbSet<TrainingCatalogEnrollment> TrainingCatalogEnrollments => Set<TrainingCatalogEnrollment>();
    public DbSet<TrainingLessonProgress> TrainingLessonProgresses => Set<TrainingLessonProgress>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FormationEntity>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Titre).IsRequired().HasMaxLength(200);
            e.Property(x => x.Prix).HasColumnType("decimal(18,2)");
            e.HasMany(x => x.Inscriptions).WithOne().HasForeignKey(i => i.FormationId);
        });

        modelBuilder.Entity<Inscription>(e =>
        {
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<Certification>(e =>
        {
            e.HasKey(x => x.Id);
        });

        modelBuilder.Entity<EmployeAnnuaire>(e =>
        {
            e.ToTable("employe_annuaires");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EmployeId).IsUnique();
            e.HasIndex(x => x.Email);
            e.Property(x => x.Nom).HasMaxLength(200);
            e.Property(x => x.Prenom).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Role).HasMaxLength(100);
            e.Property(x => x.StructureKey).HasMaxLength(200);
            e.Property(x => x.DepartmentId).HasMaxLength(200);
            e.Property(x => x.PoleId).HasMaxLength(200);
            e.Property(x => x.CelluleId).HasMaxLength(200);
            e.Property(x => x.ServiceId).HasMaxLength(200);
            e.Property(x => x.DepartmentName).HasMaxLength(200);
            e.Property(x => x.PoleName).HasMaxLength(200);
            e.Property(x => x.CelluleName).HasMaxLength(200);
            e.Property(x => x.ServiceName).HasMaxLength(200);
            e.HasIndex(x => x.PoleId);
            e.HasIndex(x => x.CelluleId);
            e.HasIndex(x => x.ServiceId);
        });

        modelBuilder.Entity<TrainingProgram>(e =>
        {
            e.ToTable("training_programs");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.HasMany(x => x.Sessions).WithOne(x => x.Program).HasForeignKey(x => x.ProgramId);
        });

        modelBuilder.Entity<TrainingSession>(e =>
        {
            e.ToTable("training_sessions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.HasIndex(x => x.ProgramId);
            e.HasIndex(x => x.CatalogItemId);
            e.HasMany(x => x.Assignments).WithOne(x => x.Session).HasForeignKey(x => x.SessionId);
            e.HasOne(x => x.Report).WithOne(x => x.Session).HasForeignKey<TrainingSessionReport>(x => x.SessionId);
            e.HasOne(x => x.Quiz).WithOne(x => x.Session).HasForeignKey<TrainingQuiz>(x => x.SessionId)
                .IsRequired(false);
            e.HasOne(x => x.CatalogItem).WithMany().HasForeignKey(x => x.CatalogItemId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TrainingCatalogItem>(e =>
        {
            e.ToTable("training_catalog_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Category).HasMaxLength(200);
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.Status);
            e.HasMany(x => x.Modules).WithOne(x => x.CatalogItem).HasForeignKey(x => x.CatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.AudienceRules).WithOne(x => x.CatalogItem).HasForeignKey(x => x.CatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);
            // Circularité CatalogItem ↔ Template : SetNull des deux côtés, sans cascade.
            e.HasOne(x => x.DefaultQuizTemplate)
                .WithMany()
                .HasForeignKey(x => x.DefaultQuizTemplateId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TrainingModule>(e =>
        {
            e.ToTable("training_modules");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.HasIndex(x => x.CatalogItemId);
            e.HasMany(x => x.Lessons).WithOne(x => x.Module).HasForeignKey(x => x.ModuleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrainingLesson>(e =>
        {
            e.ToTable("training_lessons");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.HasIndex(x => x.ModuleId);
            e.HasMany(x => x.Resources).WithOne(x => x.Lesson).HasForeignKey(x => x.LessonId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrainingResource>(e =>
        {
            e.ToTable("training_resources");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Url).HasMaxLength(2000);
            e.Property(x => x.StoragePath).HasMaxLength(1000);
            e.Property(x => x.ContentType).HasMaxLength(200);
            e.Property(x => x.FileName).HasMaxLength(500);
            e.HasIndex(x => x.LessonId);
        });

        modelBuilder.Entity<TrainingCatalogAudienceRule>(e =>
        {
            e.ToTable("training_catalog_audience_rules");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.CatalogItemId);
        });

        modelBuilder.Entity<TrainingCatalogEnrollment>(e =>
        {
            e.ToTable("training_catalog_enrollments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CatalogItemId, x.EmployeeId }).IsUnique();
            e.HasIndex(x => x.EmployeeId);
            e.HasIndex(x => x.DueAt);
            e.HasOne(x => x.CatalogItem).WithMany().HasForeignKey(x => x.CatalogItemId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrainingLessonProgress>(e =>
        {
            e.ToTable("training_lesson_progress");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.EnrollmentId, x.LessonId }).IsUnique();
            e.Property(x => x.ProgressPercent).HasColumnType("numeric(18,2)");
            e.HasOne(x => x.Enrollment).WithMany(x => x.LessonProgresses).HasForeignKey(x => x.EnrollmentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Lesson).WithMany().HasForeignKey(x => x.LessonId)
                .OnDelete(DeleteBehavior.Cascade);
        });


        modelBuilder.Entity<TrainingAssignment>(e =>
        {
            e.ToTable("training_assignments");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.SessionId, x.EmployeeId }).IsUnique();
        });

        modelBuilder.Entity<InitialTrainingPath>(e =>
        {
            e.ToTable("initial_training_paths");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.EmployeeId);
            e.Property(x => x.OrgDepartmentId).HasMaxLength(200);
            e.Property(x => x.OrgPoleId).HasMaxLength(200);
            e.Property(x => x.OrgCelluleId).HasMaxLength(200);
            e.Property(x => x.OrgServiceId).HasMaxLength(200);
            e.Property(x => x.OrgDepartmentName).HasMaxLength(200);
            e.Property(x => x.OrgPoleName).HasMaxLength(200);
            e.Property(x => x.OrgCelluleName).HasMaxLength(200);
            e.Property(x => x.OrgServiceName).HasMaxLength(200);
            e.HasMany(x => x.QuizResults)
                .WithOne(x => x.Path)
                .HasForeignKey(x => x.InitialTrainingPathId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<InitialTrainingQuizResult>(e =>
        {
            e.ToTable("initial_training_quiz_results");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.InitialTrainingPathId);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Score).HasColumnType("numeric(18,2)");
            e.Property(x => x.RecordedBy).HasMaxLength(200);
        });

        modelBuilder.Entity<FormationDocumentDefinition>(e =>
        {
            e.ToTable("formation_document_definitions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.HasIndex(x => x.SortOrder);
        });

        modelBuilder.Entity<FormationDocumentChecklistItem>(e =>
        {
            e.ToTable("formation_document_checklist_items");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.InitialTrainingPathId);
            e.HasIndex(x => x.EmployeeId);
            e.HasIndex(x => new { x.InitialTrainingPathId, x.DefinitionId }).IsUnique();
            e.Property(x => x.ReceivedBy).HasMaxLength(200);
            e.HasOne(x => x.Definition)
                .WithMany()
                .HasForeignKey(x => x.DefinitionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Path)
                .WithMany(x => x.DocumentChecklistItems)
                .HasForeignKey(x => x.InitialTrainingPathId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TrainingSessionReport>(e =>
        {
            e.ToTable("training_session_reports");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SessionId).IsUnique();
            e.Property(x => x.FileName).HasMaxLength(500).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(200).IsRequired();
            e.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();
        });

        modelBuilder.Entity<TrainingQuiz>(e =>
        {
            e.ToTable("training_quizzes");
            e.HasKey(x => x.Id);
            // Index unique portable ; remplacé en SQL par un index partiel (SessionId IS NOT NULL).
            e.HasIndex(x => x.SessionId).IsUnique();
            e.HasIndex(x => x.CatalogItemId);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.PassThreshold).HasColumnType("numeric(18,2)");
            e.HasMany(x => x.Questions).WithOne(x => x.Quiz).HasForeignKey(x => x.QuizId);
            e.HasMany(x => x.Attempts).WithOne(x => x.Quiz).HasForeignKey(x => x.QuizId);
            e.HasOne(x => x.Template).WithMany().HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CatalogItem).WithMany().HasForeignKey(x => x.CatalogItemId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TrainingQuizQuestion>(e =>
        {
            e.ToTable("training_quiz_questions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Prompt).IsRequired();
            e.Property(x => x.Points).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<TrainingQuizAttempt>(e =>
        {
            e.ToTable("training_quiz_attempts");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.QuizId, x.AssignmentId, x.AttemptNumber }).IsUnique();
            e.Property(x => x.AutoScore).HasColumnType("decimal(18,2)");
            e.Property(x => x.ManualScore).HasColumnType("decimal(18,2)");
            e.Property(x => x.FinalScore).HasColumnType("decimal(18,2)");
        });

        modelBuilder.Entity<TrainingQuizTemplate>(e =>
        {
            e.ToTable("training_quiz_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Category).HasMaxLength(200);
            e.Property(x => x.CreatedByUserId).HasMaxLength(100);
            e.Property(x => x.PassThreshold).HasColumnType("numeric(18,2)");
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Category);
            e.HasIndex(x => x.CatalogItemId);
            e.HasMany(x => x.Questions).WithOne(x => x.Template).HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.CatalogItem).WithMany().HasForeignKey(x => x.CatalogItemId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TrainingQuizTemplateQuestion>(e =>
        {
            e.ToTable("training_quiz_template_questions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Prompt).IsRequired();
            e.Property(x => x.Points).HasColumnType("decimal(18,2)");
            e.Property(x => x.ImageUrl).HasMaxLength(2000);
            e.Property(x => x.ImageStoragePath).HasMaxLength(1000);
            e.HasIndex(x => x.TemplateId);
        });
    }
}
