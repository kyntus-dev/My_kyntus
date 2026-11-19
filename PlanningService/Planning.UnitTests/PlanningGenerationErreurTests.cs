using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planning.Application.DTOs.Planning;
using Planning.Domain.Entities;
using Planning.Infrastructure.Hubs;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>
/// v51 — Une grille vide n'est jamais muette : la raison d'un échec de génération reste attachée
/// au planning, et disparaît à la génération suivante réussie.
/// </summary>
public class PlanningGenerationErreurTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static PlanningService CreateService(AppDbContext db) =>
        new(db, new FakePlanningHubContext(), new PlanningPerimeterResolver(db));

    private static User Pilote(int id) => new()
    {
        Id = id,
        FirstName = $"P{id}",
        LastName = "Test",
        Email = $"p{id}@test.ma",
        RoleId = 1,
        IsActive = true,
        SubServiceId = 1,
        Level = 2,
        PasswordHash = "x",
        HireDate = DateTime.UtcNow.AddYears(-1),
    };

    private static async Task<(AppDbContext Db, PlanningService Svc, int PlanningId)> PreparerAsync()
    {
        var db = CreateDb();
        db.Roles.Add(new Role { Id = 1, Name = "Pilote" });
        db.Floors.Add(new Floor { Id = 1, Name = "Floor 1", FloorNumber = 1 });
        db.Services.Add(new Service { Id = 1, FloorId = 1, Name = "Svc", Code = "S1" });
        db.SubServices.Add(new SubService { Id = 1, ServiceId = 1, Name = "Support technicien", Code = "ST" });
        db.Users.AddRange(Pilote(1), Pilote(2));
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await svc.SaveShiftTemplateAsync(new SaveShiftConfigDto
        {
            SubServiceId = 1,
            Shifts =
            [
                new ShiftConfigItemDto { Label = "8h", StartTime = "08:00", WorkHours = 8, RequiredCount = 1, DisplayOrder = 1 },
                new ShiftConfigItemDto { Label = "10h", StartTime = "10:00", WorkHours = 8, RequiredCount = 1, DisplayOrder = 2 },
            ]
        });

        var planning = await svc.CreatePlanningAsync(new CreateWeeklyPlanningDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeekStartDate = new DateOnly(2026, 7, 20),
            TotalEffectif = 2,
        });
        return (db, svc, planning.Id);
    }

    [Fact]
    public async Task Generation_sans_agent_refusee_et_raison_enregistree_v51()
    {
        var (db, svc, planningId) = await PreparerAsync();
        await using var _ = db;

        // Les agents quittent la cellule après la création du planning (le cas observé).
        foreach (var u in db.Users) u.IsActive = false;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
            {
                SubServiceId = 1,
                WeekCode = "2026-W30",
                WeeklyPlanningId = planningId,
            }));
        Assert.Contains("Aucun agent", ex.Message);

        var planning = await db.WeeklyPlannings.AsNoTracking().SingleAsync(p => p.Id == planningId);
        Assert.NotNull(planning.GenerationErreur);
        Assert.Contains("Aucun agent", planning.GenerationErreur);
        Assert.NotNull(planning.GenerationErreurLe);
        Assert.Empty(await db.ShiftAssignments.Where(a => a.WeeklyPlanningId == planningId).ToListAsync());
    }

    [Fact]
    public async Task Generation_reussie_efface_l_erreur_precedente_v51()
    {
        var (db, svc, planningId) = await PreparerAsync();
        await using var _ = db;

        var avant = await db.WeeklyPlannings.SingleAsync(p => p.Id == planningId);
        avant.GenerationErreur = "Le superviseur n'a pas encore configuré les modes.";
        avant.GenerationErreurLe = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var resultat = await svc.GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeeklyPlanningId = planningId,
        });

        Assert.Null(resultat.GenerationErreur);
        Assert.Equal(2, resultat.PlanifiableEmployeeCount);
        Assert.Equal(2, resultat.Assignments.Count);

        var apres = await db.WeeklyPlannings.AsNoTracking().SingleAsync(p => p.Id == planningId);
        Assert.Null(apres.GenerationErreur);
        Assert.Null(apres.GenerationErreurLe);
    }

    private sealed class FakePlanningHubContext : IHubContext<PlanningHub>
    {
        public IHubClients Clients { get; } = new FakeClients();
        public IGroupManager Groups { get; } = new FakeGroups();

        private sealed class FakeClients : IHubClients
        {
            public IClientProxy All => new FakeProxy();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new FakeProxy();
            public IClientProxy Client(string connectionId) => new FakeProxy();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new FakeProxy();
            public IClientProxy Group(string groupName) => new FakeProxy();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new FakeProxy();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => new FakeProxy();
            public IClientProxy User(string userId) => new FakeProxy();
            public IClientProxy Users(IReadOnlyList<string> userIds) => new FakeProxy();
        }

        private sealed class FakeGroups : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }

        private sealed class FakeProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
                => Task.CompletedTask;
        }
    }
}
