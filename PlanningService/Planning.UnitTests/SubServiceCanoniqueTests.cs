using Microsoft.EntityFrameworkCore;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>v51 — Un service d'Organisation → un seul service Planning de référence, même en cas de doublon.</summary>
public class SubServiceCanoniqueTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = CreateDb();
        db.Roles.Add(new Role { Id = 1, Name = "Pilote" });
        db.Floors.Add(new Floor { Id = 1, Name = "F", FloorNumber = 1 });
        db.Services.Add(new Service { Id = 1, FloorId = 1, Name = "Cellule", Code = "C" });
        // Doublon : 10 et 11 portent le même service d'Organisation.
        db.SubServices.AddRange(
            new SubService { Id = 10, ServiceId = 1, Name = "Support technicien", Code = "ST1", PrimeServiceId = "svc-st" },
            new SubService { Id = 11, ServiceId = 1, Name = "Support technicien", Code = "ST2", PrimeServiceId = "SVC-ST" },
            new SubService { Id = 20, ServiceId = 1, Name = "Support VIP", Code = "VIP", PrimeServiceId = "svc-vip" });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Le_service_qui_porte_le_modele_de_shifts_est_la_reference()
    {
        await using var db = await SeedAsync();
        db.SubServiceShiftConfigs.Add(new SubServiceShiftConfig
        {
            Id = 1, SubServiceId = 11, Label = "8h", StartTime = new TimeOnly(8, 0), WorkHours = 8,
            RequiredCount = 2, DisplayOrder = 1, IsTemplate = true,
        });
        await db.SaveChangesAsync();

        Assert.Equal(11, await SubServiceCanonique.ResoudreAsync(db, "svc-st"));
        Assert.Equal(11, await SubServiceCanonique.ResoudreAsync(db, " SVC-ST "));
        Assert.Equal(20, await SubServiceCanonique.ResoudreAsync(db, "svc-vip"));
        Assert.Null(await SubServiceCanonique.ResoudreAsync(db, "inconnu"));
        Assert.Null(await SubServiceCanonique.ResoudreAsync(db, null));
    }

    [Fact]
    public async Task Sans_modele_le_plus_ancien_est_retenu_et_la_table_ne_plante_pas_sur_un_doublon()
    {
        await using var db = await SeedAsync();

        var table = await SubServiceCanonique.TableAsync(db);

        Assert.Equal(2, table.Count);
        Assert.Equal(10, table["svc-st"]);
        Assert.Equal(10, table["SVC-ST"]);
        Assert.Equal(20, table["svc-vip"]);
    }

    [Fact]
    public async Task Un_service_archive_n_est_jamais_la_reference()
    {
        await using var db = await SeedAsync();
        var archive = await db.SubServices.SingleAsync(s => s.Id == 10);
        archive.IsArchived = true;
        await db.SaveChangesAsync();

        Assert.Equal(11, await SubServiceCanoniqueTestsHelper.Resoudre(db, "svc-st"));
    }

    private static class SubServiceCanoniqueTestsHelper
    {
        public static Task<int?> Resoudre(AppDbContext db, string prime) =>
            SubServiceCanonique.ResoudreAsync(db, prime);
    }
}
