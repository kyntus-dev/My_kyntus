using Planning.Domain.Entities;

namespace Planning.Infrastructure.Services;

/// <summary>
/// Évalue si le retrait (ou le retour) d'un agent sur une semaine casse
/// MinPresence plateau ou la couverture (quotas shifts) — jours ouverts uniquement.
/// </summary>
public static class PlanningLeaveImpactEvaluator
{
    /// <summary>
    /// true = régénération nécessaire ; false = mise à jour chirurgicale suffisante.
    /// </summary>
    /// <param name="regenerateFrom">Premier jour encore régénérable (cutoff 15h).</param>
    public static bool NeedsRegen(
        WeeklyPlanning planning,
        IReadOnlyList<SubServiceShiftConfig> shiftConfigs,
        int userId,
        DateOnly leaveStart,
        DateOnly leaveEnd,
        bool absenceRemoved,
        DateOnly regenerateFrom)
    {
        if (shiftConfigs.Count == 0)
            return false;

        var configsById = shiftConfigs.ToDictionary(c => c.Id);
        var cellMinPresence = shiftConfigs[0].MinPresencePercent <= 0
            ? 0
            : Math.Clamp(shiftConfigs[0].MinPresencePercent, 50, 100);

        var weekStart = planning.WeekStartDate;
        var weekEnd = weekStart.AddDays(5);

        var overlapStart = leaveStart > weekStart ? leaveStart : weekStart;
        var overlapEnd = leaveEnd < weekEnd ? leaveEnd : weekEnd;
        if (overlapEnd < overlapStart)
            return false;

        // Uniquement les jours encore ouverts à la regen
        var openStart = overlapStart > regenerateFrom ? overlapStart : regenerateFrom;
        if (openStart > overlapEnd)
            return false;

        var assignments = planning.ShiftAssignments?.ToList() ?? new List<ShiftAssignment>();

        if (absenceRemoved)
            return NeedsRegenAfterAbsenceRemoved(assignments, userId, openStart, overlapEnd);

        for (var date = openStart; date <= overlapEnd; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Sunday)
                continue;

            var userDay = assignments.FirstOrDefault(a =>
                a.UserId == userId
                && a.AssignedDate == date
                && !a.IsHoliday);

            if (userDay is null
                || userDay.IsOnLeave
                || userDay.SubServiceShiftConfigId is null)
                continue;

            var dayPresent = assignments
                .Where(a =>
                    a.AssignedDate == date
                    && a.SubServiceShiftConfigId != null
                    && !a.IsOnLeave
                    && !a.IsHoliday)
                .ToList();

            var after = dayPresent.Where(a => a.UserId != userId).ToList();

            if (PresenceBroken(dayPresent, after, configsById, cellMinPresence))
                return true;

            if (CoverageBroken(date, dayPresent, after, shiftConfigs, userDay.SubServiceShiftConfigId.Value))
                return true;

            if (date.DayOfWeek == DayOfWeek.Saturday && after.Count == 0 && dayPresent.Count > 0)
                return true;
        }

        return false;
    }

    private static bool NeedsRegenAfterAbsenceRemoved(
        List<ShiftAssignment> assignments,
        int userId,
        DateOnly openStart,
        DateOnly overlapEnd)
    {
        var leaveRows = assignments
            .Where(a =>
                a.UserId == userId
                && a.IsOnLeave
                && a.AssignedDate >= openStart
                && a.AssignedDate <= overlapEnd)
            .ToList();

        if (leaveRows.Count == 0)
            return false;

        return leaveRows.Any(a => a.SubServiceShiftConfigId is null);
    }

    private static bool PresenceBroken(
        List<ShiftAssignment> before,
        List<ShiftAssignment> after,
        IReadOnlyDictionary<int, SubServiceShiftConfig> configsById,
        int cellMinPresence)
    {
        if (after.Count == 0)
            return before.Count > 0;

        if (cellMinPresence <= 0)
            return false;

        static (List<PlateauBreakPacker.ShiftRange> ranges, List<PlateauBreakPacker.BreakPlacement> breaks)
            Snapshot(List<ShiftAssignment> day, IReadOnlyDictionary<int, SubServiceShiftConfig> byId)
        {
            var ranges = new List<PlateauBreakPacker.ShiftRange>();
            var breaks = new List<PlateauBreakPacker.BreakPlacement>();
            foreach (var a in day)
            {
                if (a.SubServiceShiftConfigId is null || !byId.TryGetValue(a.SubServiceShiftConfigId.Value, out var cfg))
                    continue;
                ranges.Add(new PlateauBreakPacker.ShiftRange(cfg.StartTime, cfg.EndTime));
                if (a.BreakTime is TimeOnly bt)
                {
                    var dur = cfg.BreakDurationMinutes > 0 ? cfg.BreakDurationMinutes : 60;
                    breaks.Add(new PlateauBreakPacker.BreakPlacement(bt, dur));
                }
            }
            return (ranges, breaks);
        }

        var (rangesBefore, breaksBefore) = Snapshot(before, configsById);
        var (rangesAfter, breaksAfter) = Snapshot(after, configsById);

        if (rangesAfter.Count == 0)
            return rangesBefore.Count > 0;

        var okBefore = PlateauBreakPacker.DayRespectsPresence(rangesBefore, breaksBefore, cellMinPresence);
        var okAfter = PlateauBreakPacker.DayRespectsPresence(rangesAfter, breaksAfter, cellMinPresence);
        return okBefore && !okAfter;
    }

    private static bool CoverageBroken(
        DateOnly date,
        List<ShiftAssignment> before,
        List<ShiftAssignment> after,
        IReadOnlyList<SubServiceShiftConfig> shiftConfigs,
        int affectedShiftConfigId)
    {
        if (date.DayOfWeek is DayOfWeek.Saturday)
        {
            var beforeOnShift = before.Count(a => a.SubServiceShiftConfigId == affectedShiftConfigId);
            var afterOnShift = after.Count(a => a.SubServiceShiftConfigId == affectedShiftConfigId);
            return beforeOnShift > 0 && afterOnShift == 0;
        }

        foreach (var cfg in shiftConfigs)
        {
            if (cfg.RequiredCount <= 0) continue;

            var assignedAfter = after.Count(a => a.SubServiceShiftConfigId == cfg.Id);
            var assignedBefore = before.Count(a => a.SubServiceShiftConfigId == cfg.Id);

            // v50 : le départ laisse le shift sous son quota → il faut regénérer pour que
            // quelqu'un d'autre le couvre. Avant, la couverture n'était rétablie que si le shift
            // était pile à son quota : dans une cellule déjà en sous-effectif, le congé validé
            // creusait le trou sans jamais réaffecter personne.
            if (assignedAfter < cfg.RequiredCount && assignedAfter < assignedBefore)
                return true;

            if (assignedBefore > 0 && assignedAfter == 0 && cfg.Id == affectedShiftConfigId)
                return true;
        }

        return false;
    }
}
