using Conge.Application.Abstractions;
using Conge.Application.Commands.ConfigConge;
using Conge.Domain.Entities;
using Conge.Domain.Interfaces;
using Kyntus.Iam;
using Xunit;

namespace Conge.UnitTests;

public class CongeQuotaCatalogTests
{
    private sealed class FakeQuotaRepo : IQuotaCongeServiceRepository
    {
        public Task<QuotaCongeService?> GetByServiceIdAsync(string serviceId, CancellationToken ct = default)
            => Task.FromResult<QuotaCongeService?>(null);
        public Task<IReadOnlyList<QuotaCongeService>> GetByServiceIdsAsync(IEnumerable<string> serviceIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<QuotaCongeService>>([]);
        public Task AddAsync(QuotaCongeService quota, CancellationToken ct = default) => Task.CompletedTask;
        public void Update(QuotaCongeService quota) { }
    }

    private sealed class FakeEmployeRepo : IEmployeSnapshotRepository
    {
        public List<EmployeSnapshot> Employees { get; } = new();
        public Task<EmployeSnapshot?> GetByEmployeIdAsync(Guid employeId, CancellationToken ct = default)
            => Task.FromResult(Employees.FirstOrDefault(e => e.EmployeId == employeId));
        public Task<EmployeSnapshot?> GetByEmployeIdOrEmailAsync(Guid employeId, string? email, CancellationToken ct = default)
            => GetByEmployeIdAsync(employeId, ct);
        public Task<IEnumerable<EmployeSnapshot>> GetByManagerIdAsync(Guid managerId, CancellationToken ct = default)
            => Task.FromResult(Employees.Where(e => e.ManagerId == managerId).AsEnumerable());
        public Task<IReadOnlyList<EmployeSnapshot>> GetByPerimeterAsync(
            Guid managerId, IReadOnlyList<string>? orgNodeIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(
                Employees.Where(e => e.ManagerId == managerId ||
                    (orgNodeIds is not null && e.CelluleId is not null && orgNodeIds.Contains(e.CelluleId))).ToList());
        public Task<IReadOnlyList<EmployeSnapshot>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.ToList());
        public Task<IReadOnlyList<EmployeSnapshot>> GetByServiceIdAsync(Guid serviceId, CancellationToken ct = default)
            => GetByOrgNodeIdAsync(serviceId.ToString(), ct);
        public Task<IReadOnlyList<EmployeSnapshot>> GetByOrgNodeIdAsync(string orgNodeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(
                Employees.Where(e => e.CelluleId == orgNodeId || e.OrgServiceId == orgNodeId).ToList());
        public Task AddAsync(EmployeSnapshot employe, CancellationToken ct = default) { Employees.Add(employe); return Task.CompletedTask; }
        public void Update(EmployeSnapshot employe) { }
        public void Remove(EmployeSnapshot employe) => Employees.Remove(employe);
        public Task<EmployeSnapshot?> GetAdminOuRhAsync(CancellationToken ct = default) => Task.FromResult<EmployeSnapshot?>(null);
        public Task<bool> ExistsAsync(Guid employeId, CancellationToken ct = default) => Task.FromResult(Employees.Any(e => e.EmployeId == employeId));
    }

    private sealed class FakeCatalog : IDirectoryOrgCatalog
    {
        private readonly DirectoryOrgCatalogSnapshot _snap;
        public FakeCatalog(DirectoryOrgCatalogSnapshot snap) => _snap = snap;
        public Task<DirectoryOrgCatalogSnapshot> GetSnapshotAsync(CancellationToken ct = default) => Task.FromResult(_snap);
        public Task<string?> ResolveNodeNameAsync(string nodeId, CancellationToken ct = default)
            => Task.FromResult(_snap.GetName(nodeId));
    }

