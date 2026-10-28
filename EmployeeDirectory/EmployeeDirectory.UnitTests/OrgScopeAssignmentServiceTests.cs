using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using EmployeeDirectory.Infrastructure.Services;
using Kyntus.Messaging.Contracts;
using Kyntus.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using DomainAssignmentKind = EmployeeDirectory.Domain.Enums.OrgAssignmentKind;
using DomainNodeLevel = EmployeeDirectory.Domain.Enums.OrgNodeLevel;

namespace EmployeeDirectory.UnitTests;

/// <summary>
/// Réorganisation 2026 : responsabilités par activité (superviseur / chef de projet)
/// et par cellule (référent technique), développées en affectations existantes.
/// </summary>
public class OrgScopeAssignmentServiceTests : IDisposable
{
    private static readonly Guid DeptBytel = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DeptOrange = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DeptSupport = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly DirectoryDbContext _db;
    private readonly DirectoryWriteService _write;
    private readonly OrgScopeAssignmentService _scopes;

    public OrgScopeAssignmentServiceTests()
    {
        var options = new DbContextOptionsBuilder<DirectoryDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new DirectoryDbContext(options);
        var outbox = new NoopOutboxWriter();
        _write = new DirectoryWriteService(
            _db,
            outbox,
            new DirectoryHierarchyService(_db),
            new OrgStructuralRoleExclusivityService(_db, outbox),
            new NoopPilotRotation(),
            new NoopHtelFusion());
        _scopes = new OrgScopeAssignmentService(_db, _write);
        SeedStructure();
    }

    public void Dispose() => _db.Dispose();

