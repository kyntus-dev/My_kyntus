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
                c.Status.ToString(),
                c.SourceDemandeId != null ? OrigineDemande : OriginePlanning))
            .ToListAsync(ct);
    }

    private const string OriginePlanning = "Planning";
    private const string OrigineDemande = "Demande";
    /// <summary>v64 : une absence saisie dans le planning couvre au plus un an.</summary>
    private const int DureeMaxJours = 366;

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
        var (user, absenceType) = await ValiderAsync(dto, congeExclu: null, ct);

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
            conge.Status.ToString(),
            OriginePlanning);
    }

    /// <summary>
    /// v64 — Contrôles communs à l'ajout et à la modification : employé actif, type connu, dates dans
    /// l'ordre, durée raisonnable, période comprise dans le contrat (entrée réelle → sortie effective),
    /// aucun chevauchement avec une autre absence validée du même employé.
    /// </summary>
    private async Task<(User User, AbsenceType Type)> ValiderAsync(CreateCongeDto dto, int? congeExclu, CancellationToken ct)
    {
        if (dto.UserId <= 0)
            throw new InvalidOperationException("Employé invalide.");
        if (dto.StartDate == default || dto.EndDate == default)
            throw new InvalidOperationException("Les dates de début et de fin sont obligatoires.");
        if (dto.EndDate < dto.StartDate)
            throw new InvalidOperationException("La date de fin doit être après la date de début.");
        if (dto.EndDate.DayNumber - dto.StartDate.DayNumber + 1 > DureeMaxJours)
            throw new InvalidOperationException($"Période trop longue : {DureeMaxJours} jours au plus.");
        if (string.IsNullOrWhiteSpace(dto.AbsenceType)
            || !Enum.TryParse<AbsenceType>(dto.AbsenceType, ignoreCase: true, out var absenceType)
            || !Enum.IsDefined(absenceType))
            throw new InvalidOperationException("Type d'absence invalide.");
        if (dto.Reason is { Length: > 500 })
            throw new InvalidOperationException("Motif trop long : 500 caractères au plus.");

        var user = await context.Users.FirstOrDefaultAsync(u => u.Id == dto.UserId, ct)
            ?? throw new InvalidOperationException("Employé introuvable.");
        if (!user.IsActive)
            throw new InvalidOperationException("Cet employé est inactif.");

        var profil = await context.UserHrProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        var contrat = ContratPeriode.De(user, profil);
        if (contrat.MotifHorsContrat(dto.StartDate) is string motifDebut)
            throw new InvalidOperationException($"Le début de l'absence ({dto.StartDate:dd/MM/yyyy}) est hors contrat : {motifDebut}.");
        if (contrat.MotifHorsContrat(dto.EndDate) is string motifFin)
            throw new InvalidOperationException($"La fin de l'absence ({dto.EndDate:dd/MM/yyyy}) est hors contrat : {motifFin}.");

        var chevauchement = await context.Conges.AsNoTracking()
            .Where(c => c.UserId == user.Id
                        && c.Status == CongeStatus.Approved
                        && (congeExclu == null || c.Id != congeExclu)
                        && c.StartDate <= dto.EndDate
                        && c.EndDate >= dto.StartDate)
            .OrderBy(c => c.StartDate)
            .FirstOrDefaultAsync(ct);
        if (chevauchement is not null)
            throw new InvalidOperationException(
                $"Cette période chevauche une absence déjà enregistrée du {chevauchement.StartDate:dd/MM/yyyy} au "
                + $"{chevauchement.EndDate:dd/MM/yyyy} : modifiez-la plutôt que d'en ajouter une seconde.");

        return (user, absenceType);
    }

    public async Task<PlanningCongeListItemDto?> GetByIdAsync(int id, CancellationToken ct = default) =>
        await context.Conges.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new PlanningCongeListItemDto(
                c.Id,
                c.UserId,
                UserDisplayName.Format(c.User.FirstName, c.User.LastName),
                c.StartDate,
                c.EndDate,
                c.Reason,
                c.AbsenceType.ToString(),
                c.Status.ToString(),
                c.SourceDemandeId != null ? OrigineDemande : OriginePlanning))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<PlanningCongeListItemDto>> GetByUserAsync(int userId, CancellationToken ct = default) =>
        await context.Conges.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.StartDate)
            .Select(c => new PlanningCongeListItemDto(
                c.Id,
                c.UserId,
                UserDisplayName.Format(c.User.FirstName, c.User.LastName),
                c.StartDate,
                c.EndDate,
                c.Reason,
                c.AbsenceType.ToString(),
                c.Status.ToString(),
                c.SourceDemandeId != null ? OrigineDemande : OriginePlanning))
            .ToListAsync(ct);

    public async Task<PlanningCongeListItemDto?> UpdateAsync(int id, CreateCongeDto dto, CancellationToken ct = default)
    {
        var conge = await context.Conges.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (conge is null)
            return null;
        if (conge.SourceDemandeId is not null)
            throw new InvalidOperationException(
                "Cette absence provient d'une demande de congé validée : modifiez ou annulez la demande dans Congés "
                + "(les soldes y sont tenus).");

        var (user, absenceType) = await ValiderAsync(dto, congeExclu: id, ct);
        var ancienUserId = conge.UserId;
        var ancienDebut = conge.StartDate;
        var ancienneFin = conge.EndDate;

        conge.UserId = user.Id;
        conge.StartDate = dto.StartDate;
        conge.EndDate = dto.EndDate;
        conge.Reason = dto.Reason?.Trim() ?? "";
        conge.AbsenceType = absenceType;
        await context.SaveChangesAsync(ct);

        // Le planning est resynchronisé : l'ancienne période est rendue, la nouvelle est posée.
        try
        {
            await leaveImpact.SyncAfterAbsenceChangeAsync(ancienUserId, ancienDebut, ancienneFin, absenceRemoved: true, ct);
            await leaveImpact.SyncAfterAbsenceChangeAsync(conge.UserId, conge.StartDate, conge.EndDate, absenceRemoved: false, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sync planning après modification de l'absence {CongeId} échouée.", conge.Id);
        }

        return new PlanningCongeListItemDto(
            conge.Id,
            conge.UserId,
            UserDisplayName.Format(user.FirstName, user.LastName),
            conge.StartDate,
            conge.EndDate,
            conge.Reason,
            conge.AbsenceType.ToString(),
            conge.Status.ToString(),
            OriginePlanning);
    }

    public async Task<IReadOnlySet<int>> SousServicesGerablesAsync(Guid compte, CancellationToken ct = default)
    {
        var moi = await context.Users.AsNoTracking()
            .Where(u => u.Guid == compte && compte != Guid.Empty)
            .Select(u => new { u.Id, u.SubServiceId })
            .FirstOrDefaultAsync(ct);
        if (moi is null)
            return new HashSet<int>();
        var ids = new HashSet<int>();
        if (moi.SubServiceId is int propre) ids.Add(propre);
        ids.UnionWith(await context.UserSubServices.AsNoTracking()
            .Where(us => us.UserId == moi.Id).Select(us => us.SubServiceId).ToListAsync(ct));
        var services = await context.UserManagedServices.AsNoTracking()
            .Where(us => us.UserId == moi.Id).Select(us => us.ServiceId).ToListAsync(ct);
        if (services.Count > 0)
            ids.UnionWith(await context.SubServices.AsNoTracking()
                .Where(ss => services.Contains(ss.ServiceId)).Select(ss => ss.Id).ToListAsync(ct));
        return ids;
    }

    public async Task<int?> SousServiceDeAsync(int? congeId, int? userId, CancellationToken ct = default)
    {
        var uid = userId;
        if (congeId is int cid)
            uid = await context.Conges.AsNoTracking().Where(c => c.Id == cid).Select(c => (int?)c.UserId).FirstOrDefaultAsync(ct);
        if (uid is not int u)
            return null;
        return await context.Users.AsNoTracking().Where(x => x.Id == u).Select(x => x.SubServiceId).FirstOrDefaultAsync(ct);
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
