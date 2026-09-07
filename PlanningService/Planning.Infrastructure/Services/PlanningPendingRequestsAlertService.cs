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

public class PlanningPendingRequestsAlertService : IPlanningPendingRequestsAlertService
{
    private readonly AppDbContext _context;
    private readonly IHubContext<PlanningHub> _hubContext;
    private readonly ILogger<PlanningPendingRequestsAlertService> _logger;
    private readonly IPlanningPerimeterResolver _perimeter;

    private const string NotifJ1 = "Alertes demandes";
    private const string NotifValidation = "Validation plannings";

    public PlanningPendingRequestsAlertService(
        AppDbContext context,
        IHubContext<PlanningHub> hubContext,
        ILogger<PlanningPendingRequestsAlertService> logger,
        IPlanningPerimeterResolver perimeter)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger;
        _perimeter = perimeter;
    }

    public async Task<PendingRequestsSummaryDto> GetSummaryAsync(int? viewerUserId = null, int maxItems = 50)
    {
        var rows = await LoadPendingRowsAsync();
        var scoped = await FilterByViewerAsync(rows, viewerUserId);
        return MapSummary(scoped, maxItems);
    }

    public async Task<bool> SendJ1RemindersAsync(DateOnly localDate)
    {
        var settings = await EnsureSettingsAsync();
        if (settings.LastPendingJ1ReminderDate == localDate)
            return false;

        var rows = await LoadPendingRowsAsync();
        settings.LastPendingJ1ReminderDate = localDate;
        await _context.SaveChangesAsync();

        if (rows.Count == 0)
        {
            _logger.LogInformation("J-1 pending reminder: aucun pending — date {Date} marquée.", localDate);
            return false;
        }

        var global = MapSummary(rows, maxItems: 0);
        var weekHint = rows
            .Select(r => r.WeekCode)
            .Distinct()
            .OrderBy(w => w)
            .FirstOrDefault() ?? "";

        var rhMsg =
            $"{global.ChangePendingCount} demande(s) de changement et {global.ExceptionalPendingCount} demande(s) exceptionnelle(s) non traitées — génération demain.";
        await NotifyRhAdminAsync(weekHint, rhMsg, "/planning/change-requests", NotifJ1);

        var bySub = rows.GroupBy(r => r.SubServiceId).ToList();
        var supervisorIds = await ResolveSupervisorIdsForSubServicesAsync(
            bySub.Select(g => g.Key).ToList());

        foreach (var (userId, authUserId, managedSubs) in supervisorIds)
        {
            var scoped = rows.Where(r => managedSubs.Contains(r.SubServiceId)).ToList();
            if (scoped.Count == 0) continue;
            var s = MapSummary(scoped, 0);
            var msg =
                $"{s.ChangePendingCount} switch + {s.ExceptionalPendingCount} exceptionnelle(s) non traitées dans votre périmètre — génération demain.";
            await PersistAndPushAsync(userId, authUserId, weekHint, msg, "/planning/change-requests", NotifJ1);
        }

        _logger.LogInformation(
            "J-1 pending reminder envoyé: change={C} exceptional={E}",
            global.ChangePendingCount, global.ExceptionalPendingCount);
        return true;
    }

    public async Task<bool> SendValidationRemindersAsync(string weekCode)
    {
        if (string.IsNullOrWhiteSpace(weekCode))
            return false;

        var settings = await EnsureSettingsAsync();
        if (string.Equals(settings.LastValidationReminderWeekCode, weekCode, StringComparison.OrdinalIgnoreCase))
            return false;

        var rows = await LoadPendingRowsAsync();
        settings.LastValidationReminderWeekCode = weekCode;
        await _context.SaveChangesAsync();

        if (rows.Count == 0)
        {
            _logger.LogInformation("Validation reminder {Week}: aucun pending.", weekCode);
            return false;
        }

        var global = MapSummary(rows, 0);
        var msg =
            $"Des demandes restent ouvertes (switch : {global.ChangePendingCount}, exceptionnel : {global.ExceptionalPendingCount}) avant validation de la semaine {weekCode.Replace("-W", "-S")}.";
        await NotifyRhAdminAsync(weekCode, msg, "/planning/validation", NotifValidation);

        _logger.LogInformation(
            "Validation reminder {Week}: change={C} exceptional={E}",
            weekCode, global.ChangePendingCount, global.ExceptionalPendingCount);
        return true;
    }

    private async Task<List<PendingRow>> LoadPendingRowsAsync()
    {
        var change = await _context.PlanningChangeRequests
            .AsNoTracking()
            .Include(r => r.Requester)
            .Include(r => r.CurrentAssignment)
                .ThenInclude(a => a.WeeklyPlanning)
                    .ThenInclude(p => p.SubService)
            .Where(r =>
                r.Status == PlanningChangeRequestStatus.PendingPartner
                || r.Status == PlanningChangeRequestStatus.PendingSupervisor)
            .ToListAsync();

        var exceptional = await _context.PlanningExceptionalRequests
            .AsNoTracking()
            .Include(r => r.Requester)
            .Include(r => r.SubService)
            .Where(r =>
                r.Status == PlanningExceptionalRequestStatus.PendingSupervisor
                || r.Status == PlanningExceptionalRequestStatus.PendingRh)
            .ToListAsync();

        var rows = new List<PendingRow>();
        foreach (var r in change)
        {
            var subId = r.CurrentAssignment?.WeeklyPlanning?.SubServiceId ?? 0;
            if (subId <= 0) continue;
            rows.Add(new PendingRow
            {
                Id = r.Id,
                Type = "Change",
                WeekCode = r.WeekCode,
                SubServiceId = subId,
                SubServiceName = r.CurrentAssignment?.WeeklyPlanning?.SubService?.Name ?? "",
                Status = r.Status.ToString(),
                RequesterName = DisplayName(r.Requester, r.RequesterUserId),
                CreatedAt = r.CreatedAt
            });
        }

        foreach (var r in exceptional)
        {
            rows.Add(new PendingRow
            {
                Id = r.Id,
                Type = "Exceptional",
                WeekCode = r.WeekCode,
                SubServiceId = r.SubServiceId,
                SubServiceName = r.SubService?.Name ?? "",
                Status = r.Status.ToString(),
                RequesterName = DisplayName(r.Requester, r.RequesterUserId),
                CreatedAt = r.CreatedAt
            });
        }

        return rows;
    }

    private async Task<List<PendingRow>> FilterByViewerAsync(List<PendingRow> rows, int? viewerUserId)
    {
        if (viewerUserId is not > 0) return rows;

        var viewer = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.ManagedSubServices)
            .Include(u => u.ManagedServices)
            .FirstOrDefaultAsync(u => u.Id == viewerUserId.Value);

        if (viewer == null) return new List<PendingRow>();
        if (IsAdmin(viewer) || IsRh(viewer)) return rows;

        var scoped = await GetManagedSubServiceIdsAsync(viewer);
        if (scoped.Count == 0) return new List<PendingRow>();
        return rows.Where(r => scoped.Contains(r.SubServiceId)).ToList();
    }

    private static PendingRequestsSummaryDto MapSummary(List<PendingRow> rows, int maxItems)
    {
        var dto = new PendingRequestsSummaryDto
        {
            ChangePendingCount = rows.Count(r => r.Type == "Change"),
            ExceptionalPendingCount = rows.Count(r => r.Type == "Exceptional"),
            ChangePendingPartner = rows.Count(r => r.Type == "Change" && r.Status == nameof(PlanningChangeRequestStatus.PendingPartner)),
            ChangePendingSupervisor = rows.Count(r => r.Type == "Change" && r.Status == nameof(PlanningChangeRequestStatus.PendingSupervisor)),
            ExceptionalPendingSupervisor = rows.Count(r => r.Type == "Exceptional" && r.Status == nameof(PlanningExceptionalRequestStatus.PendingSupervisor)),
            ExceptionalPendingRh = rows.Count(r => r.Type == "Exceptional" && r.Status == nameof(PlanningExceptionalRequestStatus.PendingRh)),
        };

        if (maxItems > 0)
        {
            dto.Items = rows
                .OrderByDescending(r => r.CreatedAt)
                .Take(maxItems)
                .Select(r => new PendingRequestItemDto
                {
                    Id = r.Id,
                    Type = r.Type,
                    WeekCode = r.WeekCode,
                    SubServiceId = r.SubServiceId,
                    SubServiceName = r.SubServiceName,
                    Status = r.Status,
                    RequesterName = r.RequesterName,
                    CreatedAt = r.CreatedAt
                })
                .ToList();
        }

        return dto;
    }

    private async Task NotifyRhAdminAsync(string weekCode, string message, string deepLink, string label)
    {
        var recipients = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.IsActive && u.AuthUserId != null && u.Role != null
                        && (u.Role.Name.ToLower() == "rh" || u.Role.Name.ToLower() == "admin"))
            .Select(u => new { u.Id, AuthUserId = u.AuthUserId!.Value })
            .ToListAsync();

        foreach (var r in recipients)
            await PersistAndPushAsync(r.Id, r.AuthUserId, weekCode, message, deepLink, label);
    }

    /// <summary>
    /// Superviseurs / managers (hors RH/Admin) ayant au moins une cellule parmi les subServices.
    /// </summary>
    private async Task<List<(int UserId, int AuthUserId, HashSet<int> ManagedSubs)>> ResolveSupervisorIdsForSubServicesAsync(
        List<int> subServiceIds)
    {
        if (subServiceIds.Count == 0)
            return new List<(int, int, HashSet<int>)>();

        var serviceIds = await _context.SubServices
            .AsNoTracking()
            .Where(s => subServiceIds.Contains(s.Id))
            .Select(s => new { s.Id, s.ServiceId })
            .ToListAsync();

        var viaSub = await _context.UserSubServices
            .AsNoTracking()
            .Where(us => subServiceIds.Contains(us.SubServiceId))
            .Select(us => new { us.UserId, us.SubServiceId })
            .ToListAsync();

        var svcIds = serviceIds.Select(s => s.ServiceId).Distinct().ToList();
        var viaService = await _context.UserManagedServices
            .AsNoTracking()
            .Where(ms => svcIds.Contains(ms.ServiceId))
            .Select(ms => new { ms.UserId, ms.ServiceId })
            .ToListAsync();

        var candidateIds = viaSub.Select(x => x.UserId)
            .Union(viaService.Select(x => x.UserId))
            .Distinct()
            .ToList();

        var users = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.ManagedSubServices)
            .Include(u => u.ManagedServices)
            .Where(u => candidateIds.Contains(u.Id) && u.IsActive && u.AuthUserId != null)
            .ToListAsync();

        var result = new List<(int, int, HashSet<int>)>();
        foreach (var u in users)
        {
            if (IsAdmin(u) || IsRh(u)) continue;
            var role = u.Role?.Name ?? "";
            if (!KyntusRoleNames.IsSuperviseur(role)
                && !KyntusRoleNames.IsSupportManager(role)
                && !KyntusRoleNames.IsReferentTechnique(role)
                && !KyntusRoleNames.IsChefDeProjet(role))
                continue;

            var managed = await GetManagedSubServiceIdsAsync(u);
            managed.IntersectWith(subServiceIds);
            if (managed.Count == 0) continue;
            result.Add((u.Id, u.AuthUserId!.Value, managed));
        }

        return result;
    }

    private async Task PersistAndPushAsync(
        int userId, int authUserId, string weekCode, string message, string deepLink, string label)
    {
        var notif = new PlanningNotification
        {
            UserId = userId,
            AuthUserId = authUserId,
            WeeklyPlanningId = null,
            WeekCode = weekCode,
            SubServiceName = label,
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
                deepLink,
                createdAt = notif.CreatedAt
            });
    }

    private async Task<PlanningAutoGenerateSettings> EnsureSettingsAsync()
    {
        var settings = await _context.PlanningAutoGenerateSettings
            .FirstOrDefaultAsync(s => s.Id == PlanningAutoGenerateSettings.SingletonId);
        if (settings != null) return settings;
        settings = new PlanningAutoGenerateSettings();
        _context.PlanningAutoGenerateSettings.Add(settings);
        await _context.SaveChangesAsync();
        return settings;
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

    private sealed class PendingRow
    {
        public int Id { get; set; }
        public string Type { get; set; } = "";
        public string WeekCode { get; set; } = "";
        public int SubServiceId { get; set; }
        public string SubServiceName { get; set; } = "";
        public string Status { get; set; } = "";
        public string RequesterName { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
