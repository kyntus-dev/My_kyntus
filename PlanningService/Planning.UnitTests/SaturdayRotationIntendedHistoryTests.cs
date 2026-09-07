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
/// Fériés / congés / absences ne doivent pas poisonner SaturdayHistory :
/// WorkedSaturday = tour prévu (intended), pas la présence réelle.
/// </summary>
public class SaturdayRotationIntendedHistoryTests
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

    private static async Task SeedShiftsAsync(PlanningService svc, int required = 2)
    {
        await svc.SaveShiftTemplateAsync(new SaveShiftConfigDto
        {
            SubServiceId = 1,
            Shifts =
            [
                new ShiftConfigItemDto { Label = "8h", StartTime = "08:00", WorkHours = 8, RequiredCount = required, DisplayOrder = 1 },
                new ShiftConfigItemDto { Label = "10h", StartTime = "10:00", WorkHours = 8, RequiredCount = required, DisplayOrder = 2 },
            ]
        });
    }

    /// <summary>Assomption 2026 = samedi 15/08 → semaine ISO 2026-W33 (lundi 10/08).</summary>
    [Fact]
    public async Task Holiday_saturday_saves_intended_history_not_all_false()
    {
        await using var db = CreateDb();
        await SeedBaseAsync(db);

        db.Users.AddRange(
            MakeUser(1, "beg@test.ma", level: 1),
            MakeUser(2, "on-last@test.ma", level: 2),
            MakeUser(3, "off-last@test.ma", level: 2),
            MakeUser(4, "c2@test.ma", level: 2));
        db.SaturdayGroups.AddRange(
            new SaturdayGroup { UserId = 1, GroupNumber = 1 },
            new SaturdayGroup { UserId = 2, GroupNumber = 1 },
            new SaturdayGroup { UserId = 3, GroupNumber = 2 },
            new SaturdayGroup { UserId = 4, GroupNumber = 2 });
        // Semaine précédente : 2 a travaillé → intended W33 = OFF ; 3 n'a pas → intended ON
        db.SaturdayHistories.AddRange(
            new SaturdayHistory { UserId = 2, SubServiceId = 1, WeekCode = "2026-W32", WorkedSaturday = true },
            new SaturdayHistory { UserId = 3, SubServiceId = 1, WeekCode = "2026-W32", WorkedSaturday = false },
            new SaturdayHistory { UserId = 4, SubServiceId = 1, WeekCode = "2026-W32", WorkedSaturday = true });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await SeedShiftsAsync(svc);

        var monday = new DateOnly(2026, 8, 10);
        var planning = await svc.CreatePlanningAsync(new CreateWeeklyPlanningDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W33",
            WeekStartDate = monday,
            TotalEffectif = 4
        });

        await svc.GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W33",
            WeeklyPlanningId = planning.Id
        });

        var sat = await db.ShiftAssignments
            .Where(a => a.WeeklyPlanningId == planning.Id && a.IsSaturday)
            .ToListAsync();
        Assert.All(sat, a => Assert.True(a.IsHoliday));
        Assert.All(sat, a => Assert.Null(a.SubServiceShiftConfigId));

        var hist = await db.SaturdayHistories
            .Where(h => h.WeekCode == "2026-W33" && h.SubServiceId == 1)
            .ToDictionaryAsync(h => h.UserId, h => h.WorkedSaturday);

        Assert.True(hist[1]);  // débutant toujours intended ON
        Assert.False(hist[2]); // G1 sur semaine G2
        Assert.True(hist[3]);  // G2 sur semaine G2
        Assert.True(hist[4]);  // G2 sur semaine G2 (groupe prime sur l’historique)
    }

    [Fact]
    public async Task Same_G1_agents_work_together_despite_opposite_histories()
    {
        await using var db = CreateDb();
        await SeedBaseAsync(db);

        db.Users.AddRange(
            MakeUser(1, "beg@test.ma", level: 1),
            MakeUser(2, "g1-hist-on@test.ma", level: 2),
            MakeUser(3, "g1-hist-off@test.ma", level: 2),
            MakeUser(4, "g2@test.ma", level: 2));
        db.SaturdayGroups.AddRange(
            new SaturdayGroup { UserId = 1, GroupNumber = 1 },
            new SaturdayGroup { UserId = 2, GroupNumber = 1 },
            new SaturdayGroup { UserId = 3, GroupNumber = 1 },
            new SaturdayGroup { UserId = 4, GroupNumber = 2 });
        db.SaturdayHistories.AddRange(
            new SaturdayHistory { UserId = 2, SubServiceId = 1, WeekCode = "2026-W33", WorkedSaturday = true },
            new SaturdayHistory { UserId = 3, SubServiceId = 1, WeekCode = "2026-W33", WorkedSaturday = false },
            new SaturdayHistory { UserId = 4, SubServiceId = 1, WeekCode = "2026-W33", WorkedSaturday = true });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await SeedShiftsAsync(svc);

        // W34 = semaine paire → G1
        var planning = await svc.CreatePlanningAsync(new CreateWeeklyPlanningDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W34",
            WeekStartDate = new DateOnly(2026, 8, 17),
            TotalEffectif = 4
        });
        await svc.GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W34",
            WeeklyPlanningId = planning.Id
        });

        var satWorkers = await db.ShiftAssignments
            .Where(a => a.WeeklyPlanningId == planning.Id
                        && a.IsSaturday
                        && !a.IsOnLeave
                        && !a.IsHoliday
                        && a.SubServiceShiftConfigId != null)
            .Select(a => a.UserId)
            .ToListAsync();

        Assert.Contains(1, satWorkers); // débutant toujours ON
        Assert.Contains(2, satWorkers); // G1
        Assert.Contains(3, satWorkers); // G1 malgré historique opposé
        Assert.DoesNotContain(4, satWorkers); // G2 OFF
    }

    [Fact]
    public async Task Leave_on_intended_on_saturday_still_saves_worked_true_then_flips_off()
    {
        await using var db = CreateDb();
        await SeedBaseAsync(db);

        db.Users.AddRange(
            MakeUser(1, "beg@test.ma", level: 1),
            MakeUser(2, "leave@test.ma", level: 2),
            MakeUser(3, "peer@test.ma", level: 2),
            MakeUser(4, "c2@test.ma", level: 2));
        db.SaturdayGroups.AddRange(
            new SaturdayGroup { UserId = 1, GroupNumber = 1 },
            new SaturdayGroup { UserId = 2, GroupNumber = 1 },
            new SaturdayGroup { UserId = 3, GroupNumber = 1 },
            new SaturdayGroup { UserId = 4, GroupNumber = 2 });
        // Pas d'historique W29 → intended W30 = ON pour seniors (ou groupe)
        // Forcer intended ON via historique OFF la semaine précédente
        db.SaturdayHistories.AddRange(
            new SaturdayHistory { UserId = 2, SubServiceId = 1, WeekCode = "2026-W29", WorkedSaturday = false },
            new SaturdayHistory { UserId = 3, SubServiceId = 1, WeekCode = "2026-W29", WorkedSaturday = false },
            new SaturdayHistory { UserId = 4, SubServiceId = 1, WeekCode = "2026-W29", WorkedSaturday = true });
        // Congé samedi 25/07/2026 (semaine W30, lundi 20/07)
        db.Conges.Add(new Conge
        {
            UserId = 2,
            StartDate = new DateOnly(2026, 7, 25),
            EndDate = new DateOnly(2026, 7, 25),
            Reason = "CP",
            Status = CongeStatus.Approved
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await SeedShiftsAsync(svc);

        var planning = await svc.CreatePlanningAsync(new CreateWeeklyPlanningDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeekStartDate = new DateOnly(2026, 7, 20),
            TotalEffectif = 4
        });
        await svc.GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeeklyPlanningId = planning.Id
        });

        var leaveRow = await db.ShiftAssignments.SingleAsync(a =>
            a.WeeklyPlanningId == planning.Id && a.UserId == 2 && a.IsSaturday);
        Assert.True(leaveRow.IsOnLeave);
        Assert.Null(leaveRow.SubServiceShiftConfigId);

        var hist2 = await db.SaturdayHistories.SingleAsync(h =>
            h.UserId == 2 && h.WeekCode == "2026-W30");
        Assert.True(hist2.WorkedSaturday); // intended ON malgré congé

        var next = await svc.CreatePlanningAsync(new CreateWeeklyPlanningDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W31",
            WeekStartDate = new DateOnly(2026, 7, 27),
            TotalEffectif = 4
        });
        await svc.GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W31",
            WeeklyPlanningId = next.Id
        });

        var satWorkers = await db.ShiftAssignments
            .Where(a => a.WeeklyPlanningId == next.Id
                        && a.IsSaturday
                        && !a.IsOnLeave
                        && !a.IsHoliday
                        && a.SubServiceShiftConfigId != null)
            .Select(a => a.UserId)
            .ToListAsync();

        Assert.DoesNotContain(2, satWorkers); // flip OFF après intended ON
    }

    [Fact]
    public async Task Confirmed_with_EveryHalfDay_override_works_every_saturday_half_day()
    {
        await using var db = CreateDb();
        await SeedBaseAsync(db);

        var forced = MakeUser(2, "forced4h@test.ma", level: 2);
        forced.SaturdayWorkMode = 1; // EveryHalfDay override
        db.Users.AddRange(
            MakeUser(1, "beg@test.ma", level: 1),
            forced,
            MakeUser(3, "alt@test.ma", level: 2),
            MakeUser(4, "c2@test.ma", level: 2));
        db.SaturdayGroups.AddRange(
            new SaturdayGroup { UserId = 3, GroupNumber = 1 },
            new SaturdayGroup { UserId = 4, GroupNumber = 2 });
        db.SaturdayHistories.AddRange(
            new SaturdayHistory { UserId = 3, SubServiceId = 1, WeekCode = "2026-W29", WorkedSaturday = true },
            new SaturdayHistory { UserId = 4, SubServiceId = 1, WeekCode = "2026-W29", WorkedSaturday = false });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await SeedShiftsAsync(svc);

        var planning = await svc.CreatePlanningAsync(new CreateWeeklyPlanningDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeekStartDate = new DateOnly(2026, 7, 20),
            TotalEffectif = 4
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

        Assert.Contains(sat, a => a.UserId == 2 && a.IsHalfDaySaturday);
    }

    [Fact]
    public async Task Beginner_with_Alternating_override_follows_rotation_not_every_saturday()
    {
        await using var db = CreateDb();
        await SeedBaseAsync(db);

        var altBeg = MakeUser(1, "alt-beg@test.ma", level: 1);
        altBeg.SaturdayWorkMode = 2; // Alternating despite Level 1
        db.Users.AddRange(
            altBeg,
            MakeUser(2, "c1@test.ma", level: 2),
            MakeUser(3, "c2@test.ma", level: 2),
            MakeUser(4, "c3@test.ma", level: 2));
        db.SaturdayGroups.AddRange(
            new SaturdayGroup { UserId = 1, GroupNumber = 2 },
            new SaturdayGroup { UserId = 2, GroupNumber = 1 },
            new SaturdayGroup { UserId = 3, GroupNumber = 2 },
            new SaturdayGroup { UserId = 4, GroupNumber = 2 });
        db.SaturdayHistories.Add(new SaturdayHistory
        {
            UserId = 1,
            SubServiceId = 1,
            WeekCode = "2026-W29",
            WorkedSaturday = false
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await SeedShiftsAsync(svc);

        var planning = await svc.CreatePlanningAsync(new CreateWeeklyPlanningDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeekStartDate = new DateOnly(2026, 7, 20),
            TotalEffectif = 4
        });
        await svc.GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
        {
            SubServiceId = 1,
            WeekCode = "2026-W30",
            WeeklyPlanningId = planning.Id
        });

        var satWorkers = await db.ShiftAssignments
            .Where(a => a.WeeklyPlanningId == planning.Id && a.IsSaturday && !a.IsOnLeave && !a.IsHoliday
                        && a.SubServiceShiftConfigId != null)
            .Select(a => a.UserId)
            .ToListAsync();

        Assert.DoesNotContain(1, satWorkers);
    }

    [Fact]
    public async Task Saturday_balance_flags_imbalance_when_projected_delta_ge_2()
    {
        await using var db = CreateDb();
        await SeedBaseAsync(db);

        // 1 always-on + 1 in G1 + 3 in G2 → projected 2 vs 4 → delta 2 → imbalanced
        for (var i = 1; i <= 5; i++)
            db.Users.Add(MakeUser(i, $"u{i}@test.ma", level: i == 1 ? 1 : 2));
        db.SaturdayGroups.AddRange(
            new SaturdayGroup { UserId = 2, GroupNumber = 1 },
            new SaturdayGroup { UserId = 3, GroupNumber = 2 },
            new SaturdayGroup { UserId = 4, GroupNumber = 2 },
            new SaturdayGroup { UserId = 5, GroupNumber = 2 });
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        var balance = await svc.GetSaturdayBalanceAsync(1);

        Assert.Equal(1, balance.AlwaysOnCount);
        Assert.Equal(1, balance.Group1Count);
        Assert.Equal(3, balance.Group2Count);
        Assert.Equal(2, balance.ProjectedSaturdayGroup1);
        Assert.Equal(4, balance.ProjectedSaturdayGroup2);
        Assert.True(balance.IsImbalanced);
        Assert.Equal(2, balance.ImbalanceDelta);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var (expectedG1, expectedG2) = PlanningService.ResolveSaturdayGroupDates(today);
        Assert.Equal(expectedG1, balance.Group1SaturdayDate);
        Assert.Equal(expectedG2, balance.Group2SaturdayDate);
        Assert.Equal(7, Math.Abs(expectedG2.DayNumber - expectedG1.DayNumber));
    }

    [Fact]
    public void Saturday_group_dates_even_iso_week_g1_is_this_saturday()
    {
        // Lundi 17/08/2026 = ISO W34 (paire) → G1 = sam. 22/08, G2 = sam. 29/08
        var (g1, g2) = PlanningService.ResolveSaturdayGroupDates(new DateOnly(2026, 8, 19));
        Assert.Equal(new DateOnly(2026, 8, 22), g1);
        Assert.Equal(new DateOnly(2026, 8, 29), g2);
    }

    [Fact]
    public void Saturday_group_dates_odd_iso_week_g2_is_this_saturday()
    {
        // Lundi 24/08/2026 = ISO W35 (impaire) → G2 = sam. 29/08, G1 = sam. 05/09
        var (g1, g2) = PlanningService.ResolveSaturdayGroupDates(new DateOnly(2026, 8, 26));
        Assert.Equal(new DateOnly(2026, 9, 5), g1);
        Assert.Equal(new DateOnly(2026, 8, 29), g2);
    }

    [Fact]
    public async Task SetSaturdayWorkMode_persists_override_and_group()
    {
        await using var db = CreateDb();
        await SeedBaseAsync(db);
        db.Users.Add(MakeUser(1, "u1@test.ma", level: 2));
        await db.SaveChangesAsync();

        var svc = CreateService(db);
        await svc.SetSaturdayWorkModeAsync(new SetSaturdayWorkModeDto
        {
            UserId = 1,
            SaturdayWorkMode = 2,
            GroupNumber = 2
        });

        var user = await db.Users.SingleAsync(u => u.Id == 1);
        Assert.Equal(2, user.SaturdayWorkMode);
        var g = await db.SaturdayGroups.SingleAsync(sg => sg.UserId == 1);
        Assert.Equal(2, g.GroupNumber);
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
