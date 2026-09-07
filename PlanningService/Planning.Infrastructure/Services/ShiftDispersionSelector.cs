using Planning.Domain.Entities;
using Planning.Domain.Enums;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Dispersion + équité : max 2× le même shift / semaine, pas de consécutif,
/// répartition équilibrée (priorité même niveau).
/// </summary>
public static class ShiftDispersionSelector
{
    public const int MaxSameShiftPerWeek = 2;
    private const int FairnessRepairMaxPasses = 40;
    private const int DispersionRepairMaxPasses = 24;
    private const int OverrunPenaltyWeight = 100;

    /// <summary>
    /// Max d’occurrences d’un même shift. Si jours_travaillés &gt; 2 × nb_shifts,
    /// un « extra » (3×) est inévitable.
    /// </summary>
    public static int EffectiveMaxSame(int shiftCount, int workedDays)
    {
        if (shiftCount <= 1) return int.MaxValue;
        if (workedDays > MaxSameShiftPerWeek * shiftCount)
            return MaxSameShiftPerWeek + 1;
        return MaxSameShiftPerWeek;
    }

    public sealed class SelectionResult
    {
        public required SubServiceShiftConfig Shift { get; init; }
        public bool SoftConsecutiveAllowed { get; init; }
        public bool SoftQuotaExceeded { get; init; }

        public bool HasSoftViolation => SoftConsecutiveAllowed || SoftQuotaExceeded;
    }

    public static List<SubServiceShiftConfig> PreferredOrder(
        IReadOnlyList<SubServiceShiftConfig> orderedShifts,
        int startIdx,
        int dayIdx)
    {
        var n = orderedShifts.Count;
        if (n == 0) return new List<SubServiceShiftConfig>();

        var result = new List<SubServiceShiftConfig>(n);
        for (var i = 0; i < n; i++)
            result.Add(orderedShifts[(startIdx + dayIdx + i) % n]);
        return result;
    }

    public static int CountThisWeek(
        IReadOnlyDictionary<int, List<int>> weekAssignmentsByUser,
        int userId,
        int shiftConfigId)
    {
        if (!weekAssignmentsByUser.TryGetValue(userId, out var list) || list.Count == 0)
            return 0;
        return list.Count(id => id == shiftConfigId);
    }

    public static int? YesterdayShiftId(
        IReadOnlyDictionary<int, List<int>> weekAssignmentsByUser,
        int userId)
    {
        if (!weekAssignmentsByUser.TryGetValue(userId, out var list) || list.Count == 0)
            return null;
        return list[^1];
    }

    /// <summary>Nombre de jours travaillés d’affilée sur le dernier shift (fin d’historique).</summary>
    public static int ConsecutiveStreak(
        IReadOnlyDictionary<int, List<int>> weekAssignmentsByUser,
        int userId)
    {
        if (!weekAssignmentsByUser.TryGetValue(userId, out var list) || list.Count == 0)
            return 0;
        var last = list[^1];
        var n = 0;
        for (var i = list.Count - 1; i >= 0 && list[i] == last; i--)
            n++;
        return n;
    }

    private static int EffectiveMax(int shiftCount, int workedDays = 0) =>
        EffectiveMaxSame(shiftCount, workedDays);

    /// <summary>
    /// Moyenne des occurrences d'un shift parmi les users du même Level (hors user courant).
    /// </summary>
    private static double PeerAverageForShift(
        int shiftConfigId,
        int userId,
        int userLevel,
        IReadOnlyDictionary<int, List<int>> weekHistory,
        IReadOnlyDictionary<int, User>? usersById)
    {
        if (usersById == null || usersById.Count == 0)
            return 0;

        var peers = usersById.Values
            .Where(u => u.Id != userId && u.Level == userLevel)
            .ToList();
        if (peers.Count == 0)
        {
            peers = usersById.Values.Where(u => u.Id != userId).ToList();
            if (peers.Count == 0) return 0;
        }

        return peers.Average(u => CountThisWeek(weekHistory, u.Id, shiftConfigId));
    }

