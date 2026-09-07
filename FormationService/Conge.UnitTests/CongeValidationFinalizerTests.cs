using Conge.Application.Abstractions;
using Conge.Application.Contracts;
using Conge.Application.Services;
using Conge.Domain.Entities;
using Conge.Domain.Enums;
using Conge.Domain.Interfaces;
using Xunit;

namespace Conge.UnitTests;

public class CongeValidationFinalizerTests
{
    [Fact]
    public async Task Collapse_debite_solde_annee_courante_si_conge_annee_suivante()
    {
        var emp = EmployeSnapshot.Creer(
            Guid.NewGuid(), "Naji", "Mohammed", "naji@test.com",
            Guid.NewGuid(), Guid.NewGuid(), "Svc",
            DateTime.UtcNow.AddYears(-3));
        var currentYear = DateTime.Today.Year;
        var solde = SoldeConge.Initialiser(emp.EmployeId, 18, currentYear);
        var debut = new DateTime(currentYear + 1, 11, 13);
        var fin = new DateTime(currentYear + 1, 11, 20);
        var demande = DemandeConge.CreerCongeAnnuel(emp.EmployeId, emp.ManagerId, debut, fin, solde, emp);
        demande.ValiderParSuperviseur(emp.ManagerId, "ok");

        var demandes = new FakeDemandeRepo { Items = { demande } };
        var soldes = new FakeSoldeRepo { Items = { solde } };
        var employes = new FakeEmployeRepo { Employees = { emp } };
        var publisher = new FakePublisher();
        var finalizer = new CongeValidationFinalizer(
            demandes, soldes, employes, new FakeUnitOfWork(), publisher, new FakeWorkflow(rhRequired: false));

        var rhStep = await finalizer.CollapsePendingRhIfDisabledAsync(CancellationToken.None);

        Assert.False(rhStep);
        Assert.Equal(StatutDemande.Validee, demande.Statut);
        Assert.Equal(18 - demande.NombreJours, solde.SoldeRestant);
        Assert.Single(publisher.Valides);
    }

    [Fact]
    public async Task Collapse_initialise_solde_annee_conge_si_aucun_solde()
    {
        var emp = EmployeSnapshot.Creer(
            Guid.NewGuid(), "Naji", "Mohammed", "naji@test.com",
            Guid.NewGuid(), Guid.NewGuid(), "Svc",
            DateTime.UtcNow.AddYears(-3));
        var probe = SoldeConge.Initialiser(emp.EmployeId, 18, DateTime.Today.Year);
        var debut = DateTime.UtcNow.Date.AddDays(40);
        var fin = debut.AddDays(4);
        var demande = DemandeConge.CreerCongeAnnuel(emp.EmployeId, emp.ManagerId, debut, fin, probe, emp);
        demande.ValiderParSuperviseur(emp.ManagerId, "ok");

        var soldes = new FakeSoldeRepo();
        var publisher = new FakePublisher();
        var finalizer = new CongeValidationFinalizer(
            new FakeDemandeRepo { Items = { demande } },
            soldes,
            new FakeEmployeRepo { Employees = { emp } },
            new FakeUnitOfWork(),
            publisher,
            new FakeWorkflow(rhRequired: false));

        var rhStep = await finalizer.CollapsePendingRhIfDisabledAsync(CancellationToken.None);

        Assert.False(rhStep);
        Assert.Equal(StatutDemande.Validee, demande.Statut);
        Assert.Single(soldes.Items);
        Assert.Equal(demande.DateDebut.Year, soldes.Items[0].Annee);
        Assert.Single(publisher.Valides);
    }

    private sealed class FakeWorkflow(bool rhRequired) : ILeaveWorkflowPolicy
    {
        public Task<bool> IsRhValidationRequiredAsync(CancellationToken ct = default)
            => Task.FromResult(rhRequired);
    }

    private sealed class FakePublisher : ICongeEventPublisher
    {
        public List<Guid> Valides { get; } = new();

