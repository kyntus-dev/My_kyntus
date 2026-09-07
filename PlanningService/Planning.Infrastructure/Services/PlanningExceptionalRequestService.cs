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

public class PlanningExceptionalRequestService : IPlanningExceptionalRequestService
{
    public const int FreeApprovedLimit = 3;
    public const long MaxJustificationBytes = 5 * 1024 * 1024;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/jpeg",
        "image/jpg",
        "image/png"
    };

    private readonly AppDbContext _context;
    private readonly IHubContext<PlanningHub> _hubContext;
    private readonly ILogger<PlanningExceptionalRequestService> _logger;
    private readonly IPlanningPerimeterResolver _perimeter;
    private const string NotifSubService = "Demande exceptionnelle";

    public PlanningExceptionalRequestService(
        AppDbContext context,
        IHubContext<PlanningHub> hubContext,
        ILogger<PlanningExceptionalRequestService> logger,
        IPlanningPerimeterResolver perimeter)
    {
        _context = context;
        _hubContext = hubContext;
        _logger = logger;
        _perimeter = perimeter;
    }

    public async Task<PlanningExceptionalRequestDto> CreateAsync(
        int requesterUserId,
        DateOnly requestedDate,
        int requestedShiftTemplateId,
        string reason,
        Stream? justificationStream,
        string? justificationFileName,
        string? justificationContentType)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Le motif est obligatoire.");

        var requester = await _context.Users
            .Include(u => u.SubService)
            .FirstOrDefaultAsync(u => u.Id == requesterUserId && u.IsActive)
            ?? throw new InvalidOperationException("Utilisateur introuvable ou inactif.");

        if (requester.SubServiceId is not int subServiceId || subServiceId <= 0)
            throw new InvalidOperationException("Aucun sous-service assigné — demande impossible.");

        var settings = await GetAutoSettingsAsync();
        var window = ComputeTargetWeek(settings, DateTime.UtcNow);
        var match = window.AvailableWeeks
            .FirstOrDefault(w => requestedDate >= w.WeekStartDate && requestedDate <= w.WeekEndDate);

        if (match is null)
        {
            var labels = string.Join(", ", window.AvailableWeeks.Select(w => FormatWeekLabel(w.WeekCode)));
            throw new InvalidOperationException(
                labels.Length > 0
                    ? $"La date doit être dans une semaine cible ouverte ({labels})."
                    : "Aucune semaine cible ouverte pour les demandes exceptionnelles.");
        }

        EnsureCreationDeadline(settings, DateTime.UtcNow, match.WeekStartDate);

        if (requestedDate.DayOfWeek is DayOfWeek.Sunday)
            throw new InvalidOperationException("Les demandes exceptionnelles ne sont pas autorisées le dimanche.");

        var template = await _context.SubServiceShiftConfigs
            .FirstOrDefaultAsync(c =>
                c.Id == requestedShiftTemplateId
                && c.SubServiceId == subServiceId
                && c.IsTemplate)
            ?? throw new InvalidOperationException(
                "Shift invalide : choisissez un créneau configuré pour votre cellule.");

        var activeExists = await _context.PlanningExceptionalRequests.AnyAsync(r =>
            r.RequesterUserId == requesterUserId
            && r.RequestedDate == requestedDate
            && (r.Status == PlanningExceptionalRequestStatus.PendingSupervisor
                || r.Status == PlanningExceptionalRequestStatus.PendingRh
                || r.Status == PlanningExceptionalRequestStatus.Approved));

        if (activeExists)
            throw new InvalidOperationException(
                "Une demande active existe déjà pour cette date.");

        var quota = await GetQuotaAsync(requesterUserId, requestedDate.Year);
        var justificationRequired = quota.JustificationRequiredNext;

        byte[]? fileBytes = null;
        string? fileName = null;
        string? contentType = null;

        if (justificationRequired)
        {
            if (justificationStream == null || string.IsNullOrWhiteSpace(justificationFileName))
                throw new InvalidOperationException(
                    "Justificatif obligatoire à partir de la 4ᵉ demande approuvée de l'année (PDF, JPG ou PNG, max 5 Mo).");

            (fileBytes, fileName, contentType) = await ReadJustificationAsync(
                justificationStream, justificationFileName, justificationContentType);
        }
        else if (justificationStream != null && !string.IsNullOrWhiteSpace(justificationFileName))
        {
            (fileBytes, fileName, contentType) = await ReadJustificationAsync(
                justificationStream, justificationFileName, justificationContentType);
        }

        var entity = new PlanningExceptionalRequest
        {
            WeekCode = match.WeekCode,
            RequestedDate = requestedDate,
            RequesterUserId = requesterUserId,
            SubServiceId = subServiceId,
            RequestedShiftTemplateId = template.Id,
            Reason = reason.Trim(),
            Status = PlanningExceptionalRequestStatus.PendingSupervisor,
            CreatedAt = DateTime.UtcNow,
            JustificationRequired = justificationRequired,
            JustificationFileName = fileName,
            JustificationContentType = contentType,
            JustificationContent = fileBytes
        };

        _context.PlanningExceptionalRequests.Add(entity);
        await _context.SaveChangesAsync();

        var requesterName = DisplayName(requester, requesterUserId);
        try
        {
            await NotifySupervisorsForSubServiceAsync(
                subServiceId,
                entity.WeekCode,
                $"Demande exceptionnelle de {requesterName} ({requestedDate:dd/MM} — {template.Label}). À valider.",
                "/planning/exceptional-requests");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec notif superviseurs demande exceptionnelle {RequestId}", entity.Id);
        }

        return await MapAsync(entity.Id)
            ?? throw new InvalidOperationException("Erreur création demande exceptionnelle.");
    }

    public async Task<List<PlanningExceptionalRequestDto>> GetMyAsync(int requesterUserId)
    {
        var ids = await _context.PlanningExceptionalRequests
            .Where(r => r.RequesterUserId == requesterUserId)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Id)
            .ToListAsync();

        var result = new List<PlanningExceptionalRequestDto>();
        foreach (var id in ids)
        {
            try
            {
                var dto = await MapAsync(id, requesterUserId);
                if (dto != null) result.Add(dto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mapping demande exceptionnelle {RequestId} ignoré.", id);
            }
        }
        return result;
    }

    public async Task<List<PlanningExceptionalRequestDto>> GetAllAsync(
        string? status,
        string? weekCode,
        int? viewerUserId = null,
        int? requesterUserId = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var query = _context.PlanningExceptionalRequests.AsQueryable();

        if (!string.IsNullOrWhiteSpace(weekCode))
            query = query.Where(r => r.WeekCode == weekCode);
        else
        {
            if (from.HasValue)
                query = query.Where(r => r.RequestedDate >= from.Value);
            if (to.HasValue)
                query = query.Where(r => r.RequestedDate <= to.Value);
        }

        if (requesterUserId is > 0)
            query = query.Where(r => r.RequesterUserId == requesterUserId.Value);

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(r =>
                    r.Status == PlanningExceptionalRequestStatus.PendingSupervisor
                    || r.Status == PlanningExceptionalRequestStatus.PendingRh);
            }
            else if (Enum.TryParse<PlanningExceptionalRequestStatus>(status, true, out var st))
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
                    return new List<PlanningExceptionalRequestDto>();

                query = query.Where(r => scopedIds.Contains(r.SubServiceId));
            }
        }

        var ids = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => r.Id)
            .ToListAsync();

        var result = new List<PlanningExceptionalRequestDto>();
        foreach (var id in ids)
        {
            try
            {
                var dto = await MapAsync(id, viewerUserId);
                if (dto != null) result.Add(dto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Mapping demande exceptionnelle {RequestId} ignoré.", id);
            }
        }
        return result;
    }

    public async Task<ExceptionalRequestQuotaDto> GetQuotaAsync(int requesterUserId, int? year = null)
    {
        var y = year ?? DateTime.UtcNow.Year;
        var yearStart = new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd = new DateTime(y + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var approvedCount = await _context.PlanningExceptionalRequests.CountAsync(r =>
            r.RequesterUserId == requesterUserId
            && r.Status == PlanningExceptionalRequestStatus.Approved
            && r.CreatedAt >= yearStart
            && r.CreatedAt < yearEnd);

        var freeRemaining = Math.Max(0, FreeApprovedLimit - approvedCount);
        return new ExceptionalRequestQuotaDto
        {
            Year = y,
            ApprovedCount = approvedCount,
            FreeLimit = FreeApprovedLimit,
            FreeRemaining = freeRemaining,
            JustificationRequiredNext = approvedCount >= FreeApprovedLimit
        };
    }

    public async Task<List<ExceptionalShiftOptionDto>> GetAvailableShiftsAsync(int requesterUserId)
    {
        var requester = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == requesterUserId && u.IsActive)
            ?? throw new InvalidOperationException("Utilisateur introuvable.");

        if (requester.SubServiceId is not int subServiceId || subServiceId <= 0)
            return new List<ExceptionalShiftOptionDto>();

        return await _context.SubServiceShiftConfigs
            .Where(c => c.SubServiceId == subServiceId && c.IsTemplate)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.StartTime)
            .Select(c => new ExceptionalShiftOptionDto
            {
                Id = c.Id,
                Label = c.Label,
                StartTime = c.StartTime.ToString("HH:mm"),
                WorkHours = c.WorkHours,
                DisplayOrder = c.DisplayOrder
            })
            .ToListAsync();
    }

    public async Task<ExceptionalRequestTargetWeekDto> GetTargetWeekAsync(DateTime? utcNow = null)
    {
        var settings = await GetAutoSettingsAsync();
        return ComputeTargetWeek(settings, utcNow ?? DateTime.UtcNow);
    }

    public async Task<PlanningExceptionalRequestDto> SupervisorApproveAsync(int id, int processedByUserId)
    {
        var request = await _context.PlanningExceptionalRequests
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.Status != PlanningExceptionalRequestStatus.PendingSupervisor)
            throw new InvalidOperationException("Seules les demandes en attente superviseur peuvent être validées.");

        await EnsureCanActAsSupervisorAsync(processedByUserId, request.SubServiceId);

        request.Status = PlanningExceptionalRequestStatus.PendingRh;
        request.SupervisorProcessedByUserId = processedByUserId;
        request.SupervisorProcessedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var requester = await _context.Users.FindAsync(request.RequesterUserId);
        var name = DisplayName(requester, request.RequesterUserId);

        try
        {
            await NotifyRhAsync(
                request.WeekCode,
                $"Demande exceptionnelle de {name} ({request.RequestedDate:dd/MM}) validée par le superviseur — à valider RH.",
                "/planning/exceptional-requests");
            await NotifyUserAsync(
                request.RequesterUserId,
                request.WeekCode,
                $"Votre demande exceptionnelle ({request.RequestedDate:dd/MM}) a été validée par le superviseur — en attente RH.",
                "/mes-demandes-exceptionnelles");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Échec notif supervisor-approve {RequestId}", id);
        }

        return await MapAsync(id) ?? throw new InvalidOperationException("Erreur supervisor-approve.");
    }

    public async Task<PlanningExceptionalRequestDto> SupervisorRejectAsync(
        int id, int processedByUserId, string? reason)
    {
        var request = await _context.PlanningExceptionalRequests
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.Status != PlanningExceptionalRequestStatus.PendingSupervisor)
            throw new InvalidOperationException("Seules les demandes en attente superviseur peuvent être refusées.");

        await EnsureCanActAsSupervisorAsync(processedByUserId, request.SubServiceId);

        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Le motif de refus est obligatoire.");

        request.Status = PlanningExceptionalRequestStatus.Rejected;
        request.SupervisorProcessedByUserId = processedByUserId;
        request.SupervisorProcessedAt = DateTime.UtcNow;
        request.ProcessedByUserId = processedByUserId;
        request.ProcessedAt = DateTime.UtcNow;
        request.RejectionReason = reason.Trim();
        await _context.SaveChangesAsync();

        await NotifyUserAsync(
            request.RequesterUserId,
            request.WeekCode,
            $"Votre demande exceptionnelle ({request.RequestedDate:dd/MM}) a été refusée par le superviseur : {request.RejectionReason}",
            "/mes-demandes-exceptionnelles");

        return await MapAsync(id) ?? throw new InvalidOperationException("Erreur supervisor-reject.");
    }

    public async Task<PlanningExceptionalRequestDto> RhApproveAsync(int id, int processedByUserId)
    {
        var request = await _context.PlanningExceptionalRequests
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.Status != PlanningExceptionalRequestStatus.PendingRh)
            throw new InvalidOperationException("Seules les demandes en attente RH peuvent être validées.");

        await EnsureCanActAsRhAsync(processedByUserId);

        request.Status = PlanningExceptionalRequestStatus.Approved;
        request.RhProcessedByUserId = processedByUserId;
        request.RhProcessedAt = DateTime.UtcNow;
        request.ProcessedByUserId = processedByUserId;
        request.ProcessedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await NotifyUserAsync(
            request.RequesterUserId,
            request.WeekCode,
            $"Votre demande exceptionnelle ({request.RequestedDate:dd/MM}) a été approuvée par RH. Le shift sera affecté à la génération du planning.",
            "/mes-demandes-exceptionnelles");

        return await MapAsync(id) ?? throw new InvalidOperationException("Erreur rh-approve.");
    }

    public async Task<PlanningExceptionalRequestDto> RhRejectAsync(
        int id, int processedByUserId, string? reason)
    {
        var request = await _context.PlanningExceptionalRequests
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.Status != PlanningExceptionalRequestStatus.PendingRh)
            throw new InvalidOperationException("Seules les demandes en attente RH peuvent être refusées.");

        await EnsureCanActAsRhAsync(processedByUserId);

        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Le motif de refus est obligatoire.");

        request.Status = PlanningExceptionalRequestStatus.Rejected;
        request.RhProcessedByUserId = processedByUserId;
        request.RhProcessedAt = DateTime.UtcNow;
        request.ProcessedByUserId = processedByUserId;
        request.ProcessedAt = DateTime.UtcNow;
        request.RejectionReason = reason.Trim();
        await _context.SaveChangesAsync();

        await NotifyUserAsync(
            request.RequesterUserId,
            request.WeekCode,
            $"Votre demande exceptionnelle ({request.RequestedDate:dd/MM}) a été refusée par RH : {request.RejectionReason}",
            "/mes-demandes-exceptionnelles");

        return await MapAsync(id) ?? throw new InvalidOperationException("Erreur rh-reject.");
    }

    public async Task<PlanningExceptionalRequestDto> CancelAsync(int id, int requesterUserId)
    {
        var request = await _context.PlanningExceptionalRequests.FindAsync(id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        if (request.RequesterUserId != requesterUserId)
            throw new InvalidOperationException("Annulation non autorisée.");

        if (request.Status != PlanningExceptionalRequestStatus.PendingSupervisor)
            throw new InvalidOperationException(
                "Annulation possible uniquement tant que le superviseur n'a pas tranché.");

        request.Status = PlanningExceptionalRequestStatus.Cancelled;
        request.ProcessedByUserId = requesterUserId;
        request.ProcessedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return await MapAsync(id) ?? throw new InvalidOperationException("Erreur cancel.");
    }

    public async Task<(byte[] Content, string ContentType, string FileName)?> GetJustificationAsync(
        int id, int viewerUserId)
    {
        var request = await _context.PlanningExceptionalRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new InvalidOperationException("Demande introuvable.");

        var viewer = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.ManagedSubServices)
            .Include(u => u.ManagedServices)
            .FirstOrDefaultAsync(u => u.Id == viewerUserId)
            ?? throw new InvalidOperationException("Utilisateur introuvable.");

        var allowed = request.RequesterUserId == viewerUserId
                      || IsAdmin(viewer)
                      || IsRh(viewer);
        if (!allowed && !IsAdmin(viewer) && !IsRh(viewer))
        {
            var scoped = await GetManagedSubServiceIdsAsync(viewer);
            allowed = scoped.Contains(request.SubServiceId);
        }

        if (!allowed)
            throw new InvalidOperationException("Accès au justificatif non autorisé.");

        if (request.JustificationContent == null || request.JustificationContent.Length == 0)
            return null;

        return (
            request.JustificationContent,
            request.JustificationContentType ?? "application/octet-stream",
            request.JustificationFileName ?? "justificatif");
    }

    /// <summary>
    /// Deadline = veille du jour de génération auto (settings.DayOfWeek) à 23:59 TZ settings.
    /// Ne s'applique qu'à la semaine imminente (cible auto-génération) ;
    /// les semaines plus lointaines restent ouvrables (ex. planification à 2 mois).
    /// </summary>
    public static void EnsureCreationDeadline(
        PlanningAutoGenerateSettings settings,
        DateTime utcNow,
        DateOnly? requestedWeekMonday = null)
    {
        var tz = ResolveTimeZone(settings.TimeZone);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);
        var window = ComputeGenerationWindow(settings, DateOnly.FromDateTime(nowLocal), nowLocal);

        var imminentMonday = GetTargetMonday(settings.Target, window.GenerationDate);
        if (requestedWeekMonday is DateOnly reqMon && reqMon > imminentMonday)
            return;

        if (nowLocal > window.DeadlineLocal)
            throw new InvalidOperationException(
                $"Délai dépassé : les demandes pour la semaine imminente doivent être créées au plus tard " +
                $"la veille du jour de génération automatique ({window.DeadlineLocal:dddd dd/MM/yyyy HH:mm}). " +
                $"Vous pouvez encore demander pour des semaines ultérieures.");
    }

    public static DateTime ComputeDeadlineLocal(
        PlanningAutoGenerateSettings settings,
        DateOnly localToday)
    {
        var noon = localToday.ToDateTime(new TimeOnly(12, 0));
        return ComputeGenerationWindow(settings, localToday, noon).DeadlineLocal;
    }

    /// <summary>~2 mois de semaines sélectionnables à partir de la cible RH.</summary>
    public const int AvailableWeeksHorizon = 8;

    public static ExceptionalRequestTargetWeekDto ComputeTargetWeek(
        PlanningAutoGenerateSettings settings,
        DateTime utcNow)
    {
        var tz = ResolveTimeZone(settings.TimeZone);
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), tz);
        var localToday = DateOnly.FromDateTime(nowLocal);
        var genWindow = ComputeGenerationWindow(settings, localToday, nowLocal);
        var deadlinePassed = nowLocal > genWindow.DeadlineLocal;

        var preferredKind = string.Equals(settings.Target, "CurrentWeek", StringComparison.OrdinalIgnoreCase)
            ? "CurrentWeek"
            : "NextWeek";

        var preferredMonday = GetTargetMonday(preferredKind, genWindow.GenerationDate);
        var currentMonday = GetTargetMonday("CurrentWeek", genWindow.GenerationDate);

        // Avant deadline : dès la semaine en cours (ou cible). Après deadline : semaines strictement après la cible imminente.
        DateOnly startMonday;
        if (deadlinePassed)
            startMonday = preferredMonday.AddDays(7);
        else
            startMonday = currentMonday <= preferredMonday ? currentMonday : preferredMonday;

        var options = new List<ExceptionalRequestWeekOptionDto>();
        for (var i = 0; i < AvailableWeeksHorizon; i++)
        {
            var monday = startMonday.AddDays(7 * i);
            var code = FormatWeekCode(monday);
            var kind = monday == currentMonday ? "CurrentWeek"
                : monday == preferredMonday ? preferredKind
                : "Horizon";

            options.Add(new ExceptionalRequestWeekOptionDto
            {
                WeekCode = code,
                WeekStartDate = monday,
                WeekEndDate = monday.AddDays(6),
                Kind = kind,
                IsPreferred = !deadlinePassed && monday == preferredMonday
                    || deadlinePassed && i == 0
            });
        }

        var preferred = options.FirstOrDefault(o => o.IsPreferred) ?? options.FirstOrDefault();
        var preferredStart = preferred?.WeekStartDate ?? preferredMonday;
        var preferredCode = preferred?.WeekCode ?? FormatWeekCode(preferredStart);

        return new ExceptionalRequestTargetWeekDto
        {
            WeekCode = preferredCode,
            WeekStartDate = preferredStart,
            WeekEndDate = preferredStart.AddDays(6),
            DeadlineLocal = DateTime.SpecifyKind(genWindow.DeadlineLocal, DateTimeKind.Unspecified),
            DeadlinePassed = deadlinePassed,
            AvailableWeeks = options
        };
    }

    /// <summary>
    /// Fenêtre de la prochaine génération.
    /// Deadline = veille du prochain jour de génération (settings) à 23:59.
    /// Le jour de génération lui-même : deadline déjà passée → création bloquée.
    /// </summary>
    public static (DateTime DeadlineLocal, DateOnly GenerationDate) ComputeGenerationWindow(
        PlanningAutoGenerateSettings settings,
        DateOnly localToday,
        DateTime nowLocal)
    {
        _ = nowLocal;
        var genDay = settings.DayOfWeek;
        var todayDow = (int)localToday.DayOfWeek;
        var daysUntilGen = (genDay - todayDow + 7) % 7;
        var genDate = localToday.AddDays(daysUntilGen);
        var deadline = genDate.AddDays(-1).ToDateTime(new TimeOnly(23, 59, 59));
        return (deadline, genDate);
    }

    private static DateOnly GetTargetMonday(string target, DateOnly generationLocalDate)
    {
        var diff = ((int)generationLocalDate.DayOfWeek + 6) % 7;
        var currentMonday = generationLocalDate.AddDays(-diff);
        return string.Equals(target, "CurrentWeek", StringComparison.OrdinalIgnoreCase)
            ? currentMonday
            : currentMonday.AddDays(7);
    }

    private static string FormatWeekCode(DateOnly monday)
    {
        var dt = monday.ToDateTime(TimeOnly.MinValue);
        var week = ISOWeek.GetWeekOfYear(dt);
        var year = ISOWeek.GetYear(dt);
        return $"{year}-W{week:D2}";
    }

    private async Task<PlanningAutoGenerateSettings> GetAutoSettingsAsync()
    {
        var settings = await _context.PlanningAutoGenerateSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == PlanningAutoGenerateSettings.SingletonId);

        return settings ?? new PlanningAutoGenerateSettings();
    }

    private static async Task<(byte[] Bytes, string FileName, string ContentType)> ReadJustificationAsync(
        Stream stream, string fileName, string? contentType)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            throw new InvalidOperationException("Nom de fichier invalide.");

        var ext = Path.GetExtension(safeName).ToLowerInvariant();
        if (ext is not (".pdf" or ".jpg" or ".jpeg" or ".png"))
            throw new InvalidOperationException("Formats acceptés : PDF, JPG, PNG.");

        var ct = string.IsNullOrWhiteSpace(contentType)
            ? ext switch
            {
                ".pdf" => "application/pdf",
                ".png" => "image/png",
                _ => "image/jpeg"
            }
            : contentType.Trim();

        if (!AllowedContentTypes.Contains(ct) && ext is ".jpg" or ".jpeg")
            ct = "image/jpeg";
        if (!AllowedContentTypes.Contains(ct) && !AllowedContentTypes.Contains(ct.Split(';')[0].Trim()))
            throw new InvalidOperationException("Type de fichier non autorisé.");

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        if (ms.Length == 0)
            throw new InvalidOperationException("Fichier vide.");
        if (ms.Length > MaxJustificationBytes)
            throw new InvalidOperationException("Justificatif trop volumineux (max 5 Mo).");

        return (ms.ToArray(), safeName, ct.Split(';')[0].Trim());
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
                "Le RH valide à l'étape RH — validation superviseur réservée au superviseur.");

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

    private async Task EnsureCanActAsRhAsync(int processedByUserId)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == processedByUserId)
            ?? throw new InvalidOperationException("Utilisateur introuvable.");

        if (IsAdmin(user) || IsRh(user))
            return;

        throw new InvalidOperationException("Validation RH réservée au RH (ou Admin).");
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

    private static string FormatWeekLabel(string? weekCode)
    {
        if (string.IsNullOrWhiteSpace(weekCode)) return "";
        return weekCode.Replace("-W", "-S", StringComparison.OrdinalIgnoreCase)
                       .Replace("-w", "-S", StringComparison.OrdinalIgnoreCase);
    }

    private static TimeZoneInfo ResolveTimeZone(string? tzId)
    {
        var id = string.IsNullOrWhiteSpace(tzId) ? "Africa/Casablanca" : tzId;
        try
        {
            if (string.Equals(id, "Africa/Casablanca", StringComparison.OrdinalIgnoreCase)
                && OperatingSystem.IsWindows())
                return TimeZoneInfo.FindSystemTimeZoneById("Morocco Standard Time");
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById("Morocco Standard Time"); }
            catch { return TimeZoneInfo.Utc; }
        }
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

    private async Task NotifyRhAsync(string weekCode, string message, string deepLink)
    {
        var recipients = await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Where(u => u.IsActive && u.AuthUserId != null && u.Role != null
                        && (u.Role.Name.ToLower() == "rh" || u.Role.Name.ToLower() == "admin"))
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
        int userId, int authUserId, string weekCode, string message, string deepLink)
    {
        var notif = new PlanningNotification
        {
            UserId = userId,
            AuthUserId = authUserId,
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

    private async Task<PlanningExceptionalRequestDto?> MapAsync(int id, int? viewerUserId = null)
    {
        var r = await _context.PlanningExceptionalRequests
            .AsNoTracking()
            .Include(x => x.Requester)
            .Include(x => x.SubService)
            .Include(x => x.RequestedShiftTemplate)
            .Include(x => x.SupervisorProcessedBy)
            .Include(x => x.RhProcessedBy)
            .Include(x => x.ProcessedBy)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (r == null) return null;

        return new PlanningExceptionalRequestDto
        {
            Id = r.Id,
            WeekCode = r.WeekCode,
            RequestedDate = r.RequestedDate,
            RequesterUserId = r.RequesterUserId,
            RequesterName = r.Requester is null
                ? "—"
                : UserDisplayName.Format(r.Requester.FirstName, r.Requester.LastName),
            SubServiceId = r.SubServiceId,
            SubServiceName = r.SubService?.Name ?? "—",
            RequestedShiftTemplateId = r.RequestedShiftTemplateId,
            ShiftLabel = r.RequestedShiftTemplate?.Label ?? "(shift retiré)",
            ShiftStartTime = r.RequestedShiftTemplate?.StartTime.ToString("HH:mm") ?? string.Empty,
            Reason = r.Reason,
            Status = r.Status.ToString(),
            CreatedAt = r.CreatedAt,
            JustificationRequired = r.JustificationRequired,
            HasJustification = r.JustificationContent != null && r.JustificationContent.Length > 0,
            JustificationFileName = r.JustificationFileName,
            SupervisorProcessedByUserId = r.SupervisorProcessedByUserId,
            SupervisorProcessedByName = r.SupervisorProcessedBy != null
                ? UserDisplayName.Format(r.SupervisorProcessedBy.FirstName, r.SupervisorProcessedBy.LastName)
                : null,
            SupervisorProcessedAt = r.SupervisorProcessedAt,
            RhProcessedByUserId = r.RhProcessedByUserId,
            RhProcessedByName = r.RhProcessedBy != null
                ? UserDisplayName.Format(r.RhProcessedBy.FirstName, r.RhProcessedBy.LastName)
                : null,
            RhProcessedAt = r.RhProcessedAt,
            ProcessedByUserId = r.ProcessedByUserId,
            ProcessedByName = r.ProcessedBy != null
                ? UserDisplayName.Format(r.ProcessedBy.FirstName, r.ProcessedBy.LastName)
                : null,
            ProcessedAt = r.ProcessedAt,
            RejectionReason = r.RejectionReason,
            ViewerIsRequester = viewerUserId.HasValue && r.RequesterUserId == viewerUserId.Value
        };
    }
}
