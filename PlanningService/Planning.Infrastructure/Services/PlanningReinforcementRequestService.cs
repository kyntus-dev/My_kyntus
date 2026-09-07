using System.Globalization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Kyntus.Messaging.Contracts;
using Planning.Application.Abstractions;
using Planning.Application.DTOs.Planning;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Hubs;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

public class PlanningReinforcementRequestService : IPlanningReinforcementRequestService
{
    private readonly AppDbContext _context;
    private readonly IHubContext<PlanningHub> _hubContext;
    private readonly ILogger<PlanningReinforcementRequestService> _logger;
    private readonly IPlanningPerimeterResolver _perimeter;
    private const string NotifSubService = "Demande de renfort";

    public PlanningReinforcementRequestService(
        AppDbContext context,
        IHubContext<PlanningHub> hubContext,
        ILogger<PlanningReinforcementRequestService> logger,
        IPlanningPerimeterResolver perimeter)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger;
        _perimeter = perimeter;
    }

    public async Task<PlanningReinforcementRequestDto> CreateAsync(
        int createdByUserId, CreatePlanningReinforcementRequestDto dto)
    {
        if (dto.SubServiceId <= 0)
            throw new InvalidOperationException("Cellule (subService) requise.");
        if (dto.SaturdayDate.DayOfWeek != DayOfWeek.Saturday)
            throw new InvalidOperationException("La date doit être un samedi.");
        if (dto.SaturdayDate < DateOnly.FromDateTime(DateTime.Today))
            throw new InvalidOperationException("Le samedi ciblé doit être aujourd'hui ou dans le futur.");
        if (dto.SlotsNeeded < 1 || dto.SlotsNeeded > 50)
            throw new InvalidOperationException("Nombre de postes invalide (1–50).");
        if (string.IsNullOrWhiteSpace(dto.Reason) || dto.Reason.Trim().Length < 3)
            throw new InvalidOperationException("Motif trop court.");

        await EnsureCanActAsSupervisorAsync(createdByUserId, dto.SubServiceId);

        var monday = dto.SaturdayDate.AddDays(-5);
        var weekCode = FormatWeekCode(monday);

        var openExists = await _context.PlanningReinforcementRequests.AnyAsync(r =>
            r.SubServiceId == dto.SubServiceId
            && r.SaturdayDate == dto.SaturdayDate
            && r.Status == PlanningReinforcementRequestStatus.Open);
        if (openExists)
            throw new InvalidOperationException(
                "Une demande de renfort ouverte existe déjà pour ce samedi / cette cellule.");

        var planning = await _context.WeeklyPlannings
            .AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.SubServiceId == dto.SubServiceId && p.WeekCode == weekCode);

        var eligible = await ResolveEligibleUserIdsAsync(dto.SubServiceId, dto.SaturdayDate, weekCode, monday);
        if (eligible.Count == 0)
            throw new InvalidOperationException(
                "Aucun agent éligible (OFF ce samedi) dans cette cellule.");

        var entity = new PlanningReinforcementRequest
        {
            WeekCode = weekCode,
            SaturdayDate = dto.SaturdayDate,
            SubServiceId = dto.SubServiceId,
            SlotsNeeded = dto.SlotsNeeded,
            Reason = dto.Reason.Trim(),
            Status = PlanningReinforcementRequestStatus.Open,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
            WeeklyPlanningId = planning?.Id
        };
        _context.PlanningReinforcementRequests.Add(entity);
        await _context.SaveChangesAsync();

        foreach (var userId in eligible)
        {
            _context.PlanningReinforcementVolunteers.Add(new PlanningReinforcementVolunteer
            {
                RequestId = entity.Id,
                UserId = userId,
                Status = PlanningReinforcementVolunteerStatus.Pending
            });
        }
        await _context.SaveChangesAsync();

        var label = FormatWeekLabel(weekCode);
        var msg = $"Appel au renfort pour le samedi {dto.SaturdayDate:dd/MM} ({label}) — {dto.SlotsNeeded} poste(s).";
        foreach (var userId in eligible)
            await NotifyUserAsync(userId, weekCode, msg, "/mes-renforts");

        _logger.LogInformation(
            "Renfort #{Id} créé sub={Sub} sat={Sat} slots={Slots} eligible={N}",
            entity.Id, dto.SubServiceId, dto.SaturdayDate, dto.SlotsNeeded, eligible.Count);

        return (await MapAsync(entity.Id, createdByUserId))!;
    }

    public async Task<List<PlanningReinforcementRequestDto>> GetAllAsync(
        string? status,
        string? weekCode,
        int? viewerUserId = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var query = _context.PlanningReinforcementRequests.AsQueryable();

        if (!string.IsNullOrWhiteSpace(weekCode))
            query = query.Where(r => r.WeekCode == weekCode);
        else
        {
            if (from.HasValue)
                query = query.Where(r => r.SaturdayDate >= from.Value);
            if (to.HasValue)
                query = query.Where(r => r.SaturdayDate <= to.Value);
        }

        if (!string.IsNullOrWhiteSpace(status)
            && Enum.TryParse<PlanningReinforcementRequestStatus>(status, true, out var st))
            query = query.Where(r => r.Status == st);

        if (viewerUserId.HasValue)
        {
            var viewer = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.ManagedSubServices)
                .Include(u => u.ManagedServices)
                .FirstOrDefaultAsync(u => u.Id == viewerUserId.Value);

            if (viewer != null && !IsAdmin(viewer) && !IsRh(viewer))
            {
                var scoped = await GetManagedSubServiceIdsAsync(viewer);
                if (scoped.Count == 0)
                    return new List<PlanningReinforcementRequestDto>();
                query = query.Where(r => scoped.Contains(r.SubServiceId));
            }
        }

        var ids = await query
            .OrderByDescending(r => r.SaturdayDate)
            .ThenByDescending(r => r.CreatedAt)
            .Select(r => r.Id)
            .Take(100)
            .ToListAsync();

        var result = new List<PlanningReinforcementRequestDto>();
        foreach (var id in ids)
        {
            var dto = await MapAsync(id, viewerUserId, includeVolunteerHours: false);
            if (dto != null) result.Add(dto);
        }
        return result;
    }

    public async Task<List<ReinforcementContributorStatsDto>> GetContributorStatsAsync(
        int? viewerUserId = null,
        DateOnly? from = null,
        DateOnly? to = null,
        int? subServiceId = null)
    {
        var requestQuery = _context.PlanningReinforcementRequests.AsNoTracking().AsQueryable();

        if (from.HasValue)
            requestQuery = requestQuery.Where(r => r.SaturdayDate >= from.Value);
        if (to.HasValue)
            requestQuery = requestQuery.Where(r => r.SaturdayDate <= to.Value);
        if (subServiceId is > 0)
            requestQuery = requestQuery.Where(r => r.SubServiceId == subServiceId.Value);

        if (viewerUserId.HasValue)
        {
            var viewer = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.ManagedSubServices)
                .Include(u => u.ManagedServices)
                .FirstOrDefaultAsync(u => u.Id == viewerUserId.Value);

            if (viewer != null && !IsAdmin(viewer) && !IsRh(viewer))
            {
                var scoped = await GetManagedSubServiceIdsAsync(viewer);
                if (scoped.Count == 0)
                    return new List<ReinforcementContributorStatsDto>();
                requestQuery = requestQuery.Where(r => scoped.Contains(r.SubServiceId));
            }
        }

        var requestIds = await requestQuery.Select(r => r.Id).ToListAsync();
        if (requestIds.Count == 0)
            return new List<ReinforcementContributorStatsDto>();

        var rows = await _context.PlanningReinforcementVolunteers
            .AsNoTracking()
            .Where(v => requestIds.Contains(v.RequestId))
            .Select(v => new
            {
                v.UserId,
                v.Status,
                RequestSubServiceId = v.Request.SubServiceId,
                RequestSubServiceName = v.Request.SubService != null
                    ? v.Request.SubService.Name
                    : string.Empty,
                FirstName = v.User != null ? v.User.FirstName : null,
                LastName = v.User != null ? v.User.LastName : null,
                Email = v.User != null ? v.User.Email : null,
                UserSubServiceId = v.User != null ? v.User.SubServiceId : null,
                UserSubServiceName = v.User != null && v.User.SubService != null
                    ? v.User.SubService.Name
                    : null
            })
            .ToListAsync();

        return rows
            .GroupBy(r => r.UserId)
            .Select(g =>
            {
                var first = g.First();
                var solicited = g.Count();
                var selected = g.Count(x => x.Status == PlanningReinforcementVolunteerStatus.Selected);
                var accepted = g.Count(x =>
                    x.Status is PlanningReinforcementVolunteerStatus.Accepted
                        or PlanningReinforcementVolunteerStatus.Selected);
                var declined = g.Count(x => x.Status == PlanningReinforcementVolunteerStatus.Declined);
                var name = UserDisplayName.Format(first.FirstName, first.LastName);
                if (string.IsNullOrWhiteSpace(name))
                    name = first.Email ?? $"#{g.Key}";

                return new ReinforcementContributorStatsDto
                {
                    UserId = g.Key,
                    FullName = name,
                    SubServiceId = first.UserSubServiceId ?? first.RequestSubServiceId,
                    SubServiceName = first.UserSubServiceName
                                     ?? first.RequestSubServiceName
                                     ?? string.Empty,
                    Solicited = solicited,
                    Accepted = accepted,
                    Selected = selected,
                    Declined = declined,
                    AcceptanceRate = solicited == 0
                        ? 0
                        : Math.Round(100m * accepted / solicited, 1)
                };
            })
            .OrderByDescending(x => x.Selected)
            .ThenByDescending(x => x.Accepted)
            .ThenBy(x => x.FullName)
            .ToList();
    }

    public async Task<PlanningReinforcementRequestDto?> GetByIdAsync(int id, int? viewerUserId = null)
    {
        var dto = await MapAsync(id, viewerUserId, includeVolunteerHours: true);
        if (dto is null) return null;

        if (!viewerUserId.HasValue)
            return dto;

        var viewer = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.ManagedSubServices)
            .Include(u => u.ManagedServices)
            .FirstOrDefaultAsync(u => u.Id == viewerUserId.Value);
        if (viewer is null) return null;
        if (IsAdmin(viewer) || IsRh(viewer)) return dto;

        var scoped = await GetManagedSubServiceIdsAsync(viewer);
        if (!scoped.Contains(dto.SubServiceId))
            return null;

        return dto;
    }

    public async Task<List<PlanningReinforcementRequestDto>> GetMyAsync(int planningUserId)
    {
        var ids = await _context.PlanningReinforcementVolunteers
            .Where(v => v.UserId == planningUserId)
            .Select(v => v.RequestId)
            .Distinct()
            .ToListAsync();

        var result = new List<PlanningReinforcementRequestDto>();
        foreach (var id in ids.OrderByDescending(x => x))
        {
            var dto = await MapAsync(id, planningUserId, includeVolunteerHours: false);
            if (dto != null) result.Add(dto);
        }
        return result
            .OrderByDescending(r => r.SaturdayDate)
            .ThenByDescending(r => r.CreatedAt)
            .ToList();
    }

    public async Task<PlanningReinforcementRequestDto> VolunteerAcceptAsync(int id, int planningUserId)
    {
        var request = await _context.PlanningReinforcementRequests
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.Status != PlanningReinforcementRequestStatus.Open)
            throw new InvalidOperationException("Cette demande n'est plus ouverte.");

        var vol = await _context.PlanningReinforcementVolunteers
            .FirstOrDefaultAsync(v => v.RequestId == id && v.UserId == planningUserId)
            ?? throw new InvalidOperationException("Vous n'êtes pas concerné par cette demande.");

        if (vol.Status is PlanningReinforcementVolunteerStatus.Selected
            or PlanningReinforcementVolunteerStatus.Rejected)
            throw new InvalidOperationException("Réponse déjà finalisée.");

        vol.Status = PlanningReinforcementVolunteerStatus.Accepted;
        vol.RespondedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await NotifyUserAsync(
            request.CreatedByUserId,
            request.WeekCode,
            $"Un agent a accepté le renfort du {request.SaturdayDate:dd/MM}.",
            "/planning/demandes-renfort");

        return (await MapAsync(id, planningUserId))!;
    }

    public async Task<PlanningReinforcementRequestDto> VolunteerDeclineAsync(int id, int planningUserId)
    {
        var request = await _context.PlanningReinforcementRequests
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.Status != PlanningReinforcementRequestStatus.Open)
            throw new InvalidOperationException("Cette demande n'est plus ouverte.");

        var vol = await _context.PlanningReinforcementVolunteers
            .FirstOrDefaultAsync(v => v.RequestId == id && v.UserId == planningUserId)
            ?? throw new InvalidOperationException("Vous n'êtes pas concerné par cette demande.");

        if (vol.Status is PlanningReinforcementVolunteerStatus.Selected
            or PlanningReinforcementVolunteerStatus.Rejected)
            throw new InvalidOperationException("Réponse déjà finalisée.");

        vol.Status = PlanningReinforcementVolunteerStatus.Declined;
        vol.RespondedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return (await MapAsync(id, planningUserId))!;
    }

    public async Task<PlanningReinforcementRequestDto> SelectAsync(
        int id, int processedByUserId, SelectReinforcementVolunteersDto dto)
    {
        var request = await _context.PlanningReinforcementRequests
            .Include(r => r.Volunteers)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        await EnsureCanActAsSupervisorAsync(processedByUserId, request.SubServiceId);

        if (request.Status != PlanningReinforcementRequestStatus.Open)
            throw new InvalidOperationException("Cette demande n'est plus ouverte.");

        var userIds = (dto.UserIds ?? new List<int>()).Where(x => x > 0).Distinct().ToList();
        if (userIds.Count == 0)
            throw new InvalidOperationException("Sélectionnez au moins un volontaire.");
        if (userIds.Count > request.SlotsNeeded)
            throw new InvalidOperationException(
                $"Maximum {request.SlotsNeeded} poste(s) à pourvoir.");

        var shift = await _context.SubServiceShiftConfigs
            .FirstOrDefaultAsync(c =>
                c.Id == dto.ShiftConfigId
                && c.SubServiceId == request.SubServiceId)
            ?? throw new InvalidOperationException("Créneau invalide pour cette cellule.");

        var accepted = request.Volunteers
            .Where(v => v.Status == PlanningReinforcementVolunteerStatus.Accepted)
            .ToList();

        foreach (var uid in userIds)
        {
            if (!accepted.Any(v => v.UserId == uid))
                throw new InvalidOperationException(
                    $"L'agent #{uid} n'a pas accepté (ou n'est pas éligible).");
        }

        var planning = await _context.WeeklyPlannings
            .FirstOrDefaultAsync(p =>
                p.SubServiceId == request.SubServiceId && p.WeekCode == request.WeekCode);

        foreach (var vol in request.Volunteers)
        {
            if (userIds.Contains(vol.UserId))
            {
                vol.Status = PlanningReinforcementVolunteerStatus.Selected;
                vol.SelectedAt = DateTime.UtcNow;
                vol.SelectedShiftConfigId = shift.IsTemplate
                    ? shift.Id
                    : await ResolveTemplateIdAsync(shift) ?? shift.Id;

                if (planning != null)
                    await ApplyReinforcementAssignmentAsync(planning, vol.UserId, request.SaturdayDate, shift);
            }
            else if (vol.Status == PlanningReinforcementVolunteerStatus.Accepted)
            {
                vol.Status = PlanningReinforcementVolunteerStatus.Rejected;
            }
        }

        request.Status = PlanningReinforcementRequestStatus.Filled;
        request.SelectedByUserId = processedByUserId;
        request.ClosedByUserId = processedByUserId;
        request.ClosedAt = DateTime.UtcNow;
        if (planning != null)
            request.WeeklyPlanningId = planning.Id;

        await _context.SaveChangesAsync();

        foreach (var uid in userIds)
        {
            await NotifyUserAsync(
                uid,
                request.WeekCode,
                $"Vous êtes sélectionné(e) pour le renfort du samedi {request.SaturdayDate:dd/MM} ({shift.Label}).",
                "/mes-renforts");
        }

        foreach (var vol in accepted.Where(v => !userIds.Contains(v.UserId)))
        {
            await NotifyUserAsync(
                vol.UserId,
                request.WeekCode,
                $"Renfort du {request.SaturdayDate:dd/MM} : un autre collègue a été retenu.",
                "/mes-renforts");
        }

        return (await MapAsync(id, processedByUserId, includeVolunteerHours: true))!;
    }

    public async Task<PlanningReinforcementRequestDto> CancelAsync(int id, int processedByUserId)
    {
        var request = await _context.PlanningReinforcementRequests
            .Include(r => r.Volunteers)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        await EnsureCanActAsSupervisorAsync(processedByUserId, request.SubServiceId);

        if (request.Status != PlanningReinforcementRequestStatus.Open)
            throw new InvalidOperationException("Seule une demande ouverte peut être annulée.");

        request.Status = PlanningReinforcementRequestStatus.Cancelled;
        request.CancelledByUserId = processedByUserId;
        request.ClosedByUserId = processedByUserId;
        request.ClosedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        foreach (var vol in request.Volunteers.Where(v =>
                     v.Status is PlanningReinforcementVolunteerStatus.Pending
                         or PlanningReinforcementVolunteerStatus.Accepted))
        {
            await NotifyUserAsync(
                vol.UserId,
                request.WeekCode,
                $"L'appel au renfort du {request.SaturdayDate:dd/MM} a été annulé.",
                "/mes-renforts");
        }

        return (await MapAsync(id, processedByUserId))!;
    }

    /// <summary>
    /// Pose le shift samedi sans écrire SaturdayHistory (rotation intacte).
    /// </summary>
    private async Task ApplyReinforcementAssignmentAsync(
        WeeklyPlanning planning,
        int userId,
        DateOnly saturdayDate,
        SubServiceShiftConfig shift)
    {
        // Résoudre snapshot semaine si template
        var weekShift = shift;
        if (shift.IsTemplate)
        {
            var snapshots = await _context.SubServiceShiftConfigs
                .Where(c => c.SubServiceId == planning.SubServiceId
                            && !c.IsTemplate
                            && c.WeekCode == planning.WeekCode)
                .ToListAsync();
            weekShift = snapshots.FirstOrDefault(s =>
                            s.StartTime == shift.StartTime
                            && string.Equals(s.Label, shift.Label, StringComparison.OrdinalIgnoreCase))
                        ?? snapshots.FirstOrDefault(s => s.StartTime == shift.StartTime)
                        ?? shift;
        }

        var existing = await _context.ShiftAssignments
            .FirstOrDefaultAsync(a =>
                a.WeeklyPlanningId == planning.Id
                && a.UserId == userId
                && a.IsSaturday);

        if (existing != null)
        {
            existing.SubServiceShiftConfigId = weekShift.Id;
            existing.IsOnLeave = false;
            existing.IsHoliday = false;
            existing.IsManagerOverride = true;
            existing.IsReinforcement = true;
            existing.IsHalfDaySaturday = weekShift.WorkHours <= 4;
            existing.AssignedDate = saturdayDate;
        }
        else
        {
            _context.ShiftAssignments.Add(new ShiftAssignment
            {
                WeeklyPlanningId = planning.Id,
                UserId = userId,
                SubServiceShiftConfigId = weekShift.Id,
                AssignedDate = saturdayDate,
                DayOfWeek = DayOfWeekEnum.Saturday,
                IsSaturday = true,
                IsOnLeave = false,
                IsHoliday = false,
                IsManagerOverride = true,
                IsReinforcement = true,
                IsHalfDaySaturday = weekShift.WorkHours <= 4,
                SaturdaySlot = 0
            });
        }
        // Intentionnellement PAS de SaveSaturdayHistory
    }

    private async Task<int?> ResolveTemplateIdAsync(SubServiceShiftConfig weekOrTemplate)
    {
        if (weekOrTemplate.IsTemplate) return weekOrTemplate.Id;
        var template = await _context.SubServiceShiftConfigs
            .AsNoTracking()
            .Where(c => c.SubServiceId == weekOrTemplate.SubServiceId && c.IsTemplate)
            .FirstOrDefaultAsync(c =>
                c.StartTime == weekOrTemplate.StartTime
                || c.Label == weekOrTemplate.Label);
        return template?.Id;
    }

    private async Task<HashSet<int>> ResolveEligibleUserIdsAsync(
        int subServiceId, DateOnly saturdayDate, string weekCode, DateOnly monday)
    {
        var employees = await _context.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.SubServiceId == subServiceId)
            .ToListAsync();

        var userIds = employees.Select(e => e.Id).ToList();
        if (userIds.Count == 0) return new HashSet<int>();

        // Priorité : OFF réel sur le planning samedi publié (grille), pas la seule rotation théorique.
        var planning = await _context.WeeklyPlannings
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.SubServiceId == subServiceId && p.WeekCode == weekCode);

        if (planning != null)
        {
            var saturdayAssignments = await _context.ShiftAssignments
                .AsNoTracking()
                .Where(a =>
                    a.WeeklyPlanningId == planning.Id
                    && a.IsSaturday
                    && a.AssignedDate == saturdayDate
                    && userIds.Contains(a.UserId))
                .Select(a => new
                {
                    a.UserId,
                    a.SubServiceShiftConfigId,
                    a.IsOnLeave,
                    a.IsHoliday
                })
                .ToListAsync();

            if (saturdayAssignments.Count > 0)
            {
                var offIds = saturdayAssignments
                    .Where(a =>
                        a.SubServiceShiftConfigId == null
                        && !a.IsOnLeave
                        && !a.IsHoliday)
                    .Select(a => a.UserId)
                    .ToHashSet();

                var eligibleFromGrid = new HashSet<int>();
                foreach (var emp in employees)
                {
                    if (IsEveryHalfDaySaturday(emp))
                        continue;
                    if (offIds.Contains(emp.Id))
                        eligibleFromGrid.Add(emp.Id);
                }

                return eligibleFromGrid;
            }
        }

        var previousWeekCode = GetPreviousWeekCode(weekCode);
        var previousHistories = await _context.SaturdayHistories
            .AsNoTracking()
            .Where(h =>
                h.WeekCode == previousWeekCode
                && h.SubServiceId == subServiceId
                && userIds.Contains(h.UserId))
            .ToDictionaryAsync(h => h.UserId);

        var groups = await _context.SaturdayGroups
            .AsNoTracking()
            .Where(g => userIds.Contains(g.UserId))
            .ToListAsync();

        var planningGroupId = GetSaturdayGroupForWeek(monday);
        var eligible = new HashSet<int>();

        foreach (var emp in employees)
        {
            if (IsEveryHalfDaySaturday(emp))
                continue; // always-on 4h : pas de renfort

            previousHistories.TryGetValue(emp.Id, out var prev);
            var satGroup = groups.FirstOrDefault(g => g.UserId == emp.Id);
            var intendedOn = ComputeSaturdayIntendedOn(emp, prev, satGroup, planningGroupId);
            if (!intendedOn)
                eligible.Add(emp.Id);
        }

        return eligible;
    }

    private async Task<Dictionary<int, (decimal Week, decimal Month)>> ComputeScheduledHoursAsync(
        IReadOnlyList<int> userIds, DateOnly saturdayDate)
    {
        var result = userIds.ToDictionary(id => id, _ => (Week: 0m, Month: 0m));
        if (userIds.Count == 0) return result;

        var weekStart = saturdayDate.AddDays(-5);
        var weekEnd = saturdayDate;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        var rangeStart = weekStart < monthStart ? weekStart : monthStart;
        var rangeEnd = weekEnd > monthEnd ? weekEnd : monthEnd;

        var rows = await _context.ShiftAssignments
            .AsNoTracking()
            .Include(a => a.SubServiceShiftConfig)
            .Where(a =>
                userIds.Contains(a.UserId)
                && a.AssignedDate >= rangeStart
                && a.AssignedDate <= rangeEnd
                && !a.IsOnLeave
                && !a.IsHoliday
                && a.SubServiceShiftConfigId != null)
            .Select(a => new
            {
                a.UserId,
                a.AssignedDate,
                Hours = a.SubServiceShiftConfig != null
                    ? a.SubServiceShiftConfig.WorkHours
                    : 0
            })
            .ToListAsync();

        foreach (var row in rows)
        {
            var hours = (decimal)row.Hours;
            if (hours <= 0) continue;
            var cur = result[row.UserId];
            if (row.AssignedDate >= weekStart && row.AssignedDate <= weekEnd)
                cur.Week += hours;
            if (row.AssignedDate >= monthStart && row.AssignedDate <= monthEnd)
                cur.Month += hours;
            result[row.UserId] = cur;
        }

        return result;
    }

    private async Task<PlanningReinforcementRequestDto?> MapAsync(
        int id, int? viewerUserId, bool includeVolunteerHours = true)
    {
        var r = await _context.PlanningReinforcementRequests
            .AsNoTracking()
            .Include(x => x.SubService)
            .Include(x => x.CreatedBy)
            .Include(x => x.Volunteers)
                .ThenInclude(v => v.User)
            .Include(x => x.Volunteers)
                .ThenInclude(v => v.SelectedShiftConfig)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (r == null) return null;

        Dictionary<int, (decimal Week, decimal Month)>? hours = null;
        if (includeVolunteerHours)
        {
            var ids = r.Volunteers.Select(v => v.UserId).ToList();
            hours = await ComputeScheduledHoursAsync(ids, r.SaturdayDate);
        }

        var volunteers = r.Volunteers
            .OrderBy(v => v.User.LastName)
            .ThenBy(v => v.User.FirstName)
            .Select(v =>
            {
                var h = hours != null && hours.TryGetValue(v.UserId, out var hv) ? hv : (0m, 0m);
                return new PlanningReinforcementVolunteerDto
                {
                    Id = v.Id,
                    UserId = v.UserId,
                    FullName = DisplayName(v.User, v.UserId),
                    Status = v.Status.ToString(),
                    RespondedAt = v.RespondedAt,
                    SelectedAt = v.SelectedAt,
                    SelectedShiftConfigId = v.SelectedShiftConfigId,
                    SelectedShiftLabel = v.SelectedShiftConfig?.Label,
                    ScheduledHoursWeek = h.Item1,
                    ScheduledHoursMonth = h.Item2
                };
            })
            .ToList();

        string? myStatus = null;
        if (viewerUserId is > 0)
        {
            var mine = r.Volunteers.FirstOrDefault(v => v.UserId == viewerUserId.Value);
            if (mine != null) myStatus = mine.Status.ToString();
        }

        return new PlanningReinforcementRequestDto
        {
            Id = r.Id,
            WeekCode = r.WeekCode,
            SaturdayDate = r.SaturdayDate,
            SubServiceId = r.SubServiceId,
            SubServiceName = r.SubService?.Name ?? "",
            SlotsNeeded = r.SlotsNeeded,
            Reason = r.Reason,
            Status = r.Status.ToString(),
            CreatedByUserId = r.CreatedByUserId,
            CreatedByName = DisplayName(r.CreatedBy, r.CreatedByUserId),
            SelectedByUserId = r.SelectedByUserId,
            ClosedByUserId = r.ClosedByUserId,
            CancelledByUserId = r.CancelledByUserId,
            CreatedAt = r.CreatedAt,
            ClosedAt = r.ClosedAt,
            WeeklyPlanningId = r.WeeklyPlanningId,
            AcceptedCount = r.Volunteers.Count(v =>
                v.Status is PlanningReinforcementVolunteerStatus.Accepted
                    or PlanningReinforcementVolunteerStatus.Selected),
            SelectedCount = r.Volunteers.Count(v =>
                v.Status == PlanningReinforcementVolunteerStatus.Selected),
            EligibleCount = r.Volunteers.Count,
            MyVolunteerStatus = myStatus,
            Volunteers = volunteers
        };
    }

    private async Task EnsureCanActAsSupervisorAsync(int processedByUserId, int subServiceId)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.ManagedSubServices)
            .Include(u => u.ManagedServices)
            .FirstOrDefaultAsync(u => u.Id == processedByUserId)
            ?? throw new InvalidOperationException("Utilisateur introuvable.");

        if (IsAdmin(user)) return;

        var roleName = user.Role?.Name;
        if (!KyntusRoleNames.IsSuperviseur(roleName)
            && !string.Equals(roleName, "Manager", StringComparison.OrdinalIgnoreCase)
            && !KyntusRoleNames.IsReferentTechnique(roleName)
            && !KyntusRoleNames.IsChefDeProjet(roleName)
            && !IsRh(user))
        {
            throw new InvalidOperationException("Action réservée au superviseur / manager.");
        }

        if (IsRh(user)) return;

        var scoped = await _perimeter.GetManagedSubServiceIdsAsync(user);
        if (scoped.Count == 0)
            throw new InvalidOperationException(
                "Aucun périmètre cellule n'est associé à votre compte. Contactez la RH.");
        if (!scoped.Contains(subServiceId))
            throw new InvalidOperationException("Cette cellule n'est pas dans votre périmètre.");
    }

    private Task<HashSet<int>> GetManagedSubServiceIdsAsync(User manager) =>
        _perimeter.GetManagedSubServiceIdsAsync(manager);

    private async Task NotifyUserAsync(int planningUserId, string weekCode, string message, string deepLink)
    {
        var linked = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == planningUserId && u.AuthUserId != null)
            .Select(u => new { u.Id, AuthUserId = u.AuthUserId!.Value })
            .FirstOrDefaultAsync();
        if (linked is null)
        {
            var email = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == planningUserId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync();
            _logger.LogWarning(
                "Renfort notif skip: user Planning #{UserId} ({Email}) sans AuthUserId",
                planningUserId, email ?? "?");
            return;
        }

        var notif = new PlanningNotification
        {
            UserId = linked.Id,
            AuthUserId = linked.AuthUserId,
            WeeklyPlanningId = null,
            WeekCode = weekCode,
            SubServiceName = NotifSubService,
            Message = message,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };
        _context.PlanningNotifications.Add(notif);
        await _context.SaveChangesAsync();

        await _hubContext.Clients
            .Group($"user_{linked.AuthUserId}")
            .SendAsync("PlanningPublished", new
            {
                id = notif.Id,
                weekCode = notif.WeekCode,
                subServiceName = notif.SubServiceName,
                message = notif.Message,
                deepLink,
                createdAt = notif.CreatedAt
            });
    }

    private static bool IsAdmin(User u) =>
        string.Equals(u.Role?.Name, "Admin", StringComparison.OrdinalIgnoreCase);

    private static bool IsRh(User u) =>
        string.Equals(u.Role?.Name, "RH", StringComparison.OrdinalIgnoreCase);

    private static string DisplayName(User? user, int fallbackId)
    {
        if (user == null) return $"#{fallbackId}";
        var name = UserDisplayName.Format(user.FirstName, user.LastName);
        return string.IsNullOrWhiteSpace(name) ? (user.Email ?? $"#{fallbackId}") : name;
    }

    private static string FormatWeekCode(DateOnly monday)
    {
        var dt = monday.ToDateTime(TimeOnly.MinValue);
        var week = ISOWeek.GetWeekOfYear(dt);
        var year = ISOWeek.GetYear(dt);
        return $"{year}-W{week:D2}";
    }

    private static string FormatWeekLabel(string? weekCode)
    {
        if (string.IsNullOrWhiteSpace(weekCode)) return "";
        return weekCode.Replace("-W", "-S", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetPreviousWeekCode(string weekCode)
    {
        var parts = weekCode.Split('-');
        var year = int.Parse(parts[0]);
        var week = int.Parse(parts[1].Replace("W", "", StringComparison.OrdinalIgnoreCase));
        if (week == 1) return $"{year - 1}-W52";
        return $"{year}-W{(week - 1):D2}";
    }

    private static int GetSaturdayGroupForWeek(DateOnly weekStart)
    {
        var weekNumber = ISOWeek.GetWeekOfYear(weekStart.ToDateTime(TimeOnly.MinValue));
        return weekNumber % 2 == 0 ? 1 : 2;
    }

    private static int ResolveEffectiveSaturdayWorkMode(User employee)
    {
        if (employee.SaturdayWorkMode is (int)SaturdayWorkMode.EveryHalfDay
            or (int)SaturdayWorkMode.AlternatingFullDay)
            return employee.SaturdayWorkMode.Value;
        return employee.Level == 1
            ? (int)SaturdayWorkMode.EveryHalfDay
            : (int)SaturdayWorkMode.AlternatingFullDay;
    }

    private static bool IsEveryHalfDaySaturday(User employee) =>
        ResolveEffectiveSaturdayWorkMode(employee) == (int)SaturdayWorkMode.EveryHalfDay;

    private static bool ComputeSaturdayIntendedOn(
        User employee,
        SaturdayHistory? previousHistory,
        SaturdayGroup? satGroup,
        int planningSaturdayGroupId)
    {
        if (IsEveryHalfDaySaturday(employee))
            return true;
        if (previousHistory != null)
            return !previousHistory.WorkedSaturday;
        if (satGroup != null)
            return satGroup.GroupNumber == planningSaturdayGroupId;
        return true;
    }
}
