using Conge.Application.Abstractions;
using Conge.Application.Queries.GetSoldeByEmploye;
using Conge.Application.Services;
using Conge.Domain.Entities;
using Conge.Domain.Exceptions;
using Conge.Domain.Interfaces;
using Xunit;

namespace Conge.UnitTests;

public class EmployeAccessResolverTests
{
    [Fact]
    public void Guid_planning_inconnu_mais_email_acteur_matche_est_soi_meme()
    {
        var actorId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        var planningGuid = Guid.NewGuid();
        var snap = EmployeSnapshot.Creer(
            snapshotId, "Doe", "Jane", "jane@kyntus.ma",
            Guid.Empty, Guid.Empty, "", DateTime.UtcNow.AddYears(-2));

        Assert.True(EmployeAccessResolver.IsSelf(
            planningGuid, actorId, "jane@kyntus.ma", requestedSnap: null, actorSnap: snap));
        Assert.Equal(snapshotId, EmployeAccessResolver.CanonicalId(planningGuid, null, snap));
    }

    [Fact]
    public void Guid_d_un_autre_employe_avec_snapshot_n_est_pas_soi_meme()
    {
        var actorId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var actor = EmployeSnapshot.Creer(
            actorId, "Moi", "Moi", "moi@kyntus.ma",
            Guid.Empty, Guid.Empty, "", DateTime.UtcNow.AddYears(-2));
        var other = EmployeSnapshot.Creer(
            otherId, "Autre", "Personne", "autre@kyntus.ma",
            Guid.Empty, Guid.Empty, "", DateTime.UtcNow.AddYears(-2));

        Assert.False(EmployeAccessResolver.IsSelf(
            otherId, actorId, "moi@kyntus.ma", requestedSnap: other, actorSnap: actor));
    }

    [Fact]
    public void Requested_id_egal_acteur_est_soi_meme()
    {
        var id = Guid.NewGuid();
        Assert.True(EmployeAccessResolver.IsSelf(id, id, null, null, null));
        Assert.Equal(id, EmployeAccessResolver.CanonicalId(id, null, null));
    }
}

public class EmployeSnapshotEnsureTests
{
    [Fact]
    public async Task Retourne_snapshot_existant_sans_directory()
    {
        var id = Guid.NewGuid();
        var existing = EmployeSnapshot.Creer(
            id, "Doe", "Jane", "j@test.com",
            Guid.Empty, Guid.Empty, "", DateTime.UtcNow.AddYears(-1));
        var empRepo = new FakeEmployeRepo();
        empRepo.Employees.Add(existing);
        var ensure = CreateEnsure(empRepo, directory: new FakeDirectoryLookup());

        var result = await ensure.EnsureAsync(new EmployeSnapshotEnsureRequest(
            id, "j@test.com", "Employee", id, AllowJwtStub: false));

        Assert.Same(existing, result);
        Assert.Single(empRepo.Employees);
    }

    [Fact]
    public async Task Directory_rafraichit_date_embauche_stub()
    {
        var id = Guid.NewGuid();
        var stubHire = DateTime.UtcNow;
        var realHire = DateTime.UtcNow.AddYears(-2);
        var existing = EmployeSnapshot.Creer(
            id, "Doe", "Jane", "j@test.com",
            Guid.Empty, Guid.Empty, "", stubHire);
        var empRepo = new FakeEmployeRepo();
        empRepo.Employees.Add(existing);
        var lookup = new FakeDirectoryLookup
        {
            ById = new DirectoryEmployeeRecord(
                id, "Jane", "Doe", "j@test.com", "Superviseur",
                null, null, "svc-1", null, null, null,
                realHire, false)
        };
        var ensure = CreateEnsure(empRepo, directory: lookup);

        var result = await ensure.EnsureAsync(new EmployeSnapshotEnsureRequest(
            id, "j@test.com", "Superviseur", id, AllowJwtStub: false));

        Assert.NotNull(result);
        Assert.Equal(realHire, result!.DateEmbauche);
        Assert.True(result.EstEligibleCongeAnnuel());
    }

