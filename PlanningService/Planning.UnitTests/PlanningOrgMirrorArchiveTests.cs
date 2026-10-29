using Kyntus.Messaging.Contracts;
using Microsoft.EntityFrameworkCore;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>
/// Réorganisation 2026 — nœuds supprimés dans l'Organisation RH : masqués dans Planning,
/// jamais supprimés (les plannings publiés restent rattachés).
/// </summary>
public class PlanningOrgMirrorArchiveTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(opts);
        db.Floors.AddRange(
            new Floor { Id = 1, Name = "Pôle client", FloorNumber = 1, PrimePoleId = "pole-client" },
            new Floor { Id = 2, Name = "Pôle technique", FloorNumber = 2, PrimePoleId = "pole-tech" });
        db.Services.AddRange(
            new Service { Id = 10, FloorId = 1, Name = "Cible Prio", Code = "CP", PrimeCelluleId = "cell-cible" },
            new Service { Id = 11, FloorId = 1, Name = "Support Client", Code = "SC", PrimeCelluleId = "cell-sc" },
            new Service { Id = 20, FloorId = 2, Name = "Support technicien", Code = "ST", PrimeCelluleId = "cell-st" });
        db.SubServices.AddRange(
            new SubService { Id = 100, ServiceId = 10, Name = "Cible Prio", Code = "CP1", PrimeServiceId = "svc-cible" },
            new SubService { Id = 110, ServiceId = 11, Name = "Support Client", Code = "SC1", PrimeServiceId = "svc-sc" },
            new SubService { Id = 200, ServiceId = 20, Name = "Support technicien", Code = "ST1", PrimeServiceId = "svc-st" });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Deleting_a_pole_hides_its_floor_and_descendants_without_deleting_them()
    {
        await using var db = CreateDb();

        var count = await PlanningOrgMirrorArchive.ArchiveAsync(db, OrgNodeLevel.Pole, "pole-client");

        Assert.Equal(5, count);
        Assert.Equal(2, await db.Floors.CountAsync());
        Assert.True((await db.Floors.SingleAsync(f => f.Id == 1)).IsArchived);
        Assert.False((await db.Floors.SingleAsync(f => f.Id == 2)).IsArchived);
        Assert.Equal([100, 110], await db.SubServices.Where(s => s.IsArchived).Select(s => s.Id).OrderBy(x => x).ToListAsync());
    }

    [Fact]
    public async Task Deleting_a_cellule_hides_only_that_branch()
    {
        await using var db = CreateDb();

        await PlanningOrgMirrorArchive.ArchiveAsync(db, OrgNodeLevel.Cellule, "cell-cible");

        Assert.Equal([10], await db.Services.Where(s => s.IsArchived).Select(s => s.Id).ToListAsync());
        Assert.Equal([100], await db.SubServices.Where(s => s.IsArchived).Select(s => s.Id).ToListAsync());
        Assert.False((await db.Floors.SingleAsync(f => f.Id == 1)).IsArchived);
    }

    [Fact]
    public async Task Archiving_is_idempotent()
    {
        await using var db = CreateDb();

        await PlanningOrgMirrorArchive.ArchiveAsync(db, OrgNodeLevel.Service, "svc-sc");
        var second = await PlanningOrgMirrorArchive.ArchiveAsync(db, OrgNodeLevel.Service, "svc-sc");

        Assert.Equal(0, second);
        Assert.True((await db.SubServices.SingleAsync(s => s.Id == 110)).IsArchived);
    }

    [Fact]
    public async Task Planning_lists_no_longer_show_archived_nodes()
    {
        await using var db = CreateDb();
        await PlanningOrgMirrorArchive.ArchiveAsync(db, OrgNodeLevel.Pole, "pole-client");

        var floors = await new FloorService(db).GetAllFloorsAsync();
        var services = await new ServiceService(db).GetAllServicesAsync();
        var subServices = await new SubServiceService(db).GetAllSubServicesAsync();

        Assert.Equal(["Pôle technique"], floors.Select(f => f.Name).ToArray());
        Assert.Equal([20], services.Select(s => s.Id).ToArray());
        Assert.Equal([200], subServices.Select(s => s.Id).ToArray());
    }
}
