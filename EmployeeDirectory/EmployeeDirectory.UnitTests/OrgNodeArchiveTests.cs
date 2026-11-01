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

/// <summary>
/// v9 — Organisation administrative : « supprimer » un pôle, une cellule ou un service
/// l'archive (rien n'est effacé) ; la suppression définitive est réservée aux nœuds
/// archivés qui n'ont jamais servi.
/// </summary>
public class OrgNodeArchiveTests : IDisposable
{
    private readonly DirectoryDbContext _db;
    private readonly DirectoryWriteService _write;

    public OrgNodeArchiveTests()
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
        Seed();
    }

    public void Dispose() => _db.Dispose();

    private void Seed()
    {
        var dept = new BusinessDepartment
        {
            Id = Guid.NewGuid(), Code = "OP-002", Name = "Orange",
            Kind = BusinessDepartmentKind.Operational, IsActive = true, CreatedAt = DateTime.UtcNow,
        };
        _db.BusinessDepartments.Add(dept);
        _db.OrgPoles.Add(new OrgPole { Id = "p-sfr", Name = "SFR", BusinessDepartmentId = dept.Id });
        _db.OrgCellules.Add(new OrgCellule { Id = "c-ats", Name = "ATS", PoleId = "p-sfr" });
        _db.OrgServices.Add(new OrgService { Id = "s-ats", Name = "ATS", CelluleId = "c-ats" });
        _db.OrgCellules.Add(new OrgCellule { Id = "c-vide", Name = "Cellule test", PoleId = "p-sfr" });
        _db.Employees.Add(new Employee
        {
            Id = Guid.NewGuid(), Email = "pilote@kyntus.com", FirstName = "A", LastName = "GHAZI",
            Role = "Pilote", PoleId = "p-sfr", CelluleId = "c-ats", ServiceId = "s-ats",
            IsActive = true, HireDate = DateTime.UtcNow.AddYears(-1),
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Archiver_un_pole_conserve_les_lignes_et_archive_les_descendants()
    {
        Assert.True(await _write.DeleteOrgNodeAsync(DomainNodeLevel.Pole, "p-sfr"));

        var pole = await _db.OrgPoles.SingleAsync(p => p.Id == "p-sfr");
        var cellule = await _db.OrgCellules.SingleAsync(c => c.Id == "c-ats");
        var service = await _db.OrgServices.SingleAsync(s => s.Id == "s-ats");
        Assert.True(pole.IsArchived);
        Assert.True(cellule.IsArchived);
        Assert.True(service.IsArchived);
        Assert.NotNull(pole.ArchivedAt);
        // Le collaborateur garde son rattachement (historique intact).
        Assert.Equal("c-ats", (await _db.Employees.SingleAsync()).CelluleId);
    }

    [Fact]
    public async Task Restaurer_une_cellule_restaure_aussi_son_pole()
    {
        await _write.DeleteOrgNodeAsync(DomainNodeLevel.Pole, "p-sfr");

        Assert.True(await _write.RestoreOrgNodeAsync(DomainNodeLevel.Cellule, "c-ats"));

        Assert.False((await _db.OrgPoles.SingleAsync(p => p.Id == "p-sfr")).IsArchived);
        Assert.False((await _db.OrgCellules.SingleAsync(c => c.Id == "c-ats")).IsArchived);
        // Le service reste archivé tant qu'on ne le restaure pas explicitement.
        Assert.True((await _db.OrgServices.SingleAsync(s => s.Id == "s-ats")).IsArchived);
    }

    [Fact]
    public async Task Suppression_definitive_refusee_si_le_noeud_n_est_pas_archive()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _write.PurgeOrgNodeAsync(DomainNodeLevel.Cellule, "c-vide"));
        Assert.Contains("Archivez", ex.Message);
    }

    [Fact]
    public async Task Suppression_definitive_refusee_si_des_collaborateurs_y_sont_rattaches()
    {
        await _write.DeleteOrgNodeAsync(DomainNodeLevel.Service, "s-ats");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _write.PurgeOrgNodeAsync(DomainNodeLevel.Service, "s-ats"));
        Assert.Contains("collaborateurs", ex.Message);
        Assert.True(await _db.OrgServices.AnyAsync(s => s.Id == "s-ats"));
    }

    [Fact]
    public async Task Suppression_definitive_d_un_noeud_archive_jamais_utilise()
    {
        await _write.DeleteOrgNodeAsync(DomainNodeLevel.Cellule, "c-vide");

        Assert.True(await _write.PurgeOrgNodeAsync(DomainNodeLevel.Cellule, "c-vide"));

        Assert.False(await _db.OrgCellules.AnyAsync(c => c.Id == "c-vide"));
    }

    [Fact]
    public async Task Archiver_un_contrat_avec_des_poles_actifs_est_refuse()
    {
        var dept = await _db.BusinessDepartments.SingleAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _write.DeleteBusinessDepartmentAsync(dept.Id));

        await _write.DeleteOrgNodeAsync(DomainNodeLevel.Pole, "p-sfr");
        Assert.True(await _write.DeleteBusinessDepartmentAsync(dept.Id));
        Assert.False((await _db.BusinessDepartments.SingleAsync()).IsActive);
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
