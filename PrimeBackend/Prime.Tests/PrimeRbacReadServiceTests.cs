using Microsoft.EntityFrameworkCore;
using Prime.Domain.Entities;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;
using Xunit;

namespace PrimeBackend.Tests;

public class PrimeRbacReadServiceTests
{
    [Fact]
    public void PermissionRoleAliases_IncludesCoachWhenActingAsReferentTechnique()
    {
        var aliases = PrimeRbacReadService.PermissionRoleAliases("Référent technique");

        Assert.Contains("Coach", aliases);
        Assert.Contains("Référent technique", aliases);
    }

    [Fact]
    public void PermissionRoleAliases_IncludesRpWhenActingAsChefDeProjet()
    {
        var aliases = PrimeRbacReadService.PermissionRoleAliases("Chef de projet");

        Assert.Contains("RP", aliases);
        Assert.Contains("Chef de projet", aliases);
    }

    [Fact]
    public async Task RoleHasActionAsync_MatchesCoachPermission_WhenActingAsReferentTechnique()
    {
        await using var db = NewDb();
        db.RbacPermissions.Add(new RbacPermission
        {
            Id = Guid.NewGuid(),
            Role = "Coach",
            Action = "Validate",
            Scope = "Service",
            IsAllowed = true,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var rbac = new PrimeRbacReadService(db, new PrimeOrgScopeService(db));
        Assert.True(await rbac.RoleHasActionAsync("Référent technique", "Validate"));
    }

    [Fact]
    public async Task CanAccessFicheAsync_ChefReadsPreAssignmentFiche_OnManagedPole()
    {
        await using var db = NewDb();
        SeedOrg(db);
        db.Employees.Add(new EmployeeEntity
        {
            Id = "chef-1",
            FirstName = "Chef",
            LastName = "Un",
            Role = "Chef de projet",
            Email = "chef@kyntus.ma",
            PoleId = "pole-1",
        });
        db.RbacPermissions.Add(Perm("Chef de projet", "Read", "Pole"));
        await db.SaveChangesAsync();

        var org = new PrimeOrgScopeService(db);
        var rbac = new PrimeRbacReadService(db, org);
        var actor = new Employee
        {
            Id = "chef-1",
            Role = "Chef de projet",
            PoleId = "pole-1",
        };
        var fiche = Fiche("cell-1", "svc-1", "Superviseur Approved");

        Assert.True(await rbac.CanAccessFicheAsync(actor, fiche, "Read", CancellationToken.None));
    }

    [Fact]
    public async Task CanAccessFicheAsync_SupervisorCannotValidateOutsideCellule()
    {
        await using var db = NewDb();
        SeedOrg(db);
        db.Cellules.Add(new CelluleEntity { Id = "cell-other", Name = "Autre", PoleId = "pole-1" });
        db.Employees.Add(new EmployeeEntity
        {
            Id = "sup-1",
            FirstName = "Super",
            LastName = "Viseur",
            Role = "Superviseur",
            Email = "sup@kyntus.ma",
            CelluleId = "cell-1",
            PoleId = "pole-1",
        });
        db.RbacPermissions.Add(Perm("Superviseur", "Validate", "Cellule"));
        db.WorkflowSteps.Add(new WorkflowStepConfig
        {
            Id = Guid.NewGuid(),
            SortOrder = 2,
            ApproverRole = "Superviseur",
            FromStatus = "Référent technique Approved",
            ToStatus = "Superviseur Approved",
            IsActive = true,
        });
        await db.SaveChangesAsync();

        var rbac = new PrimeRbacReadService(db, new PrimeOrgScopeService(db));
        var actor = new Employee
        {
            Id = "sup-1",
            Role = "Superviseur",
            CelluleId = "cell-1",
            PoleId = "pole-1",
        };
        var fiche = Fiche("cell-other", "svc-other", "Référent technique Approved");

        Assert.False(await rbac.CanAccessFicheAsync(actor, fiche, "Validate", CancellationToken.None));
    }

    [Fact]
    public async Task CanAccessFicheAsync_ReferentReadsPendingFiche_OnAssignedService()
    {
        await using var db = NewDb();
        SeedOrg(db);
        db.Employees.Add(new EmployeeEntity
        {
            Id = "coach-1",
            FirstName = "Coach",
            LastName = "Un",
            Role = "Coach",
            Email = "coach@kyntus.ma",
            ServiceId = "SVC-1",
            CelluleId = "cell-1",
            PoleId = "pole-1",
        });
        db.RbacPermissions.Add(Perm("Coach", "Read", "Service"));
        await db.SaveChangesAsync();

        var rbac = new PrimeRbacReadService(db, new PrimeOrgScopeService(db));
        var actor = new Employee
        {
            Id = "coach-1",
            Role = "Référent technique",
            ServiceId = "SVC-1",
            CelluleId = "cell-1",
            PoleId = "pole-1",
        };
        var fiche = Fiche("cell-1", "svc-1", "Pending");

        Assert.True(await rbac.CanAccessFicheAsync(actor, fiche, "Read", CancellationToken.None));
    }

    private static PrimeDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PrimeDbContext(options);
    }

    private static void SeedOrg(PrimeDbContext db)
    {
        db.Poles.Add(new PoleEntity { Id = "pole-1", Name = "Pôle A" });
        db.Cellules.Add(new CelluleEntity { Id = "cell-1", Name = "Cellule 1", PoleId = "pole-1" });
        db.Services.Add(new ServiceEntity { Id = "svc-1", Name = "Service 1", CelluleId = "cell-1" });
    }

    private static RbacPermission Perm(string role, string action, string scope) => new()
    {
        Id = Guid.NewGuid(),
        Role = role,
        Action = action,
        Scope = scope,
        IsAllowed = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static EmployeePrimeServiceFiche Fiche(string celluleId, string serviceId, string status) => new()
    {
        Id = Guid.NewGuid(),
        EmployeeId = "pilote-1",
        CelluleId = celluleId,
        ServiceId = serviceId,
        Period = "2026-07",
        ValidationStatus = status,
        FillingStatus = "Complete",
    };
}