    [Fact]
    public async Task GetQuotas_utilise_noms_et_effectifs_Directory()
    {
        const string celluleId = "cell-abc123def456";
        var superviseurId = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        empRepo.Employees.Add(EmployeSnapshot.Creer(
            superviseurId, "Sup", "S", "s@t.com", Guid.Empty, Guid.Empty, "",
            DateTime.UtcNow.AddYears(-3), false, "Superviseur", null, celluleId, null));
        // Un agent rattaché (effectif snapshot = 1) mais Directory dit 4.
        empRepo.Employees.Add(EmployeSnapshot.Creer(
            Guid.NewGuid(), "A", "A", "a@t.com", superviseurId, Guid.Empty, "cell-abc123def456",
            DateTime.UtcNow.AddYears(-1), false, "Employee", null, celluleId, null));

        var catalog = new FakeCatalog(new DirectoryOrgCatalogSnapshot(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [celluleId] = "Préparation RDV"
            },
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [celluleId] = 4
            },
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)));

        var handler = new GetQuotasServiceHandler(empRepo, new FakeQuotaRepo(), catalog, rebac: null);
        var rows = await handler.Handle(new GetQuotasServiceQuery(superviseurId), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal(celluleId, row.ServiceId);
        Assert.Equal("Préparation RDV", row.ServiceNom);
        Assert.Equal(4, row.Effectif);
        Assert.Equal(QuotaScopeKinds.Cellule, row.ScopeKind);
    }

