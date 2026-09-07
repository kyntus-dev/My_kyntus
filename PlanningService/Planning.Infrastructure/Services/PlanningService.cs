using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Planning.Infrastructure.Persistence;
using Planning.Application.DTOs.Planning;
using Planning.Domain.Enums;
using Planning.Infrastructure.Helpers;
using Planning.Infrastructure.Hubs;
using Planning.Application.Abstractions;
using Planning.Application.Exceptions;
using Planning.Application.PilotPlanning;
using Planning.Domain.Entities;
using Shared.Kernel.Push;

namespace Planning.Infrastructure.Services;

public partial class PlanningService : IPlanningService
{
    private readonly AppDbContext _context;
    private readonly IHubContext<PlanningHub> _hubContext;
    private readonly IPlanningPerimeterResolver _perimeter;
    private readonly IDesktopPushClient? _desktopPush;
    private readonly IPilotPlanningPersonalEmailService? _pilotEmails;

    public PlanningService(
        AppDbContext context,
        IHubContext<PlanningHub> hubContext,
        IPlanningPerimeterResolver perimeter,
        IDesktopPushClient? desktopPush = null,
        IPilotPlanningPersonalEmailService? pilotEmails = null)
    {
        _context = context;
        _hubContext = hubContext;
        _perimeter = perimeter;
        _desktopPush = desktopPush;
        _pilotEmails = pilotEmails;
    }

    // ----------------------------------------------------
    // CR�ER UN PLANNING (vide, en Draft)
    // ----------------------------------------------------
    public async Task<WeeklyPlanningResponseDto> CreatePlanningAsync(CreateWeeklyPlanningDto dto)
    {
        await ValidatePlanningInputsAsync(dto.SubServiceId, dto.WeekStartDate, dto.TotalEffectif);

        var existing = await _context.WeeklyPlannings
            .FirstOrDefaultAsync(p => p.WeekCode == dto.WeekCode &&
                                      p.SubServiceId == dto.SubServiceId);
        if (existing != null)
            throw new InvalidOperationException(
                $"Planning {dto.WeekCode} existe d�j� pour ce sous-service.");

        var planning = new WeeklyPlanning
        {
            SubServiceId = dto.SubServiceId,
            WeekCode = dto.WeekCode,
            WeekStartDate = dto.WeekStartDate,
            TotalEffectif = dto.TotalEffectif,
            SaturdayGroupId = GetSaturdayGroupForWeek(dto.WeekStartDate),
            Status = PlanningStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };

        _context.WeeklyPlannings.Add(planning);
        await _context.SaveChangesAsync();

        return await GetPlanningByIdAsync(planning.Id)
            ?? throw new Exception("Erreur cr�ation planning.");
    }

    // ----------------------------------------------------
    // OVERRIDE BREAK
    // ----------------------------------------------------
    public async Task<DayAssignmentDto> OverrideBreakAsync(OverrideBreakDto dto)
    {
        var assignment = await _context.ShiftAssignments
            .Include(a => a.Shift)
            .Include(a => a.SubServiceShiftConfig)
            .FirstOrDefaultAsync(a => a.Id == dto.ShiftAssignmentId)
            ?? throw new Exception("Assignment introuvable.");

        assignment.BreakTime = TimeOnly.Parse(dto.NewBreakTime);
        assignment.IsManagerOverride = true;
        await ReopenPublishedForRepublishAsync(assignment.WeeklyPlanningId);
        await _context.SaveChangesAsync();

        return MapToDayDtoNew(assignment);
    }

    // ----------------------------------------------------
    // CONFIG SHIFTS — TEMPLATE + SNAPSHOT
    // ----------------------------------------------------
    public async Task<WeekShiftConfigResponseDto> SaveShiftTemplateAsync(SaveShiftConfigDto dto)
    {
        dto.WeekCode = null;
        dto.WeekStartDate = null;
        return await SaveShiftConfigInternalAsync(dto, isTemplate: true);
    }

    public async Task<WeekShiftConfigResponseDto> SaveShiftConfigAsync(SaveShiftConfigDto dto)
    {
        // WeekCode vide → modèle permanent (rétro-compat + nouvelle UI)
        var isTemplate = string.IsNullOrWhiteSpace(dto.WeekCode);
        return await SaveShiftConfigInternalAsync(dto, isTemplate);
    }