    /// <summary>
    /// Choisit un shift : remplit d'abord les quotas prod (RequiredCount),
    /// puis max2 / non-consécutif / équité. Ne dépasse un quota que si
    /// tous les sièges requis sont déjà pourvus (surplus d'effectif).
    /// </summary>
    public static SelectionResult Select(
        IReadOnlyList<SubServiceShiftConfig> orderedShifts,
        int startIdx,
        int dayIdx,
        int userId,
        IReadOnlyDictionary<int, List<int>> weekAssignmentsByUser,
        IReadOnlyDictionary<int, int> shiftCountToday,
        IReadOnlyDictionary<int, User>? usersById = null)
    {
        var preferred = PreferredOrder(orderedShifts, startIdx, dayIdx);
        if (preferred.Count == 0)
            throw new InvalidOperationException("Aucun shift configuré.");

        var maxSame = EffectiveMax(orderedShifts.Count);
        var yesterdayId = YesterdayShiftId(weekAssignmentsByUser, userId);
        var userLevel = usersById != null && usersById.TryGetValue(userId, out var me)
            ? me.Level
            : 0;
        var plateauTraining = usersById != null
                              && usersById.TryGetValue(userId, out var plateauUser)
                              && plateauUser.IsPlateauTraining;

        // Quota 0 = aucun siège à pourvoir. Ne pas le traiter comme un « trou » à remplir.
        bool UnderQuota(SubServiceShiftConfig s) =>
            s.RequiredCount > 0
            && shiftCountToday.GetValueOrDefault(s.Id, 0) < s.RequiredCount;

        bool UnderMax(SubServiceShiftConfig s) =>
            CountThisWeek(weekAssignmentsByUser, userId, s.Id) < maxSame;

        // Formation plateau : jamais Opening/Closing si un Standard existe en config
        // (même hors quota / max2 — pas de repli sur extrêmes).
        var hasStandardConfig = preferred.Any(s => s.ShiftKind == ShiftKind.Standard);
        List<SubServiceShiftConfig> PlateauFilter(IEnumerable<SubServiceShiftConfig> cands)
        {
            var list = cands.ToList();
            if (!plateauTraining || list.Count == 0) return list;
            if (!hasStandardConfig) return list;
            return list.Where(s => s.ShiftKind == ShiftKind.Standard).ToList();
        }

        SubServiceShiftConfig RankPick(IEnumerable<SubServiceShiftConfig> candidates) =>
            candidates
                .OrderBy(s => CountThisWeek(weekAssignmentsByUser, userId, s.Id))
                .ThenBy(s => CountThisWeek(weekAssignmentsByUser, userId, s.Id)
                             - PeerAverageForShift(s.Id, userId, userLevel, weekAssignmentsByUser, usersById))
                .ThenBy(s => preferred.IndexOf(s))
                .First();

        // 1) Strict : siège libre + non-consécutif + max2 + équité
        var strict = PlateauFilter(preferred.Where(s =>
            UnderQuota(s) && s.Id != yesterdayId && UnderMax(s)));
        if (strict.Count > 0)
            return new SelectionResult { Shift = RankPick(strict) };

        // 2) Siège libre prioritaire, encore sans consécutif
        var underQuotaNotConsec = PlateauFilter(preferred.Where(s =>
            UnderQuota(s) && s.Id != yesterdayId));
        if (underQuotaNotConsec.Count > 0)
            return new SelectionResult { Shift = RankPick(underQuotaNotConsec) };

        var streak = ConsecutiveStreak(weekAssignmentsByUser, userId);
        var canRepeatYesterday = yesterdayId.HasValue && streak < 2;

        // 3) Dernier recours : 2e jour identique uniquement si aucun autre siège
        if (canRepeatYesterday)
        {
            var underQuotaMax = PlateauFilter(preferred.Where(s => UnderQuota(s) && UnderMax(s)));
            if (underQuotaMax.Count > 0)
                return new SelectionResult
                {
                    Shift = RankPick(underQuotaMax),
                    SoftConsecutiveAllowed = true
                };

            var underQuotaAny = PlateauFilter(preferred.Where(UnderQuota));
            if (underQuotaAny.Count > 0)
                return new SelectionResult
                {
                    Shift = RankPick(underQuotaAny),
                    SoftConsecutiveAllowed = true
                };
        }

        // 4) Quotas remplis → surplus, sans consécutif
        var underCapAlt = PlateauFilter(preferred.Where(s => UnderMax(s) && s.Id != yesterdayId));
        if (underCapAlt.Count > 0)
        {
            var chosen = RankPick(underCapAlt);
            return new SelectionResult { Shift = chosen, SoftQuotaExceeded = true };
        }

        if (canRepeatYesterday)
        {
            var underCapAny = PlateauFilter(preferred.Where(UnderMax));
            if (underCapAny.Count > 0)
            {
                var chosen = RankPick(underCapAny);
                return new SelectionResult
                {
                    Shift = chosen,
                    SoftQuotaExceeded = true,
                    SoftConsecutiveAllowed = chosen.Id == yesterdayId
                };
            }
        }

        var lastPool = PlateauFilter(preferred.Where(s => s.Id != yesterdayId || canRepeatYesterday));
        if (lastPool.Count == 0)
            lastPool = PlateauFilter(preferred);
        return new SelectionResult
        {
            Shift = RankPick(lastPool),
            SoftQuotaExceeded = true,
            SoftConsecutiveAllowed = true
        };
    }

    /// <summary>
    /// Quota samedi = moitié de l'effectif configuré (Lun–Ven),
    /// sauf si le shift est déjà un template samedi dédié (AppliesToSaturday).
    /// Ex. 20 → 10 ; 19 → 9 ; minimum 1 si le quota semaine est &gt; 0.
    /// </summary>
    public static int SaturdayRequiredCount(int weekRequiredCount)
    {
        if (weekRequiredCount <= 0) return 0;
        return Math.Max(1, weekRequiredCount / 2);
    }

    public static int EffectiveSaturdayQuota(SubServiceShiftConfig config)
        => config.AppliesToSaturday
            ? config.RequiredCount
            : SaturdayRequiredCount(config.RequiredCount);

