using Planning.Domain.Entities;
using Planning.Domain.Enums;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Construction semaine : motif round-robin interleavé par niveau, puis
/// matching des quotas journaliers sans casser max-2 / non-consécutif tant que possible.
/// </summary>
public static class WeekShiftPatternAssigner
{
    private const int MatchMaxPasses = 80;

    /// <summary>
    /// Seed de rotation : dans chaque niveau, les employés sont étalés sur les shifts
    /// (pas tous les Confirmés sur le 8h).
    /// </summary>
    public static Dictionary<int, int> BuildSeeds(
        IReadOnlyList<User> employees,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> shiftsFor,
        int weekNumber)
    {
        var seeds = new Dictionary<int, int>();
        var byLevel = employees
            .GroupBy(e => e.Level)
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(e => e.Id).ToList())
            .ToList();
        var maxLen = byLevel.Count == 0 ? 0 : byLevel.Max(g => g.Count);
        var ordered = new List<User>();
        for (var i = 0; i < maxLen; i++)
        {
            foreach (var group in byLevel)
            {
                if (i < group.Count)
                    ordered.Add(group[i]);
            }
        }

        for (var i = 0; i < ordered.Count; i++)
        {
            var n = Math.Max(shiftsFor(ordered[i].Id).Count, 1);
            seeds[ordered[i].Id] = (i + weekNumber) % n;
        }

