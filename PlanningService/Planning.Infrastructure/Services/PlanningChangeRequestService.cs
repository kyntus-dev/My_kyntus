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

public class PlanningChangeRequestService : IPlanningChangeRequestService
{
    private readonly AppDbContext _context;
    private readonly IHubContext<PlanningHub> _hubContext;
    private readonly ILogger<PlanningChangeRequestService> _logger;
    private readonly IPlanningPerimeterResolver _perimeter;
    private const string CasablancaTz = "Africa/Casablanca";
    private const string ChangeRequestSubService = "Demande de changement";

    public PlanningChangeRequestService(
        AppDbContext context,
        IHubContext<PlanningHub> hubContext,
        ILogger<PlanningChangeRequestService> logger,
        IPlanningPerimeterResolver perimeter)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger;
        _perimeter = perimeter;
    }

    public async Task<PlanningChangeRequestDto> CreateAsync(
        int requesterUserId, CreatePlanningChangeRequestDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason))
            throw new InvalidOperationException("Le motif est obligatoire.");

        if (dto.ProposedSwapUserId <= 0)
            throw new InvalidOperationException(
                "Un collègue partenaire est obligatoire pour demander un switch.");

        var assignment = await _context.ShiftAssignments
            .Include(a => a.WeeklyPlanning).ThenInclude(p => p.SubService)
            .Include(a => a.SubServiceShiftConfig)
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Id == dto.CurrentAssignmentId)
            ?? throw new InvalidOperationException("Assignation introuvable.");

        if (assignment.UserId != requesterUserId)
            throw new InvalidOperationException("Vous ne pouvez demander un changement que pour votre propre créneau.");

        if (assignment.IsOnLeave || assignment.IsHoliday || assignment.SubServiceShiftConfigId == null)
            throw new InvalidOperationException("Ce créneau n'est pas modifiable (congé, férié ou OFF).");

        EnsureCreationDeadline(assignment.AssignedDate);

        var candidates = await GetSwapCandidatesAsync(assignment.Id, requesterUserId);
        if (candidates.All(c => c.UserId != dto.ProposedSwapUserId))
            throw new InvalidOperationException(
                "Le collègue proposé n'est pas un candidat valide (même niveau, même sous-service, disponible).");

        var entity = new PlanningChangeRequest
        {
            WeekCode = assignment.WeeklyPlanning.WeekCode,
            RequesterUserId = requesterUserId,
            CurrentAssignmentId = assignment.Id,
            Reason = dto.Reason.Trim(),
            ProposedSwapUserId = dto.ProposedSwapUserId,
            Status = PlanningChangeRequestStatus.PendingPartner,
            CreatedAt = DateTime.UtcNow
        };

        _context.PlanningChangeRequests.Add(entity);
        await _context.SaveChangesAsync();

        var requesterName = DisplayName(assignment.User, requesterUserId);

        try
        {
            await NotifyUserAsync(
                dto.ProposedSwapUserId,
                entity.WeekCode,
                $"{requesterName} vous propose un switch ({FormatWeekLabel(entity.WeekCode)}) — acceptez ou refusez dans Mes plannings.",
                "/mes-demandes-changement");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Échec notification partenaire pour la demande {RequestId}",
                entity.Id);
        }

        return await MapAsync(entity.Id)
            ?? throw new InvalidOperationException("Erreur création demande.");
    }

    public async Task<List<PlanningChangeRequestDto>> GetMyAsync(int requesterUserId)
    {
        var ids = await _context.PlanningChangeRequests
            .Where(r => r.RequesterUserId == requesterUserId
                        || r.ProposedSwapUserId == requesterUserId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Id)
            .ToListAsync();

        var result = new List<PlanningChangeRequestDto>();
        foreach (var id in ids)
        {
            try
            {
                var dto = await MapAsync(id, requesterUserId);
                if (dto != null) result.Add(dto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mapping demande de changement {RequestId} ignoré.", id);
            }
        }
        return result;
    }

    public async Task<List<PlanningChangeRequestDto>> GetAllAsync(
        string? status,
        string? weekCode,
        int? viewerUserId = null,
        int? requesterUserId = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var query = _context.PlanningChangeRequests
            .Include(r => r.CurrentAssignment)
                .ThenInclude(a => a.WeeklyPlanning)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(weekCode))
            query = query.Where(r => r.WeekCode == weekCode);
        else
        {
            if (from.HasValue)
                query = query.Where(r => r.CurrentAssignment.AssignedDate >= from.Value);
            if (to.HasValue)
                query = query.Where(r => r.CurrentAssignment.AssignedDate <= to.Value);
        }

        if (requesterUserId is > 0)
            query = query.Where(r => r.RequesterUserId == requesterUserId.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r =>
                    r.Status == PlanningChangeRequestStatus.PendingPartner
                    || r.Status == PlanningChangeRequestStatus.PendingSupervisor);
            }
            else if (Enum.TryParse<PlanningChangeRequestStatus>(status, true, out var st))
            {
                query = query.Where(r => r.Status == st);
            }
        }

        if (viewerUserId.HasValue)
        {
            var viewer = await _context.Users
                .Include(u => u.Role)
                .Include(u => u.ManagedSubServices)
                .Include(u => u.ManagedServices)
                .FirstOrDefaultAsync(u => u.Id == viewerUserId.Value);

            if (viewer != null && !IsAdmin(viewer) && !IsRh(viewer))
            {
                var scopedIds = await GetManagedSubServiceIdsAsync(viewer);
                if (scopedIds.Count == 0)
                    return new List<PlanningChangeRequestDto>();

                query = query.Where(r =>
                    scopedIds.Contains(r.CurrentAssignment.WeeklyPlanning.SubServiceId));
            }
        }

        var ids = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Id)
            .ToListAsync();

        var result = new List<PlanningChangeRequestDto>();
        foreach (var id in ids)
        {
            try
            {
                var dto = await MapAsync(id, viewerUserId);
                if (dto != null) result.Add(dto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mapping demande de changement {RequestId} ignoré.", id);
            }
        }
        return result;
    }

    public async Task<List<ChangeRequestStatsByEmployeeDto>> GetStatsByEmployeeAsync(
        string? weekCode, DateOnly? from = null, DateOnly? to = null)
    {
        var query = _context.PlanningChangeRequests.AsQueryable();
        if (!string.IsNullOrWhiteSpace(weekCode))
            query = query.Where(r => r.WeekCode == weekCode);
        else
        {
            if (from.HasValue)
                query = query.Where(r => r.CurrentAssignment.AssignedDate >= from.Value);
            if (to.HasValue)
                query = query.Where(r => r.CurrentAssignment.AssignedDate <= to.Value);
        }

        var grouped = await query
            .GroupBy(r => r.RequesterUserId)
            .Select(g => new
            {
                UserId = g.Key,
                Total = g.Count(),
                Pending = g.Count(x =>
                    x.Status == PlanningChangeRequestStatus.PendingPartner
                    || x.Status == PlanningChangeRequestStatus.PendingSupervisor),
                Approved = g.Count(x => x.Status == PlanningChangeRequestStatus.Approved),
                Rejected = g.Count(x => x.Status == PlanningChangeRequestStatus.Rejected)
            })
            .ToListAsync();

        var userIds = grouped.Select(g => g.UserId).ToList();
        var users = await _context.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        return grouped.Select(g => new ChangeRequestStatsByEmployeeDto
            {
                UserId = g.UserId,
                FullName = users.TryGetValue(g.UserId, out var u)
                    ? UserDisplayName.Format(u.FirstName, u.LastName)
                    : $"#{g.UserId}",
                TotalRequests = g.Total,
                PendingCount = g.Pending,
                ApprovedCount = g.Approved,
                RejectedCount = g.Rejected
            })
            .OrderByDescending(s => s.TotalRequests)
            .ToList();
    }

    public async Task<List<SwapCandidateDto>> GetSwapCandidatesAsync(int assignmentId, int requesterUserId)
    {
        var assignment = await _context.ShiftAssignments
            .Include(a => a.WeeklyPlanning)
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Id == assignmentId)
            ?? throw new InvalidOperationException("Assignation introuvable.");

        if (assignment.UserId != requesterUserId)
            throw new InvalidOperationException("Assignation non autorisée.");

        var level = assignment.User.Level;
        var subServiceId = assignment.WeeklyPlanning.SubServiceId;
        var date = assignment.AssignedDate;

        var peers = await _context.Users
            .Where(u => u.IsActive
                     && u.SubServiceId == subServiceId
                     && u.Level == level
                     && u.Id != requesterUserId)
            .ToListAsync();

        var peerIds = peers.Select(p => p.Id).ToList();
        var peerAssignments = await _context.ShiftAssignments
            .Include(a => a.SubServiceShiftConfig)
            .Where(a => a.WeeklyPlanningId == assignment.WeeklyPlanningId
                     && a.AssignedDate == date
                     && peerIds.Contains(a.UserId)
                     && !a.IsOnLeave
                     && !a.IsHoliday
                     && a.SubServiceShiftConfigId != null)
            .ToListAsync();

        return peerAssignments
            .Where(a => a.SubServiceShiftConfigId != assignment.SubServiceShiftConfigId)
            .Select(a =>
            {
                var peer = peers.First(p => p.Id == a.UserId);
                return new SwapCandidateDto
                {
                    UserId = peer.Id,
                    FullName = UserDisplayName.Format(peer.FirstName, peer.LastName),
                    Level = peer.Level,
                    AssignmentId = a.Id,
                    ShiftLabel = a.SubServiceShiftConfig?.Label ?? "—"
                };
            })
            .OrderBy(c => c.FullName)
            .ToList();
    }

    public async Task<PlanningChangeRequestDto> PartnerAcceptAsync(int id, int partnerUserId)
    {
        var request = await _context.PlanningChangeRequests
            .Include(r => r.CurrentAssignment).ThenInclude(a => a.WeeklyPlanning)
            .Include(r => r.Requester)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.ProposedSwapUserId != partnerUserId)
            throw new InvalidOperationException("Seul le collègue proposé peut accepter.");

        if (request.Status != PlanningChangeRequestStatus.PendingPartner)
            throw new InvalidOperationException("Cette demande n'est plus en attente du collègue.");

        request.Status = PlanningChangeRequestStatus.PendingSupervisor;
        request.PartnerRespondedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var requesterName = DisplayName(request.Requester, request.RequesterUserId);
        try
        {
            await NotifySupervisorsForSubServiceAsync(
                request.CurrentAssignment.WeeklyPlanning.SubServiceId,
                request.WeekCode,
                $"Switch accepté par le collègue — {requesterName} ({FormatWeekLabel(request.WeekCode)}). À valider.",
                "/planning/change-requests");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec notif superviseurs demande {RequestId}", id);
        }

        return await MapAsync(id) ?? throw new InvalidOperationException("Erreur partner-accept.");
    }

    public async Task<PlanningChangeRequestDto> PartnerRejectAsync(
        int id, int partnerUserId, string? reason)
    {
        var request = await _context.PlanningChangeRequests.FindAsync(id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.ProposedSwapUserId != partnerUserId)
            throw new InvalidOperationException("Seul le collègue proposé peut refuser.");

        if (request.Status != PlanningChangeRequestStatus.PendingPartner)
            throw new InvalidOperationException("Cette demande n'est plus en attente du collègue.");

        request.Status = PlanningChangeRequestStatus.Rejected;
        request.PartnerRespondedAt = DateTime.UtcNow;
        request.ProcessedByUserId = partnerUserId;
        request.ProcessedAt = DateTime.UtcNow;
        request.RejectionReason = string.IsNullOrWhiteSpace(reason)
            ? "Refusé par le collègue proposé."
            : reason.Trim();

        await _context.SaveChangesAsync();

        await NotifyUserAsync(
            request.RequesterUserId,
            request.WeekCode,
            $"Votre demande de switch ({FormatWeekLabel(request.WeekCode)}) a été refusée par le collègue.",
            "/mes-demandes-changement");

        return await MapAsync(id) ?? throw new InvalidOperationException("Erreur partner-reject.");
    }

    public async Task<PlanningChangeRequestDto> ApproveAsync(int id, int processedByUserId)
    {
        var request = await _context.PlanningChangeRequests
            .Include(r => r.CurrentAssignment).ThenInclude(a => a.WeeklyPlanning)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.Status != PlanningChangeRequestStatus.PendingSupervisor)
            throw new InvalidOperationException(
                "Seules les demandes acceptées par le collègue peuvent être validées par le superviseur.");

        await EnsureCanActAsSupervisorAsync(
            processedByUserId,
            request.CurrentAssignment.WeeklyPlanning.SubServiceId);

        if (!request.ProposedSwapUserId.HasValue)
            throw new InvalidOperationException("Demande sans partenaire — switch impossible.");

        var requesterAssignment = await _context.ShiftAssignments
            .FirstOrDefaultAsync(a => a.Id == request.CurrentAssignmentId)
            ?? throw new InvalidOperationException("Assignation source disparue — demande non applicable.");

        var swapAssignment = await _context.ShiftAssignments
            .FirstOrDefaultAsync(a =>
                a.WeeklyPlanningId == requesterAssignment.WeeklyPlanningId
                && a.UserId == request.ProposedSwapUserId.Value
                && a.AssignedDate == requesterAssignment.AssignedDate)
            ?? throw new InvalidOperationException("Assignation du collègue proposé introuvable.");

        var cfgA = requesterAssignment.SubServiceShiftConfigId;
        var breakA = requesterAssignment.BreakTime;
        var halfA = requesterAssignment.IsHalfDaySaturday;
        var slotA = requesterAssignment.SaturdaySlot;

        var configIds = new List<int>();
        if (requesterAssignment.SubServiceShiftConfigId is int idA)
            configIds.Add(idA);
        if (swapAssignment.SubServiceShiftConfigId is int idB)
            configIds.Add(idB);

        var configsById = await _context.SubServiceShiftConfigs
            .AsNoTracking()
            .Where(c => configIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id);

        requesterAssignment.SubServiceShiftConfigId = swapAssignment.SubServiceShiftConfigId;
        requesterAssignment.BreakTime = swapAssignment.BreakTime;
        requesterAssignment.IsHalfDaySaturday = swapAssignment.IsHalfDaySaturday;
        requesterAssignment.SaturdaySlot = swapAssignment.SaturdaySlot;
        requesterAssignment.IsManagerOverride = true;
        requesterAssignment.IsOnLeave = false;
        requesterAssignment.IsHoliday = false;
        requesterAssignment.ShiftModeProfileId =
            requesterAssignment.SubServiceShiftConfigId is int newCfgA
            && configsById.TryGetValue(newCfgA, out var cfgNewA)
                ? cfgNewA.ShiftModeProfileId
                : swapAssignment.ShiftModeProfileId;
        requesterAssignment.IsModeOverride = true;

        swapAssignment.SubServiceShiftConfigId = cfgA;
        swapAssignment.BreakTime = breakA;
        swapAssignment.IsHalfDaySaturday = halfA;
        swapAssignment.SaturdaySlot = slotA;
        swapAssignment.IsManagerOverride = true;
        swapAssignment.IsOnLeave = false;
        swapAssignment.IsHoliday = false;
        swapAssignment.ShiftModeProfileId =
            swapAssignment.SubServiceShiftConfigId is int newCfgB
            && configsById.TryGetValue(newCfgB, out var cfgNewB)
                ? cfgNewB.ShiftModeProfileId
                : null;
        swapAssignment.IsModeOverride = true;

        request.Status = PlanningChangeRequestStatus.Approved;
        request.SupervisorProcessedByUserId = processedByUserId;
        request.ProcessedByUserId = processedByUserId;
        request.ProcessedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var weekLabel = FormatWeekLabel(request.WeekCode);
        await NotifyUserAsync(
            request.RequesterUserId,
            request.WeekCode,
            $"Votre demande de switch ({weekLabel}) a été approuvée par le superviseur. Les créneaux ont été échangés.",
            "/mes-demandes-changement");

        if (request.ProposedSwapUserId is int partnerId)
        {
            await NotifyUserAsync(
                partnerId,
                request.WeekCode,
                $"Le switch ({weekLabel}) avec un collègue a été approuvé par le superviseur. Votre planning a été mis à jour.",
                "/mes-demandes-changement");
        }

        return await MapAsync(id) ?? throw new InvalidOperationException("Erreur approve.");
    }

    public async Task<PlanningChangeRequestDto> RejectAsync(
        int id, int processedByUserId, string? reason)
    {
        var request = await _context.PlanningChangeRequests
            .Include(r => r.CurrentAssignment).ThenInclude(a => a.WeeklyPlanning)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.Status != PlanningChangeRequestStatus.PendingSupervisor)
            throw new InvalidOperationException(
                "Seules les demandes en attente superviseur peuvent être refusées.");

        await EnsureCanActAsSupervisorAsync(
            processedByUserId,
            request.CurrentAssignment.WeeklyPlanning.SubServiceId);

        request.Status = PlanningChangeRequestStatus.Rejected;
        request.SupervisorProcessedByUserId = processedByUserId;
        request.ProcessedByUserId = processedByUserId;
        request.ProcessedAt = DateTime.UtcNow;
        request.RejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

        await _context.SaveChangesAsync();

        var weekLabel = FormatWeekLabel(request.WeekCode);
        var rejectMsg = string.IsNullOrWhiteSpace(request.RejectionReason)
            ? $"Votre demande de switch ({weekLabel}) a été refusée par le superviseur."
            : $"Votre demande de switch ({weekLabel}) a été refusée : {request.RejectionReason}";

        await NotifyUserAsync(
            request.RequesterUserId,
            request.WeekCode,
            rejectMsg,
            "/mes-demandes-changement");

        if (request.ProposedSwapUserId is int partnerId)
        {
            var partnerMsg = string.IsNullOrWhiteSpace(request.RejectionReason)
                ? $"Le switch ({weekLabel}) que vous aviez accepté a été refusé par le superviseur."
                : $"Le switch ({weekLabel}) que vous aviez accepté a été refusé par le superviseur : {request.RejectionReason}";
            await NotifyUserAsync(
                partnerId,
                request.WeekCode,
                partnerMsg,
                "/mes-demandes-changement");
        }

        return await MapAsync(id) ?? throw new InvalidOperationException("Erreur reject.");
    }

    public async Task<PlanningChangeRequestDto> CancelAsync(int id, int requesterUserId)
    {
        var request = await _context.PlanningChangeRequests.FindAsync(id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.RequesterUserId != requesterUserId)
            throw new InvalidOperationException("Annulation non autorisée.");

        if (request.Status != PlanningChangeRequestStatus.PendingPartner)
            throw new InvalidOperationException(
                "Annulation possible uniquement tant que le collègue n'a pas répondu.");

        request.Status = PlanningChangeRequestStatus.Cancelled;
        request.ProcessedByUserId = requesterUserId;
        request.ProcessedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return await MapAsync(id) ?? throw new InvalidOperationException("Erreur cancel.");
    }

    /// <summary>
    /// Deadline J-1 : veille du jour concerné à 23:59:59 Africa/Casablanca.
    /// </summary>
    public static void EnsureCreationDeadline(DateOnly assignmentDate, DateTime? utcNow = null)
    {
        var tz = ResolveCasablancaTimeZone();
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(utcNow ?? DateTime.UtcNow, tz);
        var deadlineLocal = assignmentDate.AddDays(-1).ToDateTime(new TimeOnly(23, 59, 59));

        if (nowLocal > deadlineLocal)
            throw new InvalidOperationException(
                "Délai dépassé : les demandes de switch doivent être créées au plus tard " +
                "la veille du jour concerné (23:59 Casablanca).");
    }

    private async Task EnsureCanActAsSupervisorAsync(int processedByUserId, int subServiceId)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.ManagedSubServices)
            .Include(u => u.ManagedServices)
            .FirstOrDefaultAsync(u => u.Id == processedByUserId)
            ?? throw new InvalidOperationException("Utilisateur introuvable.");

        if (IsRh(user))
            throw new InvalidOperationException(
                "Le RH consulte les demandes en lecture seule — validation réservée au superviseur.");

        if (IsAdmin(user))
            return;

        var roleName = user.Role?.Name;
        if (!KyntusRoleNames.IsSuperviseur(roleName)
            && !string.Equals(roleName, "Manager", StringComparison.OrdinalIgnoreCase)
            && !KyntusRoleNames.IsReferentTechnique(roleName)
            && !KyntusRoleNames.IsChefDeProjet(roleName))
        {
            throw new InvalidOperationException("Validation réservée au superviseur (ou Admin).");
        }

        var scoped = await GetManagedSubServiceIdsAsync(user);
        if (scoped.Count == 0)
            throw new InvalidOperationException("Aucun périmètre d'équipe configuré pour votre compte.");
        if (!scoped.Contains(subServiceId))
            throw new InvalidOperationException("Hors de votre périmètre d'équipe.");
    }

    private Task<HashSet<int>> GetManagedSubServiceIdsAsync(User manager) =>
        _perimeter.GetManagedSubServiceIdsAsync(manager);

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

    private async Task NotifySupervisorsForSubServiceAsync(
        int subServiceId, string weekCode, string message, string deepLink)
    {
        var subService = await _context.SubServices
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == subServiceId);
        if (subService == null) return;

        var viaSub = await _context.UserSubServices
            .AsNoTracking()
            .Where(us => us.SubServiceId == subServiceId)
            .Select(us => us.UserId)
            .ToListAsync();

        var viaService = await _context.UserManagedServices
            .AsNoTracking()
            .Where(ms => ms.ServiceId == subService.ServiceId)
            .Select(ms => ms.UserId)
            .ToListAsync();

        var managerIds = viaSub.Union(viaService).Distinct().ToList();
        var admins = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.IsActive && u.AuthUserId != null && u.Role != null
                        && u.Role.Name.ToLower() == "admin")
            .Select(u => u.Id)
            .ToListAsync();

        var recipientIds = managerIds.Union(admins).Distinct().ToList();
        var recipients = await _context.Users
            .AsNoTracking()
            .Where(u => recipientIds.Contains(u.Id) && u.AuthUserId != null && u.IsActive)
            .Select(u => new { u.Id, AuthUserId = u.AuthUserId!.Value })
            .ToListAsync();

        foreach (var r in recipients)
            await PersistAndPushAsync(r.Id, r.AuthUserId, weekCode, message, deepLink);
    }

    private async Task NotifyUserAsync(int planningUserId, string weekCode, string message, string deepLink)
    {
        var user = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == planningUserId && u.AuthUserId != null)
            .Select(u => new { u.Id, AuthUserId = u.AuthUserId!.Value })
            .FirstOrDefaultAsync();

        if (user is null) return;
        await PersistAndPushAsync(user.Id, user.AuthUserId, weekCode, message, deepLink);
    }

    private async Task PersistAndPushAsync(
        int userId,
        int authUserId,
        string weekCode,
        string message,
        string deepLink)
    {
        var notif = new PlanningNotification
        {
            UserId = userId,
            AuthUserId = authUserId,
            WeeklyPlanningId = null,
            WeekCode = weekCode,
            SubServiceName = ChangeRequestSubService,
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
                deepLink,
                createdAt = notif.CreatedAt,
                isRead = false
            });
    }

    /** Affichage FR dans les messages (API/DB restent en W). */
    private static string FormatWeekLabel(string? weekCode)
    {
        if (string.IsNullOrWhiteSpace(weekCode)) return "";
        return weekCode.Replace("-W", "-S", StringComparison.OrdinalIgnoreCase)
                       .Replace("-w", "-S", StringComparison.OrdinalIgnoreCase);
    }

    private static TimeZoneInfo ResolveCasablancaTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(CasablancaTz);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Morocco Standard Time");
        }
    }

    private async Task<PlanningChangeRequestDto?> MapAsync(int id, int? viewerUserId = null)
    {
        var r = await _context.PlanningChangeRequests
            .AsNoTracking()
            .Include(x => x.Requester)
            .Include(x => x.ProposedSwapUser)
            .Include(x => x.ProcessedBy)
            .Include(x => x.SupervisorProcessedBy)
            .Include(x => x.CurrentAssignment)
                .ThenInclude(a => a.SubServiceShiftConfig)
            .Include(x => x.CurrentAssignment)
                .ThenInclude(a => a.WeeklyPlanning)
                    .ThenInclude(p => p.SubService)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (r == null) return null;

        return new PlanningChangeRequestDto
        {
            Id = r.Id,
            WeekCode = r.WeekCode,
            RequesterUserId = r.RequesterUserId,
            RequesterName = r.Requester is null
                ? "—"
                : UserDisplayName.Format(r.Requester.FirstName, r.Requester.LastName),
            CurrentAssignmentId = r.CurrentAssignmentId,
            AssignmentDay = r.CurrentAssignment?.DayOfWeek.ToString() ?? "—",
            AssignmentDate = r.CurrentAssignment?.AssignedDate ?? default,
            ShiftLabel = r.CurrentAssignment?.SubServiceShiftConfig?.Label ?? "—",
            Reason = r.Reason,
            ProposedSwapUserId = r.ProposedSwapUserId,
            ProposedSwapUserName = r.ProposedSwapUser != null
                ? UserDisplayName.Format(r.ProposedSwapUser.FirstName, r.ProposedSwapUser.LastName)
                : null,
            Status = r.Status.ToString(),
            CreatedAt = r.CreatedAt,
            PartnerRespondedAt = r.PartnerRespondedAt,
            SupervisorProcessedByUserId = r.SupervisorProcessedByUserId,
            SupervisorProcessedByName = r.SupervisorProcessedBy != null
                ? UserDisplayName.Format(r.SupervisorProcessedBy.FirstName, r.SupervisorProcessedBy.LastName)
                : null,
            ProcessedByUserId = r.ProcessedByUserId,
            ProcessedByName = r.ProcessedBy != null
                ? UserDisplayName.Format(r.ProcessedBy.FirstName, r.ProcessedBy.LastName)
                : null,
            ProcessedAt = r.ProcessedAt,
            RejectionReason = r.RejectionReason,
            SubServiceId = r.CurrentAssignment?.WeeklyPlanning?.SubServiceId ?? 0,
            SubServiceName = r.CurrentAssignment?.WeeklyPlanning?.SubService?.Name ?? "—",
            WeeklyPlanningId = r.CurrentAssignment?.WeeklyPlanningId ?? 0,
            ViewerIsPartner = viewerUserId.HasValue && r.ProposedSwapUserId == viewerUserId.Value,
            ViewerIsRequester = viewerUserId.HasValue && r.RequesterUserId == viewerUserId.Value,
        };
    }
}