    public static SubServiceShiftConfig SelectSaturday(
        IReadOnlyList<SubServiceShiftConfig> orderedByStart,
        int preferredIndex,
        int? fridayShiftId,
        IReadOnlyDictionary<int, List<int>> weekAssignmentsByUser,
        int userId,
        IReadOnlyDictionary<int, User>? usersById = null,
        IReadOnlyDictionary<int, int>? shiftCountToday = null)
    {
        if (orderedByStart.Count == 0)
            throw new InvalidOperationException("Aucun shift configuré.");

        var maxSame = EffectiveMax(orderedByStart.Count);
        var preferred = new List<SubServiceShiftConfig>(orderedByStart.Count);
        for (var i = 0; i < orderedByStart.Count; i++)
            preferred.Add(orderedByStart[(preferredIndex + i) % orderedByStart.Count]);

        var userLevel = usersById != null && usersById.TryGetValue(userId, out var me)
            ? me.Level
            : 0;
        var plateauTraining = usersById != null
                              && usersById.TryGetValue(userId, out var plateauUser)
                              && plateauUser.IsPlateauTraining;
        shiftCountToday ??= new Dictionary<int, int>();

        bool UnderQuota(SubServiceShiftConfig s)
        {
            var quota = EffectiveSaturdayQuota(s);
            return quota > 0
                   && shiftCountToday.GetValueOrDefault(s.Id, 0) < quota;
        }

        bool UnderMax(SubServiceShiftConfig s) =>
            CountThisWeek(weekAssignmentsByUser, userId, s.Id) < maxSame;

        var hasStandardConfig = preferred.Any(s => s.ShiftKind == ShiftKind.Standard);
        List<SubServiceShiftConfig> PlateauFilter(IEnumerable<SubServiceShiftConfig> cands)
        {
            var list = cands.ToList();
            if (!plateauTraining || list.Count == 0) return list;
            if (!hasStandardConfig) return list;
            return list.Where(s => s.ShiftKind == ShiftKind.Standard).ToList();
        }

        SubServiceShiftConfig RankPick(IEnumerable<SubServiceShiftConfig> candidates) =>
            candidates
                .OrderBy(s => CountThisWeek(weekAssignmentsByUser, userId, s.Id))
                .ThenBy(s => CountThisWeek(weekAssignmentsByUser, userId, s.Id)
                             - PeerAverageForShift(s.Id, userId, userLevel, weekAssignmentsByUser, usersById))
                .ThenBy(s => preferred.IndexOf(s))
                .First();

        // 1) Strict : ≠ vendredi + max2 + siège libre + équité niveau
        var strict = PlateauFilter(preferred.Where(s =>
            s.Id != fridayShiftId && UnderMax(s) && UnderQuota(s)));
        if (strict.Count > 0)
            return RankPick(strict);

        // 2) Siège libre, toujours ≠ vendredi
        var underQuotaAlt = PlateauFilter(preferred.Where(s => UnderQuota(s) && s.Id != fridayShiftId));
        if (underQuotaAlt.Count > 0)
            return RankPick(underQuotaAlt);

        var satStreak = ConsecutiveStreak(weekAssignmentsByUser, userId);
        var canRepeatFriday = fridayShiftId.HasValue && satStreak < 2;

        // 3) Dernier recours = vendredi
        if (canRepeatFriday)
        {
            var underQuotaMax = PlateauFilter(preferred.Where(s => UnderQuota(s) && UnderMax(s)));
            if (underQuotaMax.Count > 0)
                return RankPick(underQuotaMax);

            var underQuotaAny = PlateauFilter(preferred.Where(UnderQuota));
            if (underQuotaAny.Count > 0)
                return RankPick(underQuotaAny);
        }

        var underCap = PlateauFilter(preferred.Where(s => UnderMax(s) && s.Id != fridayShiftId));
        if (underCap.Count > 0)
            return RankPick(underCap);

        if (canRepeatFriday)
        {
            var underCapAny = PlateauFilter(preferred.Where(UnderMax));
            if (underCapAny.Count > 0)
                return RankPick(underCapAny);
        }

        var last = PlateauFilter(preferred.Where(s => s.Id != fridayShiftId || canRepeatFriday));
        return RankPick(last.Count > 0 ? last : PlateauFilter(preferred));
    }

    /// <summary>
    /// Répare dispersion Lun–Sam (présents uniquement). Ne crée pas d’assignation samedi.
    /// N’accepte pas un swap qui laisserait un débutant seul sur un créneau.
    /// </summary>
    public static void RepairWeekdayDispersion(
        List<ShiftAssignment> assignments,
        List<SubServiceShiftConfig> shiftConfigs,
        IReadOnlyDictionary<int, User>? usersById = null)
    {
        _ = shiftConfigs;
        var workDays = assignments
            .Where(a => !a.IsOnLeave && !a.IsHoliday && a.SubServiceShiftConfigId != null)
            .GroupBy(a => a.AssignedDate)
            .OrderBy(g => g.Key)
            .ToList();

        if (workDays.Count == 0) return;

        for (var pass = 0; pass < DispersionRepairMaxPasses; pass++)
        {
            var swapped = false;
            for (var dayIndex = 0; dayIndex < workDays.Count; dayIndex++)
            {
                var dayGroup = workDays[dayIndex].ToList();
                var prevDate = dayIndex > 0 ? workDays[dayIndex - 1].Key : (DateOnly?)null;
                var nextDate = dayIndex + 1 < workDays.Count ? workDays[dayIndex + 1].Key : (DateOnly?)null;
                var isSaturday = dayGroup[0].AssignedDate.DayOfWeek == DayOfWeek.Saturday
                                 || dayGroup[0].IsSaturday;

                foreach (var a in dayGroup)
                {
                    if (a.IsManagerOverride) continue;
                    if (!NeedsRepair(a, assignments, prevDate, nextDate, shiftConfigs.Count))
                        continue;

                    foreach (var b in dayGroup)
                    {
                        if (b.UserId == a.UserId) continue;
                        if (b.IsManagerOverride) continue;
                        if (b.SubServiceShiftConfigId == a.SubServiceShiftConfigId) continue;
                        // Ne pas mélanger demi-journée débutant / journée pleine
                        if (a.IsHalfDaySaturday != b.IsHalfDaySaturday) continue;

                        if (!SwapImprovesDispersion(a, b, assignments, prevDate, nextDate, shiftConfigs.Count))
                            continue;

                        if (usersById != null
                            && !SwapPreservesBeginnerRule(a, b, dayGroup, usersById, isSaturday))
                            continue;

                        if (usersById != null
                            && !SwapPreservesPlateauTrainingRule(a, b, shiftConfigs, usersById))
                            continue;

                        (a.SubServiceShiftConfigId, b.SubServiceShiftConfigId) =
                            (b.SubServiceShiftConfigId, a.SubServiceShiftConfigId);
                        swapped = true;
                        break;
                    }
                }
            }

            if (!swapped) break;
        }
    }

