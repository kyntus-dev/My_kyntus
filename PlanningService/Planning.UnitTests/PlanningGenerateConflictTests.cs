using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planning.Application.DTOs.Planning;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Hubs;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>
/// La contrainte unique UserId+AssignedDate est globale : une regen doit
/// libérer les jours déjà posés dans un autre planning (mutation / import).
/// </summary>
public class PlanningGenerateConflictTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static PlanningService CreateService(AppDbContext db) =>
        new(db, new FakePlanningHubContext(), new PlanningPerimeterResolver(db));

    private static async Task SeedAsync(AppDbContext db)
    {
        db.Roles.Add(new Role { Id = 1, Name = "Pilote" });
        db.Floors.Add(new Floor { Id = 1, Name = "Floor 1", FloorNumber = 1 });
        db.Services.Add(new Service { Id = 1, FloorId = 1, Name = "Svc", Code = "S1" });
        db.SubServices.Add(new SubService { Id = 1, ServiceId = 1, Name = "BTE", Code = "BTE" });
        db.SubServices.Add(new SubService { Id = 2, ServiceId = 1, Name = "Autre", Code = "AUT" });
        db.Users.AddRange(
            new User
            {
                Id = 1, FirstName = "A", LastName = "One", Email = "a@test.ma",
                RoleId = 1, IsActive = true, SubServiceId = 1, Level = 2,
                PasswordHash = "x", HireDate = DateTime.UtcNow.AddYears(-1)
            },
            new User
            {
                Id = 2, FirstName = "B", LastName = "Two", Email = "b@test.ma",
                RoleId = 1, IsActive = true, SubServiceId = 1, Level = 2,
                PasswordHash = "x", HireDate = DateTime.UtcNow.AddYears(-1)
            });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Generate_steals_conflicting_days_from_other_planning()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
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

        var monday = new DateOnly(2026, 7, 20);
        var other = new WeeklyPlanning
        {
            SubServiceId = 2,
            WeekCode = "2026-W30",
            WeekStartDate = monday,
            TotalEffectif = 1,
            Status = PlanningStatus.Published,
            CreatedAt = DateTime.UtcNow
        };
        db.WeeklyPlannings.Add(other);
        await db.SaveChangesAsync();

        db.ShiftAssignments.Add(new ShiftAssignment
        {
            WeeklyPlanningId = other.Id,
            UserId = 1,
            AssignedDate = monday,
            DayOfWeek = DayOfWeekEnum.Monday
        });
        await db.SaveChangesAsync();

        var planning = await svc.CreatePlanningAsync(new CreateWeeklyPlanningDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeekStartDate = monday,
            TotalEffectif = 2
        });

        await svc.GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeeklyPlanningId = planning.Id
        });

        Assert.False(await db.ShiftAssignments.AnyAsync(
            a => a.WeeklyPlanningId == other.Id && a.UserId == 1 && a.AssignedDate == monday));
        Assert.True(await db.ShiftAssignments.AnyAsync(
            a => a.WeeklyPlanningId == planning.Id && a.UserId == 1 && a.AssignedDate == monday));
        Assert.True(await db.ShiftAssignments.AnyAsync(
            a => a.WeeklyPlanningId == planning.Id && a.UserId == 2 && a.AssignedDate == monday));
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
