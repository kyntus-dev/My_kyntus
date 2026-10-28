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

    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(opts);
        db.Roles.AddRange(
            new Role { Id = 1, Name = "Chef de projet" },
            new Role { Id = 2, Name = "Superviseur" },
            new Role { Id = 3, Name = "Pilote" });
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
            new SubService { Id = 11, ServiceId = 1, Name = "Support technicien", Code = "ST" },
            new SubService { Id = 12, ServiceId = 2, Name = "Prestations réseaux & CV", Code = "CV" },
            new SubService { Id = 13, ServiceId = 3, Name = "BTB", Code = "BTB" },
            new SubService { Id = 14, ServiceId = 4, Name = "INEO", Code = "INEO" });
        db.Users.AddRange(
            new User { Id = 100, Guid = CpGuid, RoleId = 1, FirstName = "Chef", LastName = "Projet", Email = "cp@test.local" },
            new User { Id = 200, Guid = SupGuid, RoleId = 2, FirstName = "Super", LastName = "Viseur", Email = "sup@test.local" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Chef_de_projet_covers_sub_services_of_his_directory_poles()
    {
        using var db = CreateDb();
        var resolver = new PlanningPerimeterResolver(db, null, new FakeRebac(["pole-tech", "pole-btb"]));

        var ids = await resolver.GetManagedSubServiceIdsAsync(100);

        Assert.Equal([11, 12, 13], ids.OrderBy(x => x).ToArray());
    }

    [Fact]
    public async Task Chef_de_projet_without_directory_pole_keeps_empty_perimeter()
    {
        using var db = CreateDb();
        var resolver = new PlanningPerimeterResolver(db, null, new FakeRebac([]));

        Assert.Empty(await resolver.GetManagedSubServiceIdsAsync(100));
    }

    [Fact]
    public async Task Directory_failure_never_widens_the_perimeter()
    {
        using var db = CreateDb();
        var resolver = new PlanningPerimeterResolver(db, null, new FakeRebac(null));

        Assert.Empty(await resolver.GetManagedSubServiceIdsAsync(100));
    }

    [Fact]
    public async Task Superviseur_perimeter_is_unchanged_and_does_not_query_poles()
    {
        using var db = CreateDb();
        db.UserManagedServices.Add(new UserManagedService { UserId = 200, ServiceId = 1 });
        db.SaveChanges();
        var rebac = new FakeRebac(["pole-tech", "pole-btb", "pole-post"]);
        var resolver = new PlanningPerimeterResolver(db, null, rebac);

        var ids = await resolver.GetManagedSubServiceIdsAsync(200);

        Assert.Equal([11], ids.ToArray());
        Assert.Equal(0, rebac.Calls);
    }

    [Fact]
    public async Task Feature_flag_disables_chef_de_projet_extension()
    {
        using var db = CreateDb();
        var resolver = new PlanningPerimeterResolver(
            db, null, new FakeRebac(["pole-tech"]),
            new FakeConfiguration("Planning:ChefDeProjetPerimeterFromPoles", "false"));

        Assert.Empty(await resolver.GetManagedSubServiceIdsAsync(100));
    }

    private sealed class FakeRebac(IReadOnlyList<string>? poles) : IRebacClient
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<string>> GetManagedNodeIdsAsync(Guid employeeId, string kind, CancellationToken ct = default)
        {
            Calls++;
            if (poles is null) throw new HttpRequestException("401");
            Assert.Equal(CpGuid, employeeId);
            Assert.Equal("ChefDeProjet", kind);
            return Task.FromResult(poles);
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
