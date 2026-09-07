using Kyntus.Iam;
using Microsoft.EntityFrameworkCore;
using Prime.Domain.Entities;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;
using Xunit;

namespace PrimeBackend.Tests;

public class PrimeOrgScopeServiceTests
{
    [Fact]
    public async Task GetOperationalOrgTreeAsync_ReturnsFourLevelHierarchy()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);

        db.BusinessDepartments.Add(new BusinessDepartmentEntity
        {
            Id = "dept-1",
            Code = "COM",
            Name = "Commercial",
            Kind = "Operational",
            IsActive = true,
        });
        db.Poles.Add(new PoleEntity
        {
            Id = "pole-1",
            Name = "Pôle A",
            Cellules =
            [
                new CelluleEntity
                {
                    Id = "cell-1",
                    Name = "Cellule 1",
                    PoleId = "pole-1",
                    Services = [new ServiceEntity { Id = "svc-1", Name = "Service 1", CelluleId = "cell-1" }],
                },
            ],
        });
        db.BusinessDepartmentPoles.Add(new BusinessDepartmentPoleEntity
        {
            Id = Guid.NewGuid(),
            BusinessDepartmentId = "dept-1",
            PoleId = "pole-1",
        });
        await db.SaveChangesAsync();

        var org = new PrimeOrgScopeService(db);
        var tree = await org.GetOperationalOrgTreeAsync();

        Assert.Single(tree.OperationalDepartments);
        var dept = tree.OperationalDepartments[0];
        Assert.Equal("Commercial", dept.Name);
        Assert.Single(dept.Poles);
        Assert.Equal("Pôle A", dept.Poles[0].Name);
        Assert.Single(dept.Poles[0].Cellules);
        Assert.Equal("Cellule 1", dept.Poles[0].Cellules[0].Name);
        Assert.Single(dept.Poles[0].Cellules[0].Services);
        Assert.Equal("Service 1", dept.Poles[0].Cellules[0].Services[0].Name);
        Assert.Empty(tree.UnassignedPoles);
    }

    [Fact]
    public async Task GetLegacyEmployeesAsync_IncludesBusinessDepartmentFields()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);
        db.Employees.Add(new EmployeeEntity
        {
            Id = "emp-1",
            FirstName = "Salma",
            LastName = "Hajib",
            Role = "Manager",
            Email = "h@example.com",
            PoleId = "",
            BusinessDepartmentId = "dept-1",
            BusinessDepartmentKind = "Operational",
        });
        await db.SaveChangesAsync();

        var org = new PrimeOrgScopeService(db);
        var employees = await org.GetLegacyEmployeesAsync();

        Assert.Single(employees);
        Assert.Equal("dept-1", employees[0].BusinessDepartmentId);
        Assert.Equal("Operational", employees[0].BusinessDepartmentKind);
    }

    [Fact]
    public async Task GetSupervisorOrganizationalScopeAsync_FallsBackToEmployeeCellule_WhenRebacEmpty()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);

        db.Poles.Add(new PoleEntity { Id = "pole-1", Name = "Pôle A" });
        db.Cellules.Add(new CelluleEntity { Id = "cell-1", Name = "Cellule 1", PoleId = "pole-1" });
        db.Services.Add(new ServiceEntity { Id = "svc-1", Name = "Service 1", CelluleId = "cell-1" });
        db.Employees.Add(new EmployeeEntity
        {
            Id = Guid.NewGuid().ToString(),
            FirstName = "Mohammed",
            LastName = "Draoui",
            Role = "Superviseur",
            Email = "mohammed.draoui@kyntus.com",
            CelluleId = "cell-1",
            PoleId = "pole-1",
        });
        await db.SaveChangesAsync();
        var supervisorId = db.Employees.Select(e => e.Id).Single();

        var org = new PrimeOrgScopeService(db);
        var scope = await org.GetSupervisorOrganizationalScopeAsync(supervisorId);

        Assert.Single(scope);
        Assert.Equal("Pôle A", scope[0].Name);
        Assert.Single(scope[0].Cellules);
        Assert.Equal("Cellule 1", scope[0].Cellules[0].Name);
        Assert.Single(scope[0].Cellules[0].Services);
        Assert.Equal("Service 1", scope[0].Cellules[0].Services[0].Name);
    }

    [Fact]
    public async Task IsFicheUnderReferentResponsibilityAsync_IncludesPreAssignmentFiche_WhenReferentAssignedToService()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);

        db.Poles.Add(new PoleEntity { Id = "pole-1", Name = "Pôle A" });
        db.Cellules.Add(new CelluleEntity { Id = "cell-1", Name = "Cellule 1", PoleId = "pole-1" });
        db.Services.Add(new ServiceEntity { Id = "svc-1", Name = "Service 1", CelluleId = "cell-1" });
        db.Employees.AddRange(
            new EmployeeEntity
            {
                Id = "coach-new",
                FirstName = "Rh",
                LastName = "Auth",
                Role = "Coach",
                Email = "rh.auth@kyntus.ma",
                ServiceId = "svc-1",
                CelluleId = "cell-1",
                PoleId = "pole-1",
            },
            new EmployeeEntity
            {
                Id = "coach-old",
                FirstName = "Old",
                LastName = "Coach",
                Role = "Coach",
                Email = "old@kyntus.ma",
                ServiceId = "svc-other",
                CelluleId = "cell-1",
                PoleId = "pole-1",
            },
            new EmployeeEntity
            {
                Id = "pilote-1",
                FirstName = "Pilote",
                LastName = "Un",
                Role = "Pilote",
                Email = "pilote@kyntus.ma",
                ServiceId = "svc-1",
                CelluleId = "cell-1",
                PoleId = "pole-1",
                ReferentTechniqueId = "coach-old",
                ParentId = "coach-old",
            });
        await db.SaveChangesAsync();

        var org = new PrimeOrgScopeService(db);
        var fiche = new EmployeePrimeServiceFiche
        {
            Id = Guid.NewGuid(),
            EmployeeId = "pilote-1",
            ServiceId = "svc-1",
            CelluleId = "cell-1",
            Period = "2026-07",
            ValidationStatus = "Pending",
            FillingStatus = "Complete",
        };

        var allowed = await org.IsFicheUnderReferentResponsibilityAsync("coach-new", fiche, "Référent technique");

        Assert.True(allowed);
    }

    [Fact]
    public async Task IsFicheUnderReferentResponsibilityAsync_MatchesServiceId_IgnoringCase()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);

        db.Poles.Add(new PoleEntity { Id = "pole-1", Name = "Pôle A" });
        db.Cellules.Add(new CelluleEntity { Id = "cell-1", Name = "Cellule 1", PoleId = "pole-1" });
        db.Services.Add(new ServiceEntity { Id = "svc-1", Name = "Service 1", CelluleId = "cell-1" });
        db.Employees.Add(new EmployeeEntity
        {
            Id = "coach-1",
            FirstName = "Coach",
            LastName = "Test",
            Role = "Coach",
            Email = "coach@kyntus.ma",
            ServiceId = "SVC-1",
            CelluleId = "CELL-1",
            PoleId = "pole-1",
        });
        await db.SaveChangesAsync();

        var org = new PrimeOrgScopeService(db);
        var fiche = new EmployeePrimeServiceFiche
        {
            Id = Guid.NewGuid(),
            EmployeeId = "pilote-1",
            ServiceId = "svc-1",
            CelluleId = "cell-1",
            Period = "2026-07",
            ValidationStatus = "Pending",
            FillingStatus = "Complete",
        };

        Assert.True(await org.IsFicheUnderReferentResponsibilityAsync("coach-1", fiche, "Référent technique"));
    }

    [Fact]
    public async Task GetManagedServiceIdsAsync_FallsBackToScalarServiceId_WhenRebacEmpty()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);
        db.Employees.Add(new EmployeeEntity
        {
            Id = "coach-1",
            FirstName = "Coach",
            LastName = "Test",
            Role = "Référent technique",
            Email = "coach@kyntus.ma",
            ServiceId = "svc-1",
            CelluleId = "cell-1",
            PoleId = "pole-1",
        });
        await db.SaveChangesAsync();

        var org = new PrimeOrgScopeService(db);
        var managed = await org.GetManagedServiceIdsAsync("coach-1");

        Assert.Single(managed);
        Assert.Equal("svc-1", managed[0]);
    }

    [Fact]
    public async Task GetManagedPoleIdsAsync_FallsBackToScalarPoleId_WhenRebacEmpty()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);
        db.Employees.Add(new EmployeeEntity
        {
            Id = "chef-1",
            FirstName = "Chef",
            LastName = "Test",
            Role = "Chef de projet",
            Email = "chef@kyntus.ma",
            PoleId = "pole-1",
        });
        await db.SaveChangesAsync();

        var org = new PrimeOrgScopeService(db);
        var managed = await org.GetManagedPoleIdsAsync("chef-1");

        Assert.Single(managed);
        Assert.Equal("pole-1", managed[0]);
    }

    [Fact]
    public async Task IsFicheUnderChefResponsibilityAsync_IncludesPreAssignmentFiche_WhenChefAssignedToPole()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);

        db.Poles.Add(new PoleEntity { Id = "pole-1", Name = "Pôle A" });
        db.Cellules.Add(new CelluleEntity { Id = "cell-1", Name = "Cellule 1", PoleId = "pole-1" });
        db.Services.Add(new ServiceEntity { Id = "svc-1", Name = "Service 1", CelluleId = "cell-1" });
        db.Employees.Add(new EmployeeEntity
        {
            Id = "chef-new",
            FirstName = "Nouveau",
            LastName = "Chef",
            Role = "Chef de projet",
            Email = "chef.new@kyntus.ma",
            PoleId = "pole-1",
        });
        await db.SaveChangesAsync();

        var org = new PrimeOrgScopeService(db);
        var fiche = new EmployeePrimeServiceFiche
        {
            Id = Guid.NewGuid(),
            EmployeeId = "pilote-1",
            ServiceId = "svc-1",
            CelluleId = "cell-1",
            Period = "2026-07",
            ValidationStatus = "Superviseur Approved",
            FillingStatus = "Complete",
        };

        var allowed = await org.IsFicheUnderChefResponsibilityAsync("chef-new", fiche, "Chef de projet");

        Assert.True(allowed);
    }

    [Fact]
    public async Task IsFicheUnderSupervisorResponsibilityAsync_IncludesFicheOnAssignedCellule()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);

        db.Poles.Add(new PoleEntity { Id = "pole-1", Name = "Pôle A" });
        db.Cellules.Add(new CelluleEntity { Id = "cell-1", Name = "Cellule 1", PoleId = "pole-1" });
        db.Services.Add(new ServiceEntity { Id = "svc-1", Name = "Service 1", CelluleId = "cell-1" });
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
        await db.SaveChangesAsync();

        var org = new PrimeOrgScopeService(db);
        var fiche = new EmployeePrimeServiceFiche
        {
            Id = Guid.NewGuid(),
            EmployeeId = "pilote-1",
            ServiceId = "svc-1",
            CelluleId = "cell-1",
            Period = "2026-07",
            ValidationStatus = "Référent technique Approved",
            FillingStatus = "Complete",
        };

        var allowed = await org.IsFicheUnderSupervisorResponsibilityAsync("sup-1", fiche, "Superviseur");

        Assert.True(allowed);
    }

    [Fact]
    public async Task IsFicheUnderSupervisorResponsibilityAsync_IncludesSecondCellule_WhenRebacReturnsBoth()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);

        var supervisorId = Guid.NewGuid().ToString();
        db.Poles.Add(new PoleEntity { Id = "pole-1", Name = "Pôle A" });
        db.Cellules.AddRange(
            new CelluleEntity { Id = "cell-1", Name = "Cellule 1", PoleId = "pole-1" },
            new CelluleEntity { Id = "cell-2", Name = "Cellule 2", PoleId = "pole-1" });
        db.Services.AddRange(
            new ServiceEntity { Id = "svc-1", Name = "Service 1", CelluleId = "cell-1" },
            new ServiceEntity { Id = "svc-2", Name = "Service 2", CelluleId = "cell-2" });
        db.Employees.Add(new EmployeeEntity
        {
            Id = supervisorId,
            FirstName = "Super",
            LastName = "Multi",
            Role = "Superviseur",
            Email = "sup.multi@kyntus.ma",
            CelluleId = "cell-1",
            PoleId = "pole-1",
        });
        await db.SaveChangesAsync();

        var rebac = new StubRebacClient(new Dictionary<string, IReadOnlyList<string>>
        {
            ["Superviseur"] = ["cell-1", "cell-2"],
        });
        var org = new PrimeOrgScopeService(db, rebac);
        var fiche = new EmployeePrimeServiceFiche
        {
            Id = Guid.NewGuid(),
            EmployeeId = "pilote-2",
            ServiceId = "svc-2",
            CelluleId = "cell-2",
            Period = "2026-07",
            ValidationStatus = "Référent technique Approved",
            FillingStatus = "Complete",
        };

        var allowed = await org.IsFicheUnderSupervisorResponsibilityAsync(supervisorId, fiche, "Superviseur");

        Assert.True(allowed);
    }
}

internal sealed class StubRebacClient(IReadOnlyDictionary<string, IReadOnlyList<string>> nodesByKind) : IRebacClient
{
    public Task<bool> IsDescendantAsync(Guid viewerId, Guid targetId, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<string>> GetManagedNodeIdsAsync(Guid employeeId, string kind, CancellationToken ct = default)
    {
        if (nodesByKind.TryGetValue(kind, out var nodes))
            return Task.FromResult(nodes);
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(Guid actorId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task<bool> CanActOnAsync(Guid actorId, Guid targetEmployeeId, CancellationToken ct = default) =>
        Task.FromResult(false);

    public Task<IReadOnlyList<Guid>> GetResponsibleIdsAsync(string kind, string nodeId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);
}
