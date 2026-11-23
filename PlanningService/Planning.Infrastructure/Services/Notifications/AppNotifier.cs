using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;
using Planning.Domain.Entities;
using Planning.Infrastructure.Hubs;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.Notifications;

/// <summary>
/// v62 — Cloche de l'application : notification persistée (table PlanningNotifications, lue même
/// hors connexion), poussée en temps réel (SignalR, groupe user_{AuthUserId}, même canal que la
/// publication des plannings) et liée à la page concernée.
/// </summary>
public sealed class AppNotifier(
    AppDbContext db,
    IHubContext<PlanningHub> hub,
    ILogger<AppNotifier> logger) : IAppNotifier
{
    public const string PrefixeCle = "NOTIF-";

    public async Task<int> NotifierAsync(
        NotificationEmailRecipients destinataires,
        AppNotification notification,
        IReadOnlyCollection<string>? rolesExclus = null,
        CancellationToken ct = default)
    {
        if (destinataires.IsEmpty || string.IsNullOrWhiteSpace(notification.Message))
            return 0;
        var ajoutees = new List<PlanningNotification>();
        try
        {
            var comptes = await ResoudreAsync(destinataires, rolesExclus, ct);
            if (comptes.Count == 0)
                return 0;

            var cle = Couper(PrefixeCle + (string.IsNullOrWhiteSpace(notification.Cle)
                ? notification.Categorie.Trim().ToUpperInvariant()
                : notification.Cle.Trim()), 64);
            var authIds = comptes.Select(c => c.AuthUserId).ToList();
            var existantes = string.IsNullOrWhiteSpace(notification.Cle)
                ? new Dictionary<int, PlanningNotification>()
                : (await db.PlanningNotifications
                        .Where(n => authIds.Contains(n.AuthUserId) && n.WeekCode == cle && !n.IsRead)
                        .ToListAsync(ct))
                    .GroupBy(n => n.AuthUserId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.CreatedAt).First());

            var maintenant = DateTime.UtcNow;
            var aPousser = new List<PlanningNotification>();
            foreach (var (userId, authUserId) in comptes)
            {
                if (!existantes.TryGetValue(authUserId, out var n))
                {
                    n = new PlanningNotification { UserId = userId, AuthUserId = authUserId, WeekCode = cle };
                    db.PlanningNotifications.Add(n);
                    ajoutees.Add(n);
                }
                n.SubServiceName = Couper(notification.Titre, 200);
                n.Message = notification.Message.Trim();
                n.Lien = Couper(LienSur(notification.Lien), 300);
                n.Categorie = Couper(notification.Categorie.Trim().ToLowerInvariant(), 40);
                n.CreatedAt = maintenant;
                aPousser.Add(n);
            }
            await db.SaveChangesAsync(ct);
            ajoutees.Clear();

            foreach (var n in aPousser)
            {
                try
                {
                    await hub.Clients.Group($"user_{n.AuthUserId}").SendAsync("PlanningPublished", new
                    {
                        id = n.Id,
                        weekCode = n.WeekCode,
                        subServiceName = n.SubServiceName,
                        message = n.Message,
                        weeklyPlanningId = (int?)null,
                        deepLink = n.Lien,
                        categorie = n.Categorie,
                        createdAt = n.CreatedAt,
                        isRead = false,
                    }, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Le temps réel est un plus : la notification reste lisible depuis la cloche.
                    logger.LogDebug(ex, "Notification {Id} non poussée en temps réel.", n.Id);
                }
            }
            return aPousser.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Rien ne doit rester en attente d'enregistrement dans le contexte de l'action appelante.
            foreach (var n in ajoutees)
                db.Entry(n).State = EntityState.Detached;
            logger.LogWarning(ex, "Notification « {Titre} » non envoyée.", notification.Titre);
            return 0;
        }
    }

    /// <summary>Lien interne uniquement (commence par « / ») : jamais d'adresse externe dans la cloche.</summary>
    public static string LienSur(string? lien)
    {
        var l = (lien ?? string.Empty).Trim();
        return l.StartsWith('/') && !l.StartsWith("//", StringComparison.Ordinal) ? l : "/notifications";
    }

    private async Task<List<(int UserId, int AuthUserId)>> ResoudreAsync(
        NotificationEmailRecipients r, IReadOnlyCollection<string>? rolesExclus, CancellationToken ct)
    {
        var ids = r.UserIds?.Where(id => id > 0).Distinct().ToList() ?? [];
        var guids = r.EmployeeIds?.Where(g => g != Guid.Empty).Distinct().ToList() ?? [];
        var authIds = r.AuthUserIds?.Where(id => id > 0).Distinct().ToList() ?? [];
        var roles = r.Roles?.Select(x => x.Trim().ToLower()).Where(x => x.Length > 0).Distinct().ToList() ?? [];
        var exclus = rolesExclus?.Select(x => x.Trim().ToLower()).Where(x => x.Length > 0).ToList() ?? [];

        var comptes = await db.Users.AsNoTracking()
            .Where(u => u.IsActive && u.AuthUserId != null)
            .Where(u => ids.Contains(u.Id)
                        || guids.Contains(u.Guid)
                        || authIds.Contains(u.AuthUserId!.Value)
                        || (u.Role != null && roles.Contains(u.Role.Name.ToLower())))
            .Select(u => new { u.Id, AuthUserId = u.AuthUserId!.Value, Role = u.Role != null ? u.Role.Name : null })
            .ToListAsync(ct);
        return comptes
            .Where(c => c.Role is null || !exclus.Contains(c.Role.ToLower()))
            .GroupBy(c => c.AuthUserId)
            .Select(g => (g.First().Id, g.Key))
            .ToList();
    }

    private static string Couper(string? texte, int max)
    {
        var t = (texte ?? string.Empty).Trim();
        return t.Length <= max ? t : t[..max];
    }
}
