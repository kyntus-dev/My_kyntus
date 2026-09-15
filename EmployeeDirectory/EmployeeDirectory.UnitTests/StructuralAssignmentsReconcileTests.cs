using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using EmployeeDirectory.Infrastructure.Services;
using Kyntus.Messaging.Contracts;
using Kyntus.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using DomainAssignmentKind = EmployeeDirectory.Domain.Enums.OrgAssignmentKind;
using DomainNodeLevel = EmployeeDirectory.Domain.Enums.OrgNodeLevel;

namespace EmployeeDirectory.UnitTests;

public class StructuralAssignmentsReconcileTests : IDisposable
{
    private readonly DirectoryDbContext _db;
    private readonly DirectoryWriteService _write;

    public StructuralAssignmentsReconcileTests()
    {
        var options = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new DirectoryDbContext(options);
        var outbox = new NoopOutboxWriter();
        var exclusivity = new OrgStructuralRoleExclusivityService(_db, outbox);
        var hierarchy = new DirectoryHierarchyService(_db);
        _write = new DirectoryWriteService(
            _db,
            outbox,
            hierarchy,
            exclusivity,
            new NoopPilotRotation(),
            new NoopHtelFusion());
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Reconcile_creates_two_poles_with_primary_anchor()
    {
        var employeeId = Guid.NewGuid();
        SeedEmployee(employeeId, KyntusRoleNames.Employee);
        SeedPole("pole-a", "Pôle A");
        SeedPole("pole-b", "Pôle B");

        var result = await _write.ReconcileEmployeeStructuralAssignmentsAsync(
            "ChefDeProjet",
            employeeId,
            ["pole-a", "pole-b"],
            "pole-a",
            null,
            "create multi");

        Assert.Equal(["pole-a", "pole-b"], result.NodeIds.OrderBy(x => x).ToArray());
        Assert.Equal("pole-a", result.PrimaryNodeId);
        Assert.Equal(2, result.AddedNodeIds.Count);
        Assert.Empty(result.RemovedNodeIds);

        var employee = await _db.Employees.FirstAsync(e => e.Id == employeeId);
        Assert.Equal(KyntusRoleNames.ChefDeProjet, employee.Role);
        Assert.Equal("pole-a", employee.PoleId);

        var active = await _db.OrgAssignments
            .Where(a => a.EmployeeId == employeeId && a.EffectiveTo == null)
            .Select(a => a.NodeId)
            .OrderBy(x => x)
            .ToListAsync();
        Assert.Equal(["pole-a", "pole-b"], active);
    }

    [Fact]
    public async Task Reconcile_replaces_set_exactly()
    {
        var employeeId = Guid.NewGuid();
        SeedEmployee(employeeId, KyntusRoleNames.ChefDeProjet, poleId: "pole-a");
        SeedPole("pole-a", "Pôle A");
        SeedPole("pole-b", "Pôle B");
        SeedPole("pole-c", "Pôle C");
        SeedAssignment(employeeId, DomainAssignmentKind.ChefDeProjet, "pole-a", DomainNodeLevel.Pole);
        SeedAssignment(employeeId, DomainAssignmentKind.ChefDeProjet, "pole-b", DomainNodeLevel.Pole);

        var result = await _write.ReconcileEmployeeStructuralAssignmentsAsync(
            "ChefDeProjet",
            employeeId,
            ["pole-b", "pole-c"],
            "pole-c",
            null,
            "replace set");

        Assert.Contains("pole-c", result.AddedNodeIds);
        Assert.Contains("pole-a", result.RemovedNodeIds);
        Assert.DoesNotContain("pole-b", result.AddedNodeIds);
        Assert.DoesNotContain("pole-b", result.RemovedNodeIds);

        var active = await _db.OrgAssignments
            .Where(a => a.EmployeeId == employeeId && a.EffectiveTo == null)
            .Select(a => a.NodeId)
            .OrderBy(x => x)
            .ToListAsync();
        Assert.Equal(["pole-b", "pole-c"], active);

        var employee = await _db.Employees.FirstAsync(e => e.Id == employeeId);
        Assert.Equal("pole-c", employee.PoleId);
    }

    [Fact]
    public async Task Reconcile_rejects_unknown_node()
    {
        var employeeId = Guid.NewGuid();
        SeedEmployee(employeeId, KyntusRoleNames.Employee);
        SeedPole("pole-a", "Pôle A");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _write.ReconcileEmployeeStructuralAssignmentsAsync(
                "ChefDeProjet",
                employeeId,
                ["pole-a", "missing"],
                "pole-a",
                null,
                null));
    }