        public Task PublishCongeValideAsync(
            Guid employeId, Guid demandeId, DateTime dateDebut, DateTime dateFin,
            double nombreJours, string typeConge, string? typeExceptionnel = null,
            Guid? validateurId = null, string? validateurNom = null, CancellationToken ct = default)
        {
            Valides.Add(demandeId);
            return Task.CompletedTask;
        }

        public Task PublishCongeRefuseAsync(
            Guid employeId, Guid demandeId, string motif,
            Guid? validateurId = null, string? validateurNom = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task PublishCongeDemandeAsync(
            Guid employeId, Guid demandeId, Guid managerId,
            DateTime dateDebut, DateTime dateFin, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FakeSoldeRepo : ISoldeCongeRepository
    {
        public List<SoldeConge> Items { get; } = new();

        public Task<SoldeConge?> GetByEmployeAndAnneeAsync(Guid employeId, int annee, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(s => s.EmployeId == employeId && s.Annee == annee));

        public Task<IEnumerable<SoldeConge>> GetAllByEmployeAsync(Guid employeId, CancellationToken ct = default)
            => Task.FromResult(Items.Where(s => s.EmployeId == employeId).AsEnumerable());

        public Task AddAsync(SoldeConge solde, CancellationToken ct = default)
        {
            Items.Add(solde);
            return Task.CompletedTask;
        }

        public void Update(SoldeConge solde) { }
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
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.ToList());

        public Task<IReadOnlyList<EmployeSnapshot>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.ToList());

        public Task<IReadOnlyList<EmployeSnapshot>> GetByServiceIdAsync(Guid serviceId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.ToList());

        public Task<IReadOnlyList<EmployeSnapshot>> GetByOrgNodeIdAsync(string orgNodeId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.ToList());

        public Task AddAsync(EmployeSnapshot employe, CancellationToken ct = default)
        {
            Employees.Add(employe);
            return Task.CompletedTask;
        }

        public void Update(EmployeSnapshot employe) { }
        public void Remove(EmployeSnapshot employe) => Employees.Remove(employe);
        public Task<EmployeSnapshot?> GetAdminOuRhAsync(CancellationToken ct = default)
            => Task.FromResult<EmployeSnapshot?>(null);
        public Task<bool> ExistsAsync(Guid employeId, CancellationToken ct = default)
            => Task.FromResult(Employees.Any(e => e.EmployeId == employeId));
    }

    private sealed class FakeDemandeRepo : IDemandeCongeRepository
    {
        public List<DemandeConge> Items { get; } = new();

        public Task<DemandeConge?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(Items.FirstOrDefault(d => d.Id == id));

        public Task<IEnumerable<DemandeConge>> GetByEmployeIdAsync(Guid employeId, CancellationToken ct = default)
            => Task.FromResult(Items.Where(d => d.EmployeId == employeId).AsEnumerable());

        public Task<IEnumerable<DemandeConge>> GetByManagerIdAsync(
            Guid managerId, IEnumerable<string>? validationNodeIds = null, CancellationToken ct = default)
            => Task.FromResult(Items.Where(d => d.ManagerId == managerId).AsEnumerable());

        public Task<IEnumerable<DemandeConge>> GetByStatutAsync(StatutDemande statut, CancellationToken ct = default)
            => Task.FromResult(Items.Where(d => d.Statut == statut).AsEnumerable());

        public Task<IEnumerable<DemandeConge>> GetHistoriqueAsync(Guid employeId, int annee, CancellationToken ct = default)
            => Task.FromResult(Items.AsEnumerable());

        public Task<IEnumerable<DemandeConge>> GetByAnneeAsync(int annee, CancellationToken ct = default)
            => Task.FromResult(Items.AsEnumerable());

        public Task AddAsync(DemandeConge demande, CancellationToken ct = default)
        {
            Items.Add(demande);
            return Task.CompletedTask;
        }

        public void Update(DemandeConge demande) { }

        public Task<bool> ExistsCongeEnChevauchementAsync(
            Guid employeId, DateTime debut, DateTime fin, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<DemandeConge>> GetOccupyingQuotaAsync(
            IEnumerable<Guid> employeIds, DateTime debut, DateTime fin,
            Guid? excludeDemandeId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DemandeConge>>(Array.Empty<DemandeConge>());
    }
}
