using Conge.Application.Services;
using Conge.Domain.Entities;
using Conge.Domain.Enums;
using Conge.Domain.Interfaces;
using Kyntus.Iam;
using Xunit;

namespace Conge.UnitTests;

/// <summary>
/// v9 — Circuit de validation : la demande part au superviseur de la cellule ;
/// le service RH ne décide que faute de superviseur compétent.
/// </summary>
public class LeaveRoutingTests
{
    private static EmployeSnapshot Emp(string nom, string role, string? celluleId, Guid? id = null)
        => EmployeSnapshot.Creer(
            id ?? Guid.NewGuid(), nom, "X", $"{nom.ToLowerInvariant()}@kyntus.com",
            Guid.NewGuid(), Guid.Empty, "Service",
            DateTime.UtcNow.AddYears(-3), role: role, celluleId: celluleId);

    [Fact]
    public async Task Pilote_est_route_vers_le_superviseur_de_sa_cellule()
    {
        var sup = Emp("BERAHOU", "Superviseur", "cell-ats");
        var pilote = Emp("GHAZI", "Pilote", "cell-ats");
        var repo = new FakeEmployeRepo { Employees = { sup, pilote } };
        var routing = new LeaveRoutingService(repo, new FakeRebac { ["cell-ats"] = [sup.EmployeId] });

        var r = await routing.ResolveAsync(pilote);

        Assert.False(r.DecisionRh);
        Assert.Equal(sup.EmployeId, r.ValidateurId);
        Assert.Equal("cell-ats", r.NodeId);
        Assert.Equal("Cellule", r.NodeLevel);
    }

    [Fact]
    public async Task Conge_d_un_superviseur_part_au_service_rh()
    {
        var rh = Emp("TIRHANIME", "RH", null);
        var sup = Emp("BERAHOU", "Superviseur", "cell-ats");
        var repo = new FakeEmployeRepo { Employees = { rh, sup }, AdminOuRh = rh };
        var routing = new LeaveRoutingService(repo, new FakeRebac { ["cell-ats"] = [sup.EmployeId] });

        var r = await routing.ResolveAsync(sup);

        Assert.True(r.DecisionRh);
        Assert.Equal(rh.EmployeId, r.ValidateurId);
    }

    [Fact]
    public async Task Cellule_sans_superviseur_actif_part_au_service_rh()
    {
        var rh = Emp("TIRHANIME", "RH", null);
        var pilote = Emp("GHAZI", "Pilote", "cell-ats");
        var repo = new FakeEmployeRepo { Employees = { rh, pilote }, AdminOuRh = rh };
        var routing = new LeaveRoutingService(repo, new FakeRebac());

        var r = await routing.ResolveAsync(pilote);

        Assert.True(r.DecisionRh);
        Assert.Equal(rh.EmployeId, r.ValidateurId);
        Assert.Equal("Cellule sans superviseur actif", r.Motif);
    }

    [Fact]
    public async Task Un_superviseur_ne_se_valide_pas_lui_meme()
    {
        var rh = Emp("TIRHANIME", "RH", null);
        // Compte au rôle Pilote mais inscrit comme superviseur de sa propre cellule.
        var pilote = Emp("GHAZI", "Pilote", "cell-ats");
        var repo = new FakeEmployeRepo { Employees = { rh, pilote }, AdminOuRh = rh };
        var routing = new LeaveRoutingService(repo, new FakeRebac { ["cell-ats"] = [pilote.EmployeId] });

        var r = await routing.ResolveAsync(pilote);

        Assert.True(r.DecisionRh);
    }

    private sealed class FakeRebac : Dictionary<string, List<Guid>>, IRebacClient
    {
        public Task<bool> IsDescendantAsync(Guid viewerId, Guid targetId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<string>> GetManagedNodeIdsAsync(Guid employeeId, string kind, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>([]);

        public Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(Guid actorId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<bool> CanActOnAsync(Guid actorId, Guid targetEmployeeId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<Guid>> GetResponsibleIdsAsync(string kind, string nodeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(
                string.Equals(kind, "Superviseur", StringComparison.OrdinalIgnoreCase) && TryGetValue(nodeId, out var ids)
                    ? ids
                    : []);
    }

    private sealed class FakeEmployeRepo : IEmployeSnapshotRepository
    {
        public List<EmployeSnapshot> Employees { get; } = new();
        public EmployeSnapshot? AdminOuRh { get; set; }

        public Task<EmployeSnapshot?> GetByEmployeIdAsync(Guid employeId, CancellationToken ct = default)
            => Task.FromResult(Employees.FirstOrDefault(e => e.EmployeId == employeId));

        public Task<EmployeSnapshot?> GetByEmployeIdOrEmailAsync(Guid employeId, string? email, CancellationToken ct = default)
            => GetByEmployeIdAsync(employeId, ct);

        public Task<IEnumerable<EmployeSnapshot>> GetByManagerIdAsync(Guid managerId, CancellationToken ct = default)
            => Task.FromResult(Employees.AsEnumerable());

        public Task<IReadOnlyList<EmployeSnapshot>> GetByPerimeterAsync(
            Guid managerId, IReadOnlyList<string>? orgNodeIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.ToList());

        public Task<IReadOnlyList<EmployeSnapshot>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.ToList());

        public Task<IReadOnlyList<EmployeSnapshot>> GetByServiceIdAsync(Guid serviceId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>([]);

        public Task<IReadOnlyList<EmployeSnapshot>> GetByOrgNodeIdAsync(string orgNodeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(
                Employees.Where(e => string.Equals(e.CelluleId, orgNodeId, StringComparison.OrdinalIgnoreCase)).ToList());

        public Task AddAsync(EmployeSnapshot employe, CancellationToken ct = default)
        {
            Employees.Add(employe);
            return Task.CompletedTask;
        }

        public void Update(EmployeSnapshot employe) { }
        public void Remove(EmployeSnapshot employe) => Employees.Remove(employe);
        public Task<EmployeSnapshot?> GetAdminOuRhAsync(CancellationToken ct = default) => Task.FromResult(AdminOuRh);
        public Task<bool> ExistsAsync(Guid employeId, CancellationToken ct = default)
            => Task.FromResult(Employees.Any(e => e.EmployeId == employeId));
    }
}
