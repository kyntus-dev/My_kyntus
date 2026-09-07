using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planning.Application.DTOs.Planning;
using Planning.Application.Exceptions;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Hubs;

namespace Planning.Infrastructure.Services;

public partial class PlanningService
{
    private async Task<WeekShiftConfigResponseDto> SaveMultiModeShiftConfigAsync(
        SaveShiftConfigDto dto, bool isTemplate)
    {
        var subService = await _context.SubServices.FindAsync(dto.SubServiceId)
            ?? throw new InvalidOperationException("Sous-service introuvable.");

        if (dto.Modes == null || dto.Modes.Count == 0)
            throw new InvalidOperationException("Au moins un mode actif est requis.");

        foreach (var mode in dto.Modes)
        {
            if (string.IsNullOrWhiteSpace(mode.Title))
                throw new InvalidOperationException("Chaque mode doit avoir un titre.");
            if (mode.Shifts == null || mode.Shifts.Count == 0)
                throw new InvalidOperationException(
                    $"Le mode « {mode.Title} » doit contenir au moins un shift.");
            if (mode.Shifts.Count > 8)
                throw new InvalidOperationException(
                    $"Le mode « {mode.Title} » ne peut pas dépasser 8 shifts.");
            if (!dto.UseDistinctSaturdayShifts)
                mode.SaturdayShifts = new List<ShiftConfigItemDto>();

            if (mode.SaturdayShifts is { Count: > 8 })
                throw new InvalidOperationException(
                    $"Le mode « {mode.Title} » : le samedi ne peut pas dépasser 8 shifts.");

            ValidateModePercentages(mode);
        }

        var activeModes = dto.Modes.Where(m => m.IsActive).ToList();
        if (activeModes.Count == 0)
            throw new InvalidOperationException("Au moins un mode actif est requis.");

        subService.MultiShiftModesEnabled = true;

        var existingProfiles = await _context.ShiftModeProfiles
            .Where(p => p.SubServiceId == dto.SubServiceId)
            .ToListAsync();

        var keptProfileIds = new HashSet<int>();
        var modeToProfile = new List<(ShiftModeProfileSaveDto Dto, ShiftModeProfile Profile)>();

        for (var i = 0; i < dto.Modes.Count; i++)
        {
            var modeDto = dto.Modes[i];
            ShiftModeProfile? profile = null;
            if (modeDto.Id is int id && id > 0)
                profile = existingProfiles.FirstOrDefault(p => p.Id == id);

            if (profile == null)
            {
                profile = new ShiftModeProfile
                {
                    SubServiceId = dto.SubServiceId,
                    CreatedAt = DateTime.UtcNow
                };
                _context.ShiftModeProfiles.Add(profile);
                existingProfiles.Add(profile);
            }

            profile.Title = modeDto.Title.Trim();
            profile.DisplayOrder = modeDto.DisplayOrder > 0 ? modeDto.DisplayOrder : i + 1;
            profile.IsDefault = modeDto.IsDefault;
            profile.IsActive = modeDto.IsActive;
            profile.MinPresencePercent = modeDto.MinPresencePercent <= 0
                ? 0
                : Math.Clamp(modeDto.MinPresencePercent, 50, 100);
            profile.IsCriticalCell = modeDto.IsCriticalCell;
            profile.UpdatedAt = DateTime.UtcNow;
            if (profile.IsActive)
                profile.ArchivedAt = null;

            if (profile.Id > 0)
                keptProfileIds.Add(profile.Id);

            modeToProfile.Add((modeDto, profile));
        }

        // Un seul défaut parmi les actifs
        var defaults = modeToProfile.Where(x => x.Profile.IsActive && x.Profile.IsDefault).ToList();
        if (defaults.Count == 0)
        {
            var firstActive = modeToProfile.First(x => x.Profile.IsActive);
            firstActive.Profile.IsDefault = true;
        }
        else if (defaults.Count > 1)
        {
            for (var i = 1; i < defaults.Count; i++)
                defaults[i].Profile.IsDefault = false;
        }

        var removedProfiles = existingProfiles
            .Where(p => p.Id > 0 && !keptProfileIds.Contains(p.Id))
            .ToList();

        if (removedProfiles.Count > 0)
        {
            var removedIds = removedProfiles.Select(p => p.Id).ToList();
            var usedIds = await _context.WeeklyEmployeeShiftModes
                .AsNoTracking()
                .Where(e => removedIds.Contains(e.ShiftModeProfileId))
                .Select(e => e.ShiftModeProfileId)
                .Distinct()
                .ToListAsync();

            var usedSet = usedIds.ToHashSet();
            foreach (var profile in removedProfiles)
            {
                profile.IsActive = false;
                profile.IsDefault = false;
                profile.ArchivedAt ??= DateTime.UtcNow;
                profile.UpdatedAt = DateTime.UtcNow;

                if (!usedSet.Contains(profile.Id))
                {
                    var orphanConfigs = await _context.SubServiceShiftConfigs
                        .Where(c => c.ShiftModeProfileId == profile.Id)
                        .ToListAsync();
                    if (orphanConfigs.Count > 0)
                        _context.SubServiceShiftConfigs.RemoveRange(orphanConfigs);
                    _context.ShiftModeProfiles.Remove(profile);
                }
            }
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            throw new InvalidOperationException(
                "Échec enregistrement profils de modes : " + detail, ex);
        }

        foreach (var (modeDto, profile) in modeToProfile)
        {
            await UpsertModeShiftConfigsAsync(
                dto,
                isTemplate,
                profile.Id,
                modeDto.Shifts,
                modeDto.SaturdayShifts,
                profile.MinPresencePercent,
                profile.IsCriticalCell);
        }

        if (isTemplate)
        {
            await RemoveOrphanUnmodedWeekSnapshotsAsync(dto.SubServiceId);
            await SyncTemplateCellSettingsToAllWeekSnapshotsAsync(dto.SubServiceId);
        }

        return await BuildWeekShiftConfigResponseAsync(
                   dto.SubServiceId, isTemplate, isTemplate ? null : dto.WeekCode)
               ?? throw new Exception("Erreur sauvegarde config multi-modes.");
    }

