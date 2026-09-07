using Planning.Domain.Entities;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

public class SaturdayQuotaTests
{
    [Theory]
    [InlineData(20, 10)]
    [InlineData(19, 9)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    public void SaturdayRequiredCount_is_half_of_week_quota(int week, int expected)
        => Assert.Equal(expected, ShiftDispersionSelector.SaturdayRequiredCount(week));

    [Fact]
    public void EffectiveSaturdayQuota_uses_full_count_when_dedicated_saturday_shift()
    {
        var dedicated = new SubServiceShiftConfig
        {
            Id = 1,
            RequiredCount = 7,
            AppliesToSaturday = true
        };
        var weekday = new SubServiceShiftConfig
        {
            Id = 2,
            RequiredCount = 20,
            AppliesToSaturday = false
        };
        Assert.Equal(7, ShiftDispersionSelector.EffectiveSaturdayQuota(dedicated));
        Assert.Equal(10, ShiftDispersionSelector.EffectiveSaturdayQuota(weekday));
    }

    [Fact]
    public void SelectSaturday_respects_dedicated_saturday_quota_without_halving()
    {
        var opening = new SubServiceShiftConfig
        {
            Id = 1, Label = "8h", StartTime = new TimeOnly(8, 0),
            RequiredCount = 3, DisplayOrder = 1, AppliesToSaturday = true
        };
        var mid = new SubServiceShiftConfig
        {
            Id = 2, Label = "10h", StartTime = new TimeOnly(10, 0),
            RequiredCount = 1, DisplayOrder = 2, AppliesToSaturday = true
        };
        var ordered = new List<SubServiceShiftConfig> { opening, mid };
        var counts = new Dictionary<int, int> { [1] = 0, [2] = 0 };
        var history = new Dictionary<int, List<int>>();

        for (var i = 0; i < 3; i++)
        {
            history[i] = new List<int>();
            var chosen = ShiftDispersionSelector.SelectSaturday(
                ordered, preferredIndex: 0, fridayShiftId: null,
                history, userId: i, shiftCountToday: counts);
            Assert.Equal(1, chosen.Id);
            counts[1]++;
        }

        history[100] = new List<int>();
        var next = ShiftDispersionSelector.SelectSaturday(
            ordered, preferredIndex: 0, fridayShiftId: null,
            history, userId: 100, shiftCountToday: counts);
        Assert.Equal(2, next.Id);
    }

    [Fact]
    public void SelectSaturday_fills_opening_quota_before_overflowing_next_shift()
    {
        var opening = new SubServiceShiftConfig
        {
            Id = 1, Label = "8h", StartTime = new TimeOnly(8, 0),
            RequiredCount = 20, DisplayOrder = 1
        };
        var mid = new SubServiceShiftConfig
        {
            Id = 2, Label = "9h", StartTime = new TimeOnly(9, 0),
            RequiredCount = 10, DisplayOrder = 2
        };
        var ordered = new List<SubServiceShiftConfig> { opening, mid };
        var counts = new Dictionary<int, int> { [1] = 0, [2] = 0 };
        var history = new Dictionary<int, List<int>>();

        // Remplir le quota samedi opening (10) avant d'envoyer sur le 2e
        for (var i = 0; i < 10; i++)
        {
            history[i] = new List<int>();
            var chosen = ShiftDispersionSelector.SelectSaturday(
                ordered, preferredIndex: 0, fridayShiftId: null,
                history, userId: i, shiftCountToday: counts);
            Assert.Equal(1, chosen.Id);
            counts[1]++;
        }

        history[100] = new List<int>();
        var next = ShiftDispersionSelector.SelectSaturday(
            ordered, preferredIndex: 0, fridayShiftId: null,
            history, userId: 100, shiftCountToday: counts);
        // Quota opening samedi atteint (10) → peut aller sur mid
        Assert.Equal(2, next.Id);
    }

    [Fact]
    public void SelectSaturday_does_not_fill_zero_quota_before_open_seat()
    {
        var shifts = new List<SubServiceShiftConfig>
        {
            new() { Id = 10, Label = "S1", StartTime = new TimeOnly(8, 0), RequiredCount = 1, DisplayOrder = 1 },
            new() { Id = 11, Label = "S2", StartTime = new TimeOnly(9, 0), RequiredCount = 1, DisplayOrder = 2 },
            new() { Id = 12, Label = "S3", StartTime = new TimeOnly(10, 0), RequiredCount = 0, DisplayOrder = 3 },
            new() { Id = 13, Label = "S4", StartTime = new TimeOnly(11, 0), RequiredCount = 0, DisplayOrder = 4 },
        };
        var counts = new Dictionary<int, int> { [10] = 1, [11] = 0, [12] = 0, [13] = 0 };
        var history = new Dictionary<int, List<int>> { [2] = [10, 10, 10, 10, 10] };

        var chosen = ShiftDispersionSelector.SelectSaturday(
            shifts, preferredIndex: 0, fridayShiftId: 10,
            history, userId: 2, shiftCountToday: counts);

        Assert.Equal(11, chosen.Id);
    }

    [Fact]
    public void RebalanceToQuotas_moves_zero_quota_surplus_to_open_seat()
    {
        var aya = new User { Id = 7, Email = "aya@t.ma", Level = 2 };
        var chay = new User { Id = 8, Email = "chay@t.ma", Level = 2 };
        var s1 = new SubServiceShiftConfig { Id = 10, Label = "S1", StartTime = new TimeOnly(8, 0), RequiredCount = 1, DisplayOrder = 1 };
        var s2 = new SubServiceShiftConfig { Id = 11, Label = "S2", StartTime = new TimeOnly(9, 0), RequiredCount = 1, DisplayOrder = 2 };
        var s3 = new SubServiceShiftConfig { Id = 12, Label = "S3", StartTime = new TimeOnly(10, 0), RequiredCount = 0, DisplayOrder = 3 };
        var shifts = new List<SubServiceShiftConfig> { s1, s2, s3 };
        var assigned = new Dictionary<int, SubServiceShiftConfig> { [7] = s1, [8] = s3 };
        var users = new Dictionary<int, User> { [7] = aya, [8] = chay };
        var history = new Dictionary<int, List<int>> { [7] = [10], [8] = [11] };

        WeekShiftPatternAssigner.RebalanceToQuotas(
            assigned, _ => shifts, history, users,
            new Dictionary<int, SubServiceShiftConfig>(), shifts);

        Assert.Equal(1, assigned.Values.Count(s => s.Id == 10));
        Assert.Equal(1, assigned.Values.Count(s => s.Id == 11));
        Assert.Equal(0, assigned.Values.Count(s => s.Id == 12));
    }
}