    [Fact]
    public async Task Directory_date_embauche_unspecified_est_stockee_en_utc()
    {
        var id = Guid.NewGuid();
        var existing = EmployeSnapshot.Creer(
            id, "Doe", "Jane", "j@test.com",
            Guid.Empty, Guid.Empty, "", DateTime.UtcNow);
        var empRepo = new FakeEmployeRepo();
        empRepo.Employees.Add(existing);
        var unspecifiedHire = new DateTime(2020, 3, 15, 0, 0, 0, DateTimeKind.Unspecified);
        var lookup = new FakeDirectoryLookup
        {
            ById = new DirectoryEmployeeRecord(
                id, "Jane", "Doe", "j@test.com", "Pilote",
                null, null, "svc-1", null, null, null,
                unspecifiedHire, false)
        };
        var ensure = CreateEnsure(empRepo, directory: lookup);

        var result = await ensure.EnsureAsync(new EmployeSnapshotEnsureRequest(
            id, "j@test.com", "Pilote", id, AllowJwtStub: false));

        Assert.NotNull(result);
        Assert.Equal(DateTimeKind.Utc, result!.DateEmbauche.Kind);
        Assert.Equal(new DateTime(2020, 3, 15, 0, 0, 0, DateTimeKind.Utc), result.DateEmbauche);
    }

    [Fact]
    public async Task Directory_par_id_cree_snapshot_et_solde()
    {
        var id = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        var soldeRepo = new FakeSoldeRepo();
        var lookup = new FakeDirectoryLookup
        {
            ById = new DirectoryEmployeeRecord(
                id, "Jane", "Doe", "jane@kyntus.ma", "Pilote",
                null, null, "svc-1", "pole-1", "cell-1", null,
                DateTime.UtcNow.AddYears(-3), false)
        };
        var ensure = CreateEnsure(empRepo, soldeRepo, lookup);

        var result = await ensure.EnsureAsync(new EmployeSnapshotEnsureRequest(
            id, "jane@kyntus.ma", "Pilote", id, AllowJwtStub: false));

        Assert.NotNull(result);
        Assert.Equal(id, result!.EmployeId);
        Assert.Equal("Doe", result.Nom);
        Assert.Single(empRepo.Employees);
        Assert.Single(soldeRepo.Items);
        Assert.Equal(id, soldeRepo.Items[0].EmployeId);
        Assert.True(soldeRepo.Items[0].SoldeInitial > 0);
    }

    [Fact]
    public async Task Directory_par_email_utilise_le_guid_directory()
    {
        var planningId = Guid.NewGuid();
        var directoryId = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        var lookup = new FakeDirectoryLookup
        {
            ByEmail = new DirectoryEmployeeRecord(
                directoryId, "Jane", "Doe", "jane@kyntus.ma", "Pilote",
                null, null, null, null, null, null,
                DateTime.UtcNow.AddYears(-2), false)
        };
        var ensure = CreateEnsure(empRepo, directory: lookup);

        var result = await ensure.EnsureAsync(new EmployeSnapshotEnsureRequest(
            planningId, "jane@kyntus.ma", "Pilote", planningId, AllowJwtStub: true));

        Assert.NotNull(result);
        Assert.Equal(directoryId, result!.EmployeId);
        Assert.NotEqual(planningId, result.EmployeId);
    }

    [Fact]
    public async Task Stub_jwt_quand_directory_absent()
    {
        var id = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        var soldeRepo = new FakeSoldeRepo();
        var ensure = CreateEnsure(empRepo, soldeRepo, new FakeDirectoryLookup());

        var result = await ensure.EnsureAsync(new EmployeSnapshotEnsureRequest(
            id, "jean.dupont@kyntus.ma", "Employee", id, AllowJwtStub: true));

        Assert.NotNull(result);
        Assert.Equal(id, result!.EmployeId);
        Assert.Equal("Dupont", result.Nom);
        Assert.Equal("Jean", result.Prenom);
        Assert.Single(soldeRepo.Items);
        Assert.True(soldeRepo.Items[0].SoldeRestant > 0);
    }

