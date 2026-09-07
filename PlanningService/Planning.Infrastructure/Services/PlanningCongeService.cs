using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services;

public sealed class PlanningCongeService(
    AppDbContext context,
    IPlanningLeaveImpactService leaveImpact,
    ILogger<PlanningCongeService> logger) : IPlanningCongeService
{
    public async Task<IReadOnlyList<PlanningCongeListItemDto>> GetBySubServiceAsync(
        int subServiceId,
        string? weekStart,
        CancellationToken ct = default)
    {
        var userIds = await context.Users
            .Where(u => u.SubServiceId == subServiceId && u.IsActive)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var query = context.Conges
            .Include(c => c.User)
            .Where(c => userIds.Contains(c.UserId));

        if (weekStart != null && DateOnly.TryParse(weekStart, out var start))
        {
            var end = start.AddDays(6);
            query = query.Where(c => c.StartDate <= end && c.EndDate >= start);
        }

        return await query
            .OrderBy(c => c.StartDate)
            .Select(c => new PlanningCongeListItemDto(
                c.Id,
                c.UserId,
                UserDisplayName.Format(c.User.FirstName, c.User.LastName),
                c.StartDate,
                c.EndDate,
                c.Reason,
                c.AbsenceType.ToString(),
                c.Status.ToString()))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PlanningNewEmployeeDto>> GetNewEmployeesAsync(
        int subServiceId,
        CancellationToken ct = default)
    {
        var employees = await context.Users
            .Where(u => u.SubServiceId == subServiceId && u.IsActive && u.IsNewEmployee)
            .ToListAsync(ct);

        var userIds = employees.Select(e => e.Id).ToList();
        var groups = await context.SaturdayGroups
            .Where(sg => userIds.Contains(sg.UserId))
            .ToListAsync(ct);

        return employees.Select(emp =>
        {
            var group = groups.FirstOrDefault(g => g.UserId == emp.Id);
            var slot = group?.NewEmployeeSlot ?? 0;
            return new PlanningNewEmployeeDto(
                emp.Id,
                UserDisplayName.Format(emp.FirstName, emp.LastName),
                emp.HireDate,
                (int)((DateTime.UtcNow - emp.HireDate).TotalDays / 30),
                emp.IsNewEmployee,
                slot,
                slot == 1 ? "8h00–12h00" : slot == 2 ? "12h00–16h00" : "Non configuré");
        }).ToList();
    }

    public async Task<PlanningCongeListItemDto> CreateAsync(CreateCongeDto dto, CancellationToken ct = default)
    {
        if (dto.UserId <= 0)
            throw new InvalidOperationException("Employé invalide.");

        if (dto.EndDate < dto.StartDate)
            throw new InvalidOperationException("La date de fin doit être après la date de début.");

        if (!Enum.TryParse<AbsenceType>(dto.AbsenceType, ignoreCase: true, out var absenceType))
            throw new InvalidOperationException("Type d'absence invalide.");

        var user = await context.Users.FindAsync([dto.UserId], ct)
            ?? throw new InvalidOperationException("Employé introuvable.");

        if (!user.IsActive)
            throw new InvalidOperationException("Cet employé est inactif.");

        var conge = new Conge
        {
            UserId = dto.UserId,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Reason = dto.Reason ?? "",
            AbsenceType = absenceType,
            Status = CongeStatus.Approved,
            CreatedAt = DateTime.UtcNow
        };

        context.Conges.Add(conge);
        await context.SaveChangesAsync(ct);

        try
        {
            await leaveImpact.SyncAfterAbsenceChangeAsync(
                conge.UserId, conge.StartDate, conge.EndDate, absenceRemoved: false, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync planning après absence manuelle {CongeId} échouée.", conge.Id);
        }

        return new PlanningCongeListItemDto(
            conge.Id,
            conge.UserId,
            UserDisplayName.Format(user.FirstName, user.LastName),
            conge.StartDate,
            conge.EndDate,
            conge.Reason,
            conge.AbsenceType.ToString(),
            conge.Status.ToString());
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
    {
        var conge = await context.Conges.FindAsync([id], ct);
        if (conge is null)
            return false;

        var userId = conge.UserId;
        var start = conge.StartDate;
        var end = conge.EndDate;

        context.Conges.Remove(conge);
        await context.SaveChangesAsync(ct);

        try
        {
            await leaveImpact.SyncAfterAbsenceChangeAsync(
                userId, start, end, absenceRemoved: true, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync planning après suppression absence {CongeId} échouée.", id);
        }

        return true;
    }

    public async Task<SetSaturdaySlotResultDto> SetSaturdaySlotAsync(
        SetSaturdaySlotDto dto,
        CancellationToken ct = default)
    {
        var group = await context.SaturdayGroups
            .FirstOrDefaultAsync(sg => sg.UserId == dto.UserId, ct)
            ?? throw new InvalidOperationException("Employe non trouve dans les groupes samedi.");

        group.NewEmployeeSlot = dto.Slot;
        await context.SaveChangesAsync(ct);

        return new SetSaturdaySlotResultDto(
            dto.UserId,
            dto.Slot,
            dto.Slot == 1 ? "8h00-12h00" : "12h00-16h00");
    }

    public async Task<BulkAbsenceDaysResponseDto> GetBulkAbsenceDaysAsync(
        BulkAbsenceDaysRequestDto request,
        CancellationToken ct = default)
    {
        if (!PlanningAbsenceDayCounter.TryParsePrimePeriod(request.Period, out var monthStart, out var monthEnd))
            throw new InvalidOperationException("Période invalide (format attendu : YYYY-MM).");

        var guidStrings = request.EmployeeGuids
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (guidStrings.Count == 0)
            return new BulkAbsenceDaysResponseDto();

        var guidSet = new HashSet<Guid>();
        foreach (var g in guidStrings)
        {
            if (Guid.TryParse(g, out var parsed))
                guidSet.Add(parsed);
        }

        if (guidSet.Count == 0)
            return new BulkAbsenceDaysResponseDto
            {
                Items = guidStrings.Select(g => new BulkAbsenceDaysItemDto(g, 0)).ToList(),
            };

        var users = await context.Users.AsNoTracking()
            .Where(u => guidSet.Contains(u.Guid))
            .Select(u => new { u.Guid, u.Id })
            .ToListAsync(ct);

        var userIdByGuid = users.ToDictionary(u => u.Guid, u => u.Id);
        var userIds = users.Select(u => u.Id).ToList();

        var conges = await context.Conges.AsNoTracking()
            .Where(c => userIds.Contains(c.UserId)
                        && c.Status == CongeStatus.Approved
                        && c.StartDate <= monthEnd
                        && c.EndDate >= monthStart)
            .Select(c => new { c.UserId, c.StartDate, c.EndDate })
            .ToListAsync(ct);

        var rangesByUserId = conges
            .GroupBy(c => c.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(c => (c.StartDate, c.EndDate)).AsEnumerable());

        var items = new List<BulkAbsenceDaysItemDto>();
        foreach (var guidStr in guidStrings)
        {
            if (!Guid.TryParse(guidStr, out var guid) || !userIdByGuid.TryGetValue(guid, out var userId))
            {
                items.Add(new BulkAbsenceDaysItemDto(guidStr, 0));
                continue;
            }

            var ranges = rangesByUserId.GetValueOrDefault(userId) ?? [];
            var count = PlanningAbsenceDayCounter.CountUnionMonToSatDays(ranges, monthStart, monthEnd);
            items.Add(new BulkAbsenceDaysItemDto(guidStr, count));
        }

        return new BulkAbsenceDaysResponseDto { Items = items };
    }
}