    private static void ValidateModePercentages(ShiftModeProfileSaveDto mode)
    {
        ValidateShiftPercentList(mode.Title, mode.Shifts);
        if (mode.SaturdayShifts is { Count: > 0 })
            ValidateShiftPercentList(mode.Title + " (samedi)", mode.SaturdayShifts);
    }

    private static void ValidateShiftPercentList(string title, List<ShiftConfigItemDto> shifts)
    {
        if (shifts.Count == 0) return;

        var dupLabels = shifts
            .Select(s => (s.Label ?? string.Empty).Trim())
            .GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => string.IsNullOrEmpty(g.Key) ? "(vide)" : g.Key)
            .ToList();
        if (dupLabels.Count > 0)
            throw new InvalidOperationException(
                $"Le mode « {title} » : libellés de shift en double ({string.Join(", ", dupLabels)}). Chaque shift d’un mode doit avoir un nom unique.");

        var missing = shifts.Where(s => !s.Percentage.HasValue).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Le mode « {title} » : indiquer le pourcentage pour tous les shifts.");

        var sum = shifts.Sum(s => s.Percentage!.Value);
        if (Math.Abs(sum - 100m) > 0.6m)
            throw new InvalidOperationException(
                $"Les pourcentages du mode « {title} » doivent totaliser ~100 % (actuellement {sum}).");
    }

    private async Task UpsertModeShiftConfigsAsync(
        SaveShiftConfigDto dto,
        bool isTemplate,
        int modeProfileId,
        List<ShiftConfigItemDto> shifts,
        List<ShiftConfigItemDto>? saturdayShifts,
        int modeMinPresence,
        bool isCriticalCell)
    {
        List<SubServiceShiftConfig> existing;
        if (isTemplate)
        {
            existing = await _context.SubServiceShiftConfigs
                .Where(c => c.SubServiceId == dto.SubServiceId
                            && c.IsTemplate
                            && c.ShiftModeProfileId == modeProfileId)
                .ToListAsync();
        }
        else
        {
            existing = await _context.SubServiceShiftConfigs
                .Where(c => c.SubServiceId == dto.SubServiceId
                            && !c.IsTemplate
                            && c.WeekCode == dto.WeekCode
                            && c.ShiftModeProfileId == modeProfileId)
                .ToListAsync();
        }

        var cellMinPresence = modeMinPresence <= 0
            ? 0
            : Math.Clamp(modeMinPresence, 50, 100);

        var incoming = new List<(ShiftConfigItemDto Shift, int Index, SubServiceShiftConfig Built)>();

        void AddModeIncoming(IReadOnlyList<ShiftConfigItemDto> items, bool saturday)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var shift = items[i];
                var startTime = TimeOnly.Parse(shift.StartTime);
                var breakDuration = shift.BreakDurationMinutes > 0
                    ? shift.BreakDurationMinutes
                    : BreakSlotPlanner.BreakDurationMinutes;

                var breakSlots = BreakSlotPlanner.NormalizeSlots(
                    startTime, isCriticalCell, shift.BreakSlots);
                var (breakStart, breakEnd) = BreakSlotPlanner.SyncRange(breakSlots, breakDuration);

                var percentage = Math.Round(shift.Percentage ?? 0m, 1);

                incoming.Add((shift, incoming.Count, new SubServiceShiftConfig
                {
                    SubServiceId = dto.SubServiceId,
                    ShiftModeProfileId = modeProfileId,
                    WeekCode = isTemplate ? null : dto.WeekCode,
                    WeekStartDate = isTemplate ? null : dto.WeekStartDate,
                    IsTemplate = isTemplate,
                    AppliesToSaturday = saturday,
                    Label = shift.Label,
                    StartTime = startTime,
                    WorkHours = shift.WorkHours,
                    BreakDurationMinutes = breakDuration,
                    BreakRangeStart = breakStart,
                    BreakRangeEnd = breakEnd,
                    BreakSlotsJson = BreakSlotPlanner.SerializeSlots(breakSlots),
                    IsCriticalCell = isCriticalCell,
                    RequiredCount = 0,
                    Percentage = percentage,
                    MinPresencePercent = cellMinPresence,
                    DisplayOrder = shift.DisplayOrder > 0 ? shift.DisplayOrder : i + 1,
                    CreatedAt = DateTime.UtcNow
                }));
            }
        }

        AddModeIncoming(shifts, false);
        AddModeIncoming(saturdayShifts ?? new List<ShiftConfigItemDto>(), true);

        var builtList = incoming.Select(x => x.Built).ToList();
        LevelBalanceEvaluator.ApplyShiftKindsFromStartTimes(builtList);

        for (var i = 0; i < incoming.Count; i++)
        {
            var kindRaw = incoming[i].Shift.ShiftKind;
            if (!string.IsNullOrWhiteSpace(kindRaw)
                && Enum.TryParse<ShiftKind>(kindRaw, ignoreCase: true, out var parsed))
            {
                incoming[i].Built.ShiftKind = parsed;
            }
        }

        var unmatchedExisting = existing.ToList();
        var pairs = new List<(SubServiceShiftConfig Existing, SubServiceShiftConfig Desired)>();
        var pairedIncoming = new HashSet<int>();
        var toAdd = new List<SubServiceShiftConfig>();

        foreach (var item in incoming)
        {
            var label = item.Built.Label?.Trim() ?? string.Empty;
            var match = unmatchedExisting.FirstOrDefault(e =>
                e.AppliesToSaturday == item.Built.AppliesToSaturday
                && string.Equals(e.Label?.Trim(), label, StringComparison.OrdinalIgnoreCase));
            if (match == null) continue;
            pairs.Add((match, item.Built));
            unmatchedExisting.Remove(match);
            pairedIncoming.Add(item.Index);
        }

        foreach (var item in incoming.Where(x => !pairedIncoming.Contains(x.Index)))
        {
            var match = unmatchedExisting.FirstOrDefault(e =>
                e.AppliesToSaturday == item.Built.AppliesToSaturday
                && e.DisplayOrder == item.Built.DisplayOrder);
            if (match == null) continue;
            pairs.Add((match, item.Built));
            unmatchedExisting.Remove(match);
            pairedIncoming.Add(item.Index);
        }

        void PairRemainingByPosition(bool saturday)
        {
            var remainingIncoming = incoming
                .Where(x => !pairedIncoming.Contains(x.Index) && x.Built.AppliesToSaturday == saturday)
                .ToList();
            var remainingExisting = unmatchedExisting
                .Where(e => e.AppliesToSaturday == saturday)
                .ToList();
            var byPos = Math.Min(remainingIncoming.Count, remainingExisting.Count);
            for (var i = 0; i < byPos; i++)
            {
                pairs.Add((remainingExisting[i], remainingIncoming[i].Built));
                unmatchedExisting.Remove(remainingExisting[i]);
                pairedIncoming.Add(remainingIncoming[i].Index);
            }
        }
        PairRemainingByPosition(false);
        PairRemainingByPosition(true);

        foreach (var item in incoming.Where(x => !pairedIncoming.Contains(x.Index)))
            toAdd.Add(item.Built);

        if (isTemplate && unmatchedExisting.Count > 0)
        {
            await EnsureTemplatesNotReferencedByActiveExceptionalRequestsAsync(unmatchedExisting);
            await DetachClosedExceptionalRequestsFromTemplatesAsync(
                unmatchedExisting.Select(e => e.Id).ToList());
        }

        if (unmatchedExisting.Count > 0)
            _context.SubServiceShiftConfigs.RemoveRange(unmatchedExisting);

        foreach (var (row, desired) in pairs)
        {
            row.ShiftModeProfileId = desired.ShiftModeProfileId;
            row.WeekCode = desired.WeekCode;
            row.WeekStartDate = desired.WeekStartDate;
            row.IsTemplate = desired.IsTemplate;
            row.AppliesToSaturday = desired.AppliesToSaturday;
            row.Label = desired.Label;
            row.StartTime = desired.StartTime;
            row.WorkHours = desired.WorkHours;
            row.BreakDurationMinutes = desired.BreakDurationMinutes;
            row.BreakRangeStart = desired.BreakRangeStart;
            row.BreakRangeEnd = desired.BreakRangeEnd;
            row.BreakSlotsJson = desired.BreakSlotsJson;
            row.IsCriticalCell = desired.IsCriticalCell;
            row.RequiredCount = desired.RequiredCount;
            row.Percentage = desired.Percentage;
            row.MinPresencePercent = desired.MinPresencePercent;
            row.DisplayOrder = desired.DisplayOrder;
            row.ShiftKind = desired.ShiftKind;
            row.UpdatedAt = DateTime.UtcNow;
        }

        if (toAdd.Count > 0)
            _context.SubServiceShiftConfigs.AddRange(toAdd);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            throw new InvalidOperationException(
                "Échec enregistrement configuration shifts multi-modes : " + detail, ex);
        }
    }

    private async Task<WeekShiftConfigResponseDto?> BuildWeekShiftConfigResponseAsync(
        int subServiceId, bool isTemplate, string? weekCode)
    {
        var subService = await _context.SubServices.FindAsync(subServiceId);
        if (subService == null) return null;

        List<SubServiceShiftConfig> configs;
        if (isTemplate)
        {
            configs = await _context.SubServiceShiftConfigs
                .Where(c => c.SubServiceId == subServiceId && c.IsTemplate)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();
        }
        else
        {
            configs = await _context.SubServiceShiftConfigs
                .Where(c => c.SubServiceId == subServiceId
                            && !c.IsTemplate
                            && c.WeekCode == weekCode)
                .OrderBy(c => c.DisplayOrder)
                .ToListAsync();
        }

        var profiles = await _context.ShiftModeProfiles
            .AsNoTracking()
            .Where(p => p.SubServiceId == subServiceId)
            .OrderBy(p => p.DisplayOrder)
            .ThenBy(p => p.Id)
            .ToListAsync();

        var multi = subService.MultiShiftModesEnabled;
        if (configs.Count == 0 && !(multi && profiles.Any(p => p.IsActive)))
            return null;

        var weekday = configs.Where(c => !c.AppliesToSaturday).ToList();
        var saturday = configs.Where(c => c.AppliesToSaturday).ToList();

        var dto = new WeekShiftConfigResponseDto
        {
            SubServiceId = subServiceId,
            SubServiceName = subService.Name,
            WeekCode = isTemplate ? string.Empty : (weekCode ?? string.Empty),
            WeekStartDate = isTemplate
                ? default
                : configs.FirstOrDefault()?.WeekStartDate ?? DateOnly.MinValue,
            IsTemplate = isTemplate,
            IsCriticalCell = multi
                ? profiles.Any(p => p.IsActive && p.IsCriticalCell) || weekday.Any(c => c.IsCriticalCell)
                : weekday.Any(c => c.IsCriticalCell),
            MinPresencePercent = weekday.FirstOrDefault()?.MinPresencePercent is int mp && mp > 0
                ? mp
                : (multi ? 0 : 70),
            MultiShiftModesEnabled = multi,
            TotalEffectif = weekday.Sum(c => c.RequiredCount),
            Shifts = weekday.Select(MapToShiftConfigResponseDto).ToList(),
            UseDistinctSaturdayShifts = saturday.Count > 0,
            SaturdayShifts = saturday.Select(MapToShiftConfigResponseDto).ToList()
        };

        if (multi)
        {
            var activeProfiles = profiles.Where(p => p.IsActive || configs.Any(c => c.ShiftModeProfileId == p.Id))
                .ToList();
            dto.Modes = activeProfiles.Select(p => new ShiftModeProfileDto
            {
                Id = p.Id,
                Title = p.Title,
                DisplayOrder = p.DisplayOrder,
                IsDefault = p.IsDefault,
                IsActive = p.IsActive,
                MinPresencePercent = p.MinPresencePercent,
                IsCriticalCell = p.IsCriticalCell,
                Shifts = weekday
                    .Where(c => c.ShiftModeProfileId == p.Id)
                    .OrderBy(c => c.DisplayOrder)
                    .Select(MapToShiftConfigResponseDto)
                    .ToList(),
                SaturdayShifts = saturday
                    .Where(c => c.ShiftModeProfileId == p.Id)
                    .OrderBy(c => c.DisplayOrder)
                    .Select(MapToShiftConfigResponseDto)
                    .ToList()
            }).ToList();

            if (dto.Modes.Count > 0)
            {
                dto.MinPresencePercent = dto.Modes.First().MinPresencePercent;
                dto.IsCriticalCell = dto.Modes.Any(m => m.IsCriticalCell);
            }
        }

        return dto;
    }

    public async Task<WeeklyShiftModePlanDto> GetWeeklyShiftModePlanAsync(
        int subServiceId, string weekCode, DateOnly weekStartDate)
    {
        var subService = await _context.SubServices.FindAsync(subServiceId)
            ?? throw new InvalidOperationException("Sous-service introuvable.");

        var employees = await _context.Users
            .AsNoTracking()
            .Where(u => u.SubServiceId == subServiceId && u.IsActive)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .ToListAsync();

        var availableModes = await _context.ShiftModeProfiles
            .AsNoTracking()
            .Where(p => p.SubServiceId == subServiceId && p.IsActive)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Id)
            .Select(p => new ShiftModeProfileDto
            {
                Id = p.Id,
                Title = p.Title,
                DisplayOrder = p.DisplayOrder,
                IsDefault = p.IsDefault,
                IsActive = p.IsActive,
                MinPresencePercent = p.MinPresencePercent
            })
            .ToListAsync();

        var plan = await _context.WeeklyCellShiftModePlans
            .Include(p => p.EmployeeModes)
            .FirstOrDefaultAsync(p => p.SubServiceId == subServiceId && p.WeekCode == weekCode);

        var (assignmentByUser, sourceWeekCode, isCopiedPreview) =
            await ResolveModeAssignmentsForDisplayAsync(
                subServiceId, weekStartDate, plan, availableModes);

        var fallbackModeId = FallbackModeId(availableModes);
        foreach (var emp in employees)
        {
            if (assignmentByUser.ContainsKey(emp.Id))
                continue;
            if (fallbackModeId is int fid)
                assignmentByUser[emp.Id] = fid;
        }

        var modeTitles = availableModes.ToDictionary(m => m.Id, m => m.Title);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var settings = await _context.PlanningAutoGenerateSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == PlanningAutoGenerateSettings.SingletonId)
            ?? new PlanningAutoGenerateSettings();
        var nowLocal = ShiftModePlanDeadline.ToLocalNow(settings, DateTime.UtcNow);
        var todayLocal = DateOnly.FromDateTime(nowLocal);
        var deadlineLocal = ShiftModePlanDeadline.ComputeCarryDeadlineLocal(settings, weekStartDate);

        var hasPersistedAssignments = plan != null && plan.EmployeeModes.Count > 0;

        return new WeeklyShiftModePlanDto
        {
            Id = plan?.Id ?? 0,
            SubServiceId = subServiceId,
            SubServiceName = subService.Name,
            WeekCode = weekCode,
            WeekStartDate = weekStartDate,
            IsValidated = plan?.IsValidated ?? false,
            IsLocked = weekStartDate <= today,
            ValidatedAt = plan?.ValidatedAt,
            IsCopiedPreview = isCopiedPreview && !hasPersistedAssignments,
            SourceWeekCode = hasPersistedAssignments ? null : sourceWeekCode,
            IsSupervisorSaved = plan is { IsValidated: true } && hasPersistedAssignments,
            DeadlineLocal = deadlineLocal,
            DeadlinePassed = nowLocal > deadlineLocal || weekStartDate <= todayLocal,
            AvailableModes = availableModes,
            Employees = employees.Select(u =>
            {
                assignmentByUser.TryGetValue(u.Id, out var modeId);
                var hasMode = modeId > 0;
                return new WeeklyEmployeeShiftModeDto
                {
                    UserId = u.Id,
                    FullName = UserDisplayName.Format(u.FirstName, u.LastName),
                    Level = u.Level,
                    SaturdayWorkMode = u.SaturdayWorkMode,
                    ShiftModeProfileId = hasMode ? modeId : null,
                    ShiftModeTitle = hasMode && modeTitles.TryGetValue(modeId, out var title)
                        ? title
                        : null
                };
            }).ToList()
        };
    }

    public async Task<WeeklyShiftModePlanDto> SaveWeeklyShiftModePlanAsync(
        SaveWeeklyShiftModePlanDto dto)
    {
        var subService = await _context.SubServices.FindAsync(dto.SubServiceId)
            ?? throw new InvalidOperationException("Sous-service introuvable.");

        if (!subService.MultiShiftModesEnabled)
            throw new InvalidOperationException(
                "Les modes de shifts ne sont pas activés pour cette cellule.");

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (dto.WeekStartDate <= today)
            throw new InvalidOperationException(
                "Impossible de modifier les modes : la semaine est commencée (lecture seule).");

        var activeEmployees = await _context.Users
            .AsNoTracking()
            .Where(u => u.SubServiceId == dto.SubServiceId && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();

        var activeEmployeeSet = activeEmployees.ToHashSet();
        var activeModes = await _context.ShiftModeProfiles
            .AsNoTracking()
            .Where(p => p.SubServiceId == dto.SubServiceId && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync();
        var activeModeSet = activeModes.ToHashSet();

        if (dto.Employees == null || dto.Employees.Count != activeEmployeeSet.Count)
            throw new InvalidOperationException(
                "Chaque employé actif doit avoir exactement un mode.");

        var seenUsers = new HashSet<int>();
        foreach (var item in dto.Employees)
        {
            if (!activeEmployeeSet.Contains(item.UserId))
                throw new InvalidOperationException(
                    $"Employé {item.UserId} hors périmètre ou inactif.");
            if (!seenUsers.Add(item.UserId))
                throw new InvalidOperationException(
                    $"Employé {item.UserId} affecté plus d'une fois.");
            if (!activeModeSet.Contains(item.ShiftModeProfileId))
                throw new InvalidOperationException(
                    $"Mode {item.ShiftModeProfileId} invalide ou inactif.");
        }

        if (seenUsers.Count != activeEmployeeSet.Count
            || !activeEmployeeSet.SetEquals(seenUsers))
            throw new InvalidOperationException(
                "Chaque employé actif doit avoir exactement un mode.");

        var plan = await _context.WeeklyCellShiftModePlans
            .Include(p => p.EmployeeModes)
            .FirstOrDefaultAsync(p => p.SubServiceId == dto.SubServiceId && p.WeekCode == dto.WeekCode);

        if (plan == null)
        {
            plan = new WeeklyCellShiftModePlan
            {
                SubServiceId = dto.SubServiceId,
                WeekCode = dto.WeekCode,
                WeekStartDate = dto.WeekStartDate,
                CreatedAt = DateTime.UtcNow
            };
            _context.WeeklyCellShiftModePlans.Add(plan);
        }
        else
        {
            plan.WeekStartDate = dto.WeekStartDate;
            plan.UpdatedAt = DateTime.UtcNow;
            if (plan.EmployeeModes.Count > 0)
                _context.WeeklyEmployeeShiftModes.RemoveRange(plan.EmployeeModes);
        }

        plan.IsValidated = true;
        plan.ValidatedAt = DateTime.UtcNow;
        plan.ValidatedByUserId = dto.ActorUserId;

        await _context.SaveChangesAsync();

        foreach (var item in dto.Employees)
        {
            _context.WeeklyEmployeeShiftModes.Add(new WeeklyEmployeeShiftMode
            {
                WeeklyCellShiftModePlanId = plan.Id,
                UserId = item.UserId,
                ShiftModeProfileId = item.ShiftModeProfileId
            });
        }

        await _context.SaveChangesAsync();

        return await GetWeeklyShiftModePlanAsync(dto.SubServiceId, dto.WeekCode, dto.WeekStartDate);
    }

    private async Task EnsureValidatedModePlanForWeekAsync(
        int subServiceId,
        string weekCode,
        DateOnly weekStartDate,
        DateTime? utcNow = null)
    {
        var subService = await _context.SubServices.FindAsync(subServiceId)
            ?? throw new InvalidOperationException("Sous-service introuvable.");
        if (!subService.MultiShiftModesEnabled)
            return;

        var nowUtc = utcNow ?? DateTime.UtcNow;
        var settings = await EnsureAutoGenerateSettingsEntityAsync();
        var nowLocal = ShiftModePlanDeadline.ToLocalNow(settings, nowUtc);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        var activeEmployeeIds = await _context.Users
            .AsNoTracking()
            .Where(u => u.SubServiceId == subServiceId && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync();

        if (await IsModePlanCompleteAsync(weekCode, subServiceId, activeEmployeeIds))
            return;

        var shouldBlock = ShiftModePlanDeadline.ShouldBlockUntilSupervisorSave(
            settings, weekStartDate, todayLocal, nowLocal);
        if (shouldBlock)
            throw new SupervisorModesPendingException(
                ShiftModePlanDeadline.FormatPendingMessage(settings, weekStartDate));

        await PersistCarriedModePlanAsync(subServiceId, weekCode, weekStartDate, activeEmployeeIds);
    }

    private async Task<bool> IsModePlanCompleteAsync(
        string weekCode, int subServiceId, IReadOnlyList<int> activeEmployeeIds)
    {
        var plan = await _context.WeeklyCellShiftModePlans
            .AsNoTracking()
            .Include(p => p.EmployeeModes)
            .FirstOrDefaultAsync(p => p.SubServiceId == subServiceId
                                      && p.WeekCode == weekCode
                                      && p.IsValidated);
        if (plan == null)
            return false;
        var mapped = plan.EmployeeModes.Select(e => e.UserId).ToHashSet();
        return activeEmployeeIds.All(mapped.Contains);
    }

    private async Task PersistCarriedModePlanAsync(
        int subServiceId,
        string weekCode,
        DateOnly weekStartDate,
        IReadOnlyList<int> activeEmployeeIds)
    {
        var availableModes = await _context.ShiftModeProfiles
            .AsNoTracking()
            .Where(p => p.SubServiceId == subServiceId && p.IsActive)
            .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Id)
            .Select(p => new ShiftModeProfileDto
            {
                Id = p.Id,
                Title = p.Title,
                DisplayOrder = p.DisplayOrder,
                IsDefault = p.IsDefault,
                IsActive = p.IsActive
            })
            .ToListAsync();

        var (assignmentByUser, _, _) = await ResolveModeAssignmentsForDisplayAsync(
            subServiceId, weekStartDate, plan: null, availableModes);

        var fallback = FallbackModeId(availableModes)
            ?? throw new InvalidOperationException(
                "Aucun plan de modes validé à recopier et aucun mode actif pour cette cellule.");

        foreach (var userId in activeEmployeeIds)
        {
            if (!assignmentByUser.ContainsKey(userId))
                assignmentByUser[userId] = fallback;
        }

        var plan = await _context.WeeklyCellShiftModePlans
            .Include(p => p.EmployeeModes)
            .FirstOrDefaultAsync(p => p.SubServiceId == subServiceId && p.WeekCode == weekCode);

        if (plan == null)
        {
            plan = new WeeklyCellShiftModePlan
            {
                SubServiceId = subServiceId,
                WeekCode = weekCode,
                WeekStartDate = weekStartDate,
                CreatedAt = DateTime.UtcNow
            };
            _context.WeeklyCellShiftModePlans.Add(plan);
        }
        else if (plan.EmployeeModes.Count > 0)
        {
            _context.WeeklyEmployeeShiftModes.RemoveRange(plan.EmployeeModes);
        }

        plan.WeekStartDate = weekStartDate;
        plan.IsValidated = true;
        plan.ValidatedAt = DateTime.UtcNow;
        plan.ValidatedByUserId = null;
        plan.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        foreach (var userId in activeEmployeeIds)
        {
            _context.WeeklyEmployeeShiftModes.Add(new WeeklyEmployeeShiftMode
            {
                WeeklyCellShiftModePlanId = plan.Id,
                UserId = userId,
                ShiftModeProfileId = assignmentByUser[userId]
            });
        }

        await _context.SaveChangesAsync();
    }

    private async Task<(Dictionary<int, int> Assignments, string? SourceWeekCode, bool IsCopied)>
        ResolveModeAssignmentsForDisplayAsync(
            int subServiceId,
            DateOnly weekStartDate,
            WeeklyCellShiftModePlan? plan,
            IReadOnlyList<ShiftModeProfileDto> availableModes)
    {
        // 1) Config déjà enregistrée pour la semaine demandée
        if (plan is { EmployeeModes.Count: > 0 })
            return (CopyAssignments(plan, availableModes), null, false);

        // 2) Semaine calendaire en cours (si on édite une semaine future)
        var currentMonday = MondayOfToday();
        if (currentMonday < weekStartDate)
        {
            var currentPlan = await FindModePlanWithAssignmentsAsync(subServiceId, currentMonday);
            if (currentPlan == null)
            {
                var currentCode = FormatWeekCode(currentMonday);
                currentPlan = await FindModePlanWithAssignmentsByWeekCodeAsync(subServiceId, currentCode);
            }
            if (currentPlan != null)
                return (CopyAssignments(currentPlan, availableModes), currentPlan.WeekCode, true);
        }

        // 3) Dernière configuration enregistrée (avant la semaine cible)
        var previous = await FindLatestModePlanWithAssignmentsAsync(subServiceId, weekStartDate);
        if (previous != null)
            return (CopyAssignments(previous, availableModes), previous.WeekCode, true);

        // 4) Modes déduits du dernier planning généré
        var activeModeIds = availableModes.Select(m => m.Id).ToHashSet();
        var fromPlanning = await FindPreviousGeneratedPlanningModesAsync(
            subServiceId, weekStartDate, activeModeIds);
        if (fromPlanning.Modes.Count > 0)
            return (fromPlanning.Modes, fromPlanning.WeekCode, true);

        return (new Dictionary<int, int>(), null, false);
    }

    private static DateOnly MondayOfToday()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var diff = ((int)today.DayOfWeek + 6) % 7;
        return today.AddDays(-diff);
    }

    private async Task<WeeklyCellShiftModePlan?> FindModePlanWithAssignmentsAsync(
        int subServiceId, DateOnly weekStartDate) =>
        await _context.WeeklyCellShiftModePlans
            .AsNoTracking()
            .Include(p => p.EmployeeModes)
            .Where(p => p.SubServiceId == subServiceId
                        && p.WeekStartDate == weekStartDate
                        && p.EmployeeModes.Any())
            .OrderByDescending(p => p.IsValidated)
            .ThenByDescending(p => p.ValidatedAt)
            .FirstOrDefaultAsync();

    private async Task<WeeklyCellShiftModePlan?> FindModePlanWithAssignmentsByWeekCodeAsync(
        int subServiceId, string weekCode) =>
        await _context.WeeklyCellShiftModePlans
            .AsNoTracking()
            .Include(p => p.EmployeeModes)
            .Where(p => p.SubServiceId == subServiceId
                        && p.WeekCode == weekCode
                        && p.EmployeeModes.Any())
            .OrderByDescending(p => p.IsValidated)
            .ThenByDescending(p => p.ValidatedAt)
            .FirstOrDefaultAsync();

    private async Task<WeeklyCellShiftModePlan?> FindLatestModePlanWithAssignmentsAsync(
        int subServiceId, DateOnly beforeWeekStart) =>
        await _context.WeeklyCellShiftModePlans
            .AsNoTracking()
            .Include(p => p.EmployeeModes)
            .Where(p => p.SubServiceId == subServiceId
                        && p.WeekStartDate < beforeWeekStart
                        && p.EmployeeModes.Any())
            .OrderByDescending(p => p.IsValidated)
            .ThenByDescending(p => p.WeekStartDate)
            .FirstOrDefaultAsync();

    private async Task<WeeklyCellShiftModePlan?> FindPreviousValidatedModePlanAsync(
        int subServiceId, DateOnly weekStartDate) =>
        await _context.WeeklyCellShiftModePlans
            .AsNoTracking()
            .Include(p => p.EmployeeModes)
            .Where(p => p.SubServiceId == subServiceId
                        && p.IsValidated
                        && p.WeekStartDate < weekStartDate
                        && p.EmployeeModes.Any())
            .OrderByDescending(p => p.WeekStartDate)
            .FirstOrDefaultAsync();

    private async Task<(Dictionary<int, int> Modes, string? WeekCode)> FindPreviousGeneratedPlanningModesAsync(
        int subServiceId,
        DateOnly weekStartDate,
        IReadOnlySet<int> activeModeIds)
    {
        var planning = await _context.WeeklyPlannings
            .AsNoTracking()
            .Where(p => p.SubServiceId == subServiceId && p.WeekStartDate < weekStartDate)
            .OrderByDescending(p => p.WeekStartDate)
            .Select(p => new { p.Id, p.WeekCode })
            .FirstOrDefaultAsync();
        if (planning == null)
            return (new Dictionary<int, int>(), null);

        var rows = await _context.ShiftAssignments
            .AsNoTracking()
            .Where(a =>
                a.WeeklyPlanningId == planning.Id
                && a.ShiftModeProfileId != null
                && !a.IsOnLeave
                && !a.IsHoliday)
            .Select(a => new { a.UserId, ModeId = a.ShiftModeProfileId!.Value })
            .ToListAsync();

        var map = rows
            .Where(r => activeModeIds.Contains(r.ModeId))
            .GroupBy(r => r.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.ModeId).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key);

        return (map, planning.WeekCode);
    }

    private static Dictionary<int, int> CopyAssignments(
        WeeklyCellShiftModePlan previous,
        IReadOnlyList<ShiftModeProfileDto> availableModes)
    {
        var activeModeIds = availableModes.Select(m => m.Id).ToHashSet();
        var map = new Dictionary<int, int>();
        foreach (var line in previous.EmployeeModes)
        {
            if (activeModeIds.Contains(line.ShiftModeProfileId))
                map[line.UserId] = line.ShiftModeProfileId;
        }
        return map;
    }

    private static int? FallbackModeId(IReadOnlyList<ShiftModeProfileDto> availableModes)
    {
        if (availableModes.Count == 0)
            return null;
        return availableModes.FirstOrDefault(m => m.IsDefault)?.Id
               ?? availableModes[0].Id;
    }

    public async Task NotifyModeConfigPendingAsync(
        int planningUserId,
        int authUserId,
        string weekCode,
        string subServiceName,
        string message)
    {
        if (planningUserId <= 0 || authUserId <= 0)
            return;

        var notif = new PlanningNotification
        {
            UserId = planningUserId,
            AuthUserId = authUserId,
            WeekCode = weekCode,
            SubServiceName = subServiceName,
            Message = message,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.PlanningNotifications.Add(notif);
        await _context.SaveChangesAsync();

        await _hubContext.Clients
            .Group($"user_{authUserId}")
            .SendAsync("PlanningPublished", new
            {
                id = notif.Id,
                weekCode = notif.WeekCode,
                subServiceName = notif.SubServiceName,
                message = notif.Message,
                weeklyPlanningId = (int?)null,
                deepLink = "/planning",
                createdAt = notif.CreatedAt,
                isRead = false
            });
    }

    private async Task<Dictionary<int, int>> ResolveEmployeeModeMapAsync(
        int subServiceId, string weekCode, IReadOnlyList<int> userIds)
    {
        var subService = await _context.SubServices.FindAsync(subServiceId)
            ?? throw new InvalidOperationException("Sous-service introuvable.");

        if (!subService.MultiShiftModesEnabled)
            return new Dictionary<int, int>();

        var distinctIds = userIds.Distinct().ToList();
        if (distinctIds.Count == 0)
            return new Dictionary<int, int>();

        var plan = await _context.WeeklyCellShiftModePlans
            .AsNoTracking()
            .Include(p => p.EmployeeModes)
            .FirstOrDefaultAsync(p => p.SubServiceId == subServiceId
                                      && p.WeekCode == weekCode
                                      && p.IsValidated);

        if (plan == null)
            throw new InvalidOperationException(
                "Plan de modes hebdomadaires manquant ou non validé pour cette semaine.");

        var map = plan.EmployeeModes
            .Where(e => distinctIds.Contains(e.UserId))
            .ToDictionary(e => e.UserId, e => e.ShiftModeProfileId);

        var missing = distinctIds.Where(id => !map.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                "Plan de modes incomplet : employés sans mode — "
                + string.Join(", ", missing) + ".");

        return map;
    }

    private static void ApplyModeRequiredCounts(
        List<SubServiceShiftConfig> snapshotConfigs,
        IReadOnlyDictionary<int, int> headcountByMode)
    {
        foreach (var group in snapshotConfigs
                     .Where(c => c.ShiftModeProfileId.HasValue)
                     .GroupBy(c => (ModeId: c.ShiftModeProfileId!.Value, Saturday: c.AppliesToSaturday)))
        {
            headcountByMode.TryGetValue(group.Key.ModeId, out var headcount);
            var pool = group.Key.Saturday
                ? ShiftDispersionSelector.SaturdayRequiredCount(headcount)
                : headcount;
            var ordered = group.OrderBy(c => c.DisplayOrder).ThenBy(c => c.Id).ToList();
            var percentages = ordered.Select(c => c.Percentage).ToList();
            var counts = ShiftModeQuotaAllocator.AllocateModeRequiredCounts(percentages, pool);
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].RequiredCount = counts[i];
        }
    }
}
