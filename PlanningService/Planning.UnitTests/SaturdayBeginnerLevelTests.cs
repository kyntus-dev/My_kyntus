using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planning.Application.DTOs.Planning;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Hubs;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

public class SaturdayBeginnerLevelTests
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

    private static async Task SeedBaseAsync(AppDbContext db)
    {
        db.Roles.Add(new Role { Id = 1, Name = "Pilote" });
        db.Floors.Add(new Floor { Id = 1, Name = "Floor 1", FloorNumber = 1 });
        db.Services.Add(new Service { Id = 1, FloorId = 1, Name = "Svc", Code = "S1" });
        db.SubServices.Add(new SubService { Id = 1, ServiceId = 1, Name = "Cellule A", Code = "CA" });
        await db.SaveChangesAsync();
    }

    private static User MakeUser(int id, string email, int level) => new()
    {
        Id = id,
        FirstName = $"U{id}",
        LastName = "Test",
        Email = email,
        RoleId = 1,
        IsActive = true,
        SubServiceId = 1,
        Level = level,
        PasswordHash = "x",
        HireDate = DateTime.UtcNow.AddYears(-1),
    };

    [Fact]
    public async Task Beginners_work_every_saturday_half_day_with_balanced_slots()
    {
        await using var db = CreateDb();
        await SeedBaseAsync(db);
        db.Users.AddRange(
            MakeUser(1, "b1@test.ma", level: 1),
            MakeUser(2, "b2@test.ma", level: 1),
            // Confirmés pour satisfaire l'équilibre des niveaux (Opening/Closing/samedi)
            MakeUser(3, "c1@test.ma", level: 2),
            MakeUser(4, "c2@test.ma", level: 2));
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await svc.SaveShiftTemplateAsync(new SaveShiftConfigDto
        {
            SubServiceId = 1,
            Shifts =
            [
                new ShiftConfigItemDto { Label = "8h", StartTime = "08:00", WorkHours = 8, RequiredCount = 2, DisplayOrder = 1 },
                new ShiftConfigItemDto { Label = "10h", StartTime = "10:00", WorkHours = 8, RequiredCount = 2, DisplayOrder = 2 },
            ]
        });

        var monday = new DateOnly(2026, 7, 20); // ISO week 30
        var planning = await svc.CreatePlanningAsync(new CreateWeeklyPlanningDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeekStartDate = monday,
            TotalEffectif = 4
        });

        var result = await svc.GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeeklyPlanningId = planning.Id
        });

        var sat = await db.ShiftAssignments
            .Where(a => a.WeeklyPlanningId == result.Id && a.IsSaturday && !a.IsOnLeave && !a.IsHoliday)
            .ToListAsync();

        Assert.Contains(sat, a => a.UserId == 1 && a.IsHalfDaySaturday);
        Assert.Contains(sat, a => a.UserId == 2 && a.IsHalfDaySaturday);
        Assert.All(sat.Where(a => a.UserId is 1 or 2), a => Assert.True(a.IsNewEmployee));
        Assert.Contains(sat.Where(a => a.UserId is 1 or 2), a => a.SaturdaySlot == 1);
        Assert.Contains(sat.Where(a => a.UserId is 1 or 2), a => a.SaturdaySlot == 2);
    }

    [Fact]
    public async Task Confirmed_employee_does_not_always_work_saturday_like_beginner()
    {
        await using var db = CreateDb();
        await SeedBaseAsync(db);

        // Un débutant + deux confirmés ; conf#2 a déjà travaillé la semaine précédente
        db.Users.AddRange(
            MakeUser(1, "beg@test.ma", level: 1),
            MakeUser(2, "conf@test.ma", level: 2),
            MakeUser(3, "conf2@test.ma", level: 2));
        db.SaturdayGroups.AddRange(
            new SaturdayGroup { UserId = 1, GroupNumber = 1 },
            new SaturdayGroup { UserId = 2, GroupNumber = 2 },
            new SaturdayGroup { UserId = 3, GroupNumber = 1 });
        db.SaturdayHistories.AddRange(
            new SaturdayHistory
            {
                UserId = 2,
                SubServiceId = 1,
                WeekCode = "2026-W29",
                WorkedSaturday = true
            },
            new SaturdayHistory
            {
                UserId = 3,
                SubServiceId = 1,
                WeekCode = "2026-W29",
                WorkedSaturday = false
            });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await svc.SaveShiftTemplateAsync(new SaveShiftConfigDto
        {
            SubServiceId = 1,
            Shifts =
            [
                new ShiftConfigItemDto { Label = "8h", StartTime = "08:00", WorkHours = 8, RequiredCount = 2, DisplayOrder = 1 },
                new ShiftConfigItemDto { Label = "10h", StartTime = "10:00", WorkHours = 8, RequiredCount = 1, DisplayOrder = 2 },
            ]
        });

        var monday = new DateOnly(2026, 7, 20);
        var planning = await svc.CreatePlanningAsync(new CreateWeeklyPlanningDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeekStartDate = monday,
            TotalEffectif = 3
        });

        await svc.GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeeklyPlanningId = planning.Id
        });

        var sat = await db.ShiftAssignments
            .Where(a => a.WeeklyPlanningId == planning.Id && a.IsSaturday && !a.IsOnLeave && !a.IsHoliday
                        && a.SubServiceShiftConfigId != null)
            .ToListAsync();

        Assert.Contains(sat, a => a.UserId == 1 && a.IsHalfDaySaturday);
        Assert.DoesNotContain(sat, a => a.UserId == 2);
        Assert.Contains(sat, a => a.UserId == 3);
    }

    [Fact]
    public async Task AutoAssignSaturdayGroups_balances_2_3_to_3_3()
    {
        await using var db = CreateDb();
        await SeedBaseAsync(db);

        for (var i = 1; i <= 5; i++)
            db.Users.Add(MakeUser(i, $"u{i}@test.ma", level: 2));
        // Nouvelle recrue sans groupe
        db.Users.Add(MakeUser(6, "new@test.ma", level: 1));

        db.SaturdayGroups.AddRange(
            new SaturdayGroup { UserId = 1, GroupNumber = 1 },
            new SaturdayGroup { UserId = 2, GroupNumber = 1 },
            new SaturdayGroup { UserId = 3, GroupNumber = 2 },
            new SaturdayGroup { UserId = 4, GroupNumber = 2 },
            new SaturdayGroup { UserId = 5, GroupNumber = 2 });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await svc.AutoAssignSaturdayGroupsAsync(1);

        var g6 = await db.SaturdayGroups.SingleAsync(sg => sg.UserId == 6);
        Assert.Equal(1, g6.GroupNumber); // groupe minoritaire

        var g1 = await db.SaturdayGroups.CountAsync(sg => sg.GroupNumber == 1);
        var g2 = await db.SaturdayGroups.CountAsync(sg => sg.GroupNumber == 2);
        Assert.Equal(3, g1);
        Assert.Equal(3, g2);
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