    /// <summary>Dispersion puis équité (à rejouer après le repairer de niveau).</summary>
    public static void RepairWeekQuality(
        List<ShiftAssignment> assignments,
        List<SubServiceShiftConfig> shiftConfigs,
        IReadOnlyDictionary<int, User> usersById)
    {
        RepairWeekdayDispersion(assignments, shiftConfigs, usersById);
        RepairFairness(assignments, shiftConfigs, usersById);
    }

    /// <summary>
    /// Simule le swap a↔b et refuse s'il crée un créneau (ou un samedi) avec débutant seul.
    /// </summary>
    public static bool SwapPreservesBeginnerRule(
        ShiftAssignment a,
        ShiftAssignment b,
        IReadOnlyList<ShiftAssignment> dayGroup,
        IReadOnlyDictionary<int, User> usersById,
        bool isSaturday)
    {
        var idA = a.SubServiceShiftConfigId;
        var idB = b.SubServiceShiftConfigId;
        if (idA is null || idB is null) return false;

        // Snapshot virtuel des ids après swap
        List<ShiftAssignment> AfterSwapMembers(int shiftId)
        {
            return dayGroup
                .Where(x =>
                {
                    var sid = x.SubServiceShiftConfigId;
                    if (ReferenceEquals(x, a)) sid = idB;
                    else if (ReferenceEquals(x, b)) sid = idA;
                    return sid == shiftId && !x.IsOnLeave && !x.IsHoliday;
                })
                .ToList();
        }

        if (isSaturday)
        {
            // Swap ne change pas l'ensemble des présents du jour.
            return true;
        }

        foreach (var shiftId in new[] { idA.Value, idB.Value })
        {
            var group = AfterSwapMembers(shiftId);
            if (group.Count > 0 && LevelBalanceEvaluator.HasBeginnerAlone(group, usersById))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Refuse un swap qui placerait un agent en formation plateau en Opening/Closing.
    /// </summary>
    public static bool SwapPreservesPlateauTrainingRule(
        ShiftAssignment a,
        ShiftAssignment b,
        IReadOnlyList<SubServiceShiftConfig> shiftConfigs,
        IReadOnlyDictionary<int, User> usersById)
    {
        var configsById = shiftConfigs.ToDictionary(c => c.Id);
        var idA = a.SubServiceShiftConfigId;
        var idB = b.SubServiceShiftConfigId;
        if (idA is null || idB is null) return false;

        bool WouldBeExtreme(int userId, int newShiftId)
        {
            if (!usersById.TryGetValue(userId, out var u) || !u.IsPlateauTraining)
                return false;
            if (!configsById.TryGetValue(newShiftId, out var cfg))
                return false;
            return cfg.ShiftKind is ShiftKind.Opening or ShiftKind.Closing;
        }

        if (WouldBeExtreme(a.UserId, idB.Value)) return false;
        if (WouldBeExtreme(b.UserId, idA.Value)) return false;
        return true;
    }

    /// <summary>
    /// Swaps même niveau pour réduire les écarts (Lun–Sam, présents uniquement).
    /// </summary>
    public static void RepairFairness(
        List<ShiftAssignment> assignments,
        List<SubServiceShiftConfig> shiftConfigs,
        IReadOnlyDictionary<int, User> usersById)
    {
        if (shiftConfigs.Count <= 1) return;

        var workDays = assignments
            .Where(a => !a.IsOnLeave && !a.IsHoliday && a.SubServiceShiftConfigId != null)
            .GroupBy(a => a.AssignedDate)
            .OrderBy(g => g.Key)
            .ToList();

        if (workDays.Count == 0) return;

        for (var pass = 0; pass < FairnessRepairMaxPasses; pass++)
        {
            var improved = false;

            for (var dayIndex = 0; dayIndex < workDays.Count; dayIndex++)
            {
                var dayGroup = workDays[dayIndex].ToList();
                var prevDate = dayIndex > 0 ? workDays[dayIndex - 1].Key : (DateOnly?)null;
                var nextDate = dayIndex + 1 < workDays.Count ? workDays[dayIndex + 1].Key : (DateOnly?)null;

                for (var i = 0; i < dayGroup.Count; i++)
                {
                    for (var j = i + 1; j < dayGroup.Count; j++)
                    {
                        var a = dayGroup[i];
                        var b = dayGroup[j];
                        if (a.IsManagerOverride || b.IsManagerOverride) continue;
                        if (a.SubServiceShiftConfigId == b.SubServiceShiftConfigId) continue;
                        if (a.IsHalfDaySaturday != b.IsHalfDaySaturday) continue;

                        if (!usersById.TryGetValue(a.UserId, out var ua)
                            || !usersById.TryGetValue(b.UserId, out var ub)
                            || ua.Level != ub.Level)
                            continue;

                        if (!FairnessSwapAllowed(a, b, assignments, prevDate, nextDate, shiftConfigs.Count))
                            continue;

                        if (!FairnessSwapImproves(a, b, assignments, usersById, shiftConfigs))
                            continue;

                        if (!SwapPreservesPlateauTrainingRule(a, b, shiftConfigs, usersById))
                            continue;

                        var isSaturday = a.AssignedDate.DayOfWeek == DayOfWeek.Saturday || a.IsSaturday;
                        if (!SwapPreservesBeginnerRule(a, b, dayGroup, usersById, isSaturday))
                            continue;

                        (a.SubServiceShiftConfigId, b.SubServiceShiftConfigId) =
                            (b.SubServiceShiftConfigId, a.SubServiceShiftConfigId);
                        improved = true;
                    }
                }

                if (!improved && TrySameDayThreeCycle(dayGroup, assignments, shiftConfigs, usersById))
                    improved = true;
            }

            if (!improved && TryTwoDayFairnessRotations(assignments, shiftConfigs, usersById, workDays))
                improved = true;

            if (!improved) break;
        }
    }

    private static bool FairnessSwapAllowed(
        ShiftAssignment a,
        ShiftAssignment b,
        List<ShiftAssignment> all,
        DateOnly? prevDate,
        DateOnly? nextDate,
        int shiftCount)
    {
        var workedA = WorkedDays(all, a.UserId);
        var workedB = WorkedDays(all, b.UserId);
        var maxA = EffectiveMax(shiftCount, workedA);
        var maxB = EffectiveMax(shiftCount, workedB);
        var aNew = b.SubServiceShiftConfigId!.Value;
        var bNew = a.SubServiceShiftConfigId!.Value;
        var aOld = a.SubServiceShiftConfigId!.Value;
        var bOld = b.SubServiceShiftConfigId!.Value;

        var aWouldOver = WorkedCount(all, a.UserId, aNew, exclude: a) + 1 > maxA;
        var bWouldOver = WorkedCount(all, b.UserId, bNew, exclude: b) + 1 > maxB;
        if (aWouldOver || bWouldOver)
        {
            var before = TotalOverruns(all, shiftCount);
            a.SubServiceShiftConfigId = aNew;
            b.SubServiceShiftConfigId = bNew;
            var after = TotalOverruns(all, shiftCount);
            a.SubServiceShiftConfigId = aOld;
            b.SubServiceShiftConfigId = bOld;
            if (after >= before)
                return false;
        }

        var consecutive =
            AdjacentEquals(all, a.UserId, prevDate, aNew)
            || AdjacentEquals(all, a.UserId, nextDate, aNew)
            || AdjacentEquals(all, b.UserId, prevDate, bNew)
            || AdjacentEquals(all, b.UserId, nextDate, bNew);
        if (consecutive && !aWouldOver && !bWouldOver)
            return false;

        return true;
    }

    private static int TotalOverruns(List<ShiftAssignment> all, int shiftCount)
    {
        var total = 0;
        foreach (var g in all
                     .Where(x => !x.IsOnLeave && !x.IsHoliday && x.SubServiceShiftConfigId != null)
                     .GroupBy(x => x.UserId))
        {
            var maxSame = EffectiveMax(shiftCount, g.Count());
            if (maxSame == int.MaxValue) continue;
            total += g
                .GroupBy(x => x.SubServiceShiftConfigId!.Value)
                .Sum(sg => Math.Max(0, sg.Count() - maxSame));
        }

        return total;
    }

    private static bool AdjacentEquals(
        List<ShiftAssignment> all, int userId, DateOnly? date, int configId)
    {
        if (!date.HasValue) return false;
        var adj = all.FirstOrDefault(x =>
            x.UserId == userId
            && x.AssignedDate == date.Value
            && !x.IsOnLeave && !x.IsHoliday
            && x.SubServiceShiftConfigId != null);
        return adj?.SubServiceShiftConfigId == configId;
    }

    private static int WorkedCount(
        List<ShiftAssignment> all, int userId, int configId, ShiftAssignment? exclude = null) =>
        all.Count(x =>
            x.UserId == userId
            && !x.IsOnLeave && !x.IsHoliday
            && x.SubServiceShiftConfigId == configId
            && (exclude == null || !ReferenceEquals(x, exclude)));

    private static int WorkedDays(List<ShiftAssignment> all, int userId) =>
        all.Count(x =>
            x.UserId == userId
            && !x.IsOnLeave && !x.IsHoliday
            && x.SubServiceShiftConfigId != null);

    private static bool FairnessSwapImproves(
        ShiftAssignment a,
        ShiftAssignment b,
        List<ShiftAssignment> all,
        IReadOnlyDictionary<int, User> usersById,
        List<SubServiceShiftConfig> shiftConfigs)
    {
        var aOld = a.SubServiceShiftConfigId!.Value;
        var bOld = b.SubServiceShiftConfigId!.Value;
        var before = FairnessPenalty(all, usersById, shiftConfigs);
        // Simulate
        a.SubServiceShiftConfigId = bOld;
        b.SubServiceShiftConfigId = aOld;
        var after = FairnessPenalty(all, usersById, shiftConfigs);
        // Revert simulation (caller will re-apply if true)
        a.SubServiceShiftConfigId = aOld;
        b.SubServiceShiftConfigId = bOld;
        return after < before;
    }

    /// <summary>Écarts au carré à la moyenne du niveau, plus poids sur les dépassements max-2.</summary>
    private static int FairnessPenalty(
        List<ShiftAssignment> all,
        IReadOnlyDictionary<int, User> usersById,
        List<SubServiceShiftConfig> shiftConfigs)
    {
        var penalty = 0;
        var workers = all
            .Where(x => !x.IsOnLeave && !x.IsHoliday && x.SubServiceShiftConfigId != null)
            .Select(x => x.UserId)
            .Distinct()
            .Where(id => usersById.ContainsKey(id))
            .ToList();

        foreach (var levelGroup in workers.GroupBy(id => usersById[id].Level))
        {
            var ids = levelGroup.ToList();
            if (ids.Count < 2) continue;

            foreach (var cfg in shiftConfigs)
            {
                var counts = ids
                    .Select(uid => WorkedCount(all, uid, cfg.Id))
                    .ToList();
                var mean = counts.Average();
                for (var i = 0; i < ids.Count; i++)
                {
                    var d = counts[i] - mean;
                    penalty += (int)Math.Round(d * d * 10);
                    var maxSame = EffectiveMax(shiftConfigs.Count, WorkedDays(all, ids[i]));
                    if (maxSame != int.MaxValue && counts[i] > maxSame)
                        penalty += (counts[i] - maxSame) * OverrunPenaltyWeight;
                }
            }
        }

        return penalty;
    }

    private static bool TrySameDayThreeCycle(
        List<ShiftAssignment> dayGroup,
        List<ShiftAssignment> all,
        List<SubServiceShiftConfig> shiftConfigs,
        IReadOnlyDictionary<int, User> usersById)
    {
        var movable = dayGroup
            .Where(a =>
                !a.IsManagerOverride
                && !a.IsOnLeave
                && !a.IsHoliday
                && a.SubServiceShiftConfigId != null
                && usersById.ContainsKey(a.UserId))
            .ToList();
        if (movable.Count < 3) return false;

        var before = FairnessPenalty(all, usersById, shiftConfigs);
        var isSaturday = dayGroup[0].AssignedDate.DayOfWeek == DayOfWeek.Saturday
                         || dayGroup[0].IsSaturday;

        for (var i = 0; i < movable.Count; i++)
        for (var j = i + 1; j < movable.Count; j++)
        for (var k = j + 1; k < movable.Count; k++)
        {
            var a = movable[i];
            var b = movable[j];
            var c = movable[k];
            if (usersById[a.UserId].Level != usersById[b.UserId].Level
                || usersById[a.UserId].Level != usersById[c.UserId].Level)
                continue;
            if (a.IsHalfDaySaturday != b.IsHalfDaySaturday
                || a.IsHalfDaySaturday != c.IsHalfDaySaturday)
                continue;
            var idA = a.SubServiceShiftConfigId!.Value;
            var idB = b.SubServiceShiftConfigId!.Value;
            var idC = c.SubServiceShiftConfigId!.Value;
            if (idA == idB || idB == idC || idA == idC) continue;

            a.SubServiceShiftConfigId = idB;
            b.SubServiceShiftConfigId = idC;
            c.SubServiceShiftConfigId = idA;
            var after = FairnessPenalty(all, usersById, shiftConfigs);
            var ok = after < before
                     && PlateauOk(a, shiftConfigs, usersById)
                     && PlateauOk(b, shiftConfigs, usersById)
                     && PlateauOk(c, shiftConfigs, usersById)
                     && !DayHasBeginnerAlone(dayGroup, usersById, isSaturday);
            if (ok)
                return true;

            a.SubServiceShiftConfigId = idA;
            b.SubServiceShiftConfigId = idB;
            c.SubServiceShiftConfigId = idC;
        }

        return false;
    }

    private static bool TryTwoDayFairnessRotations(
        List<ShiftAssignment> all,
        List<SubServiceShiftConfig> shiftConfigs,
        IReadOnlyDictionary<int, User> usersById,
        List<IGrouping<DateOnly, ShiftAssignment>> workDays)
    {
        var before = FairnessPenalty(all, usersById, shiftConfigs);

        for (var d1 = 0; d1 < workDays.Count; d1++)
        for (var d2 = d1 + 1; d2 < workDays.Count; d2++)
        {
            var day1 = workDays[d1].ToList();
            var day2 = workDays[d2].ToList();
            var prev1 = d1 > 0 ? workDays[d1 - 1].Key : (DateOnly?)null;
            var next1 = d1 + 1 < workDays.Count ? workDays[d1 + 1].Key : (DateOnly?)null;
            var prev2 = d2 > 0 ? workDays[d2 - 1].Key : (DateOnly?)null;
            var next2 = d2 + 1 < workDays.Count ? workDays[d2 + 1].Key : (DateOnly?)null;
            var sat1 = day1[0].AssignedDate.DayOfWeek == DayOfWeek.Saturday || day1[0].IsSaturday;
            var sat2 = day2[0].AssignedDate.DayOfWeek == DayOfWeek.Saturday || day2[0].IsSaturday;

            var pairs1 = SameLevelPairs(day1, usersById);
            foreach (var (a1, b1) in pairs1)
            {
                var a2 = day2.FirstOrDefault(x => x.UserId == a1.UserId);
                var b2 = day2.FirstOrDefault(x => x.UserId == b1.UserId);
                if (a2?.SubServiceShiftConfigId == null || b2?.SubServiceShiftConfigId == null)
                    continue;
                if (a2.IsManagerOverride || b2.IsManagerOverride) continue;
                if (a2.IsHalfDaySaturday != b2.IsHalfDaySaturday) continue;
                if (a2.SubServiceShiftConfigId == b2.SubServiceShiftConfigId) continue;

                if (!FairnessSwapAllowed(a1, b1, all, prev1, next1, shiftConfigs.Count))
                    continue;
                if (!FairnessSwapAllowed(a2, b2, all, prev2, next2, shiftConfigs.Count))
                    continue;

                var a1Old = a1.SubServiceShiftConfigId;
                var b1Old = b1.SubServiceShiftConfigId;
                var a2Old = a2.SubServiceShiftConfigId;
                var b2Old = b2.SubServiceShiftConfigId;

                (a1.SubServiceShiftConfigId, b1.SubServiceShiftConfigId) = (b1Old, a1Old);
                (a2.SubServiceShiftConfigId, b2.SubServiceShiftConfigId) = (b2Old, a2Old);

                var after = FairnessPenalty(all, usersById, shiftConfigs);
                var ok = after < before
                         && PlateauOk(a1, shiftConfigs, usersById)
                         && PlateauOk(b1, shiftConfigs, usersById)
                         && PlateauOk(a2, shiftConfigs, usersById)
                         && PlateauOk(b2, shiftConfigs, usersById)
                         && !DayHasBeginnerAlone(day1, usersById, sat1)
                         && !DayHasBeginnerAlone(day2, usersById, sat2);
                if (ok)
                    return true;

                a1.SubServiceShiftConfigId = a1Old;
                b1.SubServiceShiftConfigId = b1Old;
                a2.SubServiceShiftConfigId = a2Old;
                b2.SubServiceShiftConfigId = b2Old;
            }
        }

        return false;
    }

    private static bool DayHasBeginnerAlone(
        IReadOnlyList<ShiftAssignment> dayGroup,
        IReadOnlyDictionary<int, User> usersById,
        bool isSaturday)
    {
        if (isSaturday) return false;
        foreach (var g in dayGroup
                     .Where(x => x.SubServiceShiftConfigId != null && !x.IsOnLeave && !x.IsHoliday)
                     .GroupBy(x => x.SubServiceShiftConfigId!.Value))
        {
            if (LevelBalanceEvaluator.HasBeginnerAlone(g, usersById))
                return true;
        }

        return false;
    }

    private static bool PlateauOk(
        ShiftAssignment a,
        IReadOnlyList<SubServiceShiftConfig> shiftConfigs,
        IReadOnlyDictionary<int, User> usersById)
    {
        if (!usersById.TryGetValue(a.UserId, out var u) || !u.IsPlateauTraining)
            return true;
        if (a.SubServiceShiftConfigId is null) return true;
        var cfg = shiftConfigs.FirstOrDefault(c => c.Id == a.SubServiceShiftConfigId.Value);
        return cfg is null || cfg.ShiftKind is not (ShiftKind.Opening or ShiftKind.Closing);
    }

    private static List<(ShiftAssignment A, ShiftAssignment B)> SameLevelPairs(
        List<ShiftAssignment> day,
        IReadOnlyDictionary<int, User> usersById)
    {
        var pairs = new List<(ShiftAssignment, ShiftAssignment)>();
        for (var i = 0; i < day.Count; i++)
        for (var j = i + 1; j < day.Count; j++)
        {
            var a = day[i];
            var b = day[j];
            if (a.IsManagerOverride || b.IsManagerOverride) continue;
            if (a.SubServiceShiftConfigId == null || b.SubServiceShiftConfigId == null) continue;
            if (a.SubServiceShiftConfigId == b.SubServiceShiftConfigId) continue;
            if (a.IsHalfDaySaturday != b.IsHalfDaySaturday) continue;
            if (!usersById.TryGetValue(a.UserId, out var ua)
                || !usersById.TryGetValue(b.UserId, out var ub)
                || ua.Level != ub.Level)
                continue;
            pairs.Add((a, b));
        }

        return pairs;
    }

    private static bool NeedsRepair(
        ShiftAssignment a,
        List<ShiftAssignment> all,
        DateOnly? prevDate,
        DateOnly? nextDate,
        int shiftCount)
    {
        var maxSame = EffectiveMax(shiftCount, WorkedDays(all, a.UserId));
        var cfgId = a.SubServiceShiftConfigId!.Value;

        if (AdjacentEquals(all, a.UserId, prevDate, cfgId)
            || AdjacentEquals(all, a.UserId, nextDate, cfgId))
            return true;

        var count = all.Count(x =>
            x.UserId == a.UserId
            && !x.IsOnLeave && !x.IsHoliday
            && x.SubServiceShiftConfigId == cfgId);

        return count > maxSame;
    }

    private static bool SwapImprovesDispersion(
        ShiftAssignment a,
        ShiftAssignment b,
        List<ShiftAssignment> all,
        DateOnly? prevDate,
        DateOnly? nextDate,
        int shiftCount)
    {
        var aId = a.SubServiceShiftConfigId!.Value;
        var bId = b.SubServiceShiftConfigId!.Value;

        var aBadBefore = ViolationScore(a.UserId, aId, a.AssignedDate, all, prevDate, nextDate, shiftCount);
        var bBadBefore = ViolationScore(b.UserId, bId, b.AssignedDate, all, prevDate, nextDate, shiftCount);
        var aBadAfter = ViolationScore(a.UserId, bId, a.AssignedDate, all, prevDate, nextDate, shiftCount, a, bId);
        var bBadAfter = ViolationScore(b.UserId, aId, b.AssignedDate, all, prevDate, nextDate, shiftCount, b, aId);

        return (aBadAfter + bBadAfter) < (aBadBefore + bBadBefore);
    }

    private static int ViolationScore(
        int userId,
        int configId,
        DateOnly date,
        List<ShiftAssignment> all,
        DateOnly? prevDate,
        DateOnly? nextDate,
        int shiftCount,
        ShiftAssignment? replaceAssignment = null,
        int? replaceWithConfigId = null)
    {
        var maxSame = EffectiveMax(shiftCount, WorkedDays(all, userId));
        var score = 0;
        var effectiveForDate = replaceAssignment != null && replaceWithConfigId.HasValue
            ? replaceWithConfigId.Value
            : configId;

        if (prevDate.HasValue)
        {
            var yesterday = all.FirstOrDefault(x =>
                x.UserId == userId
                && x.AssignedDate == prevDate.Value
                && !x.IsOnLeave && !x.IsHoliday
                && x.SubServiceShiftConfigId != null);
            if (yesterday?.SubServiceShiftConfigId == effectiveForDate)
                score += 2;
        }

        if (nextDate.HasValue)
        {
            var tomorrow = all.FirstOrDefault(x =>
                x.UserId == userId
                && x.AssignedDate == nextDate.Value
                && !x.IsOnLeave && !x.IsHoliday
                && x.SubServiceShiftConfigId != null);
            if (tomorrow?.SubServiceShiftConfigId == effectiveForDate)
                score += 2;
        }

        var count = 0;
        foreach (var x in all.Where(x =>
                     x.UserId == userId
                     && !x.IsOnLeave && !x.IsHoliday
                     && x.SubServiceShiftConfigId != null))
        {
            var id = replaceAssignment != null
                     && ReferenceEquals(x, replaceAssignment)
                     && replaceWithConfigId.HasValue
                ? replaceWithConfigId.Value
                : x.SubServiceShiftConfigId!.Value;
            if (id == effectiveForDate)
                count++;
        }

        if (count > maxSame)
            score += (count - maxSame) * 3;

        return score;
    }

    public static List<string> BuildDispersionWarnings(
        List<ShiftAssignment> assignments,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, SubServiceShiftConfig>? configsById = null)
    {
        var warnings = new List<string>();
        var byUser = assignments
            .Where(a => !a.IsOnLeave && !a.IsHoliday && a.SubServiceShiftConfigId != null)
            .GroupBy(a => a.UserId);

        foreach (var g in byUser)
        {
            var name = usersById.TryGetValue(g.Key, out var u)
                ? UserDisplayName.Format(u.FirstName, u.LastName)
                : $"#{g.Key}";

            var ordered = g.OrderBy(a => a.AssignedDate).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].SubServiceShiftConfigId == ordered[i - 1].SubServiceShiftConfigId)
                {
                    warnings.Add(
                        $"{name} : même shift sur jours consécutifs ({ordered[i - 1].AssignedDate:dd/MM}–{ordered[i].AssignedDate:dd/MM})");
                    break;
                }
            }

            foreach (var byShift in ordered.GroupBy(a => a.SubServiceShiftConfigId!.Value))
            {
                if (byShift.Count() > MaxSameShiftPerWeek)
                {
                    var label = configsById != null && configsById.TryGetValue(byShift.Key, out var cfg)
                        ? cfg.Label
                        : byShift.First().SubServiceShiftConfig?.Label ?? byShift.Key.ToString();
                    warnings.Add(
                        $"{name} : shift « {label} » {byShift.Count()}× sur la semaine (max {MaxSameShiftPerWeek})");
                }
            }
        }

