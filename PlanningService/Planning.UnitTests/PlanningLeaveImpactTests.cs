using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planning.Application.Abstractions;
using Planning.Application.DTOs.Planning;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

public class PlanningLeaveImpactTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    private static SubServiceShiftConfig Cfg(
        int id, int required, int minPresence = 70, TimeOnly? start = null) => new()
    {
        Id = id,
        SubServiceId = 1,
        Label = $"S{id}",
        StartTime = start ?? new TimeOnly(8, 0),
        WorkHours = 8,
        BreakDurationMinutes = 60,
        RequiredCount = required,
        MinPresencePercent = minPresence,
        DisplayOrder = id,
        IsTemplate = true
    };

    private static ShiftAssignment Assign(
        int userId, DateOnly date, int cfgId, TimeOnly? breakTime = null) => new()
    {
        UserId = userId,
        AssignedDate = date,
        DayOfWeek = (DayOfWeekEnum)(((int)date.DayOfWeek + 6) % 7),
        SubServiceShiftConfigId = cfgId,
        BreakTime = breakTime,
        IsOnLeave = false,
        IsHoliday = false
    };

    private static WeeklyPlanning Week(DateOnly monday, params ShiftAssignment[] assignments) => new()
    {
        Id = 1,
        SubServiceId = 1,
        WeekCode = "2099-W10",
        WeekStartDate = monday,
        Status = PlanningStatus.Draft,
        ShiftAssignments = assignments.ToList()
    };

    private static User MakeUser(int id) => new()
    {
        Id = id,
        FirstName = $"U{id}",
        LastName = "T",
        Email = $"u{id}@t.ma",
        RoleId = 1,
        IsActive = true,
        SubServiceId = 1,
        Level = 2,
        PasswordHash = "x",
        HireDate = DateTime.UtcNow.AddYears(-1),
        AuthUserId = 1000 + id
    };

    private static async Task SeedOrgAsync(AppDbContext db)
    {
        db.Roles.Add(new Role { Id = 1, Name = "Pilote" });
        db.Floors.Add(new Floor { Id = 1, Name = "F1", FloorNumber = 1 });
        db.Services.Add(new Service { Id = 1, FloorId = 1, Name = "Svc", Code = "S1" });
        db.SubServices.Add(new SubService { Id = 1, ServiceId = 1, Name = "Cell A", Code = "CA" });
        await db.SaveChangesAsync();
    }

    /// <summary>Lundi futur pour que le cutoff 15h n'exclus pas toute la semaine.</summary>
    private static DateOnly FutureMonday()
    {
        var d = DateOnly.FromDateTime(DateTime.Today).AddDays(14);
        while (d.DayOfWeek != DayOfWeek.Monday)
            d = d.AddDays(1);
        return d;
    }

    // ── Cutoff 15h ──────────────────────────────────────────

    [Fact]
    public void RegenWindow_before_15_starts_tomorrow()
    {
        var today = new DateTime(2026, 8, 5, 14, 59, 0);
        Assert.Equal(new DateOnly(2026, 8, 6), PlanningRegenWindow.GetEarliestRegenerableDate(today));
    }

    [Fact]
    public void RegenWindow_from_15_starts_day_after_tomorrow()
    {
        var today = new DateTime(2026, 8, 5, 15, 0, 0);
        Assert.Equal(new DateOnly(2026, 8, 7), PlanningRegenWindow.GetEarliestRegenerableDate(today));
    }

    // ── Évaluateur ──────────────────────────────────────────

    [Fact]
    public void NeedsRegen_false_when_agent_has_no_assignment()
    {
        var monday = FutureMonday();
        var planning = Week(monday,
            Assign(2, monday, 1, new TimeOnly(12, 0)),
            Assign(3, monday, 1, new TimeOnly(13, 0)));

        Assert.False(PlanningLeaveImpactEvaluator.NeedsRegen(
            planning, [Cfg(1, required: 2)], userId: 1, monday, monday,
            absenceRemoved: false, regenerateFrom: monday));
    }

    [Fact]
    public void NeedsRegen_false_when_day_before_regenerateFrom()
    {
        var monday = FutureMonday();
        var tuesday = monday.AddDays(1);
        var planning = Week(monday,
            Assign(1, monday, 1, new TimeOnly(12, 0)),
            Assign(2, monday, 1, new TimeOnly(13, 0)));

        // Impact lundi mais regen à partir de mardi → pas de regen
        Assert.False(PlanningLeaveImpactEvaluator.NeedsRegen(
            planning, [Cfg(1, required: 2, minPresence: 50)], userId: 1, monday, monday,
            absenceRemoved: false, regenerateFrom: tuesday));
    }

    [Fact]
    public void NeedsRegen_false_when_presence_and_quota_still_ok_after_removal()
    {
        var monday = FutureMonday();
        var planning = Week(monday,
            Assign(1, monday, 1, new TimeOnly(12, 0)),
            Assign(2, monday, 1, new TimeOnly(13, 0)),
            Assign(3, monday, 1, new TimeOnly(14, 0)));

        Assert.False(PlanningLeaveImpactEvaluator.NeedsRegen(
            planning, [Cfg(1, required: 2, minPresence: 50)], userId: 1, monday, monday,
            absenceRemoved: false, regenerateFrom: monday));
    }

    [Fact]
    public void NeedsRegen_true_when_removal_breaks_quota()
    {
        var monday = FutureMonday();
        var planning = Week(monday,
            Assign(1, monday, 1, new TimeOnly(12, 0)),
            Assign(2, monday, 1, new TimeOnly(13, 0)));

        Assert.True(PlanningLeaveImpactEvaluator.NeedsRegen(
            planning, [Cfg(1, required: 2, minPresence: 50)], userId: 1, monday, monday,
            absenceRemoved: false, regenerateFrom: monday));
    }

    [Fact]
    public void NeedsRegen_true_when_removal_breaks_min_presence()
    {
        var monday = FutureMonday();
        var planning = Week(monday,
            Assign(1, monday, 1, new TimeOnly(12, 0)),
            Assign(2, monday, 1, new TimeOnly(13, 0)));

        Assert.True(PlanningLeaveImpactEvaluator.NeedsRegen(
            planning, [Cfg(1, required: 1, minPresence: 50)], userId: 1, monday, monday,
            absenceRemoved: false, regenerateFrom: monday));
    }

    [Fact]
    public void NeedsRegen_after_refuse_true_when_leave_cleared_config()
    {
        var monday = FutureMonday();
        var leaveRow = Assign(1, monday, 1);
        leaveRow.IsOnLeave = true;
        leaveRow.SubServiceShiftConfigId = null;

        Assert.True(PlanningLeaveImpactEvaluator.NeedsRegen(
            Week(monday, leaveRow), [Cfg(1, 1)], userId: 1, monday, monday,
            absenceRemoved: true, regenerateFrom: monday));
    }

    [Fact]
    public void NeedsRegen_after_refuse_false_when_config_preserved()
    {
        var monday = FutureMonday();
        var leaveRow = Assign(1, monday, 1);
        leaveRow.IsOnLeave = true;

        Assert.False(PlanningLeaveImpactEvaluator.NeedsRegen(
            Week(monday, leaveRow), [Cfg(1, 1)], userId: 1, monday, monday,
            absenceRemoved: true, regenerateFrom: monday));
    }

    // ── Service ─────────────────────────────────────────────

    [Fact]
    public async Task Sync_frozen_day_applies_surgical_without_regen()
    {
        await using var db = CreateDb();
        await SeedOrgAsync(db);

        // Semaine passée → tout figé → chirurgical
        var monday = DateOnly.FromDateTime(DateTime.Today).AddDays(-14);
        while (monday.DayOfWeek != DayOfWeek.Monday)
            monday = monday.AddDays(-1);

        db.SubServiceShiftConfigs.Add(Cfg(1, required: 2, minPresence: 50));
        db.Users.AddRange(MakeUser(1), MakeUser(2));
        db.WeeklyPlannings.Add(new WeeklyPlanning
        {
            Id = 1,
            SubServiceId = 1,
            WeekCode = "2026-W01",
            WeekStartDate = monday,
            Status = PlanningStatus.Published
        });
        await db.SaveChangesAsync();

        foreach (var (uid, br) in new[] { (1, new TimeOnly(12, 0)), (2, new TimeOnly(13, 0)) })
        {
            var a = Assign(uid, monday, 1, br);
            a.WeeklyPlanningId = 1;
            db.ShiftAssignments.Add(a);
        }
        await db.SaveChangesAsync();

        var tracker = new TrackingPlanningService();
        var svc = new PlanningLeaveImpactService(db, tracker, NullLogger<PlanningLeaveImpactService>.Instance);

        await svc.SyncAfterAbsenceChangeAsync(1, monday, monday, absenceRemoved: false);

        Assert.Equal(0, tracker.GenerateCount);
        var leave = await db.ShiftAssignments.SingleAsync(a => a.UserId == 1 && a.AssignedDate == monday);
        Assert.True(leave.IsOnLeave);
    }

    [Fact]
    public async Task Sync_quota_impact_on_open_day_triggers_regen()
    {
        await using var db = CreateDb();
        await SeedOrgAsync(db);

        var monday = FutureMonday();
        db.SubServiceShiftConfigs.Add(Cfg(1, required: 2, minPresence: 50));
        db.Users.AddRange(MakeUser(1), MakeUser(2));
        db.WeeklyPlannings.Add(new WeeklyPlanning
        {
            Id = 10,
            SubServiceId = 1,
            WeekCode = "2099-W10",
            WeekStartDate = monday,
            Status = PlanningStatus.Published
        });
        await db.SaveChangesAsync();

        foreach (var (uid, br) in new[] { (1, new TimeOnly(12, 0)), (2, new TimeOnly(13, 0)) })
        {
            var a = Assign(uid, monday, 1, br);
            a.WeeklyPlanningId = 10;
            db.ShiftAssignments.Add(a);
        }
        await db.SaveChangesAsync();

        var tracker = new TrackingPlanningService();
        var svc = new PlanningLeaveImpactService(db, tracker, NullLogger<PlanningLeaveImpactService>.Instance);

        await svc.SyncAfterAbsenceChangeAsync(1, monday, monday, absenceRemoved: false);

        Assert.Equal(1, tracker.GenerateCount);
        Assert.Contains(10, tracker.GeneratedPlanningIds);
        Assert.NotNull(tracker.LastDto?.RegenerateFromDate);
    }

    [Fact]
    public async Task Sync_refuse_surgical_restores_IsOnLeave()
    {
        await using var db = CreateDb();
        await SeedOrgAsync(db);

        var monday = FutureMonday();
        db.SubServiceShiftConfigs.Add(Cfg(1, required: 1, minPresence: 50));
        db.Users.Add(MakeUser(1));
        db.WeeklyPlannings.Add(new WeeklyPlanning
        {
            Id = 1,
            SubServiceId = 1,
            WeekCode = "2099-W10",
            WeekStartDate = monday,
            Status = PlanningStatus.Draft
        });
        await db.SaveChangesAsync();

        var a = Assign(1, monday, 1, new TimeOnly(12, 0));
        a.WeeklyPlanningId = 1;
        a.IsOnLeave = true;
        a.BreakTime = null;
        db.ShiftAssignments.Add(a);
        await db.SaveChangesAsync();

        var tracker = new TrackingPlanningService();
        var svc = new PlanningLeaveImpactService(db, tracker, NullLogger<PlanningLeaveImpactService>.Instance);

        await svc.SyncAfterAbsenceChangeAsync(1, monday, monday, absenceRemoved: true);

        Assert.Equal(0, tracker.GenerateCount);
        var row = await db.ShiftAssignments.SingleAsync(x => x.UserId == 1);
        Assert.False(row.IsOnLeave);
        Assert.Equal(1, row.SubServiceShiftConfigId);
    }

    [Fact]
    public async Task Sync_refuse_with_cleared_config_triggers_regen()
    {
        await using var db = CreateDb();
        await SeedOrgAsync(db);

        var monday = FutureMonday();
        db.SubServiceShiftConfigs.Add(Cfg(1, required: 1));
        db.Users.Add(MakeUser(1));
        db.WeeklyPlannings.Add(new WeeklyPlanning
        {
            Id = 5,
            SubServiceId = 1,
            WeekCode = "2099-W10",
            WeekStartDate = monday,
            Status = PlanningStatus.Draft
        });
        await db.SaveChangesAsync();

        var a = Assign(1, monday, 1);
        a.WeeklyPlanningId = 5;
        a.IsOnLeave = true;
        a.SubServiceShiftConfigId = null;
        db.ShiftAssignments.Add(a);
        await db.SaveChangesAsync();

        var tracker = new TrackingPlanningService();
        var svc = new PlanningLeaveImpactService(db, tracker, NullLogger<PlanningLeaveImpactService>.Instance);

        await svc.SyncAfterAbsenceChangeAsync(1, monday, monday, absenceRemoved: true);

        Assert.Equal(1, tracker.GenerateCount);
    }

    private sealed class TrackingPlanningService : IPlanningService
    {
        public int GenerateCount { get; private set; }
        public int NotifyCount { get; private set; }
        public List<int> GeneratedPlanningIds { get; } = new();
        public GeneratePlanningFromConfigDto? LastDto { get; private set; }

        public Task<WeeklyPlanningResponseDto> GeneratePlanningFromConfigAsync(GeneratePlanningFromConfigDto dto)
        {
            GenerateCount++;
            LastDto = dto;
            GeneratedPlanningIds.Add(dto.WeeklyPlanningId);
            return Task.FromResult(new WeeklyPlanningResponseDto { Id = dto.WeeklyPlanningId });
        }

        public Task NotifyPlanningRepublishedAsync(int planningId, string? reason = null)
        {
            NotifyCount++;
            return Task.CompletedTask;
        }

        private static Task NI() => throw new NotImplementedException();
        private static Task<T> NIAsync<T>() => throw new NotImplementedException();

        public Task<WeeklyPlanningResponseDto> CreatePlanningAsync(CreateWeeklyPlanningDto dto) => NIAsync<WeeklyPlanningResponseDto>();
        public Task<WeeklyPlanningResponseDto?> GetPlanningByIdAsync(int id) => NIAsync<WeeklyPlanningResponseDto?>();
        public Task<IEnumerable<WeeklyPlanningResponseDto>> GetPlanningsBySubServiceAsync(int subServiceId) => NIAsync<IEnumerable<WeeklyPlanningResponseDto>>();
        public Task DeletePlanningAsync(int id) => NI();
        public Task<WeeklyPlanningResponseDto> GeneratePlanningAsync(GeneratePlanningDto dto) => NIAsync<WeeklyPlanningResponseDto>();
        public Task AutoAssignSaturdayGroupsAsync(int subServiceId) => NI();
        public Task<WeekShiftConfigResponseDto> SaveShiftConfigAsync(SaveShiftConfigDto dto) => NIAsync<WeekShiftConfigResponseDto>();
        public Task<WeekShiftConfigResponseDto?> GetShiftConfigAsync(int subServiceId, string weekCode) => NIAsync<WeekShiftConfigResponseDto?>();
        public Task<WeekShiftConfigResponseDto?> GetShiftTemplateAsync(int subServiceId) => NIAsync<WeekShiftConfigResponseDto?>();
        public Task<WeekShiftConfigResponseDto> SaveShiftTemplateAsync(SaveShiftConfigDto dto) => NIAsync<WeekShiftConfigResponseDto>();
        public Task<ShiftConfigStatusResponseDto> GetShiftConfigStatusAsync() => NIAsync<ShiftConfigStatusResponseDto>();
        public Task EnsureWeekSnapshotAsync(int subServiceId, string weekCode, DateOnly weekStartDate, bool forceRefresh = false) => NI();
        public Task<WeeklyShiftModePlanDto> GetWeeklyShiftModePlanAsync(int subServiceId, string weekCode, DateOnly weekStartDate) => NIAsync<WeeklyShiftModePlanDto>();
        public Task<WeeklyShiftModePlanDto> SaveWeeklyShiftModePlanAsync(SaveWeeklyShiftModePlanDto dto) => NIAsync<WeeklyShiftModePlanDto>();
        public Task NotifyModeConfigPendingAsync(
            int planningUserId, int authUserId, string weekCode, string subServiceName, string message) => NI();
        public Task<AutoGenerateWeekResultDto> AutoGenerateWeekAsync(
            string? weekCode = null, bool forceDraftRefresh = false, int? actorUserId = null) => NIAsync<AutoGenerateWeekResultDto>();
        public Task EnsureViewerCanManageSubServiceAsync(int viewerUserId, int subServiceId) => Task.CompletedTask;
        public Task<AutoGenerateSettingsDto> GetAutoGenerateSettingsAsync() => NIAsync<AutoGenerateSettingsDto>();
        public Task<AutoGenerateSettingsDto> SaveAutoGenerateSettingsAsync(AutoGenerateSettingsDto dto, int? updatedByUserId) => NIAsync<AutoGenerateSettingsDto>();
        public Task<PlanningWeekListDto> GetWeekOverviewAsync(string weekCode, int? viewerUserId = null) => NIAsync<PlanningWeekListDto>();
        public Task RecordConsultationAsync(int planningId, int userId) => NI();
        public Task<bool> HasConsultedAsync(int planningId, int userId) => NIAsync<bool>();
        public Task<WeeklyPlanningResponseDto> PublishPlanningAsync(int planningId, int validatorId) => NIAsync<WeeklyPlanningResponseDto>();
        public Task<DayAssignmentDto> OverrideShiftAsync(OverrideShiftDto dto) => NIAsync<DayAssignmentDto>();
        public Task<DayAssignmentDto> OverrideBreakAsync(OverrideBreakDto dto) => NIAsync<DayAssignmentDto>();
        public Task<MyPlanningDto?> GetMyCurrentPlanningAsync(int userId) => NIAsync<MyPlanningDto?>();
        public Task SetSaturdayGroupAsync(SetSaturdayGroupDto dto) => NI();
        public Task SetSaturdayWorkModeAsync(SetSaturdayWorkModeDto dto) => NI();
        public Task SetEmployeeSpecialCaseAsync(SetEmployeeSpecialCaseDto dto) => NI();
        public Task SetEmployeePlateauTrainingAsync(SetEmployeePlateauTrainingDto dto) => NI();
        public Task<SaturdayBalanceDto> GetSaturdayBalanceAsync(int subServiceId) => NIAsync<SaturdayBalanceDto>();
        public Task<int> NotifySaturdayImbalanceAsync(int subServiceId, int authUserId) => NIAsync<int>();
        public Task<IEnumerable<object>> GetSaturdayGroupsAsync(int subServiceId) => NIAsync<IEnumerable<object>>();
        public Task SyncNewEmployeesAsync() => NI();
        public Task<MyPlanningDto?> GetMyPlanningAsync(int userId, string weekCode) => NIAsync<MyPlanningDto?>();
        public Task<IEnumerable<MyPlanningDto>> GetMyPlanningHistoryAsync(int userId) => NIAsync<IEnumerable<MyPlanningDto>>();
        public Task<IReadOnlyList<MyPlanningDto>> GetAgentPlanningHistoryAsync(int planningUserId, DateOnly? from, DateOnly? to) => NIAsync<IReadOnlyList<MyPlanningDto>>();
        public Task<PlanningCommentDto> SaveCommentAsync(SavePlanningCommentDto dto) => NIAsync<PlanningCommentDto>();
        public Task DeleteCommentAsync(int planningId, int userId) => NI();
        public Task<IEnumerable<PlanningCommentDto>> GetCommentsAsync(int planningId) => NIAsync<IEnumerable<PlanningCommentDto>>();
        public Task<List<SaturdayHistoryResponseDto>> GetSaturdayHistoryAsync(int subServiceId, string weekCode) => NIAsync<List<SaturdayHistoryResponseDto>>();
        public Task<List<SaturdayYtdDto>> GetSaturdayYtdAsync(int subServiceId, int year) => NIAsync<List<SaturdayYtdDto>>();
        public Task SaveSaturdayHistoryAsync(SetSaturdayHistoryDto dto, bool isManual) => NI();
        public Task<DayAssignmentDto> OverrideSaturdayShiftAsync(OverrideSaturdayDto dto) => NIAsync<DayAssignmentDto>();
        public Task SetSaturdayOffAsync(int weeklyPlanningId, int userId) => NI();
        public Task<IReadOnlyList<EquipePlanningSummaryDto>> GetEquipePlanningsByAuthUserIdAsync(int authUserId) => NIAsync<IReadOnlyList<EquipePlanningSummaryDto>>();
        public Task<IEnumerable<PlanningNotificationDto>> GetMyNotificationsAsync(int authUserId) => NIAsync<IEnumerable<PlanningNotificationDto>>();
        public Task MarkNotificationReadAsync(int id, int authUserId) => NI();
        public Task MarkAllNotificationsReadAsync(int authUserId) => NI();
    }
}