    // Bytel Kyntus : pole technique (Support technicien, Prestations réseaux & CV), pole BTB (BTB)
    // Orange       : Appels Post (INEO)
    private void SeedStructure()
    {
        _db.BusinessDepartments.AddRange(
            new BusinessDepartment { Id = DeptBytel, Code = "OP-001", Name = "Bytel Kyntus", Kind = BusinessDepartmentKind.Operational },
            new BusinessDepartment { Id = DeptOrange, Code = "OP-002", Name = "Orange", Kind = BusinessDepartmentKind.Operational },
            new BusinessDepartment { Id = DeptSupport, Code = "SUP-001", Name = "RH", Kind = BusinessDepartmentKind.Support });
        _db.OrgPoles.AddRange(
            new OrgPole { Id = "pole-tech", Name = "pole technique", BusinessDepartmentId = DeptBytel },
            new OrgPole { Id = "pole-btb", Name = "pole BTB", BusinessDepartmentId = DeptBytel },
            new OrgPole { Id = "pole-post", Name = "Appels Post", BusinessDepartmentId = DeptOrange });
        _db.OrgCellules.AddRange(
            new OrgCellule { Id = "cell-st", Name = "Support technicien", PoleId = "pole-tech" },
            new OrgCellule { Id = "cell-cv", Name = "Prestations réseaux & CV", PoleId = "pole-tech" },
            new OrgCellule { Id = "cell-btb", Name = "BTB", PoleId = "pole-btb" },
            new OrgCellule { Id = "cell-ineo", Name = "INEO", PoleId = "pole-post" });
        _db.OrgServices.AddRange(
            new OrgService { Id = "svc-st", Name = "Support technicien", CelluleId = "cell-st" },
            new OrgService { Id = "svc-cv", Name = "Prestations réseaux & CV", CelluleId = "cell-cv" },
            new OrgService { Id = "svc-btb", Name = "BTB", CelluleId = "cell-btb" },
            new OrgService { Id = "svc-ineo", Name = "INEO", CelluleId = "cell-ineo" });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Superviseur_activity_scope_covers_every_cellule_of_the_activity()
    {
        var sup = SeedEmployee(KyntusRoleNames.Employee);

        var result = await _scopes.ReconcileScopeAsync(
            "Superviseur", sup, [DeptBytel.ToString()], DeptBytel.ToString(), null, null);

        Assert.Equal(OrgScopeLevels.Activite, result.ScopeLevel);
        Assert.Equal(["cell-btb", "cell-cv", "cell-st"], ActiveNodes(sup, DomainAssignmentKind.Superviseur));
        Assert.All(
            _db.OrgAssignments.Where(a => a.EmployeeId == sup && a.EffectiveTo == null),
            a => Assert.Equal(DomainNodeLevel.Cellule, a.NodeLevel));

        var employee = await _db.Employees.AsNoTracking().FirstAsync(e => e.Id == sup);
        Assert.Equal(KyntusRoleNames.Superviseur, employee.Role);
        Assert.Contains(employee.CelluleId, new[] { "cell-btb", "cell-cv", "cell-st" });
    }

    [Fact]
    public async Task ChefDeProjet_activity_scope_covers_every_pole_of_the_activity()
    {
        var cp = SeedEmployee(KyntusRoleNames.Employee);

        await _scopes.ReconcileScopeAsync(
            "ChefDeProjet", cp, [DeptBytel.ToString()], DeptBytel.ToString(), null, null);

        Assert.Equal(["pole-btb", "pole-tech"], ActiveNodes(cp, DomainAssignmentKind.ChefDeProjet));
        var employee = await _db.Employees.AsNoTracking().FirstAsync(e => e.Id == cp);
        Assert.Equal(KyntusRoleNames.ChefDeProjet, employee.Role);
    }

    [Fact]
    public async Task ReferentTechnique_cellule_scope_covers_every_service_of_the_cellule()
    {
        _db.OrgServices.Add(new OrgService { Id = "svc-st-2", Name = "Support technicien bis", CelluleId = "cell-st" });
        _db.SaveChanges();
        var rt = SeedEmployee(KyntusRoleNames.Employee);

        var result = await _scopes.ReconcileScopeAsync(
            "ReferentTechnique", rt, ["cell-st"], "cell-st", null, null);

        Assert.Equal(OrgScopeLevels.Cellule, result.ScopeLevel);
        Assert.Equal(["svc-st", "svc-st-2"], ActiveNodes(rt, DomainAssignmentKind.ReferentTechnique));
        var employee = await _db.Employees.AsNoTracking().FirstAsync(e => e.Id == rt);
        Assert.Equal(KyntusRoleNames.ReferentTechnique, employee.Role);
        Assert.Equal("cell-st", employee.CelluleId);
    }

    [Fact]
    public async Task Changing_activity_replaces_previous_cellules_exactly()
    {
        var sup = SeedEmployee(KyntusRoleNames.Employee);
        await _scopes.ReconcileScopeAsync("Superviseur", sup, [DeptBytel.ToString()], DeptBytel.ToString(), null, null);

        var result = await _scopes.ReconcileScopeAsync(
            "Superviseur", sup, [DeptOrange.ToString()], DeptOrange.ToString(), null, null);

        Assert.Equal(["cell-ineo"], ActiveNodes(sup, DomainAssignmentKind.Superviseur));
        Assert.Equal(3, result.Assignments.RemovedNodeIds.Count);
        // Historique conservé : les anciennes lignes sont clôturées, pas supprimées.
        Assert.Equal(3, _db.OrgAssignments.Count(a => a.EmployeeId == sup && a.EffectiveTo != null));
    }

    [Fact]
    public async Task Several_activities_can_be_held_with_a_primary_anchor()
    {
        var sup = SeedEmployee(KyntusRoleNames.Employee);

        var result = await _scopes.ReconcileScopeAsync(
            "Superviseur", sup, [DeptBytel.ToString(), DeptOrange.ToString()], DeptOrange.ToString(), null, null);

        Assert.Equal(4, ActiveNodes(sup, DomainAssignmentKind.Superviseur).Count);
        Assert.Equal(DeptOrange.ToString(), result.PrimaryScopeId);
        Assert.Equal("cell-ineo", result.Assignments.PrimaryNodeId);
    }

    [Fact]
    public async Task Existing_anchor_is_kept_when_it_belongs_to_the_primary_activity()
    {
        var sup = SeedEmployee(KyntusRoleNames.Superviseur, celluleId: "cell-st");
        SeedAssignment(sup, DomainAssignmentKind.Superviseur, "cell-st", DomainNodeLevel.Cellule);

        var result = await _scopes.ReconcileScopeAsync(
            "Superviseur", sup, [DeptBytel.ToString()], DeptBytel.ToString(), null, null);

        Assert.Equal("cell-st", result.Assignments.PrimaryNodeId);
        Assert.Equal(2, result.Assignments.AddedNodeIds.Count);
    }

    [Theory]
    [InlineData("Superviseur", "not-a-guid")]
    [InlineData("Superviseur", "33333333-3333-3333-3333-333333333333")] // département Support
    [InlineData("ChefDeProjet", "44444444-4444-4444-4444-444444444444")] // inconnu
    [InlineData("ReferentTechnique", "cell-missing")]
    [InlineData("Pilote", "cell-st")]
    public async Task Invalid_scopes_are_rejected_without_side_effect(string kind, string scopeId)
    {
        var emp = SeedEmployee(KyntusRoleNames.Employee);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _scopes.ReconcileScopeAsync(kind, emp, [scopeId], scopeId, null, null));

        Assert.False(_db.OrgAssignments.Any(a => a.EmployeeId == emp));
    }