    [Fact]
    public async Task Reconcile_rejects_primary_outside_selection()
    {
        var employeeId = Guid.NewGuid();
        SeedEmployee(employeeId, KyntusRoleNames.Employee);
        SeedPole("pole-a", "Pôle A");
        SeedPole("pole-b", "Pôle B");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _write.ReconcileEmployeeStructuralAssignmentsAsync(
                "ChefDeProjet",
                employeeId,
                ["pole-a"],
                "pole-b",
                null,
                null));
    }

    [Fact]
    public async Task Reconcile_allows_co_titulars_on_same_node()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        SeedEmployee(alice, KyntusRoleNames.ChefDeProjet, poleId: "pole-a");
        SeedEmployee(bob, KyntusRoleNames.Employee);
        SeedPole("pole-a", "Pôle A");
        SeedPole("pole-b", "Pôle B");
        SeedAssignment(alice, DomainAssignmentKind.ChefDeProjet, "pole-a", DomainNodeLevel.Pole);
        SeedAssignment(alice, DomainAssignmentKind.ChefDeProjet, "pole-b", DomainNodeLevel.Pole);

        var result = await _write.ReconcileEmployeeStructuralAssignmentsAsync(
            "ChefDeProjet",
            bob,
            ["pole-a"],
            "pole-a",
            null,
            "co-titulaire pole-a");

        Assert.Empty(result.RevokedOnNode);

        var activeOnPoleA = await _db.OrgAssignments
            .Where(a => a.Kind == DomainAssignmentKind.ChefDeProjet
                        && a.NodeId == "pole-a"
                        && a.EffectiveTo == null)
            .Select(a => a.EmployeeId)
            .OrderBy(x => x)
            .ToListAsync();
        Assert.Equal(2, activeOnPoleA.Count);
        Assert.Contains(alice, activeOnPoleA);
        Assert.Contains(bob, activeOnPoleA);

        // Alice conserve ses deux pôles.
        var aliceActive = await _db.OrgAssignments
            .Where(a => a.EmployeeId == alice && a.EffectiveTo == null)
            .Select(a => a.NodeId)
            .OrderBy(x => x)
            .ToListAsync();
        Assert.Equal(["pole-a", "pole-b"], aliceActive);
    }

    [Fact]
    public async Task Assign_allows_co_titular_without_revokeEmployeeIds()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        SeedEmployee(alice, KyntusRoleNames.ChefDeProjet, poleId: "pole-a");
        SeedEmployee(bob, KyntusRoleNames.Employee);
        SeedPole("pole-a", "Pôle A");
        SeedAssignment(alice, DomainAssignmentKind.ChefDeProjet, "pole-a", DomainNodeLevel.Pole);

        var result = await _write.AssignStructureRoleAsync(
            "ChefDeProjet",
            "pole-a",
            bob,
            null,
            "add co-responsable");

        Assert.Empty(result.RevokedOnNode);

        var active = await _db.OrgAssignments
            .Where(a => a.NodeId == "pole-a" && a.EffectiveTo == null)
            .Select(a => a.EmployeeId)
            .OrderBy(x => x)
            .ToListAsync();
        Assert.Equal(2, active.Count);
        Assert.Contains(alice, active);
        Assert.Contains(bob, active);
    }

    [Fact]
    public async Task Assign_explicit_revokeEmployeeIds_evicts_named_incumbent()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        SeedEmployee(alice, KyntusRoleNames.ChefDeProjet, poleId: "pole-a");
        SeedEmployee(bob, KyntusRoleNames.Employee);
        SeedPole("pole-a", "Pôle A");
        SeedAssignment(alice, DomainAssignmentKind.ChefDeProjet, "pole-a", DomainNodeLevel.Pole);

        var result = await _write.AssignStructureRoleAsync(
            "ChefDeProjet",
            "pole-a",
            bob,
            null,
            "replace alice",
            revokeEmployeeIds: [alice]);

        Assert.Contains(result.RevokedOnNode, r => r.EmployeeId == alice.ToString());

        var active = await _db.OrgAssignments
            .Where(a => a.NodeId == "pole-a" && a.EffectiveTo == null)
            .ToListAsync();
        Assert.Single(active);
        Assert.Equal(bob, active[0].EmployeeId);
    }

    [Fact]
    public async Task Assign_idempotent_when_already_on_node()
    {
        var alice = Guid.NewGuid();
        SeedEmployee(alice, KyntusRoleNames.ChefDeProjet, poleId: "pole-a");
        SeedPole("pole-a", "Pôle A");
        SeedAssignment(alice, DomainAssignmentKind.ChefDeProjet, "pole-a", DomainNodeLevel.Pole);

        var result = await _write.AssignStructureRoleAsync(
            "ChefDeProjet",
            "pole-a",
            alice,
            null,
            "noop");

        Assert.Empty(result.Revoked);
        var count = await _db.OrgAssignments.CountAsync(
            a => a.EmployeeId == alice && a.NodeId == "pole-a" && a.EffectiveTo == null);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Resolver_union_managed_employees_across_two_cellules()
    {
        var sup = Guid.NewGuid();
        var empA = Guid.NewGuid();
        var empB = Guid.NewGuid();
        SeedEmployee(sup, KyntusRoleNames.Superviseur, poleId: "pole-1");
        SeedEmployee(empA, KyntusRoleNames.Pilote);
        SeedEmployee(empB, KyntusRoleNames.Pilote);
        SeedPole("pole-1", "Pôle 1");
        _db.OrgCellules.Add(new OrgCellule { Id = "cell-a", Name = "Cell A", PoleId = "pole-1" });
        _db.OrgCellules.Add(new OrgCellule { Id = "cell-b", Name = "Cell B", PoleId = "pole-1" });
        _db.SaveChanges();

        var empAEntity = await _db.Employees.FirstAsync(e => e.Id == empA);
        empAEntity.CelluleId = "cell-a";
        empAEntity.PoleId = "pole-1";
        var empBEntity = await _db.Employees.FirstAsync(e => e.Id == empB);
        empBEntity.CelluleId = "cell-b";
        empBEntity.PoleId = "pole-1";
        _db.SaveChanges();

        SeedAssignment(sup, DomainAssignmentKind.Superviseur, "cell-a", DomainNodeLevel.Cellule);
        SeedAssignment(sup, DomainAssignmentKind.Superviseur, "cell-b", DomainNodeLevel.Cellule);

        var resolver = new OrgResponsibilityResolver(_db);
        var managed = await resolver.GetManagedEmployeeIdsAsync(sup);
        Assert.Contains(empA, managed);
        Assert.Contains(empB, managed);
        Assert.True(await resolver.CanActOnAsync(sup, empA));
        Assert.True(await resolver.CanActOnAsync(sup, empB));
    }

    private void SeedEmployee(Guid id, string role, string? poleId = null)
    {
        _db.Employees.Add(new Employee
        {
            Id = id,
            Email = $"{id:N}@test.local",
            FirstName = "Test",
            LastName = "User",
            Role = role,
            PoleId = poleId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();
    }

    private void SeedPole(string id, string name)
    {
        _db.OrgPoles.Add(new OrgPole { Id = id, Name = name });
        _db.SaveChanges();
    }

    private void SeedAssignment(Guid employeeId, DomainAssignmentKind kind, string nodeId, DomainNodeLevel level)
    {
        _db.OrgAssignments.Add(new OrgAssignment
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            NodeId = nodeId,
            NodeLevel = level,
            EmployeeId = employeeId,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
        });
        _db.SaveChanges();
    }

    private sealed class NoopOutboxWriter : IOutboxWriter
    {
        public Task EnqueueAsync<T>(T message, string? aggregateId = null, string? correlationId = null, CancellationToken ct = default)
            where T : class => Task.CompletedTask;
    }

    private sealed class NoopPilotRotation : IPilotRotationTenureService
    {
        public Task BootstrapProjectedPilotsAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<PilotRotationEligibilityDto> GetEligibilityAsync(Guid employeeId, string targetServiceId, CancellationToken ct = default) =>
            Task.FromResult(new PilotRotationEligibilityDto(true, true, null, null, null, null, 0));

        public Task ValidateRotationAsync(Guid employeeId, string targetServiceId, bool forceTenureOverride, string? reason, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<PilotRotationHistoryEntryDto>> GetRotationHistoryAsync(Guid employeeId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PilotRotationHistoryEntryDto>>([]);

        public Task<IReadOnlyList<PilotRotationSummaryDto>> ListRotationSummariesAsync(
            string? serviceId,
            DateTime? from,
            DateTime? to,
            int? minRotations,
            int? maxRotations,
            string? sort,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PilotRotationSummaryDto>>([]);

        public Task ApplyRotationHrProfileAsync(Guid employeeId, string previousServiceId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoopHtelFusion : IHtelFusionService
    {
        public Task<bool> ReviewIdentityAsync(HtelIdentityReviewRequest request, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<HtelTechnicienDto>> ListTechniciensAsync(bool? actifOnly = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<HtelTechnicienDto>>([]);

        public Task<HtelLiaisonsReportDto> GetLiaisonsAsync(CancellationToken ct = default) =>
            Task.FromResult(new HtelLiaisonsReportDto([], [], [], []));

        public Task<HtelSyncReportDto> SyncAsync(CancellationToken ct = default) =>
            Task.FromResult(new HtelSyncReportDto(0, 0, 0, 0, 0, 0));

        public Task<bool> LinkAsync(Guid employeeId, int idTechnicien, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task<bool> UnlinkAsync(Guid employeeId, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task ApplyLinkOnEmployeeAsync(Employee employee, int? explicitIdTechnicien, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
