using EmployeeDirectory.Application.Exceptions;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using EmployeeDirectory.Infrastructure.Services;
using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using DomainAssignmentKind = EmployeeDirectory.Domain.Enums.OrgAssignmentKind;
using DomainNodeLevel = EmployeeDirectory.Domain.Enums.OrgNodeLevel;

namespace EmployeeDirectory.UnitTests;

public class PilotRotationTenureServiceTests : IDisposable
{
    private readonly DirectoryDbContext _db;
    private readonly PilotRotationTenureService _service;

    public PilotRotationTenureServiceTests()
    {
        var options = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new DirectoryDbContext(options);
        _service = new PilotRotationTenureService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetEligibilityAsync_allows_rotation_after_six_months()
    {
        var employeeId = Guid.NewGuid();
        SeedService("svc-old", "Ancien service");
        SeedService("svc-new", "Nouveau service");
        SeedPilotAssignment(employeeId, "svc-old", DateTime.UtcNow.AddMonths(-7));

        var eligibility = await _service.GetEligibilityAsync(employeeId, "svc-new");

        Assert.True(eligibility.Eligible);
        Assert.False(eligibility.IsSameService);
        Assert.Equal(0, eligibility.DaysRemaining);
    }

    [Fact]
    public async Task GetEligibilityAsync_blocks_rotation_before_six_months()
    {
        var employeeId = Guid.NewGuid();
        SeedService("svc-old", "Ancien service");
        SeedService("svc-new", "Nouveau service");
        SeedPilotAssignment(employeeId, "svc-old", DateTime.UtcNow.AddMonths(-4));

        var eligibility = await _service.GetEligibilityAsync(employeeId, "svc-new");

        Assert.False(eligibility.Eligible);
        Assert.True(eligibility.DaysRemaining > 0);
        Assert.Equal("svc-old", eligibility.CurrentServiceId);
    }

    [Fact]
    public async Task ValidateRotationAsync_same_service_skips_guard()
    {
        var employeeId = Guid.NewGuid();
        SeedService("svc-a", "Service A");
        SeedPilotAssignment(employeeId, "svc-a", DateTime.UtcNow.AddDays(-10));

        var ex = await Record.ExceptionAsync(() =>
            _service.ValidateRotationAsync(employeeId, "svc-a", false, null));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ValidateRotationAsync_throws_when_blocked_without_override()
    {
        var employeeId = Guid.NewGuid();
        SeedService("svc-old", "Ancien service");
        SeedService("svc-new", "Nouveau service");
        SeedPilotAssignment(employeeId, "svc-old", DateTime.UtcNow.AddMonths(-2));

        await Assert.ThrowsAsync<PilotRotationTenureException>(() =>
            _service.ValidateRotationAsync(employeeId, "svc-new", false, null));
    }

    [Fact]
    public async Task ValidateRotationAsync_allows_admin_override_with_reason()
    {
        var employeeId = Guid.NewGuid();
        SeedService("svc-old", "Ancien service");
        SeedService("svc-new", "Nouveau service");
        SeedPilotAssignment(employeeId, "svc-old", DateTime.UtcNow.AddMonths(-2));

        var ex = await Record.ExceptionAsync(() =>
            _service.ValidateRotationAsync(employeeId, "svc-new", true, "Besoin urgent client"));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ValidateRotationAsync_requires_reason_for_override()
    {
        var employeeId = Guid.NewGuid();
        SeedService("svc-old", "Ancien service");
        SeedService("svc-new", "Nouveau service");
        SeedPilotAssignment(employeeId, "svc-old", DateTime.UtcNow.AddMonths(-2));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.ValidateRotationAsync(employeeId, "svc-new", true, "  "));
    }

    [Fact]
    public async Task BootstrapProjectedPilotsAsync_creates_missing_active_segment()
    {
        var employeeId = Guid.NewGuid();
        SeedService("svc-proj", "Service projeté");
        _db.Employees.Add(new Employee
        {
            Id = employeeId,
            Email = "pilot@test.local",
            FirstName = "Pil",
            LastName = "Ot",
            Role = KyntusRoleNames.Pilote,
            ServiceId = "svc-proj",
            HireDate = DateTime.UtcNow.AddYears(-1),
            CreatedAt = DateTime.UtcNow.AddYears(-1),
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        await _service.BootstrapProjectedPilotsAsync();

        var active = await _db.OrgAssignments.SingleAsync(a =>
            a.EmployeeId == employeeId && a.Kind == DomainAssignmentKind.Pilote && a.EffectiveTo == null);
        Assert.Equal("svc-proj", active.NodeId);
    }

    [Fact]
    public async Task GetRotationHistoryAsync_returns_segments_ordered_desc()
    {
        var employeeId = Guid.NewGuid();
        SeedService("svc-1", "Service 1");
        SeedService("svc-2", "Service 2");

        _db.OrgAssignments.AddRange(
            new OrgAssignment
            {
                Id = Guid.NewGuid(),
                Kind = DomainAssignmentKind.Pilote,
                NodeId = "svc-1",
                NodeLevel = DomainNodeLevel.Service,
                EmployeeId = employeeId,
                EffectiveFrom = DateTime.UtcNow.AddYears(-2),
                EffectiveTo = DateTime.UtcNow.AddYears(-1),
                ChangeReason = "Rotation initiale",
            },
            new OrgAssignment
            {
                Id = Guid.NewGuid(),
                Kind = DomainAssignmentKind.Pilote,
                NodeId = "svc-2",
                NodeLevel = DomainNodeLevel.Service,
                EmployeeId = employeeId,
                EffectiveFrom = DateTime.UtcNow.AddYears(-1),
                EffectiveTo = null,
                ChangeReason = PilotRotationTenureService.FormatOverrideReason("Urgence"),
            });
        await _db.SaveChangesAsync();

        var history = await _service.GetRotationHistoryAsync(employeeId);

        Assert.Equal(2, history.Count);
        Assert.Equal("svc-2", history[0].ServiceId);
        Assert.True(history[0].IsOverride);
        Assert.Equal("svc-1", history[1].ServiceId);
        Assert.False(history[1].IsOverride);
    }

    [Theory]
    [InlineData("[Dérogation] Besoin client", true)]
    [InlineData("[Dérogation] déjà préfixé", true)]
    [InlineData("Besoin client", false)]
    [InlineData(null, false)]
    public void IsOverrideReason_detects_derogation_prefix(string? reason, bool expected)
    {
        Assert.Equal(expected, PilotRotationTenureService.IsOverrideReason(reason));
    }

    [Theory]
    [InlineData("Ingénieur", true)]
    [InlineData("Support IT", true)]
    [InlineData("Admin", true)]
    [InlineData("Pilote", false)]
    public async Task ValidateRotationAsync_regle_des_6_mois_hors_cellule_IT_et_Admin(string role, bool passe)
    {
        var employeeId = Guid.NewGuid();
        _db.Employees.Add(new Employee { Id = employeeId, Email = $"{Guid.NewGuid():N}@k.com", Role = role, IsActive = true });
        _db.SaveChanges();
        SeedService("svc-old", "Ancien service");
        SeedService("svc-new", "Nouveau service");
        SeedPilotAssignment(employeeId, "svc-old", DateTime.UtcNow.AddMonths(-1));

        var ex = await Record.ExceptionAsync(() => _service.ValidateRotationAsync(employeeId, "svc-new", false, null));

        if (passe) Assert.Null(ex);
        else Assert.IsType<PilotRotationTenureException>(ex);
    }

    private void SeedService(string id, string name)
    {
        if (_db.OrgServices.Any(s => s.Id == id)) return;

        var poleId = "pole-test";
        if (!_db.OrgPoles.Any(p => p.Id == poleId))
        {
            _db.OrgPoles.Add(new OrgPole { Id = poleId, Name = "Pôle test" });
        }

        var celluleId = "cell-test";
        if (!_db.OrgCellules.Any(c => c.Id == celluleId))
        {
            _db.OrgCellules.Add(new OrgCellule { Id = celluleId, Name = "Cellule test", PoleId = poleId });
        }

        _db.OrgServices.Add(new OrgService { Id = id, Name = name, CelluleId = celluleId });
        _db.SaveChanges();
    }

    private void SeedPilotAssignment(Guid employeeId, string serviceId, DateTime effectiveFrom)
    {
        _db.OrgAssignments.Add(new OrgAssignment
        {
            Id = Guid.NewGuid(),
            Kind = DomainAssignmentKind.Pilote,
            NodeId = serviceId,
            NodeLevel = DomainNodeLevel.Service,
            EmployeeId = employeeId,
            EffectiveFrom = effectiveFrom,
        });
        _db.SaveChanges();
    }
}
