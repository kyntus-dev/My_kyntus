using Conge.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Conge.Infrastructure.Persistence.Configurations;

public class DemandeCongeConfiguration : IEntityTypeConfiguration<DemandeConge>
{
    public void Configure(EntityTypeBuilder<DemandeConge> builder)
    {
        builder.ToTable("demandes_conge");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EmployeId).IsRequired();
        builder.Property(x => x.ManagerId).IsRequired();

        builder.Property(x => x.TypeConge)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.TypeExceptionnel)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.Statut)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.NombreJours).IsRequired();
        builder.Property(x => x.DateDebut).IsRequired();
        builder.Property(x => x.DateFin).IsRequired();
        builder.Property(x => x.DateDemande).IsRequired();

        builder.Property(x => x.Motif).HasMaxLength(500);
        builder.Property(x => x.CommentaireManager).HasMaxLength(500);
        builder.Property(x => x.CommentaireRh).HasMaxLength(500);

        builder.Property(x => x.ValidationNodeId).HasMaxLength(100);
        builder.Property(x => x.ValidationNodeLevel).HasMaxLength(50);

        builder.HasMany(x => x.Decisions)
            .WithOne(x => x.Demande)
            .HasForeignKey(x => x.DemandeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Decisions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        // Index pour les requêtes fréquentes
        builder.HasIndex(x => x.EmployeId);
        builder.HasIndex(x => x.ManagerId);
        builder.HasIndex(x => x.ValidationNodeId);
        builder.HasIndex(x => new { x.EmployeId, x.Statut });

        // Ignorer les domain events (pas persistés)
        builder.Ignore(x => x.DomainEvents);
    }
}

public class DemandeCongeDecisionConfiguration : IEntityTypeConfiguration<DemandeCongeDecision>
{
    public void Configure(EntityTypeBuilder<DemandeCongeDecision> builder)
    {
        builder.ToTable("demande_conge_decisions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.DemandeId).IsRequired();
        builder.Property(x => x.ActeurId).IsRequired();
        builder.Property(x => x.ActeurNom).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ActeurRole).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.StatutAvant)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(x => x.StatutApres)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(x => x.Commentaire).HasMaxLength(500);
        builder.Property(x => x.At).IsRequired();

        builder.HasIndex(x => x.DemandeId);
        builder.HasIndex(x => x.ActeurId);
        builder.HasIndex(x => x.At);
    }
}

public class PeriodeInterditeCongeConfiguration : IEntityTypeConfiguration<PeriodeInterditeConge>
{
    public void Configure(EntityTypeBuilder<PeriodeInterditeConge> builder)
    {
        builder.ToTable("periodes_interdites_conge");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.MoisInterditsJson).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ScopeKind).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ScopeId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => new { x.ScopeKind, x.ScopeId }).IsUnique();
    }
}

public class QuotaCongeServiceConfiguration : IEntityTypeConfiguration<QuotaCongeService>
{
    public void Configure(EntityTypeBuilder<QuotaCongeService> builder)
    {
        builder.ToTable("quotas_conge_service");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ServiceId).IsRequired().HasMaxLength(100);
        builder.Property(x => x.ScopeKind).IsRequired().HasMaxLength(20);
        builder.Property(x => x.MaxAbsentsSimultanes).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => x.ServiceId).IsUnique();
    }
}

public class SoldeCongeConfiguration : IEntityTypeConfiguration<SoldeConge>
{
    public void Configure(EntityTypeBuilder<SoldeConge> builder)
    {
        builder.ToTable("soldes_conge");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.EmployeId).IsRequired();
        builder.Property(x => x.Annee).IsRequired();
        builder.Property(x => x.SoldeInitial).IsRequired();
        builder.Property(x => x.SoldeUtilise).IsRequired();

        // Unicité employé/année
        builder.HasIndex(x => new { x.EmployeId, x.Annee }).IsUnique();
    }
}

public class EmployeSnapshotConfiguration : IEntityTypeConfiguration<EmployeSnapshot>
{
    public void Configure(EntityTypeBuilder<EmployeSnapshot> builder)
    {
        builder.ToTable("employe_snapshots");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EmployeId).IsRequired();
        builder.Property(x => x.Nom).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Prenom).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ServiceNom).HasMaxLength(200);
        builder.Property(x => x.DateEmbauche).IsRequired();
        builder.Property(x => x.PoleId).HasMaxLength(100);
        builder.Property(x => x.CelluleId).HasMaxLength(100);
        builder.Property(x => x.OrgServiceId).HasMaxLength(100);
        builder.Property(x => x.IsArchived).IsRequired().HasDefaultValue(false);

        builder.HasIndex(x => x.EmployeId).IsUnique();
        builder.HasIndex(x => x.ManagerId);
        builder.HasIndex(x => x.CelluleId);
        builder.HasIndex(x => x.OrgServiceId);
        builder.HasIndex(x => x.PoleId);
        builder.HasIndex(x => x.IsArchived);
    }
}

public class OrgNodeCongeConfiguration : IEntityTypeConfiguration<OrgNodeConge>
{
    public void Configure(EntityTypeBuilder<OrgNodeConge> builder)
    {
        builder.ToTable("org_nodes_conge");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(100);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Level).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ParentId).HasMaxLength(100);
        builder.Property(x => x.IsDeleted).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.HasIndex(x => x.Level);
        builder.HasIndex(x => x.ParentId);
    }
}