        return seeds;
    }

    public static SubServiceShiftConfig PatternShift(
        IReadOnlyList<SubServiceShiftConfig> ordered,
        int seed,
        int workedDayIndex,
        User? employee = null)
    {
        var n = ordered.Count;
        if (n == 0)
            throw new InvalidOperationException("Aucun shift configuré.");

        var start = ((seed % n) + n) % n;
        var plateau = employee is { IsPlateauTraining: true }
                      && ordered.Any(s => s.ShiftKind == ShiftKind.Standard);

        for (var i = 0; i < n; i++)
        {
            var s = ordered[(start + workedDayIndex + i) % n];
            if (!plateau || s.ShiftKind == ShiftKind.Standard)
                return s;
        }

        return ordered[(start + workedDayIndex) % n];
    }

    /// <summary>
    /// Assigne un shift à chaque disponible : pin ou motif, puis équilibre les quotas.
    /// </summary>
    public static Dictionary<int, SubServiceShiftConfig> AssignDay(
        IReadOnlyList<User> available,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> shiftsFor,
        IReadOnlyDictionary<int, int> seeds,
        IReadOnlyDictionary<int, List<int>> weekHistory,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, SubServiceShiftConfig> pinsByUser,
        IReadOnlyList<SubServiceShiftConfig> allShifts)
    {
        var assigned = new Dictionary<int, SubServiceShiftConfig>();
        var allowedCache = new Dictionary<int, IReadOnlyList<SubServiceShiftConfig>>();

        IReadOnlyList<SubServiceShiftConfig> Allowed(int userId)
        {
            if (!allowedCache.TryGetValue(userId, out var list))
            {
                list = shiftsFor(userId);
                allowedCache[userId] = list;
            }
            return list;
        }

        foreach (var emp in available)
        {
            if (pinsByUser.TryGetValue(emp.Id, out var pin) && pin != null)
                assigned[emp.Id] = pin;
        }

        FillUnderQuotas(assigned, available, Allowed, seeds, weekHistory, usersById, pinsByUser, allShifts);

        foreach (var emp in available)
        {
            if (assigned.ContainsKey(emp.Id))
                continue;

            var allowed = Allowed(emp.Id);
            if (allowed.Count == 0)
                continue;

            var counts = CountByShift(assigned);
            var under = allowed
                .Where(s => s.RequiredCount > 0 && counts.GetValueOrDefault(s.Id, 0) < s.RequiredCount)
                .ToList();
            var pool = under.Count > 0
                ? under
                : allowed.ToList();

            var ordered = pool.OrderBy(s => s.DisplayOrder).ThenBy(s => s.StartTime).ToList();
            var seed = seeds.GetValueOrDefault(emp.Id, 0);
            var worked = weekHistory.TryGetValue(emp.Id, out var hist) ? hist.Count : 0;
            assigned[emp.Id] = PatternShift(ordered, seed, worked, emp);
        }

        MatchQuotas(assigned, Allowed, weekHistory, usersById, pinsByUser, allShifts);
        EnforceStrictQuotas(assigned, Allowed, weekHistory, usersById, pinsByUser, allShifts);
        BreakSuccessiveSameDay(assigned, Allowed, weekHistory, usersById, pinsByUser);
        EnforceStrictQuotas(assigned, Allowed, weekHistory, usersById, pinsByUser, allShifts);
        return assigned;
    }

    /// <summary>
    /// Recolle les effectifs sur les RequiredCount (sans modifier les quotas).
    /// </summary>
    public static void RebalanceToQuotas(
        Dictionary<int, SubServiceShiftConfig> assigned,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> allowedFor,
        IReadOnlyDictionary<int, List<int>> weekHistory,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, SubServiceShiftConfig> pinsByUser,
        IReadOnlyList<SubServiceShiftConfig> allShifts)
    {
        MatchQuotas(assigned, allowedFor, weekHistory, usersById, pinsByUser, allShifts);
        EnforceStrictQuotas(assigned, allowedFor, weekHistory, usersById, pinsByUser, allShifts);
    }

    /// <summary>
    /// Dernier filet semaine : chaque jour, chaque mode, les RequiredCount priment
    /// sur l'alternance, le max-2 et les réparations de niveau.
    /// </summary>
    public static void RebalanceWeekAssignments(
        List<ShiftAssignment> assignments,
        IReadOnlyList<SubServiceShiftConfig> configs,
        IReadOnlyDictionary<int, User> usersById)
    {
        if (configs.Count == 0 || assignments.Count == 0)
            return;

        var byDate = assignments
            .Where(a => a.SubServiceShiftConfigId != null && !a.IsOnLeave && !a.IsHoliday)
            .GroupBy(a => a.AssignedDate);

        foreach (var day in byDate)
        {
            var sample = day.First();
            var isSat = sample.IsSaturday || day.Key.DayOfWeek == DayOfWeek.Saturday;
            var weekday = configs.Where(c => !c.AppliesToSaturday).ToList();
            var satPool = ShiftConfigScope.Saturday(configs);
            var dayConfigs = isSat
                ? satPool.Select(c => CloneRequired(c, ShiftDispersionSelector.EffectiveSaturdayQuota(c))).ToList()
                : (weekday.Count > 0 ? weekday : configs.ToList());
            var byId = dayConfigs.ToDictionary(c => c.Id);

            var assigned = new Dictionary<int, SubServiceShiftConfig>();
            var pins = new Dictionary<int, SubServiceShiftConfig>();
            foreach (var a in day)
            {
                if (!byId.TryGetValue(a.SubServiceShiftConfigId!.Value, out var cfg))
                    continue;
                assigned[a.UserId] = cfg;
                if (a.IsManagerOverride || a.IsExceptionalRequest || a.IsHalfDaySaturday)
                    pins[a.UserId] = cfg;
            }

            if (assigned.Count == 0)
                continue;

            RebalanceToQuotas(
                assigned,
                _ => dayConfigs,
                new Dictionary<int, List<int>>(),
                usersById,
                pins,
                dayConfigs);

            foreach (var a in day)
            {
                if (pins.ContainsKey(a.UserId))
                    continue;
                if (assigned.TryGetValue(a.UserId, out var neu))
                    a.SubServiceShiftConfigId = neu.Id;
            }
        }
    }

    private static SubServiceShiftConfig CloneRequired(SubServiceShiftConfig c, int required) => new()
    {
        Id = c.Id,
        SubServiceId = c.SubServiceId,
        Label = c.Label,
        StartTime = c.StartTime,
        WorkHours = c.WorkHours,
        RequiredCount = required,
        DisplayOrder = c.DisplayOrder,
        ShiftKind = c.ShiftKind,
        ShiftModeProfileId = c.ShiftModeProfileId,
        IsTemplate = c.IsTemplate,
        AppliesToSaturday = c.AppliesToSaturday
    };

    /// <summary>
    /// Place d’abord les sièges de quota encore vides, en évitant le shift de la veille.
    /// </summary>
    private static void FillUnderQuotas(
        Dictionary<int, SubServiceShiftConfig> assigned,
        IReadOnlyList<User> available,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> allowedFor,
        IReadOnlyDictionary<int, int> seeds,
        IReadOnlyDictionary<int, List<int>> weekHistory,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, SubServiceShiftConfig> pinsByUser,
        IReadOnlyList<SubServiceShiftConfig> allShifts)
    {
        var quotaShifts = allShifts.Where(s => s.RequiredCount > 0).ToList();
        if (quotaShifts.Count == 0)
            return;

        var skipped = new HashSet<int>();
        for (var guard = 0; guard < 40; guard++)
        {
            var counts = CountByShift(assigned);
            var target = quotaShifts
                .Where(s => !skipped.Contains(s.Id))
                .Where(s => counts.GetValueOrDefault(s.Id, 0) < s.RequiredCount)
                .OrderBy(s => counts.GetValueOrDefault(s.Id, 0) - s.RequiredCount)
                .ThenBy(s => s.DisplayOrder)
                .FirstOrDefault();
            if (target is null)
                return;

            var pick = PickForQuotaSeat(
                available, assigned, allowedFor, seeds, weekHistory, usersById, pinsByUser,
                target, allowConsecutive: false)
                ?? PickForQuotaSeat(
                    available, assigned, allowedFor, seeds, weekHistory, usersById, pinsByUser,
                    target, allowConsecutive: true)
                ?? PickForQuotaSeat(
                    available, assigned, allowedFor, seeds, weekHistory, usersById, pinsByUser,
                    target, allowConsecutive: true, ignoreSoftRules: true);
            if (pick is null)
            {
                skipped.Add(target.Id);
                continue;
            }

            assigned[pick.Value] = target;
        }
    }

    private static int? PickForQuotaSeat(
        IReadOnlyList<User> available,
        Dictionary<int, SubServiceShiftConfig> assigned,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> allowedFor,
        IReadOnlyDictionary<int, int> seeds,
        IReadOnlyDictionary<int, List<int>> weekHistory,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, SubServiceShiftConfig> pinsByUser,
        SubServiceShiftConfig target,
        bool allowConsecutive,
        bool ignoreSoftRules = false)
    {
        int? best = null;
        var bestKey = (streak: 99, week: 99, patternDist: 99, id: int.MaxValue);

        foreach (var emp in available)
        {
            if (assigned.ContainsKey(emp.Id) || pinsByUser.ContainsKey(emp.Id))
                continue;
            var allowed = allowedFor(emp.Id);
            if (allowed.All(s => s.Id != target.Id))
                continue;
            if (!PlateauAllows(emp.Id, target, usersById))
                continue;
            if (!ignoreSoftRules && !MoveRespectsSoftRules(
                    weekHistory, emp.Id, target.Id, allowed.Count,
                    allowConsecutive, allowOverMax: true))
                continue;

            var yesterday = ShiftDispersionSelector.YesterdayShiftId(weekHistory, emp.Id);
            var streak = ShiftDispersionSelector.ConsecutiveStreak(weekHistory, emp.Id);
            var weekCount = ShiftDispersionSelector.CountThisWeek(weekHistory, emp.Id, target.Id);
            var ordered = allowed.OrderBy(s => s.DisplayOrder).ThenBy(s => s.StartTime).ToList();
            var seed = seeds.GetValueOrDefault(emp.Id, 0);
            var worked = weekHistory.TryGetValue(emp.Id, out var hist) ? hist.Count : 0;
            var pattern = PatternShift(ordered, seed, worked, emp);
            var patternDist = pattern.Id == target.Id
                ? 0
                : Math.Abs(ordered.FindIndex(s => s.Id == pattern.Id) - ordered.FindIndex(s => s.Id == target.Id));
            var consec = yesterday == target.Id ? 1 : 0;
            var key = (consec + (streak >= 2 && yesterday == target.Id ? 5 : 0), weekCount, patternDist, emp.Id);
            if (best is null || key.CompareTo(bestKey) < 0)
            {
                best = emp.Id;
                bestKey = key;
            }
        }

        return best;
    }

    /// <summary>
    /// Échange intra-journée pour casser 8h–8h / 9h–9h sans toucher aux quotas.
    /// Un 2e jour identique n’est gardé que s’il n’existe aucun partenaire non consécutif.
    /// </summary>
    private static void BreakSuccessiveSameDay(
        Dictionary<int, SubServiceShiftConfig> assigned,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> allowedFor,
        IReadOnlyDictionary<int, List<int>> weekHistory,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, SubServiceShiftConfig> pinsByUser)
    {
        foreach (var userId in assigned.Keys.ToList())
        {
            if (pinsByUser.ContainsKey(userId))
                continue;
            var mine = assigned[userId];
            var yesterday = ShiftDispersionSelector.YesterdayShiftId(weekHistory, userId);
            if (yesterday != mine.Id)
                continue;

            int? partner = null;
            var partnerCreatesConsec = true;
            foreach (var otherId in assigned.Keys)
            {
                if (otherId == userId || pinsByUser.ContainsKey(otherId))
                    continue;
                var theirs = assigned[otherId];
                if (theirs.Id == mine.Id)
                    continue;
                if (allowedFor(userId).All(s => s.Id != theirs.Id))
                    continue;
                if (allowedFor(otherId).All(s => s.Id != mine.Id))
                    continue;
                if (!PlateauAllows(userId, theirs, usersById) || !PlateauAllows(otherId, mine, usersById))
                    continue;

                var otherY = ShiftDispersionSelector.YesterdayShiftId(weekHistory, otherId);
                if (yesterday == theirs.Id)
                    continue;
                var otherConsec = otherY == mine.Id;
                var otherStreak = ShiftDispersionSelector.ConsecutiveStreak(weekHistory, otherId);
                if (otherConsec && otherStreak >= 2)
                    continue;

                if (!otherConsec)
                {
                    partner = otherId;
                    partnerCreatesConsec = false;
                    break;
                }

                if (partner is null)
                {
                    partner = otherId;
                    partnerCreatesConsec = true;
                }
            }

            if (partner is null || partnerCreatesConsec)
                continue;

            (assigned[userId], assigned[partner.Value]) = (assigned[partner.Value], assigned[userId]);
        }
    }

    private static void MatchQuotas(
        Dictionary<int, SubServiceShiftConfig> assigned,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> allowedFor,
        IReadOnlyDictionary<int, List<int>> weekHistory,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, SubServiceShiftConfig> pinsByUser,
        IReadOnlyList<SubServiceShiftConfig> allShifts)
    {
        var quotaShifts = allShifts.Where(s => s.RequiredCount > 0).ToList();
        if (quotaShifts.Count == 0 || assigned.Count == 0)
            return;

        var shiftCount = allShifts.Count;

        for (var pass = 0; pass < MatchMaxPasses; pass++)
        {
            var counts = CountByShift(assigned);
            var under = quotaShifts
                .Where(s => counts.GetValueOrDefault(s.Id, 0) < s.RequiredCount)
                .OrderBy(s => counts.GetValueOrDefault(s.Id, 0) - s.RequiredCount)
                .ToList();
            if (under.Count == 0)
                return;

            var moved = false;
            foreach (var target in under)
            {
                var donor = PickDonor(
                    assigned, allowedFor, weekHistory, usersById, pinsByUser,
                    target, counts, shiftCount, allowConsecutive: false, allowOverMax: false);

                if (donor is not null)
                {
                    assigned[donor.Value] = target;
                    moved = true;
                    break;
                }

                if (TryTwoStepFill(
                        assigned, allowedFor, weekHistory, usersById, pinsByUser,
                        target, counts, shiftCount, quotaShifts, allowConsecutive: false))
                {
                    moved = true;
                    break;
                }

                if (TryTwoStepFill(
                        assigned, allowedFor, weekHistory, usersById, pinsByUser,
                        target, counts, shiftCount, quotaShifts, allowConsecutive: true))
                {
                    moved = true;
                    break;
                }

                donor = PickDonor(
                    assigned, allowedFor, weekHistory, usersById, pinsByUser,
                    target, counts, shiftCount, allowConsecutive: true, allowOverMax: false)
                    ?? PickDonor(
                        assigned, allowedFor, weekHistory, usersById, pinsByUser,
                        target, counts, shiftCount, allowConsecutive: true, allowOverMax: true);
                if (donor is null)
                    continue;

                assigned[donor.Value] = target;
                moved = true;
                break;
            }

            if (!moved)
                return;
        }
    }

    /// <summary>
    /// Dernier filet : les sièges de quota passent avant l'alternance (sauf 3e jour identique).
    /// </summary>
    private static void EnforceStrictQuotas(
        Dictionary<int, SubServiceShiftConfig> assigned,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> allowedFor,
        IReadOnlyDictionary<int, List<int>> weekHistory,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, SubServiceShiftConfig> pinsByUser,
        IReadOnlyList<SubServiceShiftConfig> allShifts)
    {
        var quotaShifts = allShifts.Where(s => s.RequiredCount > 0).ToList();
        if (quotaShifts.Count == 0 || assigned.Count == 0)
            return;

        var shiftCount = allShifts.Count;
        for (var pass = 0; pass < MatchMaxPasses; pass++)
        {
            var counts = CountByShift(assigned);
            var under = quotaShifts
                .Where(s => counts.GetValueOrDefault(s.Id, 0) < s.RequiredCount)
                .OrderBy(s => counts.GetValueOrDefault(s.Id, 0) - s.RequiredCount)
                .ThenBy(s => s.DisplayOrder)
                .ToList();
            if (under.Count == 0)
                return;

            var moved = false;
            foreach (var target in under)
            {
                var donor = PickDonor(
                    assigned, allowedFor, weekHistory, usersById, pinsByUser,
                    target, counts, shiftCount, allowConsecutive: true, allowOverMax: true, forceQuota: true);
                if (donor is not null)
                {
                    assigned[donor.Value] = target;
                    moved = true;
                    break;
                }

                if (TryTwoStepFill(
                        assigned, allowedFor, weekHistory, usersById, pinsByUser,
                        target, counts, shiftCount, quotaShifts, allowConsecutive: true, ignoreSoftRules: true))
                {
                    moved = true;
                    break;
                }
            }

            if (!moved)
                return;
        }
    }

    private static int? PickDonor(
        Dictionary<int, SubServiceShiftConfig> assigned,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> allowedFor,
        IReadOnlyDictionary<int, List<int>> weekHistory,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, SubServiceShiftConfig> pinsByUser,
        SubServiceShiftConfig target,
        Dictionary<int, int> counts,
        int shiftCount,
        bool allowConsecutive,
        bool allowOverMax,
        bool forceQuota = false)
    {
        var candidates = new List<(int UserId, int Surplus, int Need, int WeekCount, bool StrictOk, int Overruns)>();

        foreach (var (userId, current) in assigned)
        {
            if (pinsByUser.ContainsKey(userId))
                continue;
            if (current.Id == target.Id)
                continue;
            if (allowedFor(userId).All(s => s.Id != target.Id))
                continue;

            var surplus = Surplus(current, counts);
            if (surplus <= 0)
                continue;

            var yesterday = ShiftDispersionSelector.YesterdayShiftId(weekHistory, userId);
            var weekCount = ShiftDispersionSelector.CountThisWeek(weekHistory, userId, target.Id);
            var workedDays = (weekHistory.TryGetValue(userId, out var hist) ? hist.Count : 0) + 1;
            var maxSame = ShiftDispersionSelector.EffectiveMaxSame(shiftCount, workedDays);
            var consecutive = yesterday == target.Id;
            var streak = ShiftDispersionSelector.ConsecutiveStreak(weekHistory, userId);
            var overMax = weekCount >= maxSame;
            var plateauOk = PlateauAllows(userId, target, usersById);

            if (!plateauOk)
                continue;
            if (!forceQuota)
            {
                if (consecutive && streak >= 2)
                    continue;
                if (consecutive && !allowConsecutive)
                    continue;
                if (overMax && !allowOverMax)
                    continue;
            }

            var strictOk = !consecutive && !overMax;
            var overruns = CountWeekOverruns(weekHistory, userId, shiftCount);
            candidates.Add((userId, surplus, Math.Max(current.RequiredCount, 0), weekCount, strictOk, overruns));
        }

        if (candidates.Count == 0)
            return null;

        return candidates
            .OrderByDescending(c => c.StrictOk)
            .ThenBy(c => c.Overruns)
            .ThenBy(c => c.Need)
            .ThenByDescending(c => c.Surplus)
            .ThenBy(c => c.WeekCount)
            .ThenBy(c => c.UserId)
            .First()
            .UserId;
    }

    /// <summary>
    /// D → créneau déficitaire (même si D n'est pas en surplus), puis un agent
    /// d'un créneau en surplus reprend l'ancien créneau de D.
    /// </summary>
    private static bool TryTwoStepFill(
        Dictionary<int, SubServiceShiftConfig> assigned,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> allowedFor,
        IReadOnlyDictionary<int, List<int>> weekHistory,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, SubServiceShiftConfig> pinsByUser,
        SubServiceShiftConfig target,
        Dictionary<int, int> counts,
        int shiftCount,
        IReadOnlyList<SubServiceShiftConfig> quotaShifts,
        bool allowConsecutive,
        bool ignoreSoftRules = false)
    {
        var quotaById = quotaShifts.ToDictionary(s => s.Id, s => s.RequiredCount);

        foreach (var (donorId, donorShift) in assigned)
        {
            if (pinsByUser.ContainsKey(donorId)) continue;
            if (donorShift.Id == target.Id) continue;
            if (allowedFor(donorId).All(s => s.Id != target.Id)) continue;
            if (!PlateauAllows(donorId, target, usersById)) continue;
            if (!ignoreSoftRules
                && !MoveRespectsSoftRules(weekHistory, donorId, target.Id, shiftCount, allowConsecutive, allowOverMax: false))
                continue;

            var vacated = donorShift;
            var vacatedAfter = counts.GetValueOrDefault(vacated.Id, 0) - 1;
            var vacatedNeed = quotaById.GetValueOrDefault(vacated.Id, 0);
            if (vacatedAfter >= vacatedNeed)
            {
                assigned[donorId] = target;
                return true;
            }

            foreach (var (fillerId, fillerShift) in assigned)
            {
                if (fillerId == donorId) continue;
                if (pinsByUser.ContainsKey(fillerId)) continue;
                if (fillerShift.Id == vacated.Id) continue;
                if (allowedFor(fillerId).All(s => s.Id != vacated.Id)) continue;
                if (!PlateauAllows(fillerId, vacated, usersById)) continue;
                if (Surplus(fillerShift, counts) <= 0) continue;
                if (!ignoreSoftRules
                    && !MoveRespectsSoftRules(weekHistory, fillerId, vacated.Id, shiftCount, allowConsecutive, allowOverMax: false))
                    continue;

                assigned[donorId] = target;
                assigned[fillerId] = vacated;
                return true;
            }
        }

        return false;
    }

    private static bool MoveRespectsSoftRules(
        IReadOnlyDictionary<int, List<int>> weekHistory,
        int userId,
        int targetId,
        int shiftCount,
        bool allowConsecutive,
        bool allowOverMax)
    {
        var yesterday = ShiftDispersionSelector.YesterdayShiftId(weekHistory, userId);
        var weekCount = ShiftDispersionSelector.CountThisWeek(weekHistory, userId, targetId);
        var workedDays = (weekHistory.TryGetValue(userId, out var hist) ? hist.Count : 0) + 1;
        var maxSame = ShiftDispersionSelector.EffectiveMaxSame(shiftCount, workedDays);
        if (yesterday == targetId)
        {
            var streak = ShiftDispersionSelector.ConsecutiveStreak(weekHistory, userId);
            if (streak >= 2)
                return false;
            if (!allowConsecutive)
                return false;
        }
        if (weekCount >= maxSame && !allowOverMax) return false;
        return true;
    }

    private static int CountWeekOverruns(
        IReadOnlyDictionary<int, List<int>> weekHistory,
        int userId,
        int shiftCount)
    {
        if (!weekHistory.TryGetValue(userId, out var hist) || hist.Count == 0)
            return 0;
        var maxSame = ShiftDispersionSelector.EffectiveMaxSame(shiftCount, hist.Count + 1);
        if (maxSame == int.MaxValue) return 0;
        return hist
            .GroupBy(id => id)
            .Sum(g => Math.Max(0, g.Count() - maxSame));
    }

    private static int Surplus(SubServiceShiftConfig current, Dictionary<int, int> counts)
    {
        var have = counts.GetValueOrDefault(current.Id, 0);
        var need = Math.Max(current.RequiredCount, 0);
        return have - need;
    }

    private static bool PlateauAllows(
        int userId,
        SubServiceShiftConfig target,
        IReadOnlyDictionary<int, User> usersById)
    {
        if (!usersById.TryGetValue(userId, out var u) || !u.IsPlateauTraining)
            return true;
        if (target.ShiftKind is ShiftKind.Opening or ShiftKind.Closing)
            return false;
        return true;
    }

    private static Dictionary<int, int> CountByShift(Dictionary<int, SubServiceShiftConfig> assigned)
    {
        var counts = new Dictionary<int, int>();
        foreach (var s in assigned.Values)
            counts[s.Id] = counts.GetValueOrDefault(s.Id, 0) + 1;
        return counts;
    }
}