    private async Task<WeekShiftConfigResponseDto> SaveShiftConfigInternalAsync(
        SaveShiftConfigDto dto, bool isTemplate)
    {
        var subService = await _context.SubServices.FindAsync(dto.SubServiceId)
            ?? throw new InvalidOperationException("Sous-service introuvable.");

        if (dto.MultiShiftModesEnabled
            || (subService.MultiShiftModesEnabled && dto.Modes is { Count: > 0 }))
        {
            return await SaveMultiModeShiftConfigAsync(dto, isTemplate);
        }

        if (subService.MultiShiftModesEnabled && !dto.MultiShiftModesEnabled)
            subService.MultiShiftModesEnabled = false;

        // Upsert (pas delete-all) : conserve les Ids pour les FK
        // PlanningExceptionalRequests.RequestedShiftTemplateId (pending = blocage app ;
        // historique = ON DELETE SET NULL).
        List<SubServiceShiftConfig> existing;
        if (isTemplate)
        {
            existing = await _context.SubServiceShiftConfigs
                .Where(c => c.SubServiceId == dto.SubServiceId && c.IsTemplate)
                .ToListAsync();
        }
        else
        {
            existing = await _context.SubServiceShiftConfigs
                .Where(c => c.SubServiceId == dto.SubServiceId
                         && !c.IsTemplate
                         && c.WeekCode == dto.WeekCode)
                .ToListAsync();
        }

        var weekdayItems = dto.Shifts ?? new List<ShiftConfigItemDto>();
        var saturdayItems = dto.UseDistinctSaturdayShifts
            ? (dto.SaturdayShifts ?? new List<ShiftConfigItemDto>())
            : new List<ShiftConfigItemDto>();
        if (saturdayItems.Count > 8)
            throw new InvalidOperationException("Le samedi ne peut pas dépasser 8 shifts.");

        var weekdayTotal = weekdayItems.Sum(s => s.RequiredCount);
        var saturdayTotal = saturdayItems.Sum(s => s.RequiredCount);
        var isCriticalCell = dto.IsCriticalCell;
        var cellMinPresence = dto.MinPresencePercent <= 0
            ? 0
            : Math.Clamp(dto.MinPresencePercent, 50, 100);

        var incoming = new List<(ShiftConfigItemDto Shift, int Index, SubServiceShiftConfig Built)>();

        void AddIncoming(IReadOnlyList<ShiftConfigItemDto> items, bool saturday, int bucketTotal)
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

                var percentage = bucketTotal > 0
                    ? Math.Round((decimal)shift.RequiredCount / bucketTotal * 100, 1)
                    : 0;

                incoming.Add((shift, incoming.Count, new SubServiceShiftConfig
                {
                    SubServiceId = dto.SubServiceId,
                    ShiftModeProfileId = null,
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
                    RequiredCount = shift.RequiredCount,
                    Percentage = percentage,
                    MinPresencePercent = cellMinPresence,
                    DisplayOrder = shift.DisplayOrder > 0 ? shift.DisplayOrder : i + 1,
                    CreatedAt = DateTime.UtcNow
                }));
            }
        }

        AddIncoming(weekdayItems, false, weekdayTotal);
        AddIncoming(saturdayItems, true, saturdayTotal);

        var builtList = incoming.Select(x => x.Built).ToList();
        LevelBalanceEvaluator.ApplyShiftKindsFromStartTimes(builtList);

        for (int i = 0; i < incoming.Count; i++)
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

        // 1) Match par (Label, AppliesToSaturday)
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

        // 2) Puis par DisplayOrder dans le même bucket (weekday / samedi)
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

        // 3) Puis par position restante, bucket par bucket
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

        // Ne pas supprimer un template encore référencé par une demande exceptionnelle *en attente*
        // (Approved/Rejected/Cancelled : détachés puis FK SET NULL).
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
                "Échec enregistrement configuration shifts : " + detail, ex);
        }

        if (isTemplate)
        {
            await SyncTemplateCellSettingsToAllWeekSnapshotsAsync(dto.SubServiceId);
            return await GetShiftTemplateAsync(dto.SubServiceId)
                ?? throw new Exception("Erreur sauvegarde template.");
        }

        return await GetShiftConfigAsync(dto.SubServiceId, dto.WeekCode!)
            ?? throw new Exception("Erreur sauvegarde config.");
    }

    public async Task<WeekShiftConfigResponseDto?> GetShiftTemplateAsync(int subServiceId)
        => await BuildWeekShiftConfigResponseAsync(subServiceId, isTemplate: true, weekCode: null);

    public async Task<ShiftConfigStatusResponseDto> GetShiftConfigStatusAsync()
    {
        var subs = await _context.SubServices
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .Select(s => new { s.Id, s.Name, s.PrimeServiceId })
            .ToListAsync();

        var templateAgg = await _context.SubServiceShiftConfigs
            .AsNoTracking()
            .Where(c => c.IsTemplate && !c.AppliesToSaturday)
            .GroupBy(c => c.SubServiceId)
            .Select(g => new
            {
                SubServiceId = g.Key,
                ShiftCount = g.Count(),
                TemplateEffectif = g.Sum(x => x.RequiredCount)
            })
            .ToListAsync();

        var templateBySub = templateAgg.ToDictionary(x => x.SubServiceId);

        var activeCounts = await _context.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.SubServiceId != null)
            .GroupBy(u => u.SubServiceId!.Value)
            .Select(g => new { SubServiceId = g.Key, Count = g.Count() })
            .ToListAsync();

        var activeBySub = activeCounts.ToDictionary(x => x.SubServiceId, x => x.Count);

        var items = subs.Select(s =>
        {
            templateBySub.TryGetValue(s.Id, out var tpl);
            activeBySub.TryGetValue(s.Id, out var active);
            return new ShiftConfigStatusItemDto
            {
                SubServiceId = s.Id,
                SubServiceName = s.Name,
                PrimeServiceId = s.PrimeServiceId,
                HasTemplate = tpl != null && tpl.ShiftCount > 0,
                ShiftCount = tpl?.ShiftCount ?? 0,
                TemplateEffectif = tpl?.TemplateEffectif ?? 0,
                ActiveEmployeeCount = active
            };
        }).ToList();

        return new ShiftConfigStatusResponseDto
        {
            Items = items,
            ConfiguredCount = items.Count(i => i.HasTemplate),
            TotalCount = items.Count
        };
    }

    public async Task<WeekShiftConfigResponseDto?> GetShiftConfigAsync(
        int subServiceId, string weekCode)
    {
        var snapshot = await BuildWeekShiftConfigResponseAsync(
            subServiceId, isTemplate: false, weekCode);
        if (snapshot != null) return snapshot;

        // Fallback template si pas encore de snapshot
        var template = await GetShiftTemplateAsync(subServiceId);
        if (template != null)
        {
            template.WeekCode = weekCode;
            template.IsTemplate = true;
            return template;
        }

        return null;
    }

    public async Task EnsureWeekSnapshotAsync(
        int subServiceId, string weekCode, DateOnly weekStartDate, bool forceRefresh = false)
    {
        var existing = await _context.SubServiceShiftConfigs
            .Where(c => c.SubServiceId == subServiceId
                     && !c.IsTemplate
                     && c.WeekCode == weekCode)
            .ToListAsync();

        var template = await _context.SubServiceShiftConfigs
            .Where(c => c.SubServiceId == subServiceId && c.IsTemplate)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();

        if (template.Count == 0)
            throw new InvalidOperationException(
                "Aucune configuration shifts (modèle) pour ce sous-service. Configurez d'abord les shifts.");

        if (existing.Count > 0 && !forceRefresh)
        {
            // Le modèle cellule (présence min / critique) doit suivre même sans rebuild complet
            await ApplyTemplateCellSettingsToSnapshotAsync(template, existing);
            return;
        }

        // EF insère avant de supprimer : delete+insert dans le même SaveChanges
        // viole IX_SubServiceShiftConfigs_Snapshot_SubService_Week_Mode_Label.
        if (existing.Count > 0)
        {
            _context.SubServiceShiftConfigs.RemoveRange(existing);
            await SavePlanningChangesAsync(
                "Échec suppression de l'ancien snapshot de shifts");
        }

        foreach (var t in template)
        {
            _context.SubServiceShiftConfigs.Add(new SubServiceShiftConfig
            {
                SubServiceId = subServiceId,
                ShiftModeProfileId = t.ShiftModeProfileId,
                WeekCode = weekCode,
                WeekStartDate = weekStartDate,
                IsTemplate = false,
                AppliesToSaturday = t.AppliesToSaturday,
                Label = t.Label,
                StartTime = t.StartTime,
                WorkHours = t.WorkHours,
                BreakDurationMinutes = t.BreakDurationMinutes,
                BreakRangeStart = t.BreakRangeStart,
                BreakRangeEnd = t.BreakRangeEnd,
                BreakSlotsJson = t.BreakSlotsJson,
                IsCriticalCell = t.IsCriticalCell,
                RequiredCount = t.RequiredCount,
                Percentage = t.Percentage,
                MinPresencePercent = t.MinPresencePercent,
                DisplayOrder = t.DisplayOrder,
                ShiftKind = t.ShiftKind,
                CreatedAt = DateTime.UtcNow
            });
        }

        await SavePlanningChangesAsync("Échec enregistrement du snapshot de shifts");

        // Rétro-compat : templates sans ShiftKind → déduire depuis StartTime
        var snapshot = await _context.SubServiceShiftConfigs
            .Where(c => c.SubServiceId == subServiceId && !c.IsTemplate && c.WeekCode == weekCode)
            .ToListAsync();
        if (snapshot.All(c => c.ShiftKind == ShiftKind.Standard) && snapshot.Count > 0)
        {
            LevelBalanceEvaluator.ApplyShiftKindsFromStartTimes(snapshot);
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Aligne MinPresencePercent / IsCriticalCell du modèle sur un snapshot semaine existant.
    /// </summary>
    private async Task ApplyTemplateCellSettingsToSnapshotAsync(
        List<SubServiceShiftConfig> template,
        List<SubServiceShiftConfig> snapshotRows)
    {
        if (template.Count == 0 || snapshotRows.Count == 0) return;

        var isCritical = template.Any(t => t.IsCriticalCell);
        var multiMode = template.Any(t => t.ShiftModeProfileId.HasValue);
        var changed = false;

        foreach (var row in snapshotRows)
        {
            SubServiceShiftConfig? match = null;
            if (multiMode && row.ShiftModeProfileId.HasValue)
            {
                match = template.FirstOrDefault(t =>
                    t.ShiftModeProfileId == row.ShiftModeProfileId
                    && t.AppliesToSaturday == row.AppliesToSaturday
                    && string.Equals(t.Label?.Trim(), row.Label?.Trim(), StringComparison.OrdinalIgnoreCase));
                match ??= template.FirstOrDefault(t =>
                    t.ShiftModeProfileId == row.ShiftModeProfileId
                    && t.AppliesToSaturday == row.AppliesToSaturday);
            }
            else
            {
                match = template.FirstOrDefault(t =>
                    t.AppliesToSaturday == row.AppliesToSaturday
                    && string.Equals(t.Label?.Trim(), row.Label?.Trim(), StringComparison.OrdinalIgnoreCase));
                match ??= template.FirstOrDefault(t => t.AppliesToSaturday == row.AppliesToSaturday);
            }

            var minPresence = (match ?? template[0]).MinPresencePercent <= 0
                ? 0
                : Math.Clamp((match ?? template[0]).MinPresencePercent, 50, 100);
            var rowCritical = multiMode
                ? (match ?? template[0]).IsCriticalCell
                : isCritical;

            if (row.MinPresencePercent != minPresence)
            {
                row.MinPresencePercent = minPresence;
                changed = true;
            }
            if (row.IsCriticalCell != rowCritical)
            {
                row.IsCriticalCell = rowCritical;
                changed = true;
            }
            if (match?.ShiftModeProfileId != null
                && row.ShiftModeProfileId != match.ShiftModeProfileId)
            {
                var targetModeId = match.ShiftModeProfileId;
                var label = (row.Label ?? string.Empty).Trim();
                var wouldDup = snapshotRows.Any(other =>
                    !ReferenceEquals(other, row)
                    && other.WeekCode == row.WeekCode
                    && other.ShiftModeProfileId == targetModeId
                    && other.AppliesToSaturday == row.AppliesToSaturday
                    && string.Equals(
                        (other.Label ?? string.Empty).Trim(),
                        label,
                        StringComparison.OrdinalIgnoreCase));
                if (!wouldDup)
                {
                    row.ShiftModeProfileId = targetModeId;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            foreach (var row in snapshotRows)
                row.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Bloque la suppression d'un template uniquement s'il est encore cité par une demande
    /// exceptionnelle en attente (superviseur ou RH). Historique Approved/Rejected/Cancelled : OK.
    /// </summary>
    private async Task EnsureTemplatesNotReferencedByActiveExceptionalRequestsAsync(
        List<SubServiceShiftConfig> unmatchedExisting)
    {
        var removeIds = unmatchedExisting.Select(e => e.Id).ToList();
        var blocked = await _context.PlanningExceptionalRequests
            .AsNoTracking()
            .Where(r => r.RequestedShiftTemplateId != null
                        && removeIds.Contains(r.RequestedShiftTemplateId.Value)
                        && (r.Status == PlanningExceptionalRequestStatus.PendingSupervisor
                            || r.Status == PlanningExceptionalRequestStatus.PendingRh))
            .Select(r => r.RequestedShiftTemplateId!.Value)
            .Distinct()
            .ToListAsync();

        if (blocked.Count == 0) return;

        var labels = unmatchedExisting
            .Where(e => blocked.Contains(e.Id))
            .Select(e => e.Label)
            .Distinct()
            .ToList();
        throw new InvalidOperationException(
            "Impossible de supprimer le(s) shift(s) « "
            + string.Join(" », « ", labels)
            + " » : des demandes exceptionnelles en attente y font encore référence. "
            + "Modifiez ces shifts ou traitez les demandes avant de les retirer.");
    }

    /// <summary>
    /// Détache les demandes historiques (Approved/Rejected/Cancelled) d'un template
    /// avant suppression, pour éviter le blocage FK.
    /// </summary>
    private async Task DetachClosedExceptionalRequestsFromTemplatesAsync(List<int> removeIds)
    {
        if (removeIds.Count == 0) return;
        var closed = await _context.PlanningExceptionalRequests
            .Where(r => r.RequestedShiftTemplateId != null
                        && removeIds.Contains(r.RequestedShiftTemplateId.Value)
                        && r.Status != PlanningExceptionalRequestStatus.PendingSupervisor
                        && r.Status != PlanningExceptionalRequestStatus.PendingRh)
            .ToListAsync();
        foreach (var row in closed)
            row.RequestedShiftTemplateId = null;
    }

    /// <summary>
    /// Après passage en multi-modes : retire les snapshots semaine sans profil
    /// (ancien modèle unique) pour éviter IX_SubServiceShiftConfigs_Snapshot_SubService_Week_Mode_Label.
    /// </summary>
    private async Task RemoveOrphanUnmodedWeekSnapshotsAsync(int subServiceId)
    {
        var orphans = await _context.SubServiceShiftConfigs
            .Where(c => c.SubServiceId == subServiceId
                        && !c.IsTemplate
                        && c.ShiftModeProfileId == null)
            .ToListAsync();
        if (orphans.Count == 0) return;
        _context.SubServiceShiftConfigs.RemoveRange(orphans);
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Après sauvegarde du modèle : propage présence min / critique à tous les snapshots semaines de la cellule.
    /// </summary>
    private async Task SyncTemplateCellSettingsToAllWeekSnapshotsAsync(int subServiceId)
    {
        var template = await _context.SubServiceShiftConfigs
            .Where(c => c.SubServiceId == subServiceId && c.IsTemplate)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();
        if (template.Count == 0) return;

        var snapshots = await _context.SubServiceShiftConfigs
            .Where(c => c.SubServiceId == subServiceId && !c.IsTemplate)
            .ToListAsync();
        if (snapshots.Count == 0) return;

        await ApplyTemplateCellSettingsToSnapshotAsync(template, snapshots);
    }

    // ----------------------------------------------------
    // GÉNÉRER DEPUIS LA CONFIG
    // ----------------------------------------------------
    public async Task<WeeklyPlanningResponseDto> GeneratePlanningFromConfigAsync(
       GeneratePlanningFromConfigDto dto)
    {
        var planning = await _context.WeeklyPlannings
            .Include(p => p.SubService)
            .FirstOrDefaultAsync(p => p.Id == dto.WeeklyPlanningId)
            ?? throw new Exception("Planning introuvable.");

        var weekCode = string.IsNullOrWhiteSpace(dto.WeekCode) ? planning.WeekCode : dto.WeekCode!;
        var subServiceId = dto.SubServiceId > 0 ? dto.SubServiceId : planning.SubServiceId;
        var wasPublished = planning.Status == PlanningStatus.Published;
        var weekSaturday = planning.WeekStartDate.AddDays(5);

        var regenerateFrom = dto.RegenerateFromDate;
        if (!regenerateFrom.HasValue)
        {
            var earliest = PlanningRegenWindow.GetEarliestRegenerableDate(DateTime.Now);
            // Semaine en cours (jours déjà passés / gelés) → toujours partielle, Draft ou Published
            if (earliest <= weekSaturday && earliest > planning.WeekStartDate)
                regenerateFrom = earliest;
            else if (wasPublished)
                regenerateFrom = earliest;
        }

        var isPartial = regenerateFrom.HasValue;
        if (isPartial && regenerateFrom!.Value > weekSaturday)
        {
            throw new InvalidOperationException(
                $"Deadline {PlanningRegenWindow.CutoffHour}h — demain figé, aucun jour restant à régénérer dans cette semaine.");
        }

        var frozenSnapshot = new List<ShiftAssignment>();
        IDbContextTransaction? tx = null;
        try
        {
        if (_context.Database.IsRelational())
            tx = await _context.Database.BeginTransactionAsync();

        if (isPartial)
        {
            // Snapshot mémoire pour seeder la dispersion uniquement — les lignes passées restent en base.
            var frozen = await _context.ShiftAssignments
                .AsNoTracking()
                .Where(a => a.WeeklyPlanningId == planning.Id
                            && a.AssignedDate < regenerateFrom!.Value)
                .ToListAsync();
            frozenSnapshot = frozen.Select(CloneShiftAssignment).ToList();

            var toRemoveQuery = await _context.ShiftAssignments
                .Where(a => a.WeeklyPlanningId == planning.Id
                            && a.AssignedDate >= regenerateFrom!.Value)
                .ToListAsync();

            // Overrides de mode (switch) et corrections RH : conserver, ne pas régénérer
            var pinned = toRemoveQuery.Where(a => a.IsModeOverride || a.IsManagerOverride).ToList();
            frozenSnapshot.AddRange(pinned.Select(CloneShiftAssignment));
            var toRemove = toRemoveQuery.Where(a => !a.IsModeOverride && !a.IsManagerOverride).ToList();
            _context.ShiftAssignments.RemoveRange(toRemove);
            await _context.SaveChangesAsync();

            await EnsureWeekSnapshotAsync(subServiceId, weekCode, planning.WeekStartDate, forceRefresh: false);
        }
        else
        {
            // Régénération brouillon / full : retirer les assignments avant refresh snapshot (FK)
            var forceRefresh = planning.Status == PlanningStatus.Draft;
            if (forceRefresh)
            {
                _context.ShiftAssignments.RemoveRange(
                    _context.ShiftAssignments.Where(a => a.WeeklyPlanningId == planning.Id));
                _context.WeeklyShiftConfigs.RemoveRange(
                    _context.WeeklyShiftConfigs.Where(c => c.WeeklyPlanningId == planning.Id));
                await _context.SaveChangesAsync();
            }

            await EnsureWeekSnapshotAsync(subServiceId, weekCode, planning.WeekStartDate, forceRefresh);

            if (!forceRefresh)
            {
                _context.ShiftAssignments.RemoveRange(
                    _context.ShiftAssignments.Where(a => a.WeeklyPlanningId == planning.Id));
                _context.WeeklyShiftConfigs.RemoveRange(
                    _context.WeeklyShiftConfigs.Where(c => c.WeeklyPlanningId == planning.Id));
            }

            await _context.SaveChangesAsync();
        }

        var shiftConfigs = await _context.SubServiceShiftConfigs
            .Where(c => c.SubServiceId == subServiceId
                     && !c.IsTemplate
                     && c.WeekCode == weekCode)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();

        if (!shiftConfigs.Any())
            throw new Exception("Aucune config de shifts trouvée.");

        var employees = await _context.Users
            .Where(u => u.SubServiceId == planning.SubServiceId && u.IsActive)
            .OrderBy(u => u.Id)
            .ToListAsync();

        planning.TotalEffectif = employees.Count;

        var subService = planning.SubService
            ?? await _context.SubServices.FindAsync(subServiceId);
        Dictionary<int, int>? employeeModeMap = null;
        if (subService?.MultiShiftModesEnabled == true)
        {
            await EnsureValidatedModePlanForWeekAsync(
                subServiceId, weekCode, planning.WeekStartDate);
            employeeModeMap = await ResolveEmployeeModeMapAsync(
                subServiceId, weekCode, employees.Select(e => e.Id).ToList());
            var headcountByMode = employeeModeMap
                .GroupBy(kv => kv.Value)
                .ToDictionary(g => g.Key, g => g.Count());
            ApplyModeRequiredCounts(shiftConfigs, headcountByMode);
            await _context.SaveChangesAsync(); // persist adjusted RequiredCount on snapshot
        }

        await AutoAssignSaturdayGroupsAsync(planning.SubServiceId);

        var weekNumber = System.Globalization.ISOWeek.GetWeekOfYear(
            planning.WeekStartDate.ToDateTime(TimeOnly.MinValue));
        planning.SaturdayGroupId = weekNumber % 2 == 0 ? 1 : 2;

        var userIds = employees.Select(e => e.Id).ToList();

        var saturdayGroups = await _context.SaturdayGroups
            .Where(sg => userIds.Contains(sg.UserId))
            .ToListAsync();

        var weekEnd = planning.WeekStartDate.AddDays(6);
        var conges = await _context.Conges
            .Where(c => userIds.Contains(c.UserId)
                     && c.Status == CongeStatus.Approved
                     && c.StartDate <= weekEnd
                     && c.EndDate >= planning.WeekStartDate)
            .ToListAsync();

        var assignments = new List<ShiftAssignment>();
        var weekDays = GetWeekDays(planning.WeekStartDate);

        // ? Jours f�ri�s fran�ais
        var holidays = FrenchHolidayHelper.GetHolidays(planning.WeekStartDate.Year);

        // ------------------------------------------------
        // ROTATION � offset par semaine + quotas respect�s
        // ------------------------------------------------
        var currentWeekNumber = System.Globalization.ISOWeek.GetWeekOfYear(
            planning.WeekStartDate.ToDateTime(TimeOnly.MinValue));

        var orderedShifts = ShiftConfigScope.Weekday(shiftConfigs)
            .OrderBy(sc => sc.DisplayOrder).ThenBy(sc => sc.StartTime).ToList();
        if (orderedShifts.Count == 0)
            throw new Exception("Aucune config de shifts (jours de semaine) trouvée.");

        var modeOverrideKeys = frozenSnapshot
            .Where(a => a.IsModeOverride)
            .Select(a => (a.UserId, a.AssignedDate))
            .ToHashSet();

        int? ResolveAssignmentModeId(int empId, SubServiceShiftConfig? config) =>
            employeeModeMap != null && employeeModeMap.TryGetValue(empId, out var mid)
                ? mid
                : config?.ShiftModeProfileId;

        List<SubServiceShiftConfig> ShiftsForEmployee(int empId)
        {
            if (employeeModeMap != null && employeeModeMap.TryGetValue(empId, out var mid))
                return orderedShifts.Where(s => s.ShiftModeProfileId == mid).ToList();
            return orderedShifts;
        }

        List<SubServiceShiftConfig> SatShiftsForEmployee(int empId)
        {
            int? mid = null;
            if (employeeModeMap != null && employeeModeMap.TryGetValue(empId, out var m))
                mid = m;
            IEnumerable<SubServiceShiftConfig> pool = shiftConfigs;
            if (mid.HasValue)
                pool = pool.Where(s => s.ShiftModeProfileId == mid);
            return ShiftConfigScope.Saturday(pool)
                .OrderBy(s => s.DisplayOrder)
                .ThenBy(s => s.StartTime)
                .ToList();
        }

        // Demandes exceptionnelles Approved → pin (userId, date) → snapshot shift
        var exceptionalPins = await LoadExceptionalShiftPinsAsync(
            weekCode, planning.SubServiceId, orderedShifts);
        var reinforcementPins = await LoadReinforcementShiftPinsAsync(
            weekCode, planning.SubServiceId, planning.WeekStartDate.AddDays(5), orderedShifts);

        var patternSeeds = BuildModePatternSeeds(
            employees, employeeModeMap, ShiftsForEmployee, currentWeekNumber);

        // ------------------------------------------------
        // GÉNÉRATION Lun → Ven (motif semaine + matching quotas)
        // ------------------------------------------------
        var weekShiftHistoryByUser = new Dictionary<int, List<int>>();
        foreach (var fa in frozenSnapshot
                     .Where(a => !a.IsSaturday && a.SubServiceShiftConfigId != null && !a.IsOnLeave && !a.IsHoliday)
                     .OrderBy(a => a.AssignedDate))
        {
            if (!weekShiftHistoryByUser.TryGetValue(fa.UserId, out var histSeed))
            {
                histSeed = new List<int>();
                weekShiftHistoryByUser[fa.UserId] = histSeed;
            }
            histSeed.Add(fa.SubServiceShiftConfigId!.Value);
        }

        var usersByIdForSelect = employees.ToDictionary(e => e.Id);
        foreach (var (day, date) in weekDays)
        {
            if (isPartial && date < regenerateFrom!.Value)
            {
                continue;
            }

            // ? Jour férié ? tous FÉRIÉ
            if (holidays.Contains(date))
            {
                foreach (var emp in employees)
                {
                    if (modeOverrideKeys.Contains((emp.Id, date)))
                        continue;

                    assignments.Add(new ShiftAssignment
                    {
                        WeeklyPlanningId = planning.Id,
                        UserId = emp.Id,
                        SubServiceShiftConfigId = null,
                        AssignedDate = date,
                        DayOfWeek = day,
                        IsSaturday = false,
                        IsOnLeave = false,
                        IsHoliday = true,
                        IsNewEmployee = IsBeginnerLevel(emp),
                        ShiftModeProfileId = ResolveAssignmentModeId(emp.Id, null)
                    });
                }
                continue;
            }

            var availableEmployees = employees.Where(e =>
                !modeOverrideKeys.Contains((e.Id, date))
                && !conges.Any(c =>
                    c.UserId == e.Id &&
                    c.StartDate <= date &&
                    c.EndDate >= date)).ToList();

            var onLeaveEmployees = employees.Where(e =>
                !modeOverrideKeys.Contains((e.Id, date))
                && conges.Any(c =>
                    c.UserId == e.Id &&
                    c.StartDate <= date &&
                    c.EndDate >= date))
                .ToList();

            foreach (var emp in onLeaveEmployees)
            {
                assignments.Add(new ShiftAssignment
                {
                    WeeklyPlanningId = planning.Id,
                    UserId = emp.Id,
                    SubServiceShiftConfigId = null,
                    AssignedDate = date,
                    DayOfWeek = day,
                    IsSaturday = false,
                    IsOnLeave = true,
                    IsHoliday = false,
                    IsNewEmployee = IsBeginnerLevel(emp),
                    ShiftModeProfileId = ResolveAssignmentModeId(emp.Id, null)
                });
            }

            var pinsByUser = new Dictionary<int, SubServiceShiftConfig>();
            foreach (var emp in availableEmployees)
            {
                if (exceptionalPins.TryGetValue((emp.Id, date), out var pinnedShift)
                    && pinnedShift != null)
                    pinsByUser[emp.Id] = pinnedShift;
            }

            var chosen = AssignDayByMode(
                availableEmployees,
                employeeModeMap,
                ShiftsForEmployee,
                patternSeeds,
                weekShiftHistoryByUser,
                usersByIdForSelect,
                pinsByUser,
                orderedShifts);

            foreach (var emp in availableEmployees)
            {
                if (!chosen.TryGetValue(emp.Id, out var finalShift))
                    continue;

                var isPinned = pinsByUser.ContainsKey(emp.Id);

                if (!weekShiftHistoryByUser.TryGetValue(emp.Id, out var hist))
                {
                    hist = new List<int>();
                    weekShiftHistoryByUser[emp.Id] = hist;
                }
                hist.Add(finalShift.Id);

                assignments.Add(new ShiftAssignment
                {
                    WeeklyPlanningId = planning.Id,
                    UserId = emp.Id,
                    SubServiceShiftConfigId = finalShift.Id,
                    AssignedDate = date,
                    DayOfWeek = day,
                    IsSaturday = false,
                    IsOnLeave = false,
                    IsHoliday = false,
                    IsNewEmployee = IsBeginnerLevel(emp),
                    IsManagerOverride = isPinned,
                    IsExceptionalRequest = isPinned,
                    ShiftModeProfileId = ResolveAssignmentModeId(emp.Id, finalShift)
                });
            }
        }

        // ------------------------------------------------
        // SAMEDI — mêmes règles dispersion/équité que Lun–Ven
        // + ON/OFF seniors + demi-journée débutants (inchangé)
        // Historique = tour prévu (intended), pas la présence réelle
        // (férié / congé / absence n'impactent pas la rotation).
        // ------------------------------------------------
        var saturdayDate = planning.WeekStartDate.AddDays(5);
        var regenerateSaturday = !isPartial || saturdayDate >= regenerateFrom!.Value;
        var saturdayShiftCountToday = new Dictionary<int, int>();

        var previousWeekCode = GetPreviousWeekCode(weekCode);
        var previousHistories = await _context.SaturdayHistories
            .AsNoTracking()
            .Where(h =>
                h.WeekCode == previousWeekCode
                && h.SubServiceId == planning.SubServiceId
                && userIds.Contains(h.UserId))
            .ToListAsync();
        var previousHistoryByUser = previousHistories.ToDictionary(h => h.UserId);

        var intendedSaturdayOn = new Dictionary<int, bool>(employees.Count);
        foreach (var emp in employees)
        {
            previousHistoryByUser.TryGetValue(emp.Id, out var prev);
            var satGroup = saturdayGroups.FirstOrDefault(sg => sg.UserId == emp.Id);
            intendedSaturdayOn[emp.Id] = ComputeSaturdayIntendedOn(
                emp, prev, satGroup, planning.SaturdayGroupId);
        }

        if (regenerateSaturday)
        {
        if (holidays.Contains(saturdayDate))
        {
            // Samedi férié → affichage FÉRIÉ pour tous ; rotation intended déjà calculée
            foreach (var emp in employees)
            {
                if (modeOverrideKeys.Contains((emp.Id, saturdayDate)))
                    continue;

                assignments.Add(new ShiftAssignment
                {
                    WeeklyPlanningId = planning.Id,
                    UserId = emp.Id,
                    SubServiceShiftConfigId = null,
                    AssignedDate = saturdayDate,
                    DayOfWeek = DayOfWeekEnum.Saturday,
                    IsSaturday = true,
                    IsOnLeave = false,
                    IsHoliday = true,
                    IsNewEmployee = IsBeginnerLevel(emp),
                    ShiftModeProfileId = ResolveAssignmentModeId(emp.Id, null)
                });
            }
        }
        else
        {
            // Compteurs d'équité demi-journée Débutant (créneau 1 = plus tôt, 2 = suivant)
            var beginnerHalfDaySlotCounts = new Dictionary<int, int> { [1] = 0, [2] = 0 };
            var modeLocalIndex = new Dictionary<int, int>();

            for (int empIndex = 0; empIndex < employees.Count; empIndex++)
            {
                var employee = employees[empIndex];
                if (modeOverrideKeys.Contains((employee.Id, saturdayDate)))
                    continue;

                var empSatShifts = SatShiftsForEmployee(employee.Id);
                var localIdx = 0;
                if (employeeModeMap != null && employeeModeMap.TryGetValue(employee.Id, out var satModeId))
                {
                    localIdx = modeLocalIndex.GetValueOrDefault(satModeId, 0);
                    modeLocalIndex[satModeId] = localIdx + 1;
                }
                else
                {
                    localIdx = empIndex;
                }

                bool isOnLeaveSaturday = conges.Any(c =>
                    c.UserId == employee.Id &&
                    c.StartDate <= saturdayDate &&
                    c.EndDate >= saturdayDate);

                if (isOnLeaveSaturday)
                {
                    assignments.Add(new ShiftAssignment
                    {
                        WeeklyPlanningId = planning.Id,
                        UserId = employee.Id,
                        SubServiceShiftConfigId = null,
                        AssignedDate = saturdayDate,
                        DayOfWeek = DayOfWeekEnum.Saturday,
                        IsSaturday = true,
                        IsOnLeave = true,
                        IsHoliday = false,
                        IsNewEmployee = IsBeginnerLevel(employee),
                        ShiftModeProfileId = ResolveAssignmentModeId(employee.Id, null)
                    });
                }
                else if (exceptionalPins.TryGetValue((employee.Id, saturdayDate), out var satPinned)
                         && satPinned != null)
                {
                    assignments.Add(new ShiftAssignment
                    {
                        WeeklyPlanningId = planning.Id,
                        UserId = employee.Id,
                        SubServiceShiftConfigId = satPinned.Id,
                        AssignedDate = saturdayDate,
                        DayOfWeek = DayOfWeekEnum.Saturday,
                        IsSaturday = true,
                        IsOnLeave = false,
                        IsHoliday = false,
                        IsNewEmployee = IsBeginnerLevel(employee),
                        IsManagerOverride = true,
                        IsExceptionalRequest = true,
                        ShiftModeProfileId = ResolveAssignmentModeId(employee.Id, satPinned)
                    });
                    saturdayShiftCountToday[satPinned.Id] =
                        saturdayShiftCountToday.GetValueOrDefault(satPinned.Id, 0) + 1;
                    if (!weekShiftHistoryByUser.TryGetValue(employee.Id, out var hist))
                    {
                        hist = new List<int>();
                        weekShiftHistoryByUser[employee.Id] = hist;
                    }
                    hist.Add(satPinned.Id);
                }
                else if (reinforcementPins.TryGetValue((employee.Id, saturdayDate), out var renPinned)
                         && renPinned != null)
                {
                    assignments.Add(new ShiftAssignment
                    {
                        WeeklyPlanningId = planning.Id,
                        UserId = employee.Id,
                        SubServiceShiftConfigId = renPinned.Id,
                        AssignedDate = saturdayDate,
                        DayOfWeek = DayOfWeekEnum.Saturday,
                        IsSaturday = true,
                        IsOnLeave = false,
                        IsHoliday = false,
                        IsNewEmployee = IsBeginnerLevel(employee),
                        IsManagerOverride = true,
                        IsReinforcement = true,
                        IsHalfDaySaturday = renPinned.WorkHours <= 4,
                        ShiftModeProfileId = ResolveAssignmentModeId(employee.Id, renPinned)
                    });
                    saturdayShiftCountToday[renPinned.Id] =
                        saturdayShiftCountToday.GetValueOrDefault(renPinned.Id, 0) + 1;
                    if (!weekShiftHistoryByUser.TryGetValue(employee.Id, out var histRen))
                    {
                        histRen = new List<int>();
                        weekShiftHistoryByUser[employee.Id] = histRen;
                    }
                    histRen.Add(renPinned.Id);
                }
                else
                {
                    var satAssignment = await GenerateSaturdayAssignmentFromConfigAsync(
                        employee, planning, empSatShifts, saturdayGroups, localIdx,
                        beginnerHalfDaySlotCounts,
                        weekShiftHistoryByUser,
                        usersByIdForSelect,
                        saturdayShiftCountToday,
                        previousHistoryByUser);

                    if (satAssignment != null)
                    {
                        var satCfg = satAssignment.SubServiceShiftConfigId is int sid0
                            ? empSatShifts.FirstOrDefault(c => c.Id == sid0)
                              ?? shiftConfigs.FirstOrDefault(c => c.Id == sid0)
                            : null;
                        satAssignment.ShiftModeProfileId =
                            ResolveAssignmentModeId(employee.Id, satCfg);
                        assignments.Add(satAssignment);
                        if (satAssignment.SubServiceShiftConfigId.HasValue)
                        {
                            var sid = satAssignment.SubServiceShiftConfigId.Value;
                            saturdayShiftCountToday[sid] =
                                saturdayShiftCountToday.GetValueOrDefault(sid, 0) + 1;
                            if (!weekShiftHistoryByUser.TryGetValue(employee.Id, out var hist))
                            {
                                hist = new List<int>();
                                weekShiftHistoryByUser[employee.Id] = hist;
                            }
                            hist.Add(sid);
                        }
                    }
                }
            }
        }
        } // end regenerateSaturday

        RebalanceSaturdayModeQuotas(
            assignments,
            employees,
            employeeModeMap,
            SatShiftsForEmployee,
            ShiftConfigScope.Saturday(shiftConfigs)
                .OrderBy(s => s.DisplayOrder)
                .ThenBy(s => s.StartTime)
                .ToList(),
            usersByIdForSelect,
            weekShiftHistoryByUser);

        var usersById = employees.ToDictionary(e => e.Id);
        if (employeeModeMap != null)
        {
            foreach (var modeId in employeeModeMap.Values.Distinct())
            {
                var modeEmpIds = employees
                    .Where(e => employeeModeMap[e.Id] == modeId)
                    .Select(e => e.Id)
                    .ToHashSet();
                var modeAssignments = assignments
                    .Where(a => a.ShiftModeProfileId == modeId
                                || (a.ShiftModeProfileId == null && modeEmpIds.Contains(a.UserId)))
                    .ToList();
                var modeConfigs = shiftConfigs
                    .Where(c => c.ShiftModeProfileId == modeId)
                    .ToList();
                var modeUsers = usersById
                    .Where(kv => modeEmpIds.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
                var modeRoster = employees.Where(e => modeEmpIds.Contains(e.Id)).ToList();

                ShiftDispersionSelector.RepairWeekQuality(
                    modeAssignments, modeConfigs, modeUsers);
                LevelBalanceRepairer.Repair(
                    modeAssignments, modeConfigs, modeUsers, modeRoster, planning);
                ShiftDispersionSelector.RepairWeekQuality(
                    modeAssignments, modeConfigs, modeUsers);
                WeekShiftPatternAssigner.RebalanceWeekAssignments(
                    modeAssignments, modeConfigs, modeUsers);
            }
        }
        else
        {
            ShiftDispersionSelector.RepairWeekQuality(assignments, shiftConfigs, usersById);
            LevelBalanceRepairer.Repair(assignments, shiftConfigs, usersById, employees, planning);
            ShiftDispersionSelector.RepairWeekQuality(assignments, shiftConfigs, usersById);
            WeekShiftPatternAssigner.RebalanceWeekAssignments(
                assignments, shiftConfigs, usersById);
        }

        if (regenerateSaturday)
        {
            await SaveSaturdayHistoryAsync(new SetSaturdayHistoryDto(
                planning.SubServiceId,
                weekCode,
                employees.Select(emp => new SaturdayHistoryEntryDto(
                    emp.Id,
                    intendedSaturdayOn.GetValueOrDefault(emp.Id, false)
                )).ToList()
            ), false);
        }

        // Passé figé : déjà en base — ne pas ré-insérer (conflit unique UserId+AssignedDate).
        // Les jours régénérés peuvent encore exister dans un autre planning (mutation / import).
        await RemoveConflictingAssignmentsAsync(assignments);
        _context.ShiftAssignments.AddRange(assignments);

        // -- PAUSES (uniquement jours normaux travaillés) --
        var fairnessCounters = new PlateauBreakPacker.BreakFairnessCounters();
        var specialCaseUserIds = employees
            .Where(e => e.IsSpecialCase)
            .Select(e => e.Id)
            .ToHashSet();
        var workDayAssignments = assignments
            .Where(a => !a.IsSaturday && !a.IsOnLeave && !a.IsHoliday
                     && a.SubServiceShiftConfigId != null)
            .GroupBy(a => a.AssignedDate)
            .OrderBy(g => g.Key)
            .ToList();

        foreach (var dayGroup in workDayAssignments)
        {
            if (employeeModeMap != null)
            {
                foreach (var modeGroup in dayGroup.GroupBy(a => a.ShiftModeProfileId))
                {
                    var modeConfigs = modeGroup.Key is int mid
                        ? shiftConfigs.Where(c => c.ShiftModeProfileId == mid).ToList()
                        : shiftConfigs;
                    if (modeConfigs.Count == 0)
                        modeConfigs = shiftConfigs;
                    AssignBreakTimesFromConfig(
                        modeGroup.ToList(),
                        modeConfigs,
                        modeGroup.Count(),
                        fairnessCounters,
                        specialCaseUserIds);
                }
            }
            else
            {
                AssignBreakTimesFromConfig(
                    dayGroup.ToList(), shiftConfigs, employees.Count, fairnessCounters, specialCaseUserIds);
            }
        }

        var saturdayWorkAssignments = assignments
            .Where(a => a.IsSaturday && !a.IsOnLeave && !a.IsHoliday
                     && a.SubServiceShiftConfigId != null)
            .ToList();
        if (saturdayWorkAssignments.Any())
        {
            if (employeeModeMap != null)
            {
                foreach (var modeGroup in saturdayWorkAssignments.GroupBy(a => a.ShiftModeProfileId))
                {
                    var modeConfigs = modeGroup.Key is int mid
                        ? shiftConfigs.Where(c => c.ShiftModeProfileId == mid).ToList()
                        : shiftConfigs;
                    if (modeConfigs.Count == 0)
                        modeConfigs = shiftConfigs;
                    AssignBreakTimesFromConfig(
                        modeGroup.ToList(),
                        modeConfigs,
                        modeGroup.Count(),
                        fairnessCounters,
                        specialCaseUserIds);
                }
            }
            else
            {
                AssignBreakTimesFromConfig(
                    saturdayWorkAssignments, shiftConfigs, employees.Count, fairnessCounters, specialCaseUserIds);
            }
        }

        // Diversité +3h / +4h / +5h : même niveau, même shift — sans casser P
        RepairBreakOffsetDiversity(assignments, shiftConfigs, usersById);

        // Anomalies éventuelles (cas forcés) exposées via CoverageReport — pas de blocage.

        // Regen d'un Published → repasse en Draft : la RH doit revalider avant publication.
        if (wasPublished)
        {
            planning.Status = PlanningStatus.Draft;
            planning.ValidatedBy = null;
        }

        await SavePlanningChangesAsync("Échec enregistrement du planning généré");

        if (tx != null)
            await tx.CommitAsync();

        if (wasPublished)
            await NotifyPlanningRepublishedAsync(planning.Id, dto.RepublishReason);

        return await GetPlanningByIdAsync(planning.Id)
            ?? throw new Exception("Erreur g�n�ration planning.");
        }
        catch
        {
            if (tx != null)
                await tx.RollbackAsync();
            throw;
        }
        finally
        {
            if (tx != null)
                await tx.DisposeAsync();
        }
    }

    /// <summary>
    /// IX_ShiftAssignments_UserId_AssignedDate est global. Libère les jours
    /// déjà posés (autre planning ou reliquat) avant insert, dans un SaveChanges
    /// séparé — sinon EF insère avant de supprimer et la contrainte unique saute.
    /// </summary>
    private async Task RemoveConflictingAssignmentsAsync(
        List<ShiftAssignment> incoming)
    {
        if (incoming.Count == 0)
            return;

        var incomingKeys = incoming
            .Select(a => (a.UserId, a.AssignedDate))
            .ToHashSet();
        var userIds = incoming.Select(a => a.UserId).Distinct().ToList();
        var dates = incoming.Select(a => a.AssignedDate).Distinct().ToList();

        var existing = await _context.ShiftAssignments
            .Where(a => userIds.Contains(a.UserId) && dates.Contains(a.AssignedDate))
            .ToListAsync();

        var conflicts = existing
            .Where(a => incomingKeys.Contains((a.UserId, a.AssignedDate)))
            .ToList();

        if (conflicts.Count == 0)
            return;

        _context.ShiftAssignments.RemoveRange(conflicts);
        await SavePlanningChangesAsync(
            "Échec libération des affectations déjà posées à ces dates");
    }

    private async Task SavePlanningChangesAsync(string failureMessage)
    {
        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            var current = (Exception)ex;
            while (current.InnerException is not null)
                current = current.InnerException;
            throw new InvalidOperationException(
                $"{failureMessage} : {current.Message}", ex);
        }
    }

    private static ShiftAssignment CloneShiftAssignment(ShiftAssignment a) => new()
    {
        WeeklyPlanningId = a.WeeklyPlanningId,
        UserId = a.UserId,
        ShiftId = a.ShiftId,
        AssignedDate = a.AssignedDate,
        DayOfWeek = a.DayOfWeek,
        IsSaturday = a.IsSaturday,
        IsNewEmployee = a.IsNewEmployee,
        IsManagerOverride = a.IsManagerOverride,
        IsExceptionalRequest = a.IsExceptionalRequest,
        IsReinforcement = a.IsReinforcement,
        BreakTime = a.BreakTime,
        IsOnLeave = a.IsOnLeave,
        IsHalfDaySaturday = a.IsHalfDaySaturday,
        SaturdaySlot = a.SaturdaySlot,
        IsHoliday = a.IsHoliday,
        SubServiceShiftConfigId = a.SubServiceShiftConfigId,
        ShiftModeProfileId = a.ShiftModeProfileId,
        IsModeOverride = a.IsModeOverride
    };

    // ----------------------------------------------------
    // METTRE SAMEDI OFF (supprimer l'assignation)
    // ----------------------------------------------------
    public async Task SetSaturdayOffAsync(int weeklyPlanningId, int userId)
    {
        var assignment = await _context.ShiftAssignments
            .FirstOrDefaultAsync(a =>
                a.WeeklyPlanningId == weeklyPlanningId &&
                a.UserId == userId &&
                a.IsSaturday);

        if (assignment != null)
        {
            _context.ShiftAssignments.Remove(assignment);
            await ReopenPublishedForRepublishAsync(weeklyPlanningId);
            await _context.SaveChangesAsync();
        }
    }

    // ----------------------------------------------------
    // SATURDAY HISTORY
    // ----------------------------------------------------
    public async Task<List<SaturdayHistoryResponseDto>> GetSaturdayHistoryAsync(
        int subServiceId, string weekCode)
    {
        var employees = await _context.Users
            .Where(u => u.SubServiceId == subServiceId && u.IsActive)
            .ToListAsync();

        var histories = await _context.SaturdayHistories
            .Where(h => h.SubServiceId == subServiceId && h.WeekCode == weekCode)
            .ToListAsync();

        return employees.Select(emp =>
        {
            var history = histories.FirstOrDefault(h => h.UserId == emp.Id);
            return new SaturdayHistoryResponseDto(
                emp.Id,
                UserDisplayName.Format(emp.FirstName, emp.LastName),
                weekCode,
                history?.WorkedSaturday ?? false,
                history?.IsManualEntry ?? false
            );
        }).ToList();
    }

    public async Task<List<SaturdayYtdDto>> GetSaturdayYtdAsync(int subServiceId, int year)
    {
        var prefix = $"{year}-";
        var employees = await _context.Users
            .Where(u => u.SubServiceId == subServiceId && u.IsActive)
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .ToListAsync();

        var histories = await _context.SaturdayHistories
            .Where(h => h.SubServiceId == subServiceId && h.WeekCode.StartsWith(prefix))
            .ToListAsync();

        return employees.Select(emp =>
        {
            var rows = histories.Where(h => h.UserId == emp.Id).ToList();
            var worked = rows.Count(h => h.WorkedSaturday);
            var total = rows.Count;
            var off = total - worked;
            var pct = total > 0
                ? Math.Round((decimal)worked / total * 100, 1)
                : 0m;
            return new SaturdayYtdDto(
                emp.Id,
                UserDisplayName.Format(emp.FirstName, emp.LastName),
                worked,
                off,
                total,
                pct);
        }).ToList();
    }

    public async Task SaveSaturdayHistoryAsync(SetSaturdayHistoryDto dto, bool isManual)
    {
        foreach (var entry in dto.Entries)
        {
            var existing = await _context.SaturdayHistories
                .FirstOrDefaultAsync(h =>
                    h.UserId == entry.UserId &&
                    h.WeekCode == dto.WeekCode &&
                    h.SubServiceId == dto.SubServiceId);

            if (existing != null)
            {
                existing.WorkedSaturday = entry.WorkedSaturday;
                existing.IsManualEntry = isManual;
            }
            else
            {
                _context.SaturdayHistories.Add(new SaturdayHistory
                {
                    UserId = entry.UserId,
                    SubServiceId = dto.SubServiceId,
                    WeekCode = dto.WeekCode,
                    WorkedSaturday = entry.WorkedSaturday,
                    IsManualEntry = isManual,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }
        await _context.SaveChangesAsync();
    }

    // ----------------------------------------------------
    // ANCIENNE G�N�RATION (gard�e pour compatibilit�)
    // ----------------------------------------------------
    public async Task<WeeklyPlanningResponseDto> GeneratePlanningAsync(GeneratePlanningDto dto)
    {
        var planning = await _context.WeeklyPlannings
            .Include(p => p.SubService)
            .Include(p => p.ShiftAssignments)
            .FirstOrDefaultAsync(p => p.Id == dto.WeeklyPlanningId)
            ?? throw new Exception("Planning introuvable.");

        await ValidatePlanningInputsAsync(planning.SubServiceId, planning.WeekStartDate, dto.TotalEffectif);
        planning.TotalEffectif = dto.TotalEffectif;

        var employees = await _context.Users
            .Where(u => u.SubServiceId == planning.SubServiceId && u.IsActive)
            .OrderBy(u => u.EarlyShiftCount)
            .ToListAsync();

        if (employees.Count == 0)
            throw new InvalidOperationException("Ce service n'a aucun employ� actif.");

        var shifts = await _context.Shifts
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        if (shifts.Count < 4)
            throw new InvalidOperationException(
                "Il faut au moins 4 shifts configur�s. Contactez l'administrateur.");

        await AutoAssignSaturdayGroupsAsync(planning.SubServiceId);

        var weekNumber = System.Globalization.ISOWeek.GetWeekOfYear(
            planning.WeekStartDate.ToDateTime(TimeOnly.MinValue));
        planning.SaturdayGroupId = weekNumber % 2 == 0 ? 1 : 2;
        await _context.SaveChangesAsync();

        _context.WeeklyShiftConfigs.RemoveRange(
            _context.WeeklyShiftConfigs.Where(c => c.WeeklyPlanningId == planning.Id));
        _context.ShiftAssignments.RemoveRange(
            _context.ShiftAssignments.Where(a => a.WeeklyPlanningId == planning.Id));

        var shiftConfigs = CalculateShiftQuotas(shifts, dto.TotalEffectif, planning.Id);
        _context.WeeklyShiftConfigs.AddRange(shiftConfigs);

        var userIds = employees.Select(e => e.Id).ToList();
        var saturdayGroups = await _context.SaturdayGroups
            .Where(sg => userIds.Contains(sg.UserId))
            .ToListAsync();

        var weekEnd = planning.WeekStartDate.AddDays(6);
        var conges = await _context.Conges
            .Where(c => userIds.Contains(c.UserId)
                     && c.Status == CongeStatus.Approved
                     && c.StartDate <= weekEnd
                     && c.EndDate >= planning.WeekStartDate)
            .ToListAsync();

        var assignments = new List<ShiftAssignment>();
        var weekDays = GetWeekDays(planning.WeekStartDate);

        for (int empIndex = 0; empIndex < employees.Count; empIndex++)
        {
            var employee = employees[empIndex];

            var recentShiftIds = await _context.ShiftAssignments
                .Where(a => a.UserId == employee.Id &&
                            a.ShiftId != null &&
                            a.WeeklyPlanning.WeekStartDate < planning.WeekStartDate)
                .OrderByDescending(a => a.AssignedDate)
                .Take(20)
                .Select(a => a.ShiftId!.Value)
                .ToListAsync();

            var employeeShiftRotation = GetEmployeeWeekRotation(shifts, empIndex, recentShiftIds);

            for (int dayIndex = 0; dayIndex < weekDays.Count; dayIndex++)
            {
                var (day, date) = weekDays[dayIndex];

                bool isOnLeave = conges.Any(c =>
                    c.UserId == employee.Id &&
                    c.StartDate <= date &&
                    c.EndDate >= date);

                if (isOnLeave)
                {
                    assignments.Add(new ShiftAssignment
                    {
                        WeeklyPlanningId = planning.Id,
                        UserId = employee.Id,
                        ShiftId = null,
                        AssignedDate = date,
                        DayOfWeek = day,
                        IsSaturday = false,
                        IsOnLeave = true,
                        IsNewEmployee = IsBeginnerLevel(employee)
                    });
                    continue;
                }

                var shiftId = employeeShiftRotation[dayIndex % shifts.Count];
                assignments.Add(new ShiftAssignment
                {
                    WeeklyPlanningId = planning.Id,
                    UserId = employee.Id,
                    ShiftId = shiftId,
                    AssignedDate = date,
                    DayOfWeek = day,
                    IsSaturday = false,
                    IsOnLeave = false,
                    IsNewEmployee = IsBeginnerLevel(employee)
                });

                if (shiftId == shifts.First().Id)
                    employee.EarlyShiftCount++;
            }

            var saturdayDate2 = planning.WeekStartDate.AddDays(5);
            bool isOnLeaveSat = conges.Any(c =>
                c.UserId == employee.Id &&
                c.StartDate <= saturdayDate2 &&
                c.EndDate >= saturdayDate2);

            if (!isOnLeaveSat)
            {
                var beginnerSlots = new Dictionary<int, int> { [1] = 0, [2] = 0 };
                // Compter les débutants déjà placés dans ce batch (boucle par employé)
                foreach (var a in assignments.Where(x => x.IsSaturday && x.IsHalfDaySaturday && x.SaturdaySlot is 1 or 2))
                    beginnerSlots[a.SaturdaySlot] = beginnerSlots.GetValueOrDefault(a.SaturdaySlot, 0) + 1;

                var satAssignment = GenerateSaturdayAssignment(
                    employee, planning, shifts, saturdayGroups, empIndex, beginnerSlots);
                if (satAssignment != null)
                    assignments.Add(satAssignment);
            }
        }

        _context.ShiftAssignments.AddRange(assignments);

        var allDays = assignments
            .Where(a => !a.IsSaturday && !a.IsOnLeave && a.ShiftId != null)
            .GroupBy(a => a.AssignedDate)
            .ToList();

        foreach (var dayGroup in allDays)
            AssignBreakTimes(dayGroup.ToList(), shifts, employees.Count);

        var saturdayAssignments = assignments
            .Where(a => a.IsSaturday && !a.IsOnLeave && a.ShiftId != null)
            .ToList();
        if (saturdayAssignments.Any())
            AssignBreakTimes(saturdayAssignments, shifts, employees.Count);

        await _context.SaveChangesAsync();

        return await GetPlanningByIdAsync(planning.Id)
            ?? throw new Exception("Erreur g�n�ration planning.");
    }

    public async Task SyncNewEmployeesAsync()
    {
        var threeMonthsAgo = DateTime.UtcNow.AddMonths(-3);
        var employees = await _context.Users.Where(u => u.IsActive).ToListAsync();

        foreach (var emp in employees)
        {
            if (emp.HireDate >= threeMonthsAgo && !emp.IsNewEmployee)
                emp.IsNewEmployee = true;
        }

        await _context.SaveChangesAsync();
    }

    public async Task AutoAssignSaturdayGroupsAsync(int subServiceId)
    {
        var employees = await _context.Users
            .Where(u => u.SubServiceId == subServiceId && u.IsActive)
            .OrderBy(u => u.Id)
            .ToListAsync();

        if (!employees.Any()) return;

        var existingUserIds = await _context.SaturdayGroups
            .Where(sg => employees.Select(e => e.Id).Contains(sg.UserId))
            .Select(sg => sg.UserId)
            .ToListAsync();

        var employeesWithoutGroup = employees
            .Where(e => !existingUserIds.Contains(e.Id))
            .ToList();

        if (!employeesWithoutGroup.Any()) return;

        var group1Count = await _context.SaturdayGroups
            .CountAsync(sg => existingUserIds.Contains(sg.UserId) && sg.GroupNumber == 1);
        var group2Count = await _context.SaturdayGroups
            .CountAsync(sg => existingUserIds.Contains(sg.UserId) && sg.GroupNumber == 2);

        foreach (var emp in employeesWithoutGroup)
        {
            var groupNumber = group1Count <= group2Count ? 1 : 2;
            _context.SaturdayGroups.Add(new SaturdayGroup
            {
                UserId = emp.Id,
                GroupNumber = groupNumber,
                IsNewEmployee = false,
                ManagerOverride = false,
                AssignedAt = DateTime.UtcNow,
                AssignedBy = 0
            });
            if (groupNumber == 1) group1Count++;
            else group2Count++;
        }

        await _context.SaveChangesAsync();
    }
    public async Task<MyPlanningDto?> GetMyCurrentPlanningAsync(int userId)
    {
        // Semaine calendaire courante (Casablanca), pas le dernier planning généré par RH.
        var today = GetCasablancaToday();

        // Chercher parmi les plannings publiés proches (pas seulement le dernier généré).
        var candidates = await _context.WeeklyPlannings
            .Include(p => p.SubService)
            .Include(p => p.ShiftAssignments)
                .ThenInclude(a => a.Shift)
            .Include(p => p.ShiftAssignments)
                .ThenInclude(a => a.SubServiceShiftConfig)
            .Include(p => p.ShiftAssignments)
                .ThenInclude(a => a.ShiftModeProfile)
            .Where(p => p.Status == PlanningStatus.Published
                     && p.ShiftAssignments.Any(a => a.UserId == userId))
            .OrderByDescending(p => p.WeekStartDate)
            .Take(20)
            .ToListAsync();

        var planning = candidates.FirstOrDefault(p =>
        {
            var end = p.WeekStartDate.AddDays(6);
            return today >= p.WeekStartDate && today <= end;
        });

        // Fallback : affectation datée aujourd'hui
        planning ??= candidates.FirstOrDefault(p =>
            p.ShiftAssignments.Any(a => a.UserId == userId && a.AssignedDate == today));

        if (planning == null) return null;

        var exceptionalApplied = await LoadExceptionalAppliedKeysAsync(
            planning.WeekCode, planning.SubServiceId);

        return new MyPlanningDto
        {
            WeeklyPlanningId = planning.Id,
            WeekCode = planning.WeekCode,
            WeekStartDate = planning.WeekStartDate,
            Status = planning.Status.ToString(),
            SubServiceName = planning.SubService.Name,
            Days = planning.ShiftAssignments
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.AssignedDate)
                .Select(a => MapToDayDtoNew(a, null, exceptionalApplied))
                .ToList()
        };
    }
    // ----------------------------------------------------
    // OVERRIDE MANAGER
    // ----------------------------------------------------
    public async Task<DayAssignmentDto> OverrideShiftAsync(OverrideShiftDto dto)
    {
        var assignment = await _context.ShiftAssignments
            .Include(a => a.Shift)
            .Include(a => a.SubServiceShiftConfig)
            .FirstOrDefaultAsync(a => a.Id == dto.ShiftAssignmentId)
            ?? throw new Exception("Assignment introuvable.");

        // ? TOUJOURS remettre IsHoliday � false lors d'un override
        assignment.IsHoliday = false;
        assignment.IsManagerOverride = true;

        if (dto.NewSubServiceShiftConfigId > 0)
        {
            var config = await _context.SubServiceShiftConfigs
                .FindAsync(dto.NewSubServiceShiftConfigId)
                ?? throw new Exception("Config shift introuvable.");

            assignment.SubServiceShiftConfigId = dto.NewSubServiceShiftConfigId;
            assignment.ShiftId = null;
            assignment.IsOnLeave = false;
            assignment.IsHoliday = false;
            assignment.IsManagerOverride = true;

            // ? Assigner une pause automatiquement
            if (assignment.BreakTime == null)
            {
                var slots = BreakSlotPlanner.ResolveBreakSlots(config);
                if (slots.Count > 0)
                    assignment.BreakTime = slots.First();
            }

            await ReopenPublishedForRepublishAsync(assignment.WeeklyPlanningId);
            await _context.SaveChangesAsync();

            await _context.Entry(assignment)
                .Reference(a => a.SubServiceShiftConfig)
                .LoadAsync();

            return MapToDayDtoNew(assignment);
        }
        else
        {
            // ? action = 'off' ? repos (config null, shift null)
            assignment.SubServiceShiftConfigId = null;
            assignment.ShiftId = null;
            assignment.IsOnLeave = false;
            await ReopenPublishedForRepublishAsync(assignment.WeeklyPlanningId);
            await _context.SaveChangesAsync();

            return MapToDayDtoNew(assignment);
        }
    }

    /// <summary>
    /// Une correction RH sur un planning déjà validé le remet en brouillon
    /// pour que le bouton Valider republie et notifie les équipes.
    /// </summary>
    private async Task ReopenPublishedForRepublishAsync(int weeklyPlanningId)
    {
        if (weeklyPlanningId <= 0) return;
        var planning = await _context.WeeklyPlannings.FindAsync(weeklyPlanningId);
        if (planning is null || planning.Status != PlanningStatus.Published)
            return;
        planning.Status = PlanningStatus.Draft;
        planning.ValidatedBy = null;
    }

    // ----------------------------------------------------
    // PUBLIER (validation Admin/RH — consultation obligatoire)
    // ----------------------------------------------------
    public async Task RecordConsultationAsync(int planningId, int userId)
    {
        var exists = await _context.WeeklyPlannings.AnyAsync(p => p.Id == planningId);
        if (!exists) throw new InvalidOperationException("Planning introuvable.");

        var existing = await _context.PlanningConsultations
            .FirstOrDefaultAsync(c => c.PlanningId == planningId && c.UserId == userId);

        if (existing != null)
        {
            existing.ConsultedAt = DateTime.UtcNow;
        }
        else
        {
            _context.PlanningConsultations.Add(new PlanningConsultation
            {
                PlanningId = planningId,
                UserId = userId,
                ConsultedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }

    public async Task<bool> HasConsultedAsync(int planningId, int userId) =>
        await _context.PlanningConsultations
            .AnyAsync(c => c.PlanningId == planningId && c.UserId == userId);

    public async Task<WeeklyPlanningResponseDto> PublishPlanningAsync(int planningId, int validatorId)
    {
        var planning = await _context.WeeklyPlannings
            .Include(p => p.ShiftAssignments)
            .Include(p => p.SubService)
            .FirstOrDefaultAsync(p => p.Id == planningId);

        if (planning == null) throw new Exception("Planning introuvable");

        if (planning.Status != PlanningStatus.Draft)
            throw new InvalidOperationException("Seuls les brouillons peuvent être validés.");

        var consulted = await HasConsultedAsync(planningId, validatorId);
        if (!consulted)
            throw new InvalidOperationException(
                "Consultation obligatoire avant validation. Ouvrez d'abord le planning.");

        // Anomalies niveau : warning coverage uniquement — ne bloque pas la publication.

        planning.Status = PlanningStatus.Published;
        planning.ValidatedBy = validatorId;
        await _context.SaveChangesAsync();

        var planningUserIds = planning.ShiftAssignments
            .Select(a => a.UserId)
            .Distinct()
            .ToList();

        var users = await _context.Users
            .Where(u => planningUserIds.Contains(u.Id) && u.AuthUserId != null)
            .Select(u => new { u.Id, u.AuthUserId })
            .ToListAsync();

        var alreadyNotified = await _context.PlanningNotifications
            .AnyAsync(n => n.WeeklyPlanningId == planning.Id);
        var message = alreadyNotified
            ? $"Votre planning {planning.WeekCode} a été mis à jour — veuillez le consulter."
            : $"Votre planning {planning.WeekCode} est disponible !";
        var subServiceName = planning.SubService.Name;
        const string deepLink = "/mes-plannings";
        var created = new List<PlanningNotification>();

        foreach (var user in users)
        {
            var notif = new PlanningNotification
            {
                UserId = user.Id,
                AuthUserId = user.AuthUserId!.Value,
                WeeklyPlanningId = planning.Id,
                WeekCode = planning.WeekCode,
                SubServiceName = subServiceName,
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            _context.PlanningNotifications.Add(notif);
            created.Add(notif);
        }

        // Persister d'abord pour disposer des Id avant le push SignalR
        await _context.SaveChangesAsync();

        foreach (var notif in created)
        {
            await _hubContext.Clients
                .Group($"user_{notif.AuthUserId}")
                .SendAsync("PlanningPublished", new
                {
                    id = notif.Id,
                    weekCode = notif.WeekCode,
                    subServiceName = notif.SubServiceName,
                    message = notif.Message,
                    weeklyPlanningId = notif.WeeklyPlanningId,
                    deepLink,
                    createdAt = notif.CreatedAt,
                    isRead = false
                });
        }

        var desktopRecipients = created.Select(n => n.AuthUserId).Distinct().ToList();
        if (_desktopPush is not null && desktopRecipients.Count > 0)
        {
            await _desktopPush.NotifyAsync(new DesktopPushRequest(
                desktopRecipients,
                [],
                "Planning",
                $"Le planning de la semaine {planning.WeekCode} est disponible.",
                "planning",
                deepLink));
        }

        if (_pilotEmails is not null)
            await _pilotEmails.SendToAssignedPilotsAsync(planning.Id, updated: false);

        return await GetPlanningByIdAsync(planning.Id)
            ?? throw new Exception("Erreur publication planning.");
    }

    public async Task NotifyPlanningRepublishedAsync(int planningId, string? reason = null)
    {
        var planning = await _context.WeeklyPlannings
            .Include(p => p.ShiftAssignments)
            .Include(p => p.SubService)
            .FirstOrDefaultAsync(p => p.Id == planningId);

        if (planning == null)
            return;

        if (_pilotEmails is not null)
            await _pilotEmails.SendToAssignedPilotsAsync(planning.Id, updated: true);

        if (planning.Status != PlanningStatus.Published)
            return;

        var subServiceName = planning.SubService?.Name ?? "";
        var reasonText = string.IsNullOrWhiteSpace(reason)
            ? "mise à jour du planning"
            : reason.Trim();

        var agentMsg = $"Votre planning {planning.WeekCode} a été mis à jour — veuillez le consulter.";
        var supervisorMsg =
            "Le planning de votre équipe a été régénéré, veuillez les informer de consulter leurs plannings.";
        var rhMsg =
            $"Alerte RH : planning régénéré pour « {reasonText} » — {subServiceName} ({planning.WeekCode}).";

        var recipients = new Dictionary<int, (int AuthUserId, string Message, string DeepLink)>();

        // Agents affectés
        var planningUserIds = planning.ShiftAssignments.Select(a => a.UserId).Distinct().ToList();
        var agents = await _context.Users
            .Where(u => planningUserIds.Contains(u.Id) && u.AuthUserId != null)
            .Select(u => new { u.Id, AuthUserId = u.AuthUserId!.Value })
            .ToListAsync();
        foreach (var a in agents)
            recipients[a.Id] = (a.AuthUserId, agentMsg, "/mes-plannings");

        // Superviseurs / managers du SubService (Managed* + fallback SubServiceId)
        var supervisors = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.ManagedSubServices)
            .Include(u => u.ManagedServices)
            .Where(u => u.IsActive && u.AuthUserId != null)
            .ToListAsync();

        var subService = await _context.SubServices
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == planning.SubServiceId);
        var serviceId = subService?.ServiceId ?? 0;

        foreach (var u in supervisors)
        {
            var role = u.Role?.Name ?? "";
            var isSupRole = string.Equals(role, "Superviseur", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(role, "Référent technique", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(role, "Coach", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(role, "Chef de projet", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(role, "RP", StringComparison.OrdinalIgnoreCase);
            if (!isSupRole) continue;

            var managedSubs = u.ManagedSubServices?.Select(m => m.SubServiceId).ToHashSet()
                              ?? new HashSet<int>();
            var managedSvcs = u.ManagedServices?.Select(m => m.ServiceId).ToHashSet()
                              ?? new HashSet<int>();
            var inScope = managedSubs.Contains(planning.SubServiceId)
                          || (serviceId > 0 && managedSvcs.Contains(serviceId))
                          || (managedSubs.Count == 0 && managedSvcs.Count == 0
                              && u.SubServiceId == planning.SubServiceId);
            if (!inScope) continue;

            recipients[u.Id] = (u.AuthUserId!.Value, supervisorMsg, "/planning/equipe");
        }

        // RH / Admin — alerte dédiée (écrase si même user)
        var rhAdmins = await _context.Users
            .Include(u => u.Role)
            .Where(u => u.IsActive && u.AuthUserId != null
                        && u.Role != null
                        && (u.Role.Name == "RH" || u.Role.Name == "Admin"
                            || u.Role.Name == "rh" || u.Role.Name == "admin"))
            .ToListAsync();
        foreach (var u in rhAdmins)
            recipients[u.Id] = (u.AuthUserId!.Value, rhMsg, $"/planning/{planning.Id}");

        var created = new List<(PlanningNotification Notif, string DeepLink)>();
        foreach (var (userId, (authUserId, message, deepLink)) in recipients)
        {
            var notif = new PlanningNotification
            {
                UserId = userId,
                AuthUserId = authUserId,
                WeeklyPlanningId = planning.Id,
                WeekCode = planning.WeekCode,
                SubServiceName = subServiceName,
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };
            _context.PlanningNotifications.Add(notif);
            created.Add((notif, deepLink));
        }

        await _context.SaveChangesAsync();

        foreach (var (notif, deepLink) in created)
        {
            await _hubContext.Clients
                .Group($"user_{notif.AuthUserId}")
                .SendAsync("PlanningPublished", new
                {
                    id = notif.Id,
                    weekCode = notif.WeekCode,
                    subServiceName = notif.SubServiceName,
                    message = notif.Message,
                    weeklyPlanningId = notif.WeeklyPlanningId,
                    deepLink,
                    createdAt = notif.CreatedAt,
                    isRead = false
                });
        }
    }

    // ----------------------------------------------------
    // GROUPES SAMEDI
    // ----------------------------------------------------
    public async Task SetSaturdayGroupAsync(SetSaturdayGroupDto dto)
    {
        var existing = await _context.SaturdayGroups
            .FirstOrDefaultAsync(sg => sg.UserId == dto.UserId);

        if (existing != null)
        {
            existing.GroupNumber = dto.GroupNumber;
            existing.IsNewEmployee = dto.IsNewEmployee;
        }
        else
        {
            _context.SaturdayGroups.Add(new SaturdayGroup
            {
                UserId = dto.UserId,
                GroupNumber = dto.GroupNumber,
                IsNewEmployee = dto.IsNewEmployee
            });
        }
        await _context.SaveChangesAsync();
    }

    public async Task SetSaturdayWorkModeAsync(SetSaturdayWorkModeDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId)
            ?? throw new InvalidOperationException("Employé introuvable.");

        if (dto.SaturdayWorkMode is int mode
            && mode != (int)SaturdayWorkMode.EveryHalfDay
            && mode != (int)SaturdayWorkMode.AlternatingFullDay)
        {
            throw new InvalidOperationException(
                "Mode samedi invalide (attendu : null, 1 = tous les samedis 4h, 2 = alternance 8h).");
        }

        user.SaturdayWorkMode = dto.SaturdayWorkMode;

        var effectiveAlternating = !IsEveryHalfDaySaturday(user);
        if (effectiveAlternating)
        {
            var groupNumber = dto.GroupNumber is 1 or 2
                ? dto.GroupNumber.Value
                : 0;

            var existing = await _context.SaturdayGroups
                .FirstOrDefaultAsync(sg => sg.UserId == user.Id);

            if (groupNumber is 1 or 2)
            {
                if (existing != null)
                    existing.GroupNumber = groupNumber;
                else
                {
                    _context.SaturdayGroups.Add(new SaturdayGroup
                    {
                        UserId = user.Id,
                        GroupNumber = groupNumber,
                        IsNewEmployee = false
                    });
                }
            }
            else if (existing == null && user.SubServiceId is int subId)
            {
                await EnsureBalancedSaturdayGroupForUserAsync(user.Id, subId);
            }
        }

        await _context.SaveChangesAsync();

        if (user.SubServiceId is int subAfter && subAfter > 0)
            await NotifySaturdayImbalanceAsync(subAfter, dto.AuthUserId ?? 0);
    }

    public async Task SetEmployeeSpecialCaseAsync(SetEmployeeSpecialCaseDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId)
            ?? throw new InvalidOperationException("Employé introuvable.");

        if (dto.IsSpecialCase)
        {
            var desc = (dto.Description ?? "").Trim();
            if (desc.Length < 3)
                throw new InvalidOperationException(
                    "Description obligatoire pour un cas particulier (ex. diabétique, expatrié).");
            if (desc.Length > 500)
                throw new InvalidOperationException("Description trop longue (max 500).");
            user.IsSpecialCase = true;
            user.SpecialCaseDescription = desc;
        }
        else
        {
            user.IsSpecialCase = false;
            user.SpecialCaseDescription = null;
        }

        await _context.SaveChangesAsync();
    }

    public async Task SetEmployeePlateauTrainingAsync(SetEmployeePlateauTrainingDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId)
            ?? throw new InvalidOperationException("Employé introuvable.");

        user.IsPlateauTraining = dto.IsPlateauTraining;
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Notifie le(s) superviseur(s) si déséquilibre G1/G2.
    /// authUserId &gt; 0 : destinataire principal (celui qui consulte le périmètre).
    /// Idempotent : une notification non lue par destinataire / cellule (WeekCode SAT-IMBALANCE-*).
    /// </summary>
    public async Task<int> NotifySaturdayImbalanceAsync(int subServiceId, int authUserId)
    {
        var balance = await GetSaturdayBalanceAsync(subServiceId);
        if (!balance.IsImbalanced)
            return 0;

        var subName = await _context.SubServices.AsNoTracking()
            .Where(s => s.Id == subServiceId)
            .Select(s => s.Name)
            .FirstOrDefaultAsync() ?? $"Service #{subServiceId}";

        var message =
            $"Déséquilibre des effectifs du samedi ({subName}) : " +
            $"{balance.ProjectedSaturdayGroup1} (semaine G1) vs {balance.ProjectedSaturdayGroup2} (semaine G2) " +
            $"— écart {balance.ImbalanceDelta}. " +
            $"Effectif = {balance.AlwaysOnCount} (tous sam. 4h) + {balance.Group1Count} (G1) + {balance.Group2Count} (G2). " +
            "Rééquilibrez les modes / groupes sur le Périmètre.";

        var weekCode = $"SAT-IMBALANCE-{subServiceId}";
        var recipients = await ResolveSaturdayImbalanceRecipientsAsync(subServiceId, authUserId);
        if (recipients.Count == 0)
            return 0;

        var createdOrUpdated = 0;
        var toPush = new List<PlanningNotification>();

        foreach (var (userId, recipientAuthId) in recipients)
        {
            var existing = await _context.PlanningNotifications
                .FirstOrDefaultAsync(n =>
                    n.AuthUserId == recipientAuthId
                    && n.WeekCode == weekCode
                    && !n.IsRead);

            if (existing != null)
            {
                existing.Message = message;
                existing.SubServiceName = subName;
                existing.CreatedAt = DateTime.UtcNow;
                createdOrUpdated++;
                toPush.Add(existing);
            }
            else
            {
                var notif = new PlanningNotification
                {
                    UserId = userId,
                    AuthUserId = recipientAuthId,
                    WeeklyPlanningId = null,
                    WeekCode = weekCode,
                    SubServiceName = subName,
                    Message = message,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };
                _context.PlanningNotifications.Add(notif);
                toPush.Add(notif);
                createdOrUpdated++;
            }
        }

        await _context.SaveChangesAsync();

        foreach (var notif in toPush)
        {
            await _hubContext.Clients
                .Group($"user_{notif.AuthUserId}")
                .SendAsync("PlanningPublished", new
                {
                    id = notif.Id,
                    weekCode = notif.WeekCode,
                    subServiceName = notif.SubServiceName,
                    message = notif.Message,
                    weeklyPlanningId = (int?)null,
                    deepLink = "/prime",
                    createdAt = notif.CreatedAt,
                    isRead = false
                });
        }

        return createdOrUpdated;
    }

    private async Task<List<(int UserId, int AuthUserId)>> ResolveSaturdayImbalanceRecipientsAsync(
        int subServiceId,
        int authUserId)
    {
        var result = new Dictionary<int, (int UserId, int AuthUserId)>();

        if (authUserId > 0)
        {
            var self = await _context.Users.AsNoTracking()
                .Where(u => u.AuthUserId == authUserId && u.IsActive)
                .Select(u => new { u.Id, AuthId = u.AuthUserId!.Value })
                .FirstOrDefaultAsync();
            if (self != null)
                result[self.AuthId] = (self.Id, self.AuthId);
        }

        var managers = await _context.UserSubServices
            .AsNoTracking()
            .Where(ms => ms.SubServiceId == subServiceId
                         && ms.User.IsActive
                         && ms.User.AuthUserId != null)
            .Select(ms => new { ms.UserId, AuthId = ms.User.AuthUserId!.Value })
            .ToListAsync();

        foreach (var m in managers)
            result[m.AuthId] = (m.UserId, m.AuthId);

        return result.Values.ToList();
    }

    public async Task<SaturdayBalanceDto> GetSaturdayBalanceAsync(int subServiceId)
    {
        const int imbalanceThreshold = 2;

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.SubServiceId == subServiceId && u.IsActive)
            .OrderBy(u => u.LastName)
            .ThenBy(u => u.FirstName)
            .ToListAsync();

        var userIds = users.Select(u => u.Id).ToList();
        var groups = await _context.SaturdayGroups
            .AsNoTracking()
            .Where(sg => userIds.Contains(sg.UserId))
            .ToListAsync();
        var groupByUser = groups.ToDictionary(g => g.UserId);

        var employees = new List<SaturdayEmployeeModeDto>(users.Count);
        var alwaysOn = 0;
        var g1 = 0;
        var g2 = 0;

        foreach (var u in users)
        {
            var effective = ResolveEffectiveSaturdayWorkMode(u);
            groupByUser.TryGetValue(u.Id, out var sg);
            var groupNumber = sg?.GroupNumber ?? 0;

            if (effective == (int)SaturdayWorkMode.EveryHalfDay)
                alwaysOn++;
            else if (groupNumber == 1)
                g1++;
            else if (groupNumber == 2)
                g2++;

            employees.Add(new SaturdayEmployeeModeDto(
                u.Id,
                u.Guid,
                UserDisplayName.Format(u.FirstName, u.LastName),
                u.Level,
                u.SaturdayWorkMode,
                effective,
                groupNumber,
                u.IsSpecialCase,
                u.SpecialCaseDescription,
                u.IsPlateauTraining));
        }

        var projected1 = alwaysOn + g1;
        var projected2 = alwaysOn + g2;
        var delta = Math.Abs(projected1 - projected2);
        var (g1Sat, g2Sat) = ResolveSaturdayGroupDates(DateOnly.FromDateTime(DateTime.Today));

        return new SaturdayBalanceDto(
            subServiceId,
            alwaysOn,
            g1,
            g2,
            projected1,
            projected2,
            delta >= imbalanceThreshold,
            delta,
            employees,
            g1Sat,
            g2Sat);
    }

    public async Task<IEnumerable<object>> GetSaturdayGroupsAsync(int subServiceId)
    {
        var users = await _context.Users
            .Where(u => u.SubServiceId == subServiceId && u.IsActive)
            .ToListAsync();

        var userIds = users.Select(u => u.Id).ToList();
        var groups = await _context.SaturdayGroups
            .Where(sg => userIds.Contains(sg.UserId))
            .ToListAsync();

        return users.Select(u =>
        {
            var g = groups.FirstOrDefault(sg => sg.UserId == u.Id);
            return (object)new
            {
                userId = u.Id,
                guid = u.Guid,
                fullName = UserDisplayName.Format(u.FirstName, u.LastName),
                level = u.Level,
                saturdayWorkMode = u.SaturdayWorkMode,
                effectiveMode = ResolveEffectiveSaturdayWorkMode(u),
                groupNumber = g?.GroupNumber ?? 0,
                isNewEmployee = g?.IsNewEmployee ?? false
            };
        });
    }

    /// <summary>Assigne le groupe minoritaire si l'employé n'en a pas encore.</summary>
    private async Task EnsureBalancedSaturdayGroupForUserAsync(int userId, int subServiceId)
    {
        var already = await _context.SaturdayGroups.AnyAsync(sg => sg.UserId == userId);
        if (already) return;

        var peerUserIds = await _context.Users
            .Where(u => u.SubServiceId == subServiceId && u.IsActive && u.Id != userId)
            .Select(u => u.Id)
            .ToListAsync();

        var group1Count = await _context.SaturdayGroups
            .CountAsync(sg => peerUserIds.Contains(sg.UserId) && sg.GroupNumber == 1);
        var group2Count = await _context.SaturdayGroups
            .CountAsync(sg => peerUserIds.Contains(sg.UserId) && sg.GroupNumber == 2);

        _context.SaturdayGroups.Add(new SaturdayGroup
        {
            UserId = userId,
            GroupNumber = group1Count <= group2Count ? 1 : 2,
            IsNewEmployee = false
        });
    }

    // ----------------------------------------------------
    // VUE EMPLOY�
    // ----------------------------------------------------
    public async Task<MyPlanningDto?> GetMyPlanningAsync(int userId, string weekCode)
    {
        var planning = await _context.WeeklyPlannings
            .Include(p => p.SubService)
            .Include(p => p.ShiftAssignments)
                .ThenInclude(a => a.Shift)
            .Include(p => p.ShiftAssignments)
                .ThenInclude(a => a.SubServiceShiftConfig)
            .Include(p => p.ShiftAssignments)
                .ThenInclude(a => a.ShiftModeProfile)
            .FirstOrDefaultAsync(p => p.WeekCode == weekCode &&
                                      p.ShiftAssignments.Any(a => a.UserId == userId) &&
                                      p.Status == PlanningStatus.Published);

        if (planning == null) return null;

        var exceptionalApplied = await LoadExceptionalAppliedKeysAsync(
            planning.WeekCode, planning.SubServiceId);

        return new MyPlanningDto
        {
            WeeklyPlanningId = planning.Id,
            WeekCode = planning.WeekCode,
            WeekStartDate = planning.WeekStartDate,
            Status = planning.Status.ToString(),
            SubServiceName = planning.SubService.Name,
            Days = planning.ShiftAssignments
                .Where(a => a.UserId == userId)
                .OrderBy(a => a.AssignedDate)
                .Select(a => MapToDayDtoNew(a, null, exceptionalApplied))
                .ToList()
        };
    }

    public async Task<IEnumerable<MyPlanningDto>> GetMyPlanningHistoryAsync(int userId)
    {
        var plannings = await _context.WeeklyPlannings
            .Include(p => p.SubService)
            .Include(p => p.ShiftAssignments.Where(a => a.UserId == userId))
                .ThenInclude(a => a.Shift)
            .Include(p => p.ShiftAssignments.Where(a => a.UserId == userId))
                .ThenInclude(a => a.SubServiceShiftConfig)
            .Include(p => p.ShiftAssignments.Where(a => a.UserId == userId))
                .ThenInclude(a => a.ShiftModeProfile)
            .Where(p => p.ShiftAssignments.Any(a => a.UserId == userId) &&
                        p.Status == PlanningStatus.Published)
            .OrderByDescending(p => p.WeekStartDate)
            .Take(52)
            .ToListAsync();

        var result = new List<MyPlanningDto>();
        foreach (var p in plannings)
        {
            var exceptionalApplied = await LoadExceptionalAppliedKeysAsync(p.WeekCode, p.SubServiceId);
            result.Add(new MyPlanningDto
            {
                WeeklyPlanningId = p.Id,
                WeekCode = p.WeekCode,
                WeekStartDate = p.WeekStartDate,
                Status = p.Status.ToString(),
                SubServiceName = p.SubService.Name,
                Days = p.ShiftAssignments
                    .OrderBy(a => a.AssignedDate)
                    .Select(a => MapToDayDtoNew(a, null, exceptionalApplied))
                    .ToList()
            });
        }
        return result;
    }

    public async Task<IReadOnlyList<MyPlanningDto>> GetAgentPlanningHistoryAsync(
        int planningUserId,
        DateOnly? from,
        DateOnly? to)
    {
        var exists = await _context.Users.AsNoTracking()
            .AnyAsync(u => u.Id == planningUserId);
        if (!exists)
            return [];

        var query = _context.WeeklyPlannings
            .AsNoTracking()
            .Include(p => p.SubService)
            .Include(p => p.ShiftAssignments.Where(a => a.UserId == planningUserId))
                .ThenInclude(a => a.Shift)
            .Include(p => p.ShiftAssignments.Where(a => a.UserId == planningUserId))
                .ThenInclude(a => a.SubServiceShiftConfig)
            .Include(p => p.ShiftAssignments.Where(a => a.UserId == planningUserId))
                .ThenInclude(a => a.ShiftModeProfile)
            .Where(p => p.ShiftAssignments.Any(a => a.UserId == planningUserId)
                        && p.Status == PlanningStatus.Published);

        if (from.HasValue)
            query = query.Where(p => p.WeekStartDate >= from.Value);
        if (to.HasValue)
            query = query.Where(p => p.WeekStartDate <= to.Value);

        var plannings = await query
            .OrderByDescending(p => p.WeekStartDate)
            .Take(52)
            .ToListAsync();

        var result = new List<MyPlanningDto>();
        foreach (var p in plannings)
        {
            var exceptionalApplied = await LoadExceptionalAppliedKeysAsync(p.WeekCode, p.SubServiceId);
            result.Add(new MyPlanningDto
            {
                WeeklyPlanningId = p.Id,
                WeekCode = p.WeekCode,
                WeekStartDate = p.WeekStartDate,
                Status = p.Status.ToString(),
                SubServiceName = p.SubService?.Name ?? string.Empty,
                Days = p.ShiftAssignments
                    .Where(a => a.UserId == planningUserId)
                    .OrderBy(a => a.AssignedDate)
                    .Select(a => MapToDayDtoNew(a, null, exceptionalApplied))
                    .ToList()
            });
        }
        return result;
    }

    // ----------------------------------------------------
    // NOTIFICATIONS DE PUBLICATION (persist�es)
    // ----------------------------------------------------
    public async Task<IEnumerable<PlanningNotificationDto>> GetMyNotificationsAsync(int authUserId)
    {
        var rows = await _context.PlanningNotifications
            .Where(n => n.AuthUserId == authUserId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync();

        return rows.Select(n => new PlanningNotificationDto
        {
            Id = n.Id,
            WeeklyPlanningId = n.WeeklyPlanningId,
            WeekCode = n.WeekCode,
            SubServiceName = n.SubServiceName,
            Message = n.Message,
            IsRead = n.IsRead,
            CreatedAt = n.CreatedAt,
            DeepLink = ResolveNotificationDeepLink(n.WeekCode, n.SubServiceName, n.Message)
        });
    }

    /// <summary>Route SPA dérivée du type de notification (planning / formation / demande).</summary>
    public static string ResolveNotificationDeepLink(string weekCode, string subServiceName, string message)
    {
        var sub = (subServiceName ?? string.Empty).Trim();
        var code = (weekCode ?? string.Empty).ToUpperInvariant();

        if (sub.Contains("Demande", StringComparison.OrdinalIgnoreCase))
        {
            if ((message ?? string.Empty).Contains("Nouvelle demande", StringComparison.OrdinalIgnoreCase))
                return "/planning/change-requests";
            return "/mes-plannings";
        }

        if (code.StartsWith("SAT-IMBALANCE-", StringComparison.Ordinal))
            return "/prime";

        if (code.StartsWith("INIT-DOCS-", StringComparison.Ordinal))
            return "/formations?tab=initial";

        if (code.StartsWith("TRAINING-ANIM-", StringComparison.Ordinal)
            || code.StartsWith("TRAINING-START-ANIM-", StringComparison.Ordinal))
            return "/mes-sessions";

        if (code.StartsWith("TRAIN-", StringComparison.Ordinal)
            || code.StartsWith("TRAINING-", StringComparison.Ordinal)
            || sub.Contains("Formation", StringComparison.OrdinalIgnoreCase))
            return "/mes-formations";

        return "/mes-plannings";
    }

    public async Task MarkNotificationReadAsync(int id, int authUserId)
    {
        var notif = await _context.PlanningNotifications
            .FirstOrDefaultAsync(n => n.Id == id && n.AuthUserId == authUserId);
        if (notif is null || notif.IsRead) return;

        notif.IsRead = true;
        notif.ReadAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    public async Task MarkAllNotificationsReadAsync(int authUserId)
    {
        var unread = await _context.PlanningNotifications
            .Where(n => n.AuthUserId == authUserId && !n.IsRead)
            .ToListAsync();
        if (unread.Count == 0) return;

        var now = DateTime.UtcNow;
        foreach (var n in unread)
        {
            n.IsRead = true;
            n.ReadAt = now;
        }
        await _context.SaveChangesAsync();
    }

    // ----------------------------------------------------
    // GET PLANNINGS
    // ----------------------------------------------------
    public async Task<WeeklyPlanningResponseDto?> GetPlanningByIdAsync(int id)
    {
        var planning = await _context.WeeklyPlannings
            .AsNoTracking()
            .AsSplitQuery()
            .Include(p => p.SubService)
            .Include(p => p.WeeklyShiftConfigs).ThenInclude(c => c.Shift)
            .Include(p => p.ShiftAssignments).ThenInclude(a => a.Shift)
            .Include(p => p.ShiftAssignments).ThenInclude(a => a.SubServiceShiftConfig)
            .Include(p => p.ShiftAssignments).ThenInclude(a => a.ShiftModeProfile)
            .Include(p => p.ShiftAssignments).ThenInclude(a => a.User)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (planning == null) return null;

        // Présence min / critique : aligner snapshot semaine sur le modèle avant couverture
        try
        {
            await EnsureWeekSnapshotAsync(
                planning.SubServiceId, planning.WeekCode, planning.WeekStartDate, forceRefresh: false);
        }
        catch (InvalidOperationException)
        {
            // Pas de modèle shifts — continuer avec le snapshot éventuel
        }

        var comments = await _context.PlanningComments
            .Where(c => c.WeeklyPlanningId == id)
            .ToListAsync();

        // ? AJOUTER � charger les cong�s de la semaine
        var userIds = planning.ShiftAssignments.Select(a => a.UserId).Distinct().ToList();
        var weekEnd = planning.WeekStartDate.AddDays(6);
        var conges = await _context.Conges
            .Where(c => userIds.Contains(c.UserId)
                     && c.Status == CongeStatus.Approved
                     && c.StartDate <= weekEnd
                     && c.EndDate >= planning.WeekStartDate)
            .ToListAsync();

        var subConfigs = await _context.SubServiceShiftConfigs
            .Include(c => c.ShiftModeProfile)
            .Where(c => c.SubServiceId == planning.SubServiceId && c.WeekCode == planning.WeekCode)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();

        List<ShiftConfigResponseDto> shiftConfigs;
        if (planning.WeeklyShiftConfigs.Count > 0)
        {
            shiftConfigs = planning.WeeklyShiftConfigs.Select(c => new ShiftConfigResponseDto
            {
                ShiftId = c.ShiftId,
                ShiftLabel = c.Shift.Label,
                StartTime = c.Shift.StartTime.ToString("HH:mm"),
                RequiredCount = c.RequiredCount,
                Percentage = c.Percentage
            }).ToList();
        }
        else
        {
            var totalRequired = subConfigs.Sum(c => c.RequiredCount);
            shiftConfigs = subConfigs.Select(c => new ShiftConfigResponseDto
            {
                ShiftId = c.Id,
                ShiftLabel = c.Label,
                StartTime = c.StartTime.ToString("HH:mm"),
                ShiftKind = c.ShiftKind.ToString(),
                RequiredCount = c.RequiredCount,
                Percentage = totalRequired > 0
                    ? Math.Round((decimal)c.RequiredCount / totalRequired * 100, 1)
                    : 0,
                BreakSlots = BreakSlotPlanner.ResolveBreakSlots(c)
                    .Select(s => s.ToString("HH:mm")).ToList(),
                BreakDurationMinutes = c.BreakDurationMinutes > 0 ? c.BreakDurationMinutes : 60,
                IsCriticalCell = c.IsCriticalCell,
                ShiftModeProfileId = c.ShiftModeProfileId,
                ShiftModeTitle = c.ShiftModeProfile?.Title
            }).ToList();
        }

        var usersForCoverage = planning.ShiftAssignments
            .Select(a => a.User)
            .Where(u => u != null)
            .GroupBy(u => u!.Id)
            .ToDictionary(g => g.Key, g => g.First()!);
        var modeProfiles = await _context.ShiftModeProfiles
            .AsNoTracking()
            .Where(p => p.SubServiceId == planning.SubServiceId)
            .ToListAsync();
        var coverage = BuildCoverageReport(planning, subConfigs, usersForCoverage, modeProfiles);

        var exceptionalApplied = await LoadExceptionalAppliedKeysAsync(
            planning.WeekCode, planning.SubServiceId);

        return new WeeklyPlanningResponseDto
        {
            Id = planning.Id,
            SubServiceId = planning.SubServiceId,
            WeekCode = planning.WeekCode,
            WeekStartDate = planning.WeekStartDate,
            Status = planning.Status.ToString(),
            TotalEffectif = planning.TotalEffectif,
            SaturdayGroupId = planning.SaturdayGroupId,
            SubServiceName = planning.SubService.Name,
            ShiftConfigs = shiftConfigs,
            CoverageReport = coverage,
            Assignments = planning.ShiftAssignments
                .GroupBy(a => a.UserId)
                .Select(g =>
                {
                    var user = g.First().User;
                    return new EmployeePlanningDto
                    {
                        UserId = g.Key,
                        FullName = UserDisplayName.Format(user.FirstName, user.LastName),
                        FirstName = user.FirstName ?? "",
                        LastName = user.LastName ?? "",
                        IsNewEmployee = g.First().IsNewEmployee,
                        Level = user.Level,
                        ManagerComment = comments.FirstOrDefault(c => c.UserId == g.Key)?.Comment,
                        IsSpecialCase = user.IsSpecialCase,
                        SpecialCaseDescription = user.IsSpecialCase
                            ? user.SpecialCaseDescription
                            : null,
                        IsPlateauTraining = user.IsPlateauTraining,
                        IdTechnicien = user.IdTechnicien,
                        HtelCode = user.HtelCode,
                        HtelTechnicienName = user.HtelTechnicienName,
                        Days = g.OrderBy(a => a.AssignedDate)
                                 .Select(a => MapToDayDtoNew(a, conges, exceptionalApplied))
                                 .ToList()
                    };
                }).ToList()
        };
    }

    public async Task<IEnumerable<WeeklyPlanningResponseDto>> GetPlanningsBySubServiceAsync(
        int subServiceId)
    {
        var ids = await _context.WeeklyPlannings
            .Where(p => p.SubServiceId == subServiceId)
            .OrderByDescending(p => p.WeekStartDate)
            .Select(p => p.Id)
            .ToListAsync();

        var result = new List<WeeklyPlanningResponseDto>();
        foreach (var id in ids)
        {
            var dto = await GetPlanningByIdAsync(id);
            if (dto != null) result.Add(dto);
        }
        return result;
    }

    public async Task DeletePlanningAsync(int id)
    {
        var planning = await _context.WeeklyPlannings
            .Include(p => p.ShiftAssignments)
            .Include(p => p.WeeklyShiftConfigs)
            .FirstOrDefaultAsync(p => p.Id == id)
            ?? throw new Exception("Planning introuvable.");

        _context.ShiftAssignments.RemoveRange(planning.ShiftAssignments);
        _context.WeeklyShiftConfigs.RemoveRange(planning.WeeklyShiftConfigs);
        _context.WeeklyPlannings.Remove(planning);

        await _context.SaveChangesAsync();
    }

    // ----------------------------------------------------
    // OVERRIDE SAMEDI
    // ----------------------------------------------------
    public async Task<DayAssignmentDto> OverrideSaturdayShiftAsync(OverrideSaturdayDto dto)
    {
        ShiftAssignment assignment;

        if (dto.ShiftAssignmentId > 0)
        {
            assignment = await _context.ShiftAssignments
                .Include(a => a.SubServiceShiftConfig)
                .FirstOrDefaultAsync(a => a.Id == dto.ShiftAssignmentId)
                ?? throw new Exception("Assignment introuvable.");

            assignment.SubServiceShiftConfigId = dto.NewSubServiceShiftConfigId;
            assignment.IsManagerOverride = true;
        }
        else
        {
            if (dto.WeeklyPlanningId == 0 || dto.UserId == 0)
                throw new Exception(
                    "WeeklyPlanningId et UserId sont requis pour cr�er une assignation samedi.");

            var planning = await _context.WeeklyPlannings
                .FirstOrDefaultAsync(p => p.Id == dto.WeeklyPlanningId)
                ?? throw new Exception("Planning introuvable.");

            var existing = await _context.ShiftAssignments
                .FirstOrDefaultAsync(a =>
                    a.WeeklyPlanningId == dto.WeeklyPlanningId &&
                    a.UserId == dto.UserId &&
                    a.IsSaturday);

            if (existing != null)
            {
                existing.SubServiceShiftConfigId = dto.NewSubServiceShiftConfigId;
                existing.IsOnLeave = false;
                existing.IsManagerOverride = true;
                assignment = existing;
            }
            else
            {
                var saturdayDate = planning.WeekStartDate.AddDays(5);

                assignment = new ShiftAssignment
                {
                    WeeklyPlanningId = dto.WeeklyPlanningId,
                    UserId = dto.UserId,
                    SubServiceShiftConfigId = dto.NewSubServiceShiftConfigId,
                    AssignedDate = saturdayDate,
                    DayOfWeek = DayOfWeekEnum.Saturday,
                    IsSaturday = true,
                    IsOnLeave = false,
                    IsManagerOverride = true,
                    IsNewEmployee = false,
                    IsHalfDaySaturday = false,
                    SaturdaySlot = 0
                };
                _context.ShiftAssignments.Add(assignment);
            }
        }

        await ReopenPublishedForRepublishAsync(assignment.WeeklyPlanningId);
        await _context.SaveChangesAsync();

        // Recharger la config pour avoir label/heure/plage pause
        await _context.Entry(assignment)
            .Reference(a => a.SubServiceShiftConfig)
            .LoadAsync();

        // FIX � Assigner une pause si elle n existe pas encore
        if (assignment.SubServiceShiftConfig != null && assignment.BreakTime == null)
        {
            var slots = BreakSlotPlanner.ResolveBreakSlots(assignment.SubServiceShiftConfig);

            if (slots.Count > 0)
            {
                assignment.BreakTime = slots.First();
                await _context.SaveChangesAsync();
            }
        }

        return MapToDayDtoNew(assignment);
    }

    // ----------------------------------------------------
    // COMMENTAIRES MANAGER
    // ----------------------------------------------------
    public async Task<PlanningCommentDto> SaveCommentAsync(SavePlanningCommentDto dto)
    {
        var planning = await _context.WeeklyPlannings.FindAsync(dto.WeeklyPlanningId)
            ?? throw new Exception("Planning introuvable.");

        if (planning.Status == PlanningStatus.Archived)
            throw new InvalidOperationException(
                "Impossible d'ajouter un commentaire sur un planning archivé.");

        var existing = await _context.PlanningComments
            .FirstOrDefaultAsync(c =>
                c.WeeklyPlanningId == dto.WeeklyPlanningId &&
                c.UserId == dto.UserId);

        if (existing != null)
        {
            existing.Comment = dto.Comment;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new PlanningComment
            {
                WeeklyPlanningId = dto.WeeklyPlanningId,
                UserId = dto.UserId,
                Comment = dto.Comment,
                CreatedBy = dto.CreatedBy,
                CreatedAt = DateTime.UtcNow
            };
            _context.PlanningComments.Add(existing);
        }

        await _context.SaveChangesAsync();

        var user = await _context.Users.FindAsync(dto.UserId);
        return new PlanningCommentDto
        {
            Id = existing.Id,
            UserId = existing.UserId,
            FullName = user != null ? UserDisplayName.Format(user.FirstName, user.LastName) : "",
            Comment = existing.Comment,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = existing.UpdatedAt
        };
    }

    public async Task DeleteCommentAsync(int planningId, int userId)
    {
        var comment = await _context.PlanningComments
            .FirstOrDefaultAsync(c =>
                c.WeeklyPlanningId == planningId &&
                c.UserId == userId);

        if (comment != null)
        {
            _context.PlanningComments.Remove(comment);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<IEnumerable<PlanningCommentDto>> GetCommentsAsync(int planningId)
    {
        return await _context.PlanningComments
            .Include(c => c.User)
            .Where(c => c.WeeklyPlanningId == planningId)
            .Select(c => new PlanningCommentDto
            {
                Id = c.Id,
                UserId = c.UserId,
                FullName = UserDisplayName.Format(c.User.FirstName, c.User.LastName),
                Comment = c.Comment,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync();
    }

    // ----------------------------------------------------
    // HELPERS — génération samedi (niveau contractuel)
    // ----------------------------------------------------
    private Task<ShiftAssignment?> GenerateSaturdayAssignmentFromConfigAsync(
        User employee,
        WeeklyPlanning planning,
        List<SubServiceShiftConfig> shiftConfigs,
        List<SaturdayGroup> saturdayGroups,
        int employeeIndex,
        Dictionary<int, int> beginnerHalfDaySlotCounts,
        IReadOnlyDictionary<int, List<int>> weekShiftHistoryByUser,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, int> saturdayShiftCountToday,
        IReadOnlyDictionary<int, SaturdayHistory> previousHistoryByUser)
    {
        var satGroup = saturdayGroups.FirstOrDefault(sg => sg.UserId == employee.Id);
        var orderedConfigs = shiftConfigs.OrderBy(s => s.StartTime).ThenBy(s => s.DisplayOrder).ToList();

        previousHistoryByUser.TryGetValue(employee.Id, out var previousHistory);
        var worksThisSaturday = ComputeSaturdayIntendedOn(
            employee, previousHistory, satGroup, planning.SaturdayGroupId);

        // Débutant / mode « tous les samedis 4h » : demi-journée, créneau auto équitable
        if (IsEveryHalfDaySaturday(employee))
        {
            if (orderedConfigs.Count == 0) return Task.FromResult<ShiftAssignment?>(null);

            var slot = PickBalancedHalfDaySlot(beginnerHalfDaySlotCounts);
            var shiftConfig = slot == 1 || orderedConfigs.Count == 1
                ? orderedConfigs[0]
                : orderedConfigs[Math.Min(1, orderedConfigs.Count - 1)];

            beginnerHalfDaySlotCounts[slot] = beginnerHalfDaySlotCounts.GetValueOrDefault(slot, 0) + 1;

            return Task.FromResult<ShiftAssignment?>(new ShiftAssignment
            {
                WeeklyPlanningId = planning.Id,
                UserId = employee.Id,
                SubServiceShiftConfigId = shiftConfig.Id,
                AssignedDate = planning.WeekStartDate.AddDays(5),
                DayOfWeek = DayOfWeekEnum.Saturday,
                IsSaturday = true,
                IsNewEmployee = true,
                IsHalfDaySaturday = true,
                SaturdaySlot = slot
            });
        }

        if (!worksThisSaturday)
            return Task.FromResult<ShiftAssignment?>(null);

        if (orderedConfigs.Count == 0)
            return Task.FromResult<ShiftAssignment?>(null);

        var weekNumber = System.Globalization.ISOWeek.GetWeekOfYear(
            planning.WeekStartDate.ToDateTime(TimeOnly.MinValue));
        var shiftIndex = (employeeIndex + weekNumber) % orderedConfigs.Count;

        var fridayId = ShiftDispersionSelector.YesterdayShiftId(weekShiftHistoryByUser, employee.Id);
        var chosen = ShiftDispersionSelector.SelectSaturday(
            orderedConfigs,
            shiftIndex,
            fridayId,
            weekShiftHistoryByUser,
            employee.Id,
            usersById,
            saturdayShiftCountToday);

        return Task.FromResult<ShiftAssignment?>(new ShiftAssignment
        {
            WeeklyPlanningId = planning.Id,
            UserId = employee.Id,
            SubServiceShiftConfigId = chosen.Id,
            AssignedDate = planning.WeekStartDate.AddDays(5),
            DayOfWeek = DayOfWeekEnum.Saturday,
            IsSaturday = true,
            IsNewEmployee = false,
            IsHalfDaySaturday = false,
            SaturdaySlot = 0
        });
    }

    /// <summary>
    /// Tour de rotation samedi prévu (indépendant de férié / congé / présence réelle).
    /// Mode tous-samedis-4h → toujours ON ; G1/G2 prime sur l’historique (même groupe = même samedi).
    /// </summary>
    private static bool ComputeSaturdayIntendedOn(
        User employee,
        SaturdayHistory? previousHistory,
        SaturdayGroup? satGroup,
        int planningSaturdayGroupId)
    {
        if (IsEveryHalfDaySaturday(employee))
            return true;

        if (satGroup != null)
            return satGroup.GroupNumber == planningSaturdayGroupId;

        if (previousHistory != null)
            return !previousHistory.WorkedSaturday;

        // Pas de groupe ni d'historique → ON (assignation de groupe ensuite)
        return true;
    }

    /// <summary>Choisit le créneau demi-journée le moins chargé (égalité → 1).</summary>
    private static int PickBalancedHalfDaySlot(Dictionary<int, int> counts)
    {
        var c1 = counts.GetValueOrDefault(1, 0);
        var c2 = counts.GetValueOrDefault(2, 0);
        return c1 <= c2 ? 1 : 2;
    }

    /// <summary>
    /// Mode effectif : override superviseur, sinon défaut Niveau (1 → 4h tous, sinon alternance).
    /// </summary>
    private static int ResolveEffectiveSaturdayWorkMode(User employee)
    {
        if (employee.SaturdayWorkMode == (int)SaturdayWorkMode.EveryHalfDay
            || employee.SaturdayWorkMode == (int)SaturdayWorkMode.AlternatingFullDay)
            return employee.SaturdayWorkMode.Value;

        return employee.Level == 1
            ? (int)SaturdayWorkMode.EveryHalfDay
            : (int)SaturdayWorkMode.AlternatingFullDay;
    }

    private static bool IsEveryHalfDaySaturday(User employee) =>
        ResolveEffectiveSaturdayWorkMode(employee) == (int)SaturdayWorkMode.EveryHalfDay;

    private static bool IsBeginnerLevel(User employee) => employee.Level == 1;

    /// <summary>
    /// Clés (userId, date) des demandes exceptionnelles Approved — pour tag DE à l'affichage.
    /// </summary>
    private async Task<HashSet<(int UserId, DateOnly Date)>> LoadExceptionalAppliedKeysAsync(
        string weekCode,
        int subServiceId)
    {
        var rows = await _context.PlanningExceptionalRequests
            .AsNoTracking()
            .Where(r =>
                r.WeekCode == weekCode
                && r.SubServiceId == subServiceId
                && r.Status == PlanningExceptionalRequestStatus.Approved)
            .Select(r => new { r.RequesterUserId, r.RequestedDate })
            .ToListAsync();

        return rows.Select(r => (r.RequesterUserId, r.RequestedDate)).ToHashSet();
    }

    /// <summary>
    /// Charge les demandes exceptionnelles Approved et résout template → snapshot semaine.
    /// </summary>
    private async Task<Dictionary<(int UserId, DateOnly Date), SubServiceShiftConfig?>> LoadExceptionalShiftPinsAsync(
        string weekCode,
        int subServiceId,
        List<SubServiceShiftConfig> weekSnapshots)
    {
        var result = new Dictionary<(int UserId, DateOnly Date), SubServiceShiftConfig?>();

        var approved = await _context.PlanningExceptionalRequests
            .AsNoTracking()
            .Include(r => r.RequestedShiftTemplate)
            .Where(r =>
                r.WeekCode == weekCode
                && r.SubServiceId == subServiceId
                && r.Status == PlanningExceptionalRequestStatus.Approved)
            .ToListAsync();

        if (approved.Count == 0)
            return result;

        foreach (var req in approved)
        {
            var template = req.RequestedShiftTemplate;
            var snapshot = weekSnapshots.FirstOrDefault(s =>
                                s.StartTime == template.StartTime
                                && string.Equals(s.Label, template.Label, StringComparison.OrdinalIgnoreCase))
                           ?? weekSnapshots.FirstOrDefault(s => s.StartTime == template.StartTime)
                           ?? weekSnapshots.FirstOrDefault(s => s.DisplayOrder == template.DisplayOrder);

            result[(req.RequesterUserId, req.RequestedDate)] = snapshot;
        }

        return result;
    }

    /// <summary>
    /// Pins renfort Selected (Filled) — appliqués au samedi sans toucher intended history.
    /// </summary>
    private async Task<Dictionary<(int UserId, DateOnly Date), SubServiceShiftConfig?>> LoadReinforcementShiftPinsAsync(
        string weekCode,
        int subServiceId,
        DateOnly saturdayDate,
        List<SubServiceShiftConfig> weekSnapshots)
    {
        var result = new Dictionary<(int UserId, DateOnly Date), SubServiceShiftConfig?>();

        var selected = await _context.PlanningReinforcementVolunteers
            .AsNoTracking()
            .Include(v => v.Request)
            .Include(v => v.SelectedShiftConfig)
            .Where(v =>
                v.Status == PlanningReinforcementVolunteerStatus.Selected
                && v.SelectedShiftConfigId != null
                && v.Request.WeekCode == weekCode
                && v.Request.SubServiceId == subServiceId
                && v.Request.SaturdayDate == saturdayDate
                && v.Request.Status == PlanningReinforcementRequestStatus.Filled)
            .ToListAsync();

        foreach (var vol in selected)
        {
            var template = vol.SelectedShiftConfig;
            if (template == null) continue;
            var snapshot = weekSnapshots.FirstOrDefault(s =>
                                s.StartTime == template.StartTime
                                && string.Equals(s.Label, template.Label, StringComparison.OrdinalIgnoreCase))
                           ?? weekSnapshots.FirstOrDefault(s => s.StartTime == template.StartTime)
                           ?? weekSnapshots.FirstOrDefault(s => s.DisplayOrder == template.DisplayOrder)
                           ?? (template.IsTemplate ? null : template);

            if (snapshot != null)
                result[(vol.UserId, saturdayDate)] = snapshot;
        }

        return result;
    }

    private static string GetPreviousWeekCode(string weekCode)
    {
        var parts = weekCode.Split('-');
        var year = int.Parse(parts[0]);
        var week = int.Parse(parts[1].Replace("W", ""));

        if (week == 1)
            return $"{year - 1}-W52";

        return $"{year}-W{(week - 1):D2}";
    }

    /// <summary>
    /// OBSOLETE — ne plus appeler. Les quotas prod (RequiredCount) sont figés ;
    /// la génération ne doit pas les réécrire pour « coller » à l'effectif.
    /// </summary>
    [Obsolete("Ne pas rescale les quotas à la génération — besoin prod figé.")]
    private static void RescaleShiftQuotasToEffectif(
        List<SubServiceShiftConfig> shiftConfigs, int employeeCount)
    {
        _ = shiftConfigs;
        _ = employeeCount;
        // No-op volontaire : 18 reste 18.
    }

    private void AssignBreakTimesFromConfig(
        List<ShiftAssignment> dayAssignments,
        List<SubServiceShiftConfig> shiftConfigs,
        int totalEmployees,
        PlateauBreakPacker.BreakFairnessCounters? fairnessCounters = null,
        IReadOnlySet<int>? specialCaseUserIds = null)
    {
        _ = totalEmployees;
        if (!dayAssignments.Any()) return;

        var configsById = shiftConfigs.ToDictionary(c => c.Id);
        var cellMinPresence = shiftConfigs.Count > 0
            ? BreakSlotPlanner.ClampMinPresence(shiftConfigs.First().MinPresencePercent)
            : 70;

        PlateauBreakPacker.AssignDayBreaks(
            dayAssignments, configsById, cellMinPresence, fairnessCounters, specialCaseUserIds);
    }

    /// <summary>
    /// Diversifie +3h / +4h / +5h entre collègues du même niveau (même shift / même jour)
    /// pour éviter qu'un pilote enchaîne les +3h pendant qu'un pair reste en +5h (ou l'inverse).
    /// Un swap n'est accepté que s'il conserve le seuil plateau (métrique P).
    /// Cas particuliers : jamais swap vers un break extrême (+3h/+5h).
    /// </summary>
    private static void RepairBreakOffsetDiversity(
        List<ShiftAssignment> assignments,
        List<SubServiceShiftConfig> shiftConfigs,
        IReadOnlyDictionary<int, User> usersById)
    {
        var configsById = shiftConfigs.ToDictionary(c => c.Id);
        var cellMinPresence = shiftConfigs.Count > 0
            ? BreakSlotPlanner.ClampMinPresence(shiftConfigs.First().MinPresencePercent)
            : 70;
        const int maxPasses = 60;

        for (var pass = 0; pass < maxPasses; pass++)
        {
            var earlyCounts = CountOffsetBucket(assignments, configsById, BreakSlotPlanner.BreakOffsetBucket.Early);
            var lateCounts = CountOffsetBucket(assignments, configsById, BreakSlotPlanner.BreakOffsetBucket.Late);
            var improved = false;

            var dayGroups = assignments
                .Where(a =>
                    !a.IsOnLeave && !a.IsHoliday
                    && a.SubServiceShiftConfigId != null
                    && a.BreakTime.HasValue)
                .GroupBy(a => a.AssignedDate)
                .OrderBy(g => g.Key);

            foreach (var day in dayGroups)
            {
                var dayList = day.ToList();
                var (ranges, breaks) = BuildDayPresenceSnapshot(dayList, configsById);

                for (var i = 0; i < dayList.Count; i++)
                {
                    for (var j = i + 1; j < dayList.Count; j++)
                    {
                        var a = dayList[i];
                        var b = dayList[j];
                        if (!usersById.TryGetValue(a.UserId, out var ua)
                            || !usersById.TryGetValue(b.UserId, out var ub)
                            || ua.Level != ub.Level)
                            continue;

                        if (!configsById.TryGetValue(a.SubServiceShiftConfigId!.Value, out var cfgA)
                            || !configsById.TryGetValue(b.SubServiceShiftConfigId!.Value, out var cfgB))
                            continue;

                        if (a.SubServiceShiftConfigId != b.SubServiceShiftConfigId)
                            continue;

                        var bucketA = BreakSlotPlanner.GetBreakOffsetBucket(cfgA.StartTime, a.BreakTime!.Value);
                        var bucketB = BreakSlotPlanner.GetBreakOffsetBucket(cfgB.StartTime, b.BreakTime!.Value);
                        if (bucketA == bucketB) continue;

                        if (!ShouldSwapForDiversity(
                                a.UserId, bucketA, b.UserId, bucketB, earlyCounts, lateCounts))
                            continue;

                        // Cas particulier : ne jamais recevoir un extrême via swap
                        if (ua.IsSpecialCase
                            && BreakSlotPlanner.IsExtremeCaseBreak(cfgB.StartTime, b.BreakTime!.Value))
                            continue;
                        if (ub.IsSpecialCase
                            && BreakSlotPlanner.IsExtremeCaseBreak(cfgA.StartTime, a.BreakTime!.Value))
                            continue;

                        var aBt = a.BreakTime;
                        var bBt = b.BreakTime;
                        a.BreakTime = bBt;
                        b.BreakTime = aBt;

                        var (_, breaksAfter) = BuildDayPresenceSnapshot(dayList, configsById);
                        if (!PlateauBreakPacker.DayRespectsPresence(ranges, breaksAfter, cellMinPresence)
                            && PlateauBreakPacker.DayRespectsPresence(ranges, breaks, cellMinPresence))
                        {
                            a.BreakTime = aBt;
                            b.BreakTime = bBt;
                            continue;
                        }

                        ApplyBucketCountDelta(earlyCounts, lateCounts, a.UserId, bucketA, bucketB);
                        ApplyBucketCountDelta(earlyCounts, lateCounts, b.UserId, bucketB, bucketA);
                        breaks = breaksAfter;
                        improved = true;
                    }
                }
            }

            if (!improved) break;
        }
    }

    /// <summary>
    /// Swap utile si l'un est plus chargé en Early (ou Late) que l'autre et que les buckets diffèrent.
    /// </summary>
    private static bool ShouldSwapForDiversity(
        int userA,
        BreakSlotPlanner.BreakOffsetBucket bucketA,
        int userB,
        BreakSlotPlanner.BreakOffsetBucket bucketB,
        Dictionary<int, int> earlyCounts,
        Dictionary<int, int> lateCounts)
    {
        var earlyA = earlyCounts.GetValueOrDefault(userA);
        var earlyB = earlyCounts.GetValueOrDefault(userB);
        var lateA = lateCounts.GetValueOrDefault(userA);
        var lateB = lateCounts.GetValueOrDefault(userB);

        // +3h répété vs +4h/+5h d'un pair moins chargé en Early
        if (bucketA == BreakSlotPlanner.BreakOffsetBucket.Early
            && bucketB != BreakSlotPlanner.BreakOffsetBucket.Early
            && earlyA > earlyB)
            return true;
        if (bucketB == BreakSlotPlanner.BreakOffsetBucket.Early
            && bucketA != BreakSlotPlanner.BreakOffsetBucket.Early
            && earlyB > earlyA)
            return true;

        // +5h répété vs +4h/+3h d'un pair moins chargé en Late
        if (bucketA == BreakSlotPlanner.BreakOffsetBucket.Late
            && bucketB != BreakSlotPlanner.BreakOffsetBucket.Late
            && lateA > lateB)
            return true;
        if (bucketB == BreakSlotPlanner.BreakOffsetBucket.Late
            && bucketA != BreakSlotPlanner.BreakOffsetBucket.Late
            && lateB > lateA)
            return true;

        return false;
    }

    private static void ApplyBucketCountDelta(
        Dictionary<int, int> earlyCounts,
        Dictionary<int, int> lateCounts,
        int userId,
        BreakSlotPlanner.BreakOffsetBucket from,
        BreakSlotPlanner.BreakOffsetBucket to)
    {
        if (from == BreakSlotPlanner.BreakOffsetBucket.Early)
            earlyCounts[userId] = Math.Max(0, earlyCounts.GetValueOrDefault(userId) - 1);
        else if (from == BreakSlotPlanner.BreakOffsetBucket.Late)
            lateCounts[userId] = Math.Max(0, lateCounts.GetValueOrDefault(userId) - 1);

        if (to == BreakSlotPlanner.BreakOffsetBucket.Early)
            earlyCounts[userId] = earlyCounts.GetValueOrDefault(userId) + 1;
        else if (to == BreakSlotPlanner.BreakOffsetBucket.Late)
            lateCounts[userId] = lateCounts.GetValueOrDefault(userId) + 1;
    }

    private static Dictionary<int, int> CountOffsetBucket(
        List<ShiftAssignment> assignments,
        IReadOnlyDictionary<int, SubServiceShiftConfig> configsById,
        BreakSlotPlanner.BreakOffsetBucket target)
    {
        var counts = new Dictionary<int, int>();
        foreach (var a in assignments)
        {
            if (!a.BreakTime.HasValue || a.SubServiceShiftConfigId == null) continue;
            if (a.IsOnLeave || a.IsHoliday) continue;
            if (!configsById.TryGetValue(a.SubServiceShiftConfigId.Value, out var cfg)) continue;
            if (BreakSlotPlanner.GetBreakOffsetBucket(cfg.StartTime, a.BreakTime.Value) != target)
                continue;
            counts[a.UserId] = counts.GetValueOrDefault(a.UserId) + 1;
        }

        return counts;
    }

    private static (
        List<PlateauBreakPacker.ShiftRange> Ranges,
        List<PlateauBreakPacker.BreakPlacement> Breaks)
        BuildDayPresenceSnapshot(
            List<ShiftAssignment> dayList,
            IReadOnlyDictionary<int, SubServiceShiftConfig> configsById)
    {
        var ranges = new List<PlateauBreakPacker.ShiftRange>();
        var breaks = new List<PlateauBreakPacker.BreakPlacement>();
        foreach (var a in dayList)
        {
            if (a.SubServiceShiftConfigId == null
                || !configsById.TryGetValue(a.SubServiceShiftConfigId.Value, out var cfg))
                continue;
            ranges.Add(new PlateauBreakPacker.ShiftRange(cfg.StartTime, cfg.EndTime));
            if (!a.BreakTime.HasValue) continue;
            var dur = cfg.BreakDurationMinutes > 0
                ? cfg.BreakDurationMinutes
                : BreakSlotPlanner.BreakDurationMinutes;
            breaks.Add(new PlateauBreakPacker.BreakPlacement(a.BreakTime.Value, dur));
        }
        return (ranges, breaks);
    }

    private static Dictionary<int, int> CountExtremeBreaks(
        List<ShiftAssignment> assignments,
        IReadOnlyDictionary<int, SubServiceShiftConfig> configsById)
    {
        var counts = new Dictionary<int, int>();
        foreach (var a in assignments)
        {
            if (!a.BreakTime.HasValue || a.SubServiceShiftConfigId == null) continue;
            if (a.IsOnLeave || a.IsHoliday) continue;
            if (!configsById.TryGetValue(a.SubServiceShiftConfigId.Value, out var cfg)) continue;
            if (!BreakSlotPlanner.IsExtremeCaseBreak(cfg.StartTime, a.BreakTime.Value)) continue;
            counts[a.UserId] = counts.GetValueOrDefault(a.UserId) + 1;
        }

        return counts;
    }

    // FindBestOpenSlot / PickLoadBalancedBreak retirés : packing via PlateauBreakPacker (métrique P).

    private static CoverageReportDto BuildCoverageReport(
        WeeklyPlanning planning,
        List<SubServiceShiftConfig> shiftConfigs,
        IReadOnlyDictionary<int, User>? usersById = null,
        IReadOnlyList<ShiftModeProfile>? modeProfiles = null)
    {
        var report = new CoverageReportDto();
        if (shiftConfigs.Count == 0)
            return report;

        usersById ??= planning.ShiftAssignments
            .Where(a => a.User != null)
            .GroupBy(a => a.UserId)
            .ToDictionary(g => g.Key, g => g.First().User);

        var modeTitleById = (modeProfiles ?? Array.Empty<ShiftModeProfile>())
            .GroupBy(p => p.Id)
            .ToDictionary(g => g.Key, g => g.First());
        foreach (var a in planning.ShiftAssignments)
        {
            if (a.ShiftModeProfile != null && !modeTitleById.ContainsKey(a.ShiftModeProfile.Id))
                modeTitleById[a.ShiftModeProfile.Id] = a.ShiftModeProfile;
        }

        var levelAnomalies = LevelBalanceEvaluator.Evaluate(
            planning.ShiftAssignments, shiftConfigs, usersById, usersById?.Values.ToList());
        report.LevelBalanceAnomalies = levelAnomalies;
        report.HasLevelBalanceAnomaly = levelAnomalies.Count > 0;
        foreach (var a in levelAnomalies)
            report.Warnings.Add(a.Message);

        if (usersById != null)
        {
            var configsById = shiftConfigs.ToDictionary(c => c.Id);
            foreach (var w in ShiftDispersionSelector.BuildDispersionWarnings(
                         planning.ShiftAssignments.ToList(), usersById, configsById))
                report.Warnings.Add(w);
        }

        var anomalyKeys = levelAnomalies
            .Select(a => (a.Date, a.ShiftConfigId))
            .ToHashSet();
        var anomalyDates = levelAnomalies.Select(a => a.Date).ToHashSet();

        var cellMinPresence = shiftConfigs[0].MinPresencePercent <= 0
            ? 0
            : Math.Clamp(shiftConfigs[0].MinPresencePercent, 50, 100);

        var dayNames = new[]
        {
            "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"
        };

        report.PlateauAvailabilityTargetPercent = cellMinPresence;

        var breakDurationMinutes = Math.Clamp(
            shiftConfigs.Select(c => c.BreakDurationMinutes <= 0 ? 60 : c.BreakDurationMinutes).DefaultIfEmpty(60).Max(),
            30,
            120);

        // Historique shifts Lun–Sam travaillés (rotation / dispersion)
        var weekByUser = planning.ShiftAssignments
            .Where(a =>
                !a.IsOnLeave
                && !a.IsHoliday
                && a.SubServiceShiftConfigId != null)
            .GroupBy(a => a.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(a => a.AssignedDate).ToList());

        var weekRotationViolators = new HashSet<int>();
        foreach (var (userId, ordered) in weekByUser)
        {
            // KPI rotation semaine : uniquement pas 2 jours travaillés d'affilée au même shift
            // (le max 2× / semaine n'entre PAS dans le KPI).
            for (var k = 1; k < ordered.Count; k++)
            {
                if (ordered[k].SubServiceShiftConfigId == ordered[k - 1].SubServiceShiftConfigId)
                {
                    weekRotationViolators.Add(userId);
                    break;
                }
            }
        }

        var weekLevelOk = 0;
        var weekLevelEval = 0;
        var dailyPlateauValues = new List<decimal>();

        for (var i = 0; i < 6; i++)
        {
            var date = planning.WeekStartDate.AddDays(i);
            var dayName = dayNames[i];
            var daySynth = new DaySynthesisDto { Date = date, Day = dayName };

            daySynth.LeaveCount = planning.ShiftAssignments.Count(a =>
                a.AssignedDate == date && a.IsOnLeave);
            daySynth.HolidayCount = planning.ShiftAssignments.Count(a =>
                a.AssignedDate == date && a.IsHoliday);

            var dayPresent = planning.ShiftAssignments
                .Where(a =>
                    a.AssignedDate == date
                    && a.SubServiceShiftConfigId != null
                    && !a.IsOnLeave
                    && !a.IsHoliday)
                .ToList();
            daySynth.PresentCount = dayPresent.Count;

            // Timeline disponibilité + min = KPI plateau du jour
            var dayShiftConfigs = i == 5
                ? ShiftConfigScope.Saturday(shiftConfigs)
                : ShiftConfigScope.Weekday(shiftConfigs);
            if (dayShiftConfigs.Count == 0)
                dayShiftConfigs = shiftConfigs;

            daySynth.AvailabilityTimeline = BuildDayAvailabilityTimeline(
                dayPresent, dayShiftConfigs, breakDurationMinutes);
            daySynth.PlateauAvailabilityPercent = daySynth.AvailabilityTimeline.Count == 0
                ? 100m
                : daySynth.AvailabilityTimeline.Min(p => p.AvailabilityPercent);
            dailyPlateauValues.Add(daySynth.PlateauAvailabilityPercent);

            if (dayPresent.Any(a => a.ShiftModeProfileId.HasValue))
            {
                foreach (var modeId in dayPresent
                             .Where(a => a.ShiftModeProfileId.HasValue)
                             .Select(a => a.ShiftModeProfileId!.Value)
                             .Distinct()
                             .OrderBy(id => id))
                {
                    var modePresent = dayPresent
                        .Where(a => a.ShiftModeProfileId == modeId)
                        .ToList();
                    var modeConfigs = i == 5
                        ? ShiftConfigScope.Saturday(
                            shiftConfigs.Where(c => c.ShiftModeProfileId == modeId))
                        : ShiftConfigScope.Weekday(
                            shiftConfigs.Where(c => c.ShiftModeProfileId == modeId));
                    if (modeConfigs.Count == 0)
                        modeConfigs = dayShiftConfigs;

                    var modeTimeline = BuildDayAvailabilityTimeline(
                        modePresent, modeConfigs, breakDurationMinutes);
                    modeTitleById.TryGetValue(modeId, out var profile);
                    var target = profile?.MinPresencePercent ?? cellMinPresence;
                    daySynth.AvailabilityByMode.Add(new DayModeAvailabilityDto
                    {
                        ShiftModeProfileId = modeId,
                        ShiftModeTitle = profile?.Title ?? string.Empty,
                        TargetPercent = target <= 0
                            ? 0
                            : Math.Clamp(target, 50, 100),
                        PlateauAvailabilityPercent = modeTimeline.Count == 0
                            ? 100m
                            : modeTimeline.Min(p => p.AvailabilityPercent),
                        AvailabilityTimeline = modeTimeline
                    });
                }
            }

            // Présence min cellule : pic de pauses qui se chevauchent (durée 1h)
            var cellPresenceIssue = false;
            if (cellMinPresence > 0 && dayPresent.Count > 1)
            {
                var maxBreakAllowed = (int)Math.Floor(dayPresent.Count * (100 - cellMinPresence) / 100.0);
                if (maxBreakAllowed == 0)
                    maxBreakAllowed = 1;

                var breakStarts = dayPresent
                    .Where(a => a.BreakTime.HasValue)
                    .Select(a => a.BreakTime!.Value)
                    .ToList();
                var breakDur = breakDurationMinutes;
                var peak = 0;
                foreach (var start in breakStarts.Distinct())
                {
                    for (var t = start; t < start.AddMinutes(breakDur); t = t.AddMinutes(5))
                    {
                        var onBreak = breakStarts.Count(b =>
                            b <= t && b.AddMinutes(breakDur) > t);
                        if (onBreak > peak) peak = onBreak;
                    }
                }

                cellPresenceIssue = peak > maxBreakAllowed;

                if (cellPresenceIssue)
                {
                    report.HasUnderstaffing = true;
                    report.Warnings.Add(
                        $"{dayName} {date:dd/MM} — trop de pauses simultanées (présence min cellule {cellMinPresence} %, pic {peak}/{dayPresent.Count})");
                    var configsById = shiftConfigs.ToDictionary(c => c.Id);
                    foreach (var w in PlateauBreakPacker.BuildRestrictedBandWarnings(
                                 dayPresent, configsById, cellMinPresence, dayName, date))
                        report.Warnings.Add(w);
                }
            }

            if (i == 5)
            {
                daySynth.SaturdayBeginners = dayPresent.Count(a =>
                    usersById != null
                    && usersById.TryGetValue(a.UserId, out var u)
                    && u.Level == 1);
                daySynth.SaturdaySeniors = dayPresent.Count(a =>
                    usersById != null
                    && usersById.TryGetValue(a.UserId, out var u)
                    && u.Level >= 2);
            }

            var dayLevelOk = 0;
            var dayLevelEval = 0;

            foreach (var cfg in dayShiftConfigs.OrderBy(c => c.DisplayOrder))
            {
                var dayAssignments = planning.ShiftAssignments
                    .Where(a =>
                        a.AssignedDate == date
                        && a.SubServiceShiftConfigId == cfg.Id
                        && !a.IsOnLeave
                        && !a.IsHoliday)
                    .ToList();

                var assigned = dayAssignments.Count;
                var beginnerCount = dayAssignments.Count(a =>
                    usersById != null
                    && usersById.TryGetValue(a.UserId, out var u)
                    && u.Level == 1);
                var seniorCount = dayAssignments.Count(a =>
                    usersById != null
                    && usersById.TryGetValue(a.UserId, out var u)
                    && u.Level >= 2);

                var dayRequired = i == 5
                    ? ShiftDispersionSelector.EffectiveSaturdayQuota(cfg)
                    : cfg.RequiredCount;

                var staffingPct = dayRequired > 0
                    ? Math.Round((decimal)assigned / dayRequired * 100, 1)
                    : 100m;

                var understaffed = dayRequired > 0 && assigned < dayRequired;

                var hasLevel = anomalyKeys.Contains((date, cfg.Id))
                               || (i == 5 && anomalyDates.Contains(date));
                var isUnder = understaffed || cellPresenceIssue;

                // Équilibre niveau : créneaux Lun–Ven évalués (+ samedi si anomalie/présents)
                if (i < 5 || (i == 5 && (assigned > 0 || anomalyDates.Contains(date))))
                {
                    dayLevelEval++;
                    if (!hasLevel)
                        dayLevelOk++;
                }

                report.Items.Add(new CoverageDayShiftDto
                {
                    Date = date,
                    Day = dayName,
                    ShiftConfigId = cfg.Id,
                    ShiftLabel = cfg.Label,
                    ShiftKind = cfg.ShiftKind.ToString(),
                    RequiredCount = dayRequired,
                    AssignedCount = assigned,
                    MinPresencePercent = cellMinPresence,
                    PresencePercent = staffingPct,
                    IsUnderstaffed = isUnder,
                    HasLevelBalanceAnomaly = hasLevel,
                });

                daySynth.Shifts.Add(new DaySynthesisShiftDto
                {
                    ShiftConfigId = cfg.Id,
                    ShiftLabel = cfg.Label,
                    ShiftKind = cfg.ShiftKind.ToString(),
                    ShiftModeProfileId = cfg.ShiftModeProfileId,
                    ShiftModeTitle = cfg.ShiftModeProfileId is int mid
                        && modeTitleById.TryGetValue(mid, out var modeProf)
                        ? modeProf.Title
                        : null,
                    AssignedCount = assigned,
                    RequiredCount = dayRequired,
                    Delta = assigned - dayRequired,
                    BeginnerCount = beginnerCount,
                    SeniorCount = seniorCount,
                    IsUnderstaffed = isUnder,
                    HasLevelBalanceAnomaly = hasLevel
                });

                if (isUnder || hasLevel)
                    daySynth.HasAnyAnomaly = true;

                if (understaffed)
                {
                    report.HasUnderstaffing = true;
                    report.Warnings.Add(
                        $"{dayName} {date:dd/MM} — {cfg.Label}: {assigned}/{dayRequired} affectés (quota{(i == 5 ? " samedi 50%" : "")})");
                }
            }

            if (i < 5)
            {
                weekLevelOk += dayLevelOk;
                weekLevelEval += dayLevelEval;
                daySynth.LevelBalancePercent = dayLevelEval == 0
                    ? 100m
                    : Math.Round((decimal)dayLevelOk / dayLevelEval * 100, 1);
            }
            else
            {
                // Samedi : règle jour (débutant seul sans senior) + contribution semaine
                daySynth.LevelBalancePercent = anomalyDates.Contains(date) ? 0m : 100m;
                weekLevelEval++;
                if (!anomalyDates.Contains(date))
                    weekLevelOk++;
            }

            // Rotation locale du jour (Lun–Sam : ≠ veille + max2 cumul)
            if (dayPresent.Count > 0)
            {
                var okCount = 0;
                foreach (var a in dayPresent)
                {
                    if (!HasLocalRotationViolation(a, weekByUser, date))
                        okCount++;
                }

                daySynth.RotationCompliancePercent =
                    Math.Round((decimal)okCount / dayPresent.Count * 100, 1);
            }
            else
            {
                daySynth.RotationCompliancePercent = 100m;
            }

            // Cas extrêmes : pauses +3h / +5h (relatif au start, plafond métier)
            var configsByIdForBreaks = shiftConfigs.ToDictionary(c => c.Id);
            var extreme = 0;
            var extremeTier = 0;
            foreach (var a in dayPresent.Where(x => x.BreakTime.HasValue && x.SubServiceShiftConfigId != null))
            {
                if (!configsByIdForBreaks.TryGetValue(a.SubServiceShiftConfigId!.Value, out var cfg))
                    continue;
                var bt = a.BreakTime!.Value;
                if (BreakSlotPlanner.IsExtremeCaseBreak(cfg.StartTime, bt))
                    extreme++;
                if (BreakSlotPlanner.IsExtremeBreak(cfg.StartTime, bt))
                    extremeTier++;
            }

            daySynth.ExtremeBreakCount = extreme;
            daySynth.ExtremeTierBreakCount = extremeTier;

            report.DaySynthesis.Add(daySynth);
        }

        report.PlateauAvailabilityPercent = dailyPlateauValues.Count == 0
            ? 100m
            : dailyPlateauValues.Min();
        report.LevelBalancePercent = weekLevelEval == 0
            ? 100m
            : Math.Round((decimal)weekLevelOk / weekLevelEval * 100, 1);

        report.RotationEmployeesCount = weekByUser.Count;
        report.RotationViolatorsCount = weekRotationViolators.Count;
        report.RotationCompliancePercent = weekByUser.Count == 0
            ? 100m
            : Math.Round(
                (decimal)(weekByUser.Count - weekRotationViolators.Count) / weekByUser.Count * 100,
                1);

        report.ExtremeBreakCount = report.DaySynthesis.Sum(d => d.ExtremeBreakCount);
        report.ExtremeTierBreakCount = report.DaySynthesis.Sum(d => d.ExtremeTierBreakCount);

        // Rotation cas extrêmes : équité par niveau (écart max−min ≤ 1)
        ComputeExtremeRotationKpis(report, weekByUser, shiftConfigs, usersById);

        return report;
    }

    /// <summary>
    /// KPI fairness cas extrêmes : dans chaque niveau, si max−min des comptes +3h/+5h &gt; 1,
    /// les employés au-dessus du min du groupe sont des violators.
    /// </summary>
    private static void ComputeExtremeRotationKpis(
        CoverageReportDto report,
        Dictionary<int, List<ShiftAssignment>> weekByUser,
        List<SubServiceShiftConfig> shiftConfigs,
        IReadOnlyDictionary<int, User>? usersById)
    {
        if (usersById == null || usersById.Count == 0 || weekByUser.Count == 0)
        {
            report.ExtremeRotationCompliancePercent = 100m;
            report.ExtremeRotationEmployeesCount = weekByUser.Count;
            report.ExtremeRotationViolatorsCount = 0;
            return;
        }

        var configsById = shiftConfigs.ToDictionary(c => c.Id);
        var extremeCounts = new Dictionary<int, int>();
        foreach (var (userId, days) in weekByUser)
        {
            var n = 0;
            foreach (var a in days)
            {
                if (!a.BreakTime.HasValue || a.SubServiceShiftConfigId == null) continue;
                if (!configsById.TryGetValue(a.SubServiceShiftConfigId.Value, out var cfg)) continue;
                if (BreakSlotPlanner.IsExtremeCaseBreak(cfg.StartTime, a.BreakTime.Value))
                    n++;
            }

            extremeCounts[userId] = n;
        }

        var violators = new HashSet<int>();
        var evaluated = new HashSet<int>();

        foreach (var levelGroup in weekByUser.Keys
                     .Where(id => usersById.ContainsKey(id))
                     .GroupBy(id => usersById[id].Level))
        {
            var members = levelGroup.ToList();
            if (members.Count == 0) continue;

            foreach (var id in members)
                evaluated.Add(id);

            if (members.Count == 1)
                continue;

            var minC = members.Min(id => extremeCounts.GetValueOrDefault(id));
            var maxC = members.Max(id => extremeCounts.GetValueOrDefault(id));
            if (maxC - minC <= 1)
                continue;

            foreach (var id in members)
            {
                if (extremeCounts.GetValueOrDefault(id) > minC)
                    violators.Add(id);
            }
        }

        report.ExtremeRotationEmployeesCount = evaluated.Count;
        report.ExtremeRotationViolatorsCount = violators.Count;
        report.ExtremeRotationCompliancePercent = evaluated.Count == 0
            ? 100m
            : Math.Round(
                (decimal)(evaluated.Count - violators.Count) / evaluated.Count * 100,
                1);
    }

    private static List<DayAvailabilityPointDto> BuildDayAvailabilityTimeline(
        List<ShiftAssignment> dayPresent,
        List<SubServiceShiftConfig> shiftConfigs,
        int breakDurationMinutes)
    {
        var result = new List<DayAvailabilityPointDto>();
        if (dayPresent.Count == 0)
            return result;

        var configsById = shiftConfigs.ToDictionary(c => c.Id);
        var ranges = new List<(ShiftAssignment Assignment, TimeOnly Start, TimeOnly End)>();
        TimeOnly? windowStart = null;
        TimeOnly? windowEnd = null;

        foreach (var a in dayPresent)
        {
            if (a.SubServiceShiftConfigId is not int cfgId)
                continue;

            SubServiceShiftConfig? cfg = a.SubServiceShiftConfig;
            if (cfg == null && !configsById.TryGetValue(cfgId, out cfg))
                continue;

            ranges.Add((a, cfg.StartTime, cfg.EndTime));
            if (windowStart == null || cfg.StartTime < windowStart)
                windowStart = cfg.StartTime;
            if (windowEnd == null || cfg.EndTime > windowEnd)
                windowEnd = cfg.EndTime;
        }

        if (windowStart == null || windowEnd == null || windowStart >= windowEnd)
            return result;

        const int stepMinutes = 5;
        var t = new TimeOnly(windowStart.Value.Hour, (windowStart.Value.Minute / stepMinutes) * stepMinutes);
        while (t < windowEnd.Value)
        {
            var slotEnd = t.AddMinutes(stepMinutes);
            var presentAt = ranges.Count(r => r.Start <= t && r.End > t);
            if (presentAt > 0)
            {
                var onBreak = ranges.Count(r =>
                    r.Start <= t
                    && r.End > t
                    && r.Assignment.BreakTime.HasValue
                    && r.Assignment.BreakTime.Value < slotEnd
                    && r.Assignment.BreakTime.Value.AddMinutes(breakDurationMinutes) > t);
                var available = presentAt - onBreak;
                result.Add(new DayAvailabilityPointDto
                {
                    Time = t.ToString("HH:mm"),
                    PresentCount = presentAt,
                    OnBreakCount = onBreak,
                    AvailableCount = available,
                    AvailabilityPercent = Math.Round((decimal)available / presentAt * 100, 1)
                });
            }

            t = slotEnd;
        }

        return result;
    }

    /// <summary>
    /// Violation locale KPI : même shift que le jour travaillé précédent uniquement
    /// (le max 2× / semaine n'entre PAS dans le KPI).
    /// </summary>
    private static bool HasLocalRotationViolation(
        ShiftAssignment assignment,
        Dictionary<int, List<ShiftAssignment>> weekByUser,
        DateOnly date)
    {
        if (!weekByUser.TryGetValue(assignment.UserId, out var ordered))
            return false;

        var shiftId = assignment.SubServiceShiftConfigId;
        if (shiftId == null)
            return false;

        var prev = ordered.LastOrDefault(a => a.AssignedDate < date);
        return prev?.SubServiceShiftConfigId == shiftId;
    }

    private static List<TimeOnly> GenerateBreakSlots(TimeOnly rangeStart, TimeOnly rangeEnd)
    {
        var slots = new List<TimeOnly>();
        var current = rangeStart;

        while (current < rangeEnd)
        {
            slots.Add(current);
            current = current.AddMinutes(30);
        }

        if (!slots.Any()) slots.Add(rangeStart);
        return slots;
    }

    private static DayAssignmentDto MapToDayDtoNew(
     ShiftAssignment a,
     List<Conge>? conges = null,
     HashSet<(int UserId, DateOnly Date)>? exceptionalApplied = null)
    {
        var label = a.IsHoliday ? "F�RI�"
                  : a.IsOnLeave ? "CONG�"
                  : a.SubServiceShiftConfig?.Label
                    ?? a.Shift?.Label
                    ?? "�";

        var startTime = a.SubServiceShiftConfig?.StartTime.ToString("HH:mm")
                        ?? a.Shift?.StartTime.ToString("HH:mm")
                        ?? "";

        var endTime = a.SubServiceShiftConfig?.EndTime.ToString("HH:mm") ?? "";

        // ? Trouver le type d'absence
        string? absenceType = null;
        if (a.IsOnLeave && conges != null)
        {
            var conge = conges.FirstOrDefault(c =>
                c.UserId == a.UserId &&
                c.StartDate <= a.AssignedDate &&
                c.EndDate >= a.AssignedDate);
            absenceType = conge?.AbsenceType.ToString();
        }

        var isExceptional = a.IsExceptionalRequest
            || (exceptionalApplied != null
                && !a.IsOnLeave
                && !a.IsHoliday
                && a.SubServiceShiftConfigId != null
                && exceptionalApplied.Contains((a.UserId, a.AssignedDate)));

        return new DayAssignmentDto
        {
            AssignmentId = a.Id,
            Day = a.DayOfWeek.ToString(),
            AssignedDate = a.AssignedDate,
            ShiftLabel = label,
            StartTime = startTime,
            EndTime = endTime,
            IsSaturday = a.IsSaturday,
            IsManagerOverride = a.IsManagerOverride,
            IsExceptionalRequest = isExceptional,
            IsReinforcement = a.IsReinforcement,
            BreakTime = a.BreakTime?.ToString("HH:mm"),
            IsOnLeave = a.IsOnLeave,
            AbsenceType = absenceType, // ? NOUVEAU
            IsHalfDaySaturday = a.IsHalfDaySaturday,
            SaturdaySlot = a.SaturdaySlot,
            SlotLabel = a.SaturdaySlot == 1 ? "8h00-12h00"
                              : a.SaturdaySlot == 2 ? "12h00-16h00"
                              : string.Empty,
            IsHoliday = a.IsHoliday,
            HolidayName = a.IsHoliday
                ? FrenchHolidayHelper.GetHolidayName(a.AssignedDate)
                : string.Empty,
            ShiftModeProfileId = a.ShiftModeProfileId,
            ShiftModeTitle = a.ShiftModeProfile?.Title,
            IsModeOverride = a.IsModeOverride
        };
    }

    // ----------------------------------------------------
    // HELPERS � ANCIEN SYST�ME
    // ----------------------------------------------------
    private static DayAssignmentDto MapToDayDto(ShiftAssignment a, Shift? shift) => new()
    {
        AssignmentId = a.Id,
        Day = a.DayOfWeek.ToString(),
        AssignedDate = a.AssignedDate,
        ShiftLabel = shift?.Label ?? "CONG�",
        StartTime = shift?.StartTime.ToString("HH:mm") ?? "",
        IsSaturday = a.IsSaturday,
        IsManagerOverride = a.IsManagerOverride,
        BreakTime = a.BreakTime?.ToString("HH:mm"),
        IsOnLeave = a.IsOnLeave,
        IsHalfDaySaturday = a.IsHalfDaySaturday,
        SaturdaySlot = a.SaturdaySlot,
        SlotLabel = a.SaturdaySlot == 1 ? "8h00-12h00"
                          : a.SaturdaySlot == 2 ? "12h00-16h00"
                          : string.Empty
    };

    private static ShiftConfigResponseNewDto MapToShiftConfigResponseDto(
        SubServiceShiftConfig c)
    {
        var slots = BreakSlotPlanner.ResolveBreakSlots(c);
        return new()
        {
            Id = c.Id,
            Label = c.Label,
            StartTime = c.StartTime.ToString("HH:mm"),
            EndTime = c.EndTime.ToString("HH:mm"),
            WorkHours = c.WorkHours,
            BreakRangeStart = c.BreakRangeStart.ToString("HH:mm"),
            BreakRangeEnd = c.BreakRangeEnd.ToString("HH:mm"),
            BreakDurationMinutes = c.BreakDurationMinutes,
            BreakSlots = slots.Select(s => s.ToString("HH:mm")).ToList(),
            IsCriticalCell = c.IsCriticalCell,
            RequiredCount = c.RequiredCount,
            Percentage = c.Percentage,
            MinPresencePercent = c.MinPresencePercent,
            DisplayOrder = c.DisplayOrder,
            ShiftKind = c.ShiftKind.ToString(),
            ShiftModeProfileId = c.ShiftModeProfileId,
            AppliesToSaturday = c.AppliesToSaturday
        };
    }

    private List<int> GetEmployeeWeekRotation(
        List<Shift> shifts, int employeeIndex, List<int> recentShiftIds)
    {
        var shiftUsageCount = shifts.ToDictionary(
            s => s.Id,
            s => recentShiftIds.Count(r => r == s.Id));

        var sortedShifts = shifts
            .OrderBy(s => shiftUsageCount[s.Id])
            .ThenBy(s => s.StartTime)
            .ToList();

        var rotation = new List<int>();
        var shiftCount = sortedShifts.Count;
        var offset = employeeIndex % shiftCount;

        for (int day = 0; day < 5; day++)
            rotation.Add(sortedShifts[(day + offset) % shiftCount].Id);

        return rotation;
    }

    private List<WeeklyShiftConfig> CalculateShiftQuotas(
        List<Shift> shifts, int totalEffectif, int planningId)
    {
        var configs = new List<WeeklyShiftConfig>();
        var baseCount = totalEffectif / shifts.Count;
        var remainder = totalEffectif % shifts.Count;

        for (int i = 0; i < shifts.Count; i++)
        {
            var count = baseCount + (i < remainder ? 1 : 0);
            configs.Add(new WeeklyShiftConfig
            {
                WeeklyPlanningId = planningId,
                ShiftId = shifts[i].Id,
                RequiredCount = count,
                Percentage = totalEffectif > 0
                    ? Math.Round((decimal)count / totalEffectif * 100, 1) : 0
            });
        }
        return configs;
    }

    private ShiftAssignment? GenerateSaturdayAssignment(
        User employee, WeeklyPlanning planning,
        List<Shift> shifts, List<SaturdayGroup> saturdayGroups, int employeeIndex,
        Dictionary<int, int> beginnerHalfDaySlotCounts)
    {
        var satGroup = saturdayGroups.FirstOrDefault(sg => sg.UserId == employee.Id);
        var orderedShifts = shifts.OrderBy(s => s.StartTime).ToList();

        if (IsEveryHalfDaySaturday(employee))
        {
            if (orderedShifts.Count == 0) return null;

            var slot = PickBalancedHalfDaySlot(beginnerHalfDaySlotCounts);
            var shiftId = slot == 1 || orderedShifts.Count == 1
                ? orderedShifts[0].Id
                : orderedShifts[Math.Min(1, orderedShifts.Count - 1)].Id;

            beginnerHalfDaySlotCounts[slot] = beginnerHalfDaySlotCounts.GetValueOrDefault(slot, 0) + 1;

            return new ShiftAssignment
            {
                WeeklyPlanningId = planning.Id,
                UserId = employee.Id,
                ShiftId = shiftId,
                AssignedDate = planning.WeekStartDate.AddDays(5),
                DayOfWeek = DayOfWeekEnum.Saturday,
                IsSaturday = true,
                IsNewEmployee = true,
                IsHalfDaySaturday = true,
                SaturdaySlot = slot
            };
        }

        if (satGroup == null) return null;
        if (satGroup.GroupNumber != planning.SaturdayGroupId) return null;

        var weekNumber = System.Globalization.ISOWeek.GetWeekOfYear(
            planning.WeekStartDate.ToDateTime(TimeOnly.MinValue));
        var shiftIndex = (employeeIndex + weekNumber) % orderedShifts.Count;

        return new ShiftAssignment
        {
            WeeklyPlanningId = planning.Id,
            UserId = employee.Id,
            ShiftId = orderedShifts[shiftIndex].Id,
            AssignedDate = planning.WeekStartDate.AddDays(5),
            DayOfWeek = DayOfWeekEnum.Saturday,
            IsSaturday = true,
            IsNewEmployee = false,
            IsHalfDaySaturday = false,
            SaturdaySlot = 0
        };
    }

    private void AssignBreakTimes(
        List<ShiftAssignment> dayAssignments, List<Shift> shifts, int totalEmployees)
    {
        if (!dayAssignments.Any()) return;
        int maxSimultaneous = Math.Max(1, (int)Math.Floor(totalEmployees * 0.30));
        var breakSlotUsage = new Dictionary<TimeOnly, int>();

        foreach (var group in dayAssignments.GroupBy(a => a.ShiftId))
        {
            var shift = shifts.First(s => s.Id == group.Key);
            var slots = GetBreakSlots(shift.StartTime);

            foreach (var assignment in group)
            {
                var bestSlot = slots
                    .OrderBy(slot => breakSlotUsage.GetValueOrDefault(slot, 0))
                    .First(slot => breakSlotUsage.GetValueOrDefault(slot, 0) < maxSimultaneous);

                assignment.BreakTime = bestSlot;
                breakSlotUsage[bestSlot] = breakSlotUsage.GetValueOrDefault(bestSlot, 0) + 1;
            }
        }
    }

    private static List<TimeOnly> GetBreakSlots(TimeOnly shiftStart)
    {
        var slots = new List<TimeOnly>();
        var earliest = shiftStart.AddHours(4);
        for (int h = 0; h < 4; h++)
            slots.Add(earliest.AddHours(h));
        return slots;
    }

    private async Task ValidatePlanningInputsAsync(int subServiceId, DateOnly weekStartDate, int totalEffectif)
    {
        if (weekStartDate.DayOfWeek != DayOfWeek.Monday)
            throw new InvalidOperationException("La semaine doit commencer un lundi.");

        if (totalEffectif <= 0)
            throw new InvalidOperationException("L'effectif total doit �tre sup�rieur � 0.");

        var subServiceExists = await _context.SubServices.AnyAsync(s => s.Id == subServiceId);
        if (!subServiceExists)
            throw new InvalidOperationException("Service introuvable.");

        var employeeCount = await _context.Users
            .CountAsync(u => u.SubServiceId == subServiceId && u.IsActive);

        if (employeeCount == 0)
            throw new InvalidOperationException("Ce service n'a aucun employ� actif.");

        if (totalEffectif > employeeCount)
            throw new InvalidOperationException(
                $"L'effectif ne peut pas d�passer {employeeCount} (employ�s actifs du service).");
    }

    private static int GetSaturdayGroupForWeek(DateOnly weekStart)
    {
        var weekNumber = System.Globalization.ISOWeek.GetWeekOfYear(
            weekStart.ToDateTime(TimeOnly.MinValue));
        return weekNumber % 2 == 0 ? 1 : 2;
    }

    /// <summary>
    /// G1 = samedi de la semaine ISO dont la parité correspond au groupe 1 ;
    /// G2 = l’autre samedi (courant ou suivant).
    /// </summary>
    public static (DateOnly Group1Saturday, DateOnly Group2Saturday) ResolveSaturdayGroupDates(DateOnly referenceDay)
    {
        var monday = MondayOfContainingWeek(referenceDay);
        var thisSaturday = monday.AddDays(5);
        var nextSaturday = monday.AddDays(12);
        return GetSaturdayGroupForWeek(monday) == 1
            ? (thisSaturday, nextSaturday)
            : (nextSaturday, thisSaturday);
    }

    private static DateOnly MondayOfContainingWeek(DateOnly day)
    {
        var delta = ((int)day.DayOfWeek + 6) % 7;
        return day.AddDays(-delta);
    }

    private static Dictionary<int, int> BuildModePatternSeeds(
        IReadOnlyList<User> employees,
        Dictionary<int, int>? employeeModeMap,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> shiftsFor,
        int weekNumber)
    {
        if (employeeModeMap is null || employeeModeMap.Count == 0)
            return WeekShiftPatternAssigner.BuildSeeds(employees, shiftsFor, weekNumber);

        var seeds = new Dictionary<int, int>();
        foreach (var group in employees.GroupBy(e => employeeModeMap.GetValueOrDefault(e.Id)))
        {
            var roster = group.OrderBy(e => e.Id).ToList();
            foreach (var kv in WeekShiftPatternAssigner.BuildSeeds(roster, shiftsFor, weekNumber))
                seeds[kv.Key] = kv.Value;
        }

        return seeds;
    }

    private static Dictionary<int, SubServiceShiftConfig> AssignDayByMode(
        IReadOnlyList<User> available,
        Dictionary<int, int>? employeeModeMap,
        Func<int, IReadOnlyList<SubServiceShiftConfig>> shiftsFor,
        IReadOnlyDictionary<int, int> seeds,
        IReadOnlyDictionary<int, List<int>> weekHistory,
        IReadOnlyDictionary<int, User> usersById,
        IReadOnlyDictionary<int, SubServiceShiftConfig> pinsByUser,
        IReadOnlyList<SubServiceShiftConfig> orderedShifts)
    {
        if (employeeModeMap is null || employeeModeMap.Count == 0)
        {
            return WeekShiftPatternAssigner.AssignDay(
                available, shiftsFor, seeds, weekHistory, usersById, pinsByUser, orderedShifts);
        }

        var merged = new Dictionary<int, SubServiceShiftConfig>();
        foreach (var group in available.GroupBy(e => employeeModeMap.GetValueOrDefault(e.Id)))
        {
            var roster = group.ToList();
            if (roster.Count == 0)
                continue;

            var modeId = group.Key;
            var modeShifts = modeId == 0
                ? orderedShifts.ToList()
                : orderedShifts.Where(s => s.ShiftModeProfileId == modeId).ToList();
            if (modeShifts.Count == 0)
                modeShifts = orderedShifts.ToList();

            var modePins = new Dictionary<int, SubServiceShiftConfig>();
            foreach (var emp in roster)
            {
                if (pinsByUser.TryGetValue(emp.Id, out var pin) && pin != null)
                    modePins[emp.Id] = pin;
            }

            var part = WeekShiftPatternAssigner.AssignDay(
                roster, shiftsFor, seeds, weekHistory, usersById, modePins, modeShifts);
            foreach (var kv in part)
                merged[kv.Key] = kv.Value;
        }

        return merged;
    }

    private static List<SubServiceShiftConfig> CloneWithRequiredCounts(
        IEnumerable<SubServiceShiftConfig> configs,
        Func<SubServiceShiftConfig, int> requiredOf)
        => configs.Select(c => new SubServiceShiftConfig
        {
            Id = c.Id,
            SubServiceId = c.SubServiceId,
            Label = c.Label,
            StartTime = c.StartTime,
            WorkHours = c.WorkHours,
            RequiredCount = requiredOf(c),
            DisplayOrder = c.DisplayOrder,
            ShiftKind = c.ShiftKind,
            ShiftModeProfileId = c.ShiftModeProfileId,
            IsTemplate = c.IsTemplate,
            AppliesToSaturday = c.AppliesToSaturday
        }).ToList();

    private static void RebalanceSaturdayModeQuotas(
        List<ShiftAssignment> assignments,
        IReadOnlyList<User> employees,
        Dictionary<int, int>? employeeModeMap,
        Func<int, List<SubServiceShiftConfig>> shiftsFor,
        IReadOnlyList<SubServiceShiftConfig> orderedShifts,
        IReadOnlyDictionary<int, User> usersById,
        Dictionary<int, List<int>> weekHistory)
    {
        var satWork = assignments
            .Where(a => a.IsSaturday && !a.IsOnLeave && !a.IsHoliday && a.SubServiceShiftConfigId != null)
            .ToList();
        if (satWork.Count == 0)
            return;

        void RebalanceGroup(
            List<ShiftAssignment> group,
            IReadOnlyList<SubServiceShiftConfig> modeShifts)
        {
            var satShifts = CloneWithRequiredCounts(
                modeShifts, ShiftDispersionSelector.EffectiveSaturdayQuota);
            var satById = satShifts.ToDictionary(s => s.Id);
            var assigned = new Dictionary<int, SubServiceShiftConfig>();
            var pins = new Dictionary<int, SubServiceShiftConfig>();
            foreach (var a in group)
            {
                if (!satById.TryGetValue(a.SubServiceShiftConfigId!.Value, out var cfg))
                    continue;
                assigned[a.UserId] = cfg;
                if (a.IsManagerOverride || a.IsHalfDaySaturday)
                    pins[a.UserId] = cfg;
            }

            if (assigned.Count == 0)
                return;

            var historyFri = new Dictionary<int, List<int>>();
            foreach (var uid in assigned.Keys)
            {
                if (!weekHistory.TryGetValue(uid, out var hist) || hist.Count == 0)
                    continue;
                historyFri[uid] = hist.Take(hist.Count - 1).ToList();
            }

            IReadOnlyList<SubServiceShiftConfig> Allowed(int id) =>
                CloneWithRequiredCounts(shiftsFor(id), ShiftDispersionSelector.EffectiveSaturdayQuota);

            WeekShiftPatternAssigner.RebalanceToQuotas(
                assigned, Allowed, historyFri, usersById, pins, satShifts);

            foreach (var a in group)
            {
                if (pins.ContainsKey(a.UserId))
                    continue;
                if (!assigned.TryGetValue(a.UserId, out var neu))
                    continue;
                a.SubServiceShiftConfigId = neu.Id;
                if (neu.ShiftModeProfileId.HasValue)
                    a.ShiftModeProfileId = neu.ShiftModeProfileId;
                if (weekHistory.TryGetValue(a.UserId, out var hist) && hist.Count > 0)
                    hist[^1] = neu.Id;
            }
        }

        if (employeeModeMap is { Count: > 0 })
        {
            foreach (var group in satWork.GroupBy(a => employeeModeMap.GetValueOrDefault(a.UserId)))
            {
                var modeShifts = group.Key == 0
                    ? orderedShifts.ToList()
                    : orderedShifts.Where(s => s.ShiftModeProfileId == group.Key).ToList();
                if (modeShifts.Count == 0)
                    modeShifts = orderedShifts.ToList();
                RebalanceGroup(group.ToList(), modeShifts);
            }
        }
        else
        {
            RebalanceGroup(satWork, orderedShifts);
        }
    }

    private static List<(DayOfWeekEnum day, DateOnly date)> GetWeekDays(DateOnly weekStart)
        => new()
        {
            (DayOfWeekEnum.Monday,    weekStart),
            (DayOfWeekEnum.Tuesday,   weekStart.AddDays(1)),
            (DayOfWeekEnum.Wednesday, weekStart.AddDays(2)),
            (DayOfWeekEnum.Thursday,  weekStart.AddDays(3)),
            (DayOfWeekEnum.Friday,    weekStart.AddDays(4)),
        };

    public async Task<IReadOnlyList<EquipePlanningSummaryDto>> GetEquipePlanningsByAuthUserIdAsync(int authUserId)
    {
        // Prefer AuthUserId; fall back to planning User.Id (legacy session id).
        var manager = await _context.Users
            .AsNoTracking()
            .Include(u => u.ManagedSubServices)
            .Include(u => u.ManagedServices)
            .FirstOrDefaultAsync(u => u.AuthUserId == authUserId);

        if (manager is null)
        {
            manager = await _context.Users
                .AsNoTracking()
                .Include(u => u.ManagedSubServices)
                .Include(u => u.ManagedServices)
                .FirstOrDefaultAsync(u => u.Id == authUserId);
        }

        if (manager is null)
            return [];

        // Périmètre Managed* uniquement — pas de repli SubServiceId (appartenance ≠ responsabilité).
        var subServiceIds = (await _perimeter.GetManagedSubServiceIdsAsync(manager)).ToList();

        if (subServiceIds.Count == 0)
            return [];

        // One light query — no GetPlanningByIdAsync / coverage / timelines.
        var summaries = await _context.WeeklyPlannings
            .AsNoTracking()
            .Where(p => subServiceIds.Contains(p.SubServiceId)
                        && p.Status == PlanningStatus.Published)
            .OrderByDescending(p => p.WeekStartDate)
            .Take(10)
            .Select(p => new EquipePlanningSummaryDto
            {
                Id = p.Id,
                WeekCode = p.WeekCode,
                WeekStartDate = p.WeekStartDate,
                Status = p.Status.ToString(),
                SubServiceId = p.SubServiceId,
                SubServiceName = p.SubService != null ? p.SubService.Name : string.Empty,
                EmployeeCount = p.ShiftAssignments.Select(a => a.UserId).Distinct().Count()
            })
            .ToListAsync();

        await FillAssignedUserIdsAsync(summaries);
        return summaries;
    }

    private async Task FillAssignedUserIdsAsync(List<EquipePlanningSummaryDto> summaries)
    {
        if (summaries.Count == 0) return;

        var planningIds = summaries.Select(s => s.Id).ToList();
        var rows = await _context.ShiftAssignments
            .AsNoTracking()
            .Where(a => planningIds.Contains(a.WeeklyPlanningId))
            .Select(a => new { a.WeeklyPlanningId, a.UserId })
            .Distinct()
            .ToListAsync();

        var byPlanning = rows
            .GroupBy(r => r.WeeklyPlanningId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).Distinct().ToList());

        foreach (var s in summaries)
            s.AssignedUserIds = byPlanning.GetValueOrDefault(s.Id) ?? [];
    }

    // ----------------------------------------------------
    // VUE SEMAINE + AUTO-GÉNÉRATION
    // ----------------------------------------------------
    public Task<PlanningWeekListDto> GetWeekOverviewAsync(string weekCode, int? viewerUserId = null) =>
        GetWeekOverviewCoreAsync(weekCode, viewerUserId, explicitScope: null);

    public async Task<PlanningWeekListDto> GetWeekOverviewForManagedCellsAsync(
        string weekCode,
        int viewerUserId,
        IReadOnlyCollection<string> primeCelluleIds)
    {
        var cells = primeCelluleIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scope = cells.Count == 0
            ? []
            : (await _context.SubServices
                .AsNoTracking()
                .Where(ss => ss.Service != null
                    && ss.Service.PrimeCelluleId != null
                    && cells.Contains(ss.Service.PrimeCelluleId))
                .Select(ss => ss.Id)
                .ToListAsync()).ToHashSet();

        return await GetWeekOverviewCoreAsync(weekCode, viewerUserId, scope);
    }

    private async Task<PlanningWeekListDto> GetWeekOverviewCoreAsync(
        string weekCode,
        int? viewerUserId,
        HashSet<int>? explicitScope)
    {
        var weekStart = ParseWeekCodeToMonday(weekCode);
        var subServices = await _context.SubServices
            .Include(s => s.Service)
            .OrderBy(s => s.Name)
            .ToListAsync();

        var scoped = explicitScope ?? await ResolveViewerSubServiceScopeAsync(viewerUserId);
        if (scoped is not null)
            subServices = subServices.Where(s => scoped.Contains(s.Id)).ToList();

        var plannings = await _context.WeeklyPlannings
            .Where(p => p.WeekCode == weekCode)
            .ToListAsync();

        var templateSubIds = await _context.SubServiceShiftConfigs
            .Where(c => c.IsTemplate)
            .Select(c => c.SubServiceId)
            .Distinct()
            .ToListAsync();

        var consultedIds = new HashSet<int>();
        if (viewerUserId.HasValue)
        {
            var planningIds = plannings.Select(p => p.Id).ToList();
            consultedIds = (await _context.PlanningConsultations
                .Where(c => c.UserId == viewerUserId.Value && planningIds.Contains(c.PlanningId))
                .Select(c => c.PlanningId)
                .ToListAsync()).ToHashSet();
        }

        var liveBySub = (await _context.Users
                .AsNoTracking()
                .Where(u => u.IsActive && u.SubServiceId != null)
                .GroupBy(u => u.SubServiceId!.Value)
                .Select(g => new { SubServiceId = g.Key, Count = g.Count() })
                .ToListAsync())
            .ToDictionary(x => x.SubServiceId, x => x.Count);

        var assignedByPlanning = new Dictionary<int, List<int>>();
        var manualModsByPlanning = new Dictionary<int, int>();
        if (plannings.Count > 0)
        {
            var pIds = plannings.Select(p => p.Id).ToList();
            var rows = await _context.ShiftAssignments
                .AsNoTracking()
                .Where(a => pIds.Contains(a.WeeklyPlanningId))
                .Select(a => new { a.WeeklyPlanningId, a.UserId })
                .Distinct()
                .ToListAsync();
            assignedByPlanning = rows
                .GroupBy(r => r.WeeklyPlanningId)
                .ToDictionary(g => g.Key, g => g.Select(x => x.UserId).Distinct().ToList());

            // Aligné sur le badge planning-view : override manuel hors DE / renfort.
            var manualRows = await _context.ShiftAssignments
                .AsNoTracking()
                .Where(a => pIds.Contains(a.WeeklyPlanningId)
                    && a.IsManagerOverride
                    && !a.IsExceptionalRequest
                    && !a.IsReinforcement)
                .GroupBy(a => a.WeeklyPlanningId)
                .Select(g => new { WeeklyPlanningId = g.Key, Count = g.Count() })
                .ToListAsync();
            manualModsByPlanning = manualRows.ToDictionary(x => x.WeeklyPlanningId, x => x.Count);
        }

        var items = new List<PlanningWeekItemDto>();
        foreach (var ss in subServices)
        {
            var planning = plannings.FirstOrDefault(p => p.SubServiceId == ss.Id);
            var hasTemplate = templateSubIds.Contains(ss.Id);
            // SubService = plus petite entité orga (= « service » métier pour le planning)
            var orgLabel = ss.Service != null
                ? $"{ss.Name} · {ss.Service.Name}"
                : ss.Name;

            items.Add(new PlanningWeekItemDto
            {
                SubServiceId = ss.Id,
                PrimeServiceId = ss.PrimeServiceId,
                SubServiceName = ss.Name,
                OrgLabel = orgLabel,
                PlanningId = planning?.Id,
                Status = planning?.Status.ToString(),
                TotalEffectif = planning is { TotalEffectif: > 0 }
                    ? planning.TotalEffectif
                    : liveBySub.GetValueOrDefault(ss.Id),
                HasTemplate = hasTemplate,
                CoverageOk = true,
                HasConsulted = planning != null && consultedIds.Contains(planning.Id),
                ManualModificationCount = planning != null
                    ? manualModsByPlanning.GetValueOrDefault(planning.Id)
                    : 0,
                AssignedUserIds = planning != null
                    ? assignedByPlanning.GetValueOrDefault(planning.Id) ?? []
                    : []
            });
        }

        return new PlanningWeekListDto
        {
            WeekCode = weekCode,
            WeekStartDate = weekStart,
            Items = items
        };
    }

    public async Task<AutoGenerateSettingsDto> GetAutoGenerateSettingsAsync()
    {
        var settings = await EnsureAutoGenerateSettingsEntityAsync();
        return MapAutoGenerateSettings(settings);
    }

    public async Task<AutoGenerateSettingsDto> SaveAutoGenerateSettingsAsync(
        AutoGenerateSettingsDto dto, int? updatedByUserId)
    {
        var settings = await EnsureAutoGenerateSettingsEntityAsync();
        settings.Enabled = dto.Enabled;
        settings.DayOfWeek = dto.DayOfWeek;
        settings.HourLocal = Math.Clamp(dto.HourLocal, 0, 23);
        settings.MinuteLocal = Math.Clamp(dto.MinuteLocal, 0, 59);
        settings.TimeZone = string.IsNullOrWhiteSpace(dto.TimeZone) ? "Africa/Casablanca" : dto.TimeZone.Trim();
        settings.Target = string.Equals(dto.Target, "CurrentWeek", StringComparison.OrdinalIgnoreCase)
            ? "CurrentWeek"
            : "NextWeek";
        settings.SenderFromEmail = PlanningSenderAddress.NormalizeEmailForSave(dto.SenderFromEmail);
        settings.SenderDisplayName = PlanningSenderAddress.NormalizeDisplayName(dto.SenderDisplayName);
        PlanningSmtpSettings.ApplyToEntity(settings, dto);
        PlanningMailApiSettings.ApplyToEntity(settings, dto);
        settings.UpdatedAt = DateTime.UtcNow;
        settings.UpdatedByUserId = updatedByUserId;
        await _context.SaveChangesAsync();
        return MapAutoGenerateSettings(settings);
    }

    public async Task EnsureViewerCanManageSubServiceAsync(int viewerUserId, int subServiceId)
    {
        var scoped = await ResolveViewerSubServiceScopeAsync(viewerUserId);
        if (scoped is null) return;
        if (scoped.Contains(subServiceId)) return;
        throw new UnauthorizedAccessException("Hors périmètre.");
    }

    /// <summary>null = pas de filtre (Admin/RH). Ensemble vide = aucune cellule.</summary>
    private async Task<HashSet<int>?> ResolveViewerSubServiceScopeAsync(int? viewerUserId)
    {
        if (viewerUserId is not > 0) return null;

        var viewer = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.ManagedSubServices)
            .Include(u => u.ManagedServices)
            .Include(u => u.SubService)
            .FirstOrDefaultAsync(u => u.Id == viewerUserId.Value);
        if (viewer is null) return [];

        var role = viewer.Role?.Name ?? string.Empty;
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase))
            return null;

        var scoped = await _perimeter.GetManagedSubServiceIdsAsync(viewer);
        if (scoped.Count > 0) return scoped;

        // L'affectation personnelle n'est pas une responsabilité de supervision.
        // Aucun miroir de responsabilité = aucun périmètre, jamais la cellule d'appartenance.
        return [];
    }

    public async Task<AutoGenerateWeekResultDto> AutoGenerateWeekAsync(
        string? weekCode = null, bool forceDraftRefresh = false, int? actorUserId = null)
    {
        DateOnly weekStart;
        string code;
        if (!string.IsNullOrWhiteSpace(weekCode))
        {
            code = weekCode!;
            weekStart = ParseWeekCodeToMonday(code);
        }
        else
        {
            var settings = await EnsureAutoGenerateSettingsEntityAsync();
            weekStart = GetTargetWeekMonday(settings.Target);
            code = FormatWeekCode(weekStart);
        }

        var result = new AutoGenerateWeekResultDto { WeekCode = code };

        var templateSubIds = await _context.SubServiceShiftConfigs
            .Where(c => c.IsTemplate)
            .Select(c => c.SubServiceId)
            .Distinct()
            .ToListAsync();

        var scoped = await ResolveViewerSubServiceScopeAsync(actorUserId);
        if (scoped is not null)
            templateSubIds = templateSubIds.Where(scoped.Contains).ToList();

        var allSubs = await _context.SubServices.Select(s => s.Id).ToListAsync();
        if (scoped is not null)
            allSubs = allSubs.Where(scoped.Contains).ToList();
        foreach (var subId in allSubs.Where(id => !templateSubIds.Contains(id)))
        {
            result.Skipped++;
            result.Messages.Add($"SubService {subId}: config shifts manquante (template).");
        }

        foreach (var subId in templateSubIds)
        {
            try
            {
                var existing = await _context.WeeklyPlannings
                    .FirstOrDefaultAsync(p => p.SubServiceId == subId && p.WeekCode == code);

                if (existing != null)
                {
                    if (existing.Status == PlanningStatus.Published)
                    {
                        result.Skipped++;
                        result.Messages.Add($"SubService {subId}: déjà publié — ignoré.");
                        continue;
                    }

                    if (!forceDraftRefresh)
                    {
                        result.Skipped++;
                        result.Messages.Add($"SubService {subId}: brouillon existant — ignoré.");
                        continue;
                    }

                    await GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
                    {
                        SubServiceId = subId,
                        WeekCode = code,
                        WeeklyPlanningId = existing.Id
                    });
                    result.Created++;
                    result.Messages.Add($"SubService {subId}: brouillon régénéré.");
                    continue;
                }

                var employeesCount = await _context.Users
                    .CountAsync(u => u.SubServiceId == subId && u.IsActive);

                var created = await CreatePlanningAsync(new CreateWeeklyPlanningDto
                {
                    SubServiceId = subId,
                    WeekCode = code,
                    WeekStartDate = weekStart,
                    TotalEffectif = Math.Max(1, employeesCount)
                });

                await GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
                {
                    SubServiceId = subId,
                    WeekCode = code,
                    WeeklyPlanningId = created.Id
                });

                result.Created++;
                result.Messages.Add($"SubService {subId}: brouillon créé.");
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.Messages.Add($"SubService {subId}: {ex.Message}");
            }
        }

        var settingsEntity = await EnsureAutoGenerateSettingsEntityAsync();
        settingsEntity.LastRunAt = DateTime.UtcNow;
        settingsEntity.LastRunWeekCode = code;
        await _context.SaveChangesAsync();

        return result;
    }

    private async Task<PlanningAutoGenerateSettings> EnsureAutoGenerateSettingsEntityAsync()
    {
        var settings = await _context.PlanningAutoGenerateSettings
            .FirstOrDefaultAsync(s => s.Id == PlanningAutoGenerateSettings.SingletonId);

        if (settings != null) return settings;

        settings = new PlanningAutoGenerateSettings();
        _context.PlanningAutoGenerateSettings.Add(settings);
        await _context.SaveChangesAsync();
        return settings;
    }

    private static AutoGenerateSettingsDto MapAutoGenerateSettings(PlanningAutoGenerateSettings s)
    {
        var dto = new AutoGenerateSettingsDto
        {
            Enabled = s.Enabled,
            DayOfWeek = s.DayOfWeek,
            HourLocal = s.HourLocal,
            MinuteLocal = s.MinuteLocal,
            TimeZone = s.TimeZone,
            Target = s.Target,
            LastRunAt = s.LastRunAt,
            LastRunWeekCode = s.LastRunWeekCode,
            SenderFromEmail = s.SenderFromEmail,
            SenderDisplayName = s.SenderDisplayName,
        };
        PlanningSmtpSettings.WriteToDto(s, dto);
        PlanningMailApiSettings.WriteToDto(s, dto);
        return dto;
    }

    private static DateOnly GetTargetWeekMonday(string target)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var currentMonday = GetMonday(today);
        return string.Equals(target, "CurrentWeek", StringComparison.OrdinalIgnoreCase)
            ? currentMonday
            : currentMonday.AddDays(7);
    }

    private static DateOnly GetMonday(DateOnly date)
    {
        var diff = ((int)date.DayOfWeek + 6) % 7; // Monday=0
        return date.AddDays(-diff);
    }

    private static string FormatWeekCode(DateOnly monday)
    {
        var dt = monday.ToDateTime(TimeOnly.MinValue);
        var week = System.Globalization.ISOWeek.GetWeekOfYear(dt);
        var year = System.Globalization.ISOWeek.GetYear(dt);
        return $"{year}-W{week:D2}";
    }

    private static DateOnly ParseWeekCodeToMonday(string weekCode)
    {
        var parts = weekCode.Split('-');
        var year = int.Parse(parts[0]);
        var week = int.Parse(parts[1].Replace("W", "", StringComparison.OrdinalIgnoreCase));
        return DateOnly.FromDateTime(System.Globalization.ISOWeek.ToDateTime(year, week, DayOfWeek.Monday));
    }

    private static DateOnly GetCasablancaToday()
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById("Africa/Casablanca");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                tz = TimeZoneInfo.FindSystemTimeZoneById("Morocco Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                tz = TimeZoneInfo.Utc;
            }
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz));
    }


}
