using Conge.Domain.Entities;
using Conge.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace Conge.Infrastructure.Persistence;

public class CongeDbContext : DbContext, IUnitOfWork
{
    public CongeDbContext(DbContextOptions<CongeDbContext> options) : base(options) { }

    public DbSet<DemandeConge> DemandeConges => Set<DemandeConge>();
    public DbSet<DemandeCongeDecision> DemandeCongeDecisions => Set<DemandeCongeDecision>();
    public DbSet<SoldeConge> SoldeConges => Set<SoldeConge>();
    public DbSet<EmployeSnapshot> EmployeSnapshots => Set<EmployeSnapshot>();
    public DbSet<PeriodeInterditeConge> PeriodesInterdites => Set<PeriodeInterditeConge>();
    public DbSet<QuotaCongeService> QuotasCongeService => Set<QuotaCongeService>();
    public DbSet<OrgNodeConge> OrgNodesConge => Set<OrgNodeConge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CongeDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        MarkNewDecisionsAsAdded();
        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// DetectChanges traite une entité liée avec Guid déjà généré comme existante (Modified/UPDATE).
    /// Les décisions sont immuables : Modified = ligne neuve → INSERT.
    /// </summary>
    private void MarkNewDecisionsAsAdded()
    {
        ChangeTracker.DetectChanges();
        foreach (var entry in ChangeTracker.Entries<DemandeCongeDecision>())
        {
            if (entry.State == EntityState.Modified)
                entry.State = EntityState.Added;
        }
    }
}