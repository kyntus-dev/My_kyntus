using Conge.Application.Services;
using Conge.Domain.Entities;
using Conge.Domain.Enums;
using Conge.Domain.Interfaces;
using Xunit;

namespace Conge.UnitTests;

public class CongeReglesQuotaTests
{
    /// <summary>Année des congés de test : toujours dans le futur, quel que soit le jour d'exécution.</summary>
    private static readonly int An = DateTime.Today.Year + 1;

    private sealed class FakePeriodeRepo : IPeriodeInterditeRepository
    {
        public PeriodeInterditeConge Row { get; } = PeriodeInterditeConge.CreerParDefaut();
        public List<PeriodeInterditeConge> Rows { get; } = new();

        public FakePeriodeRepo() => Rows.Add(Row);

        public Task<PeriodeInterditeConge> GetOrCreateAsync(CancellationToken ct = default) => Task.FromResult(Row);

        public Task<PeriodeInterditeConge?> GetByScopeAsync(string scopeKind, string? scopeId, CancellationToken ct = default)
        {
            var kind = PeriodeInterditeScopeKinds.Normalize(scopeKind);
            var id = PeriodeInterditeScopeKinds.NormalizeScopeId(kind, scopeId);
            if (kind == PeriodeInterditeScopeKinds.General) return Task.FromResult<PeriodeInterditeConge?>(Row);
            return Task.FromResult(Rows.FirstOrDefault(r =>
                PeriodeInterditeScopeKinds.Normalize(r.ScopeKind) == kind
                && string.Equals(r.ScopeId, id, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<IReadOnlyList<PeriodeInterditeConge>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PeriodeInterditeConge>>(Rows);

        public Task AddAsync(PeriodeInterditeConge config, CancellationToken ct = default)
        {
            Rows.Add(config);
            return Task.CompletedTask;
        }

        public void Update(PeriodeInterditeConge config) { }

        public void Remove(PeriodeInterditeConge config) => Rows.Remove(config);
    }

    private sealed class FakeQuotaRepo : IQuotaCongeServiceRepository
    {
        public QuotaCongeService? Quota { get; set; }
        public Task<QuotaCongeService?> GetByServiceIdAsync(string serviceId, CancellationToken ct = default)
        {
            var id = QuotaCongeService.NormalizeNodeId(serviceId);
            if (id is null || Quota is null) return Task.FromResult<QuotaCongeService?>(null);
            return Task.FromResult(
                string.Equals(Quota.ServiceId, id, StringComparison.OrdinalIgnoreCase) ? Quota : null);
        }
        public Task<IReadOnlyList<QuotaCongeService>> GetByServiceIdsAsync(IEnumerable<string> serviceIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<QuotaCongeService>>(Quota is null ? [] : [Quota]);
        public Task AddAsync(QuotaCongeService quota, CancellationToken ct = default) { Quota = quota; return Task.CompletedTask; }
        public void Update(QuotaCongeService quota) => Quota = quota;
    }

    private sealed class FakeDemandeRepo : IDemandeCongeRepository
    {
        public List<DemandeConge> Occupying { get; } = new();
        public Task<DemandeConge?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<DemandeConge?>(null);
        public Task<IEnumerable<DemandeConge>> GetByEmployeIdAsync(Guid employeId, CancellationToken ct = default) => Task.FromResult<IEnumerable<DemandeConge>>([]);
        public Task<IEnumerable<DemandeConge>> GetByManagerIdAsync(
            Guid managerId,
            IEnumerable<string>? validationNodeIds = null,
            CancellationToken ct = default)
            => Task.FromResult<IEnumerable<DemandeConge>>([]);
        public Task<IEnumerable<DemandeConge>> GetByStatutAsync(StatutDemande statut, CancellationToken ct = default) => Task.FromResult<IEnumerable<DemandeConge>>([]);
        public Task<IEnumerable<DemandeConge>> GetHistoriqueAsync(Guid employeId, int annee, CancellationToken ct = default) => Task.FromResult<IEnumerable<DemandeConge>>([]);
        public Task<IEnumerable<DemandeConge>> GetByAnneeAsync(int annee, CancellationToken ct = default) => Task.FromResult<IEnumerable<DemandeConge>>([]);
        public Task AddAsync(DemandeConge demande, CancellationToken ct = default) { Occupying.Add(demande); return Task.CompletedTask; }
        public void Update(DemandeConge demande) { }
        public Task<bool> ExistsCongeEnChevauchementAsync(Guid employeId, DateTime debut, DateTime fin, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<DemandeConge>> GetOccupyingQuotaAsync(
            IEnumerable<Guid> employeIds, DateTime debut, DateTime fin, Guid? excludeDemandeId = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DemandeConge>>(
                Occupying.Where(d =>
                    employeIds.Contains(d.EmployeId) &&
                    CongeQuotaStatuts.Occupants.Contains(d.Statut) &&
                    d.DateDebut <= fin && d.DateFin >= debut &&
                    (!excludeDemandeId.HasValue || d.Id != excludeDemandeId.Value)).ToList());
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
            Guid managerId,
            IReadOnlyList<string>? orgNodeIds,
            CancellationToken ct = default)
        {
            var nodes = orgNodeIds?
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<EmployeSnapshot> q = Employees;
            if (nodes.Count == 0)
                q = q.Where(e => e.ManagerId == managerId);
            else
                q = q.Where(e =>
                    e.ManagerId == managerId
                    || (e.CelluleId != null && nodes.Contains(e.CelluleId))
                    || (e.OrgServiceId != null && nodes.Contains(e.OrgServiceId))
                    || (e.PoleId != null && nodes.Contains(e.PoleId))
                    || (e.ServiceId != Guid.Empty && nodes.Contains(e.ServiceId.ToString())));
            return Task.FromResult<IReadOnlyList<EmployeSnapshot>>(q.ToList());
        }
        public Task<IReadOnlyList<EmployeSnapshot>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmployeSnapshot>>(Employees.ToList());
        public Task<IReadOnlyList<EmployeSnapshot>> GetByServiceIdAsync(Guid serviceId, CancellationToken ct = default)
            => GetByOrgNodeIdAsync(serviceId.ToString(), ct);
        public Task<IReadOnlyList<EmployeSnapshot>> GetByOrgNodeIdAsync(string orgNodeId, CancellationToken ct = default)
        {
            var id = QuotaCongeService.NormalizeNodeId(orgNodeId) ?? "";
            Guid.TryParse(id, out var asGuid);
            return Task.FromResult<IReadOnlyList<EmployeSnapshot>>(
                Employees.Where(e =>
                    (asGuid != Guid.Empty && e.ServiceId == asGuid)
                    || (e.OrgServiceId != null && string.Equals(e.OrgServiceId, id, StringComparison.OrdinalIgnoreCase))
                    || (e.CelluleId != null && string.Equals(e.CelluleId, id, StringComparison.OrdinalIgnoreCase))).ToList());
        }
        public Task AddAsync(EmployeSnapshot employe, CancellationToken ct = default) { Employees.Add(employe); return Task.CompletedTask; }
        public void Update(EmployeSnapshot employe) { }
        public void Remove(EmployeSnapshot employe) => Employees.Remove(employe);
        public Task<EmployeSnapshot?> GetAdminOuRhAsync(CancellationToken ct = default) => Task.FromResult<EmployeSnapshot?>(null);
        public Task<bool> ExistsAsync(Guid employeId, CancellationToken ct = default) => Task.FromResult(Employees.Any(e => e.EmployeId == employeId));
    }

    [Fact]
    public async Task Quota_plein_refuse_nouvelle_periode()
    {
        var serviceId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        var demandeRepo = new FakeDemandeRepo();
        var quotaRepo = new FakeQuotaRepo
        {
            Quota = QuotaCongeService.Creer(serviceId.ToString(), 1)
        };

        var e1 = EmployeSnapshot.Creer(Guid.NewGuid(), "A", "A", "a@t.com", managerId, serviceId, "S", DateTime.UtcNow.AddYears(-2));
        var e2 = EmployeSnapshot.Creer(Guid.NewGuid(), "B", "B", "b@t.com", managerId, serviceId, "S", DateTime.UtcNow.AddYears(-2));
        empRepo.Employees.AddRange([e1, e2]);

        var debut = new DateTime(An, 11, 9); // futur, hors mois interdits
        var fin = new DateTime(An, 11, 11);
        var solde = SoldeConge.Initialiser(e1.EmployeId, 20, An);
        var existing = DemandeConge.CreerCongeAnnuel(
            e1.EmployeId, managerId, debut, fin, solde, e1,
            statutInitial: StatutDemande.EnAttenteRh);
        demandeRepo.Occupying.Add(existing);

        var regles = new CongeReglesService(new FakePeriodeRepo(), quotaRepo, demandeRepo, empRepo);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            regles.AssertQuotaServiceDisponibleAsync(serviceId, debut, fin));
    }

    /// <summary>v11.4 : sans quota configuré, un jour déjà pris par un collègue n'est plus disponible.</summary>
    [Fact]
    public async Task Sans_quota_un_jour_pris_est_bloque_pour_les_autres()
    {
        var serviceId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        var demandeRepo = new FakeDemandeRepo();
        var quotaRepo = new FakeQuotaRepo();

        var e1 = EmployeSnapshot.Creer(Guid.NewGuid(), "A", "A", "a@t.com", managerId, serviceId, "S", DateTime.UtcNow.AddYears(-2));
        var e2 = EmployeSnapshot.Creer(Guid.NewGuid(), "B", "B", "b@t.com", managerId, serviceId, "S", DateTime.UtcNow.AddYears(-2));
        empRepo.Employees.AddRange([e1, e2]);

        var debut = new DateTime(An, 11, 23);
        var fin = new DateTime(An, 11, 25);
        var solde = SoldeConge.Initialiser(e1.EmployeId, 20, An);
        demandeRepo.Occupying.Add(DemandeConge.CreerCongeAnnuel(e1.EmployeId, managerId, debut, fin, solde, e1));

        var regles = new CongeReglesService(new FakePeriodeRepo(), quotaRepo, demandeRepo, empRepo);
        await Assert.ThrowsAsync<InvalidOperationException>(() => regles.AssertQuotaForEmployeAsync(e2, debut, fin));

        var mois = await regles.EvaluerMoisAsync(e2.EmployeId, An, 11);
        Assert.Equal(CongeReglesService.QuotaParDefaut, mois.Quota);
        Assert.True(mois.Jours.Single(j => j.Jour.Day == 24).Sature);
        Assert.False(mois.Jours.Single(j => j.Jour.Day == 20).Sature);
    }

    /// <summary>v9 : « déjà posé ou obtenu » — une demande en attente occupe sa place.</summary>
    [Fact]
    public async Task EnAttente_compte_dans_quota()
    {
        var serviceId = Guid.NewGuid();
        var managerId = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        var demandeRepo = new FakeDemandeRepo();
        var quotaRepo = new FakeQuotaRepo { Quota = QuotaCongeService.Creer(serviceId.ToString(), 1) };

        var e1 = EmployeSnapshot.Creer(Guid.NewGuid(), "A", "A", "a@t.com", managerId, serviceId, "S", DateTime.UtcNow.AddYears(-2));
        empRepo.Employees.Add(e1);

        var debut = new DateTime(An, 11, 16);
        var fin = new DateTime(An, 11, 18);
        var solde = SoldeConge.Initialiser(e1.EmployeId, 20, An);
        var pending = DemandeConge.CreerCongeAnnuel(e1.EmployeId, managerId, debut, fin, solde, e1);
        Assert.Equal(StatutDemande.EnAttente, pending.Statut);
        demandeRepo.Occupying.Add(pending);

        var regles = new CongeReglesService(new FakePeriodeRepo(), quotaRepo, demandeRepo, empRepo);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            regles.AssertQuotaServiceDisponibleAsync(serviceId, debut, fin));
    }

    /// <summary>Calendrier : jours saturés du mois, pour les griser avant la sélection.</summary>
    [Fact]
    public async Task Mois_signale_les_jours_satures()
    {
        var celluleId = "cell-ats";
        var managerId = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        var demandeRepo = new FakeDemandeRepo();
        var quotaRepo = new FakeQuotaRepo { Quota = QuotaCongeService.Creer(celluleId, 1, scopeKind: QuotaScopeKinds.Cellule) };

        var e1 = EmployeSnapshot.Creer(
            Guid.NewGuid(), "A", "A", "a@t.com", managerId, Guid.Empty, "S",
            DateTime.UtcNow.AddYears(-2), celluleId: celluleId);
        var e2 = EmployeSnapshot.Creer(
            Guid.NewGuid(), "B", "B", "b@t.com", managerId, Guid.Empty, "S",
            DateTime.UtcNow.AddYears(-2), celluleId: celluleId);
        empRepo.Employees.AddRange([e1, e2]);

        var debut = new DateTime(An, 11, 9);
        var fin = new DateTime(An, 11, 11);
        var solde = SoldeConge.Initialiser(e1.EmployeId, 20, An);
        demandeRepo.Occupying.Add(DemandeConge.CreerCongeAnnuel(e1.EmployeId, managerId, debut, fin, solde, e1));

        var regles = new CongeReglesService(new FakePeriodeRepo(), quotaRepo, demandeRepo, empRepo);
        var mois = await regles.EvaluerMoisAsync(e2.EmployeId, An, 11);

        Assert.Equal(1, mois.Quota);
        Assert.Equal(2, mois.Effectif);
        Assert.Equal(30, mois.Jours.Count);
        var satures = mois.Jours.Where(j => j.Sature).Select(j => j.Jour.Day).ToList();
        Assert.Equal(new[] { 9, 10, 11 }, satures);
        Assert.All(mois.Jours, j => Assert.False(j.DejaDemande));
    }

    [Fact]
    public async Task Mois_interdit_bloque()
    {
        var regles = new CongeReglesService(
            new FakePeriodeRepo(), new FakeQuotaRepo(), new FakeDemandeRepo(), new FakeEmployeRepo());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            regles.AssertHorsPeriodeInterditeAsync(new DateTime(2026, 9, 1), new DateTime(2026, 9, 5)));
    }

    [Fact]
    public async Task Quota_cellule_sature_retourne_joursSatures()
    {
        const string celluleId = "cell-abc123def456";
        var managerId = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        var demandeRepo = new FakeDemandeRepo();
        var quotaRepo = new FakeQuotaRepo
        {
            Quota = QuotaCongeService.Creer(celluleId, 1, null, QuotaScopeKinds.Cellule)
        };

        var e1 = EmployeSnapshot.Creer(
            Guid.NewGuid(), "A", "A", "a@t.com", managerId, Guid.Empty, "Préparation RDV",
            DateTime.UtcNow.AddYears(-2), false, "Employee", null, celluleId, null);
        var e2 = EmployeSnapshot.Creer(
            Guid.NewGuid(), "B", "B", "b@t.com", managerId, Guid.Empty, "Préparation RDV",
            DateTime.UtcNow.AddYears(-2), false, "Employee", null, celluleId, null);
        empRepo.Employees.AddRange([e1, e2]);

        var debut = new DateTime(An, 11, 9);
        var fin = new DateTime(An, 11, 11);
        var solde = SoldeConge.Initialiser(e1.EmployeId, 20, An);
        var existing = DemandeConge.CreerCongeAnnuel(
            e1.EmployeId, managerId, debut, fin, solde, e1,
            statutInitial: StatutDemande.EnAttenteRh);
        demandeRepo.Occupying.Add(existing);

        var regles = new CongeReglesService(new FakePeriodeRepo(), quotaRepo, demandeRepo, empRepo);
        var result = await regles.EvaluerDisponibiliteAsync(e2.EmployeId, debut, fin);

        Assert.False(result.Ok);
        Assert.Contains($"{An}-11-09", result.JoursSatures);
        Assert.Contains("cellule", result.Motif ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssertQuotaForEmploye_cellule_refuse_si_plein()
    {
        const string celluleId = "cell-xyz789abc012";
        var managerId = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        var demandeRepo = new FakeDemandeRepo();
        var quotaRepo = new FakeQuotaRepo
        {
            Quota = QuotaCongeService.Creer(celluleId, 1, null, QuotaScopeKinds.Cellule)
        };

        var e1 = EmployeSnapshot.Creer(
            Guid.NewGuid(), "A", "A", "a@t.com", managerId, Guid.Empty, "Cell",
            DateTime.UtcNow.AddYears(-2), false, "Employee", null, celluleId, null);
        var e2 = EmployeSnapshot.Creer(
            Guid.NewGuid(), "B", "B", "b@t.com", managerId, Guid.Empty, "Cell",
            DateTime.UtcNow.AddYears(-2), false, "Employee", null, celluleId, null);
        empRepo.Employees.AddRange([e1, e2]);

        var debut = new DateTime(An, 11, 23);
        var fin = new DateTime(An, 11, 24);
        var solde = SoldeConge.Initialiser(e1.EmployeId, 20, An);
        demandeRepo.Occupying.Add(DemandeConge.CreerCongeAnnuel(
            e1.EmployeId, managerId, debut, fin, solde, e1,
            statutInitial: StatutDemande.EnAttenteRh));

        var regles = new CongeReglesService(new FakePeriodeRepo(), quotaRepo, demandeRepo, empRepo);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            regles.AssertQuotaForEmployeAsync(e2, debut, fin));
    }
}
