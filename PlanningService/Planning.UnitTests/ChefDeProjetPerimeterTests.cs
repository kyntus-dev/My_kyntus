using Kyntus.Iam;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>
/// Réorganisation 2026 — un Chef de projet couvre les sous-services des pôles qu'il détient
/// dans Employee Directory (avant : périmètre Planning vide pour ce rôle).
/// </summary>
public class ChefDeProjetPerimeterTests
{
    private static readonly Guid CpGuid = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid SupGuid = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid RefGuid = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003");
    private static readonly Guid PiloteGuid = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004");

    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(opts);
        db.Roles.AddRange(
            new Role { Id = 1, Name = "Chef de projet" },
            new Role { Id = 2, Name = "Superviseur" },
            new Role { Id = 3, Name = "Pilote" },
            new Role { Id = 4, Name = "Référent technique" });
        db.Floors.AddRange(
            new Floor { Id = 1, Name = "pole technique", FloorNumber = 1, PrimePoleId = "pole-tech" },
            new Floor { Id = 2, Name = "pole BTB", FloorNumber = 2, PrimePoleId = "pole-btb" },
            new Floor { Id = 3, Name = "Appels Post", FloorNumber = 3, PrimePoleId = "pole-post" });
        db.Services.AddRange(
            new Service { Id = 1, FloorId = 1, Name = "Support technicien", Code = "ST", PrimeCelluleId = "cell-st" },
            new Service { Id = 2, FloorId = 1, Name = "Prestations réseaux & CV", Code = "CV", PrimeCelluleId = "cell-cv" },
            new Service { Id = 3, FloorId = 2, Name = "BTB", Code = "BTB", PrimeCelluleId = "cell-btb" },
            new Service { Id = 4, FloorId = 3, Name = "INEO", Code = "INEO", PrimeCelluleId = "cell-ineo" });
        db.SubServices.AddRange(
            new SubService { Id = 11, ServiceId = 1, Name = "Support technicien", Code = "ST", PrimeServiceId = "svc-st" },
            new SubService { Id = 12, ServiceId = 2, Name = "Prestations réseaux & CV", Code = "CV", PrimeServiceId = "svc-cv" },
            new SubService { Id = 13, ServiceId = 3, Name = "BTB", Code = "BTB" },
            new SubService { Id = 14, ServiceId = 4, Name = "INEO", Code = "INEO" });
        db.Users.AddRange(
            new User { Id = 100, Guid = CpGuid, RoleId = 1, FirstName = "Chef", LastName = "Projet", Email = "cp@test.local" },
            new User { Id = 200, Guid = SupGuid, RoleId = 2, FirstName = "Super", LastName = "Viseur", Email = "sup@test.local" },
            new User { Id = 300, Guid = RefGuid, RoleId = 4, FirstName = "Ref", LastName = "Technique", Email = "ref@test.local" },
            new User { Id = 400, Guid = PiloteGuid, RoleId = 3, FirstName = "Pi", LastName = "Lote", Email = "pilote@test.local" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Chef_de_projet_covers_sub_services_of_his_directory_poles()
    {
        using var db = CreateDb();
        var resolver = new PlanningPerimeterResolver(db, null, FakeRebac.Cp(["pole-tech", "pole-btb"]));

        var ids = await resolver.GetManagedSubServiceIdsAsync(100);

        Assert.Equal([11, 12, 13], ids.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Chef_de_projet_without_directory_pole_keeps_empty_perimeter()
    {
        using var db = CreateDb();
        var resolver = new PlanningPerimeterResolver(db, null, FakeRebac.Cp([]));

        Assert.Empty(await resolver.GetManagedSubServiceIdsAsync(100));
    }

    [Fact]
    public async Task Directory_failure_never_widens_the_perimeter()
    {
        using var db = CreateDb();
        var resolver = new PlanningPerimeterResolver(db, null, FakeRebac.Panne());

        Assert.Empty(await resolver.GetManagedSubServiceIdsAsync(100));
    }

    [Fact]
    public async Task Superviseur_couvre_ses_cellules_organisation_rh_en_plus_des_liens_planning()
    {
        using var db = CreateDb();
        db.UserManagedServices.Add(new UserManagedService { UserId = 200, ServiceId = 1 });
        db.SaveChanges();
        var rebac = new FakeRebac(new() { [(SupGuid, "Superviseur")] = ["cell-btb"] });
        var resolver = new PlanningPerimeterResolver(db, null, rebac);

        var ids = await resolver.GetManagedSubServiceIdsAsync(200);

        Assert.Equal(new[] { 11, 13 }, ids.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Superviseur_sans_lien_planning_garde_ses_cellules_apres_changement_de_role()
    {
        // Cas signalé : ancien référent technique passé superviseur, fiche réenregistrée (liens
        // Planning effacés) ; les cellules viennent d'Organisation RH.
        using var db = CreateDb();
        var rebac = new FakeRebac(new() { [(SupGuid, "Superviseur")] = ["cell-st", "cell-cv"] });
        var resolver = new PlanningPerimeterResolver(db, null, rebac);

        var ids = await resolver.GetManagedSubServiceIdsAsync(200);

        Assert.Equal(new[] { 11, 12 }, ids.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Referent_technique_couvre_ses_services_organisation_rh()
    {
        using var db = CreateDb();
        var rebac = new FakeRebac(new() { [(RefGuid, "ReferentTechnique")] = ["svc-cv"] });
        var resolver = new PlanningPerimeterResolver(db, null, rebac);

        Assert.Equal(new[] { 12 }, (await resolver.GetManagedSubServiceIdsAsync(300)).ToArray());
    }

    [Fact]
    public async Task Pilote_ne_consulte_pas_l_annuaire()
    {
        using var db = CreateDb();
        var rebac = new FakeRebac(new() { [(PiloteGuid, "Superviseur")] = ["cell-st"] });
        var resolver = new PlanningPerimeterResolver(db, null, rebac);

        Assert.Empty(await resolver.GetManagedSubServiceIdsAsync(400));
        Assert.Equal(0, rebac.Calls);
    }

    [Fact]
    public async Task Interrupteur_desactive_le_perimetre_annuaire_du_superviseur()
    {
        using var db = CreateDb();
        db.UserManagedServices.Add(new UserManagedService { UserId = 200, ServiceId = 1 });
        db.SaveChanges();
        var rebac = new FakeRebac(new() { [(SupGuid, "Superviseur")] = ["cell-btb"] });
        var resolver = new PlanningPerimeterResolver(db, null, rebac,
            new FakeConfiguration("Planning:PerimetreDepuisAnnuaire", "false"));

        Assert.Equal(new[] { 11 }, (await resolver.GetManagedSubServiceIdsAsync(200)).ToArray());
        Assert.Equal(0, rebac.Calls);
    }

    [Fact]
    public async Task Feature_flag_disables_chef_de_projet_extension()
    {
        using var db = CreateDb();
        var resolver = new PlanningPerimeterResolver(
            db, null, FakeRebac.Cp(["pole-tech"]),
            new FakeConfiguration("Planning:ChefDeProjetPerimeterFromPoles", "false"));

        Assert.Empty(await resolver.GetManagedSubServiceIdsAsync(100));
    }

    private sealed class FakeRebac(Dictionary<(Guid, string), IReadOnlyList<string>>? noeuds) : IRebacClient
    {
        public static FakeRebac Cp(IReadOnlyList<string> poles) => new(new() { [(CpGuid, "ChefDeProjet")] = poles });
        public static FakeRebac Panne() => new(null);

        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> GetManagedNodeIdsAsync(Guid employeeId, string kind, CancellationToken ct = default)
        {
            Calls++;
            if (noeuds is null) throw new HttpRequestException("401");
            return Task.FromResult(noeuds.TryGetValue((employeeId, kind), out var n) ? n : Array.Empty<string>());
        }

        public Task<bool> IsDescendantAsync(Guid viewerId, Guid targetId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(Guid actorId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
        public Task<bool> CanActOnAsync(Guid actorId, Guid targetEmployeeId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<IReadOnlyList<Guid>> GetResponsibleIdsAsync(string kind, string nodeId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
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
}
