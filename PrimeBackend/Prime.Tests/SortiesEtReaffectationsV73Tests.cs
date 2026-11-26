using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;
using Xunit;

namespace PrimeBackend.Tests;

/// <summary>
/// v73 — Un agent désactivé ou sorti reste dans la base (historique des primes) mais est marqué
/// « sorti » : l'interface le grise et l'exclut de l'effectif planifiable. Un agent réaffecté suit
/// l'annuaire, encadrement compris, et n'apparaît plus que dans son nouveau service.
/// </summary>
public class SortiesEtReaffectationsV73Tests
{
    private static PrimeDbContext NouvelleBase() =>
        new(new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static EmployeeEntity Agent(string id, string role = "Employee", string? serviceId = "svc-a") =>
        new()
        {
            Id = id,
            FirstName = "A",
            LastName = "B",
            Email = $"{id}@kyntus.ma",
            Role = role,
            PoleId = "pole-1",
            CelluleId = "cell-1",
            ServiceId = serviceId,
        };

    [Fact]
    public async Task Un_agent_enregistre_est_actif_par_defaut()
    {
        await using var db = NouvelleBase();
        db.Employees.Add(Agent("emp-1"));
        await db.SaveChangesAsync();

        var employees = await new PrimeOrgScopeService(db).GetLegacyEmployeesAsync();

        Assert.Single(employees);
        Assert.True(employees[0].IsActive);
    }

    [Fact]
    public async Task Un_depart_marque_la_fiche_sortie_sans_la_supprimer()
    {
        await using var db = NouvelleBase();
        var id = Guid.NewGuid();
        db.Employees.Add(Agent(id.ToString()));
        await db.SaveChangesAsync();

        var sync = new EmployeeDirectorySyncService(db, NullLogger<EmployeeDirectorySyncService>.Instance);
        Assert.True(await sync.MarquerSortiAsync(id));

        // La fiche reste (historique des primes), mais elle est marquée sortie.
        var employees = await new PrimeOrgScopeService(db).GetLegacyEmployeesAsync();
        Assert.Single(employees);
        Assert.False(employees[0].IsActive);
    }

    [Fact]
    public async Task Un_depart_sur_un_agent_inconnu_ne_casse_rien()
    {
        await using var db = NouvelleBase();
        var sync = new EmployeeDirectorySyncService(db, NullLogger<EmployeeDirectorySyncService>.Instance);

        Assert.False(await sync.MarquerSortiAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Le_retour_d_un_agent_le_reactive()
    {
        await using var db = NouvelleBase();
        var id = Guid.NewGuid();
        var agent = Agent(id.ToString());
        agent.IsActive = false;
        db.Employees.Add(agent);
        await db.SaveChangesAsync();

        var sync = new EmployeeDirectorySyncService(db, NullLogger<EmployeeDirectorySyncService>.Instance);
        await sync.EnsureFromPlanningAsync(new EmployeeDirectoryUpsertRequest(
            EmployeeId: id,
            FirstName: "A",
            LastName: "B",
            Email: $"{id}@kyntus.ma",
            Role: "Employee"));

        var recharge = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == id.ToString());
        Assert.True(recharge.IsActive);
    }

    [Fact]
    public async Task Un_superviseur_reaffecte_suit_l_annuaire()
    {
        await using var db = NouvelleBase();
        db.Poles.Add(new PoleEntity { Id = "pole-2", Name = "Pôle 2" });
        db.Cellules.Add(new CelluleEntity { Id = "cell-2", Name = "Cellule 2", PoleId = "pole-2" });
        db.Services.Add(new ServiceEntity { Id = "svc-b", Name = "Service B", CelluleId = "cell-2" });
        var id = Guid.NewGuid();
        db.Employees.Add(Agent(id.ToString(), role: "Superviseur", serviceId: "svc-a"));
        await db.SaveChangesAsync();

        var sync = new EmployeeDirectorySyncService(db, NullLogger<EmployeeDirectorySyncService>.Instance);
        await sync.EnsureFromPlanningAsync(new EmployeeDirectoryUpsertRequest(
            EmployeeId: id,
            FirstName: "A",
            LastName: "B",
            Email: $"{id}@kyntus.ma",
            Role: "Superviseur",
            PrimeServiceId: "svc-b"));

        // Avant la v73 : le rattachement de l'encadrement était gelé et restait sur svc-a.
        var recharge = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == id.ToString());
        Assert.Equal("svc-b", recharge.ServiceId);
        Assert.Equal("cell-2", recharge.CelluleId);
    }

    [Fact]
    public async Task Sans_rattachement_dans_le_message_l_actuel_est_conserve()
    {
        await using var db = NouvelleBase();
        var id = Guid.NewGuid();
        db.Employees.Add(Agent(id.ToString(), role: "Superviseur", serviceId: "svc-a"));
        await db.SaveChangesAsync();

        var sync = new EmployeeDirectorySyncService(db, NullLogger<EmployeeDirectorySyncService>.Instance);
        await sync.EnsureFromPlanningAsync(new EmployeeDirectoryUpsertRequest(
            EmployeeId: id,
            FirstName: "A",
            LastName: "B",
            Email: $"{id}@kyntus.ma",
            Role: "Superviseur"));

        var recharge = await db.Employees.AsNoTracking().FirstAsync(e => e.Id == id.ToString());
        Assert.Equal("svc-a", recharge.ServiceId);
    }
}