        // Écarts inter-employés même niveau
        if (configsById != null && configsById.Count > 0)
        {
            var workers = byUser.Select(g => g.Key).Where(usersById.ContainsKey).ToList();
            foreach (var levelGroup in workers.GroupBy(id => usersById[id].Level))
            {
                var ids = levelGroup.ToList();
                if (ids.Count < 2) continue;
                var levelLabel = levelGroup.Key switch
                {
                    1 => "Débutant",
                    2 => "Confirmé",
                    _ => levelGroup.Key >= 3 ? "Expert" : $"N{levelGroup.Key}"
                };

                foreach (var cfg in configsById.Values)
                {
                    var counts = ids
                        .Select(uid => (
                            uid,
                            name: UserDisplayName.Format(usersById[uid].FirstName, usersById[uid].LastName),
                            n: WorkedCount(assignments, uid, cfg.Id)))
                        .ToList();
                    var min = counts.Min(c => c.n);
                    var max = counts.Max(c => c.n);
                    if (max - min <= 1) continue;

                    var rich = counts.First(c => c.n == max);
                    var poor = counts.First(c => c.n == min);
                    warnings.Add(
                        $"Écart « {cfg.Label} » ({levelLabel}) : {rich.name} a {rich.n}, {poor.name} a {poor.n}");
                }
            }
        }

        return warnings;
    }
}