    [Fact]
    public async Task GetQuotas_superviseur_avec_rebac_expose_aussi_services_equipe()
    {
        const string celluleId = "cell-reseaux-d2";
        const string serviceId = "svc-blo-ftto";
        var superviseurId = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        empRepo.Employees.Add(EmployeSnapshot.Creer(
            superviseurId, "Sup", "S", "s@t.com", Guid.Empty, Guid.Empty, "",
            DateTime.UtcNow.AddYears(-3), false, "Superviseur", null, celluleId, null));
        empRepo.Employees.Add(EmployeSnapshot.Creer(
            Guid.NewGuid(), "A", "A", "a@t.com", superviseurId, Guid.Empty, "BLO FTTO",
            DateTime.UtcNow.AddYears(-1), false, "Employee", null, celluleId, serviceId));

        var catalog = new FakeCatalog(new DirectoryOrgCatalogSnapshot(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [celluleId] = "RESEAUX D2",
                [serviceId] = "BLO / FTTO"
            },
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [celluleId] = 6 },
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [serviceId] = 3 },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [serviceId] = celluleId }));

        var rebac = new FakeRebac(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Superviseur"] = new[] { celluleId },
            ["ReferentTechnique"] = Array.Empty<string>()
        });

        var handler = new GetQuotasServiceHandler(empRepo, new FakeQuotaRepo(), catalog, rebac);
        var rows = await handler.Handle(new GetQuotasServiceQuery(superviseurId), CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ScopeKind == QuotaScopeKinds.Cellule && r.ServiceId == celluleId);
        Assert.Contains(rows, r => r.ScopeKind == QuotaScopeKinds.Service && r.ServiceId == serviceId && r.ServiceNom == "BLO / FTTO");
    }

    [Fact]
    public async Task GetQuotas_exclut_service_hors_cellule_supervisee_meme_si_OrgServiceId_obsolete()
    {
        const string celluleId = "cell-reseaux-d2";
        const string serviceOk = "svc-blo-ftto";
        const string serviceHors = "svc-cible-prio";
        const string autreCellule = "cell-cible-prio";
        var superviseurId = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        // Superviseur rattaché à RESEAUX D2 mais OrgServiceId encore sur « Cible Prio ».
        empRepo.Employees.Add(EmployeSnapshot.Creer(
            superviseurId, "Sup", "S", "s@t.com", Guid.Empty, Guid.Empty, "Cible Prio",
            DateTime.UtcNow.AddYears(-3), false, "Superviseur", null, celluleId, serviceHors));
        empRepo.Employees.Add(EmployeSnapshot.Creer(
            Guid.NewGuid(), "A", "A", "a@t.com", superviseurId, Guid.Empty, "BLO FTTO",
            DateTime.UtcNow.AddYears(-1), false, "Employee", null, celluleId, serviceOk));

        var catalog = new FakeCatalog(new DirectoryOrgCatalogSnapshot(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [celluleId] = "RESEAUX D2",
                [serviceOk] = "BLO / FTTO",
                [serviceHors] = "Cible Prio",
                [autreCellule] = "Cible Prio"
            },
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [celluleId] = 6 },
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [serviceOk] = 1,
                [serviceHors] = 5
            },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [serviceOk] = celluleId,
                [serviceHors] = autreCellule
            }));

        var rebac = new FakeRebac(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Superviseur"] = new[] { celluleId },
            ["ReferentTechnique"] = Array.Empty<string>()
        });

        var handler = new GetQuotasServiceHandler(empRepo, new FakeQuotaRepo(), catalog, rebac);
        var rows = await handler.Handle(new GetQuotasServiceQuery(superviseurId), CancellationToken.None);

        Assert.DoesNotContain(rows, r => r.ServiceId == serviceHors || r.ServiceNom == "Cible Prio");
        Assert.Contains(rows, r => r.ScopeKind == QuotaScopeKinds.Cellule && r.ServiceId == celluleId);
        Assert.Contains(rows, r => r.ScopeKind == QuotaScopeKinds.Service && r.ServiceId == serviceOk);
    }

    [Fact]
    public void ResolveNodeLabel_ignore_ServiceNom_qui_est_un_id()
    {
        const string celluleId = "cell-xyz789abc012";
        var e = EmployeSnapshot.Creer(
            Guid.NewGuid(), "A", "A", "a@t.com", Guid.NewGuid(), Guid.Empty, celluleId,
            DateTime.UtcNow.AddYears(-1), false, "Employee", null, celluleId, null);

        var label = CongeQuotaPerimeter.ResolveNodeLabel(e, celluleId, QuotaScopeKinds.Cellule);
        Assert.StartsWith("Cellule ", label);

        var catalog = new DirectoryOrgCatalogSnapshot(
            new Dictionary<string, string> { [celluleId] = "Cellule Nord" },
            new Dictionary<string, int>(),
            new Dictionary<string, int>());
        var nice = CongeQuotaPerimeter.ResolveNodeLabel(e, celluleId, QuotaScopeKinds.Cellule, catalog);
        Assert.Equal("Cellule Nord", nice);
    }

    [Fact]
    public void Noeud_archive_hors_arbre_actif_v36()
    {
        var catalog = new DirectoryOrgCatalogSnapshot(
            new Dictionary<string, string>(),
            new Dictionary<string, int>(),
            new Dictionary<string, int>(),
            ActiveOrgNodeIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cell-actif" });
        Assert.False(catalog.EstHorsArbre("cell-actif"));
        Assert.True(catalog.EstHorsArbre("cell-archivee"));
        Assert.False(DirectoryOrgCatalogSnapshot.Empty.EstHorsArbre("cell-archivee"));
    }

    private sealed class FakeRebac : IRebacClient
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _byRelation;
        public FakeRebac(IReadOnlyDictionary<string, IReadOnlyList<string>> byRelation) => _byRelation = byRelation;
        public Task<bool> IsDescendantAsync(Guid viewerId, Guid targetId, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<IReadOnlyList<string>> GetManagedNodeIdsAsync(Guid employeeId, string kind, CancellationToken ct = default)
            => Task.FromResult(_byRelation.TryGetValue(kind, out var ids) ? ids : Array.Empty<string>());
        public Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(Guid actorId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<bool> CanActOnAsync(Guid actorId, Guid targetEmployeeId, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<IReadOnlyList<Guid>> GetResponsibleIdsAsync(string kind, string nodeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