    [Fact]
    public async Task Directory_par_email_sans_stub_quand_guid_planning_differe()
    {
        var planningId = Guid.NewGuid();
        var authSub = Guid.NewGuid();
        var directoryId = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        var lookup = new FakeDirectoryLookup
        {
            ByEmail = new DirectoryEmployeeRecord(
                directoryId, "Issal", "Damaliou", "damaliouissal@kyntus.ma", "Employee",
                null, null, null, null, null, null,
                DateTime.UtcNow.AddYears(-1), false)
        };
        var ensure = CreateEnsure(empRepo, directory: lookup);

        var result = await ensure.EnsureAsync(new EmployeSnapshotEnsureRequest(
            planningId, "damaliouissal@kyntus.ma", "Employee", authSub, AllowJwtStub: false));

        Assert.NotNull(result);
        Assert.Equal(directoryId, result!.EmployeId);
        Assert.True(EmployeAccessResolver.IsSelf(
            planningId, authSub, "damaliouissal@kyntus.ma", requestedSnap: null, actorSnap: result));
    }

    [Fact]
    public async Task Sans_stub_et_sans_directory_retourne_null()
    {
        var ensure = CreateEnsure(new FakeEmployeRepo(), directory: new FakeDirectoryLookup());
        var result = await ensure.EnsureAsync(new EmployeSnapshotEnsureRequest(
            Guid.NewGuid(), "x@y.z", "Employee", Guid.NewGuid(), AllowJwtStub: false));
        Assert.Null(result);
    }

    [Fact]
    public async Task GetSolde_sans_snapshot_cree_via_ensure()
    {
        var id = Guid.NewGuid();
        var empRepo = new FakeEmployeRepo();
        var soldeRepo = new FakeSoldeRepo();
        var uow = new FakeUnitOfWork();
        var ensure = CreateEnsure(empRepo, soldeRepo, new FakeDirectoryLookup());
        await ensure.EnsureAsync(new EmployeSnapshotEnsureRequest(
            id, "a@b.c", "Employee", id, AllowJwtStub: true));

        var handler = new GetSoldeByEmployeHandler(soldeRepo, empRepo, uow, ensure);
        var dto = await handler.Handle(new GetSoldeByEmployeQuery(id), CancellationToken.None);

        Assert.Equal(id, dto.EmployeId);
        Assert.True(dto.SoldeInitial > 0);
        Assert.Equal(dto.SoldeInitial, dto.SoldeRestant);
    }

    [Fact]
    public async Task GetSolde_sans_snapshot_ni_ensure_leve_404_metier()
    {
        var id = Guid.NewGuid();
        var handler = new GetSoldeByEmployeHandler(
            new FakeSoldeRepo(),
            new FakeEmployeRepo(),
            new FakeUnitOfWork(),
            CreateEnsure(new FakeEmployeRepo(), directory: new FakeDirectoryLookup()));

        await Assert.ThrowsAsync<SoldeNotFoundException>(
            () => handler.Handle(new GetSoldeByEmployeQuery(id), CancellationToken.None));
    }

    private static EmployeSnapshotEnsure CreateEnsure(
        FakeEmployeRepo empRepo,
        FakeSoldeRepo? soldeRepo = null,
        IDirectoryEmployeeLookup? directory = null)
        => new(
            empRepo,
            soldeRepo ?? new FakeSoldeRepo(),
            new FakeUnitOfWork(),
            directory ?? new FakeDirectoryLookup());

    private sealed class FakeDirectoryLookup : IDirectoryEmployeeLookup
    {
        public DirectoryEmployeeRecord? ById { get; set; }
        public DirectoryEmployeeRecord? ByEmail { get; set; }

        public Task<DirectoryEmployeeRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(ById is not null && ById.Id == id ? ById : null);

        public Task<DirectoryEmployeeRecord?> GetByEmailAsync(string email, CancellationToken ct = default)
            => Task.FromResult(
                ByEmail is not null
                && string.Equals(ByEmail.Email, email, StringComparison.OrdinalIgnoreCase)
                    ? ByEmail
                    : null);
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
        {
            var byId = Employees.FirstOrDefault(e => e.EmployeId == employeId);
            if (byId is not null) return Task.FromResult<EmployeSnapshot?>(byId);
            if (string.IsNullOrWhiteSpace(email)) return Task.FromResult<EmployeSnapshot?>(null);
            return Task.FromResult(Employees.FirstOrDefault(e =>
                string.Equals(e.Email, email, StringComparison.OrdinalIgnoreCase)));
        }

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
}