    [Fact]
    public async Task Referent_on_cellule_without_service_is_rejected_with_explicit_message()
    {
        _db.OrgCellules.Add(new OrgCellule { Id = "cell-empty", Name = "Cellule vide", PoleId = "pole-tech" });
        _db.SaveChanges();
        var rt = SeedEmployee(KyntusRoleNames.Employee);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _scopes.ReconcileScopeAsync("ReferentTechnique", rt, ["cell-empty"], "cell-empty", null, null));
        Assert.Contains("Cellule vide", ex.Message);
    }

    [Fact]
    public async Task New_cellule_extends_superviseurs_covering_the_whole_activity_only()
    {
        var fullSup = SeedEmployee(KyntusRoleNames.Employee);
        await _scopes.ReconcileScopeAsync("Superviseur", fullSup, [DeptBytel.ToString()], DeptBytel.ToString(), null, null);
        var partialSup = SeedEmployee(KyntusRoleNames.Superviseur, celluleId: "cell-st");
        SeedAssignment(partialSup, DomainAssignmentKind.Superviseur, "cell-st", DomainNodeLevel.Cellule);
        var orangeSup = SeedEmployee(KyntusRoleNames.Employee);
        await _scopes.ReconcileScopeAsync("Superviseur", orangeSup, [DeptOrange.ToString()], DeptOrange.ToString(), null, null);

        var newCell = await _write.CreateCelluleAsync("pole-tech", "Gestion des RDV");
        var extended = await _scopes.PropagateAfterCelluleCreatedAsync(newCell, null);

        Assert.Equal(1, extended);
        Assert.Contains(newCell, ActiveNodes(fullSup, DomainAssignmentKind.Superviseur));
        Assert.DoesNotContain(newCell, ActiveNodes(partialSup, DomainAssignmentKind.Superviseur));
        Assert.DoesNotContain(newCell, ActiveNodes(orangeSup, DomainAssignmentKind.Superviseur));
    }

    [Fact]
    public async Task New_service_extends_referents_covering_the_whole_cellule()
    {
        var rt = SeedEmployee(KyntusRoleNames.Employee);
        await _scopes.ReconcileScopeAsync("ReferentTechnique", rt, ["cell-st"], "cell-st", null, null);

        var newSvc = await _write.CreateServiceAsync("cell-st", "Support technicien soir");
        var extended = await _scopes.PropagateAfterServiceCreatedAsync(newSvc, null);

        Assert.Equal(1, extended);
        Assert.Contains(newSvc, ActiveNodes(rt, DomainAssignmentKind.ReferentTechnique));
    }

    [Fact]
    public async Task New_pole_extends_chef_de_projet_and_superviseur_of_the_activity()
    {
        var cp = SeedEmployee(KyntusRoleNames.Employee);
        await _scopes.ReconcileScopeAsync("ChefDeProjet", cp, [DeptBytel.ToString()], DeptBytel.ToString(), null, null);
        var sup = SeedEmployee(KyntusRoleNames.Employee);
        await _scopes.ReconcileScopeAsync("Superviseur", sup, [DeptBytel.ToString()], DeptBytel.ToString(), null, null);

        _db.OrgPoles.Add(new OrgPole { Id = "pole-new", Name = "pole client" });
        _db.OrgCellules.Add(new OrgCellule { Id = "cell-new", Name = "Planification", PoleId = "pole-new" });
        _db.SaveChanges();
        await _write.AttachPoleToBusinessDepartmentAsync("pole-new", DeptBytel);

        var extended = await _scopes.PropagateAfterPoleAttachedAsync("pole-new", null);

        Assert.Equal(2, extended);
        Assert.Contains("pole-new", ActiveNodes(cp, DomainAssignmentKind.ChefDeProjet));
        Assert.Contains("cell-new", ActiveNodes(sup, DomainAssignmentKind.Superviseur));
    }

    [Fact]
    public async Task Propagation_can_be_switched_off_by_configuration()
    {
        var scopes = new OrgScopeAssignmentService(
            _db, _write, new FakeConfiguration("Directory:OrgScopePropagation:Enabled", "false"));
        var sup = SeedEmployee(KyntusRoleNames.Employee);
        await scopes.ReconcileScopeAsync("Superviseur", sup, [DeptBytel.ToString()], DeptBytel.ToString(), null, null);

        var newCell = await _write.CreateCelluleAsync("pole-tech", "Gestion des RDV");

        Assert.Equal(0, await scopes.PropagateAfterCelluleCreatedAsync(newCell, null));
        Assert.DoesNotContain(newCell, ActiveNodes(sup, DomainAssignmentKind.Superviseur));
    }

    [Fact]
    public async Task Coverage_report_flags_partial_and_full_scopes()
    {
        var fullSup = SeedEmployee(KyntusRoleNames.Employee);
        await _scopes.ReconcileScopeAsync("Superviseur", fullSup, [DeptBytel.ToString()], DeptBytel.ToString(), null, null);
        var partialSup = SeedEmployee(KyntusRoleNames.Superviseur, celluleId: "cell-st");
        SeedAssignment(partialSup, DomainAssignmentKind.Superviseur, "cell-st", DomainNodeLevel.Cellule);

        var report = await _scopes.GetCoverageReportAsync();

        var full = Assert.Single(report, r => r.EmployeeId == fullSup.ToString());
        Assert.True(full.IsFullCoverage);
        Assert.Equal(3, full.TotalNodes);
        Assert.Equal("OP-001 — Bytel Kyntus", full.ScopeLabel);

        var partial = Assert.Single(report, r => r.EmployeeId == partialSup.ToString());
        Assert.False(partial.IsFullCoverage);
        Assert.Equal(1, partial.HeldNodes);
        Assert.Equal(3, partial.TotalNodes);
    }

    // ── Affectation unitaire (page Organisation RH / import Excel) étendue au périmètre ──

    [Fact]
    public async Task Single_superviseur_assignment_is_extended_to_the_whole_activity()
    {
        var sup = SeedEmployee(KyntusRoleNames.Superviseur, "cell-st");
        SeedAssignment(sup, DomainAssignmentKind.Superviseur, "cell-st", DomainNodeLevel.Cellule);

        var added = await _scopes.ExpandAssignmentToScopeAsync("Superviseur", sup, "cell-st", null);

        Assert.Equal(2, added);
        Assert.Equal(["cell-btb", "cell-cv", "cell-st"], ActiveNodes(sup, DomainAssignmentKind.Superviseur));
        var employee = await _db.Employees.AsNoTracking().FirstAsync(e => e.Id == sup);
        Assert.Equal("cell-st", employee.CelluleId); // ancre = nœud choisi
    }

    [Fact]
    public async Task Superviseur_extension_keeps_other_activities_and_co_holders()
    {
        var sup = SeedEmployee(KyntusRoleNames.Superviseur, "cell-ineo");
        SeedAssignment(sup, DomainAssignmentKind.Superviseur, "cell-ineo", DomainNodeLevel.Cellule);
        SeedAssignment(sup, DomainAssignmentKind.Superviseur, "cell-btb", DomainNodeLevel.Cellule);
        var other = SeedEmployee(KyntusRoleNames.Superviseur, "cell-cv");
        SeedAssignment(other, DomainAssignmentKind.Superviseur, "cell-cv", DomainNodeLevel.Cellule);

        await _scopes.ExpandAssignmentToScopeAsync("Superviseur", sup, "cell-btb", null);

        Assert.Equal(
            ["cell-btb", "cell-cv", "cell-ineo", "cell-st"],
            ActiveNodes(sup, DomainAssignmentKind.Superviseur));
        Assert.Equal(["cell-cv"], ActiveNodes(other, DomainAssignmentKind.Superviseur));
    }

    [Fact]
    public async Task Single_chef_de_projet_assignment_is_extended_to_every_pole_of_the_activity()
    {
        var cp = SeedEmployee(KyntusRoleNames.ChefDeProjet);
        SeedAssignment(cp, DomainAssignmentKind.ChefDeProjet, "pole-tech", DomainNodeLevel.Pole);

        var added = await _scopes.ExpandAssignmentToScopeAsync("ChefDeProjet", cp, "pole-tech", null);

        Assert.Equal(1, added);
        Assert.Equal(["pole-btb", "pole-tech"], ActiveNodes(cp, DomainAssignmentKind.ChefDeProjet));
    }

    [Fact]
    public async Task Single_referent_assignment_is_extended_to_the_services_of_its_cellule_only()
    {
        _db.OrgServices.Add(new OrgService { Id = "svc-st-2", Name = "Support technicien bis", CelluleId = "cell-st" });
        _db.SaveChanges();
        var rt = SeedEmployee(KyntusRoleNames.ReferentTechnique);
        SeedAssignment(rt, DomainAssignmentKind.ReferentTechnique, "svc-st", DomainNodeLevel.Service);

        var added = await _scopes.ExpandAssignmentToScopeAsync("ReferentTechnique", rt, "svc-st", null);

        Assert.Equal(1, added);
        Assert.Equal(["svc-st", "svc-st-2"], ActiveNodes(rt, DomainAssignmentKind.ReferentTechnique));
    }

    [Fact]
    public async Task Extension_does_nothing_when_the_original_assignment_is_not_held()
    {
        var sup = SeedEmployee(KyntusRoleNames.Employee);

        Assert.Equal(0, await _scopes.ExpandAssignmentToScopeAsync("Superviseur", sup, "cell-st", null));
        Assert.Empty(ActiveNodes(sup, DomainAssignmentKind.Superviseur));
    }

    [Fact]
    public async Task Pilote_assignments_are_never_extended()
    {
        var pilot = SeedEmployee(KyntusRoleNames.Employee, "cell-st");
        SeedAssignment(pilot, DomainAssignmentKind.Pilote, "svc-st", DomainNodeLevel.Service);

        Assert.Equal(0, await _scopes.ExpandAssignmentToScopeAsync("Pilote", pilot, "svc-st", null));
        Assert.Equal(["svc-st"], ActiveNodes(pilot, DomainAssignmentKind.Pilote));
    }

    [Fact]
    public async Task Extension_is_disabled_by_the_feature_flag()
    {
        var scopes = new OrgScopeAssignmentService(
            _db, _write, new FakeConfiguration("Directory:OrgScopePropagation:Enabled", "false"));
        var sup = SeedEmployee(KyntusRoleNames.Superviseur, "cell-st");
        SeedAssignment(sup, DomainAssignmentKind.Superviseur, "cell-st", DomainNodeLevel.Cellule);

        Assert.Equal(0, await scopes.ExpandAssignmentToScopeAsync("Superviseur", sup, "cell-st", null));
        Assert.Equal(["cell-st"], ActiveNodes(sup, DomainAssignmentKind.Superviseur));
    }

    private List<string> ActiveNodes(Guid employeeId, DomainAssignmentKind kind) =>
        _db.OrgAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.Kind == kind && a.EffectiveTo == null)
            .Select(a => a.NodeId)
            .OrderBy(x => x)
            .ToList();

    private Guid SeedEmployee(string role, string? celluleId = null)
    {
        var id = Guid.NewGuid();
        _db.Employees.Add(new Employee
        {
            Id = id,
            Email = $"{id:N}@test.local",
            FirstName = "Test",
            LastName = id.ToString("N")[..6],
            Role = role,
            CelluleId = celluleId,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        _db.SaveChanges();
        return id;
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

    private sealed class FakeConfiguration(string key, string value) : IConfiguration
    {
        public string? this[string k]
        {
            get => string.Equals(k, key, StringComparison.OrdinalIgnoreCase) ? value : null;
            set { }
        }

        public IEnumerable<IConfigurationSection> GetChildren() => [];
        public IChangeToken GetReloadToken() => new CancellationChangeToken(CancellationToken.None);
        public IConfigurationSection GetSection(string k) => throw new NotSupportedException();
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
