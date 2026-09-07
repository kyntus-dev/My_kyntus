using System.Net;
using System.Text.Json;
using Auth.Application.Push;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces;
using Auth.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WebPush;

namespace Auth.Infrastructure.Services;

public sealed class PushNotificationService(
    AuthDbContext db,
    IConfiguration configuration,
    ILogger<PushNotificationService> logger) : IPushNotificationService
{
    public string? GetVapidPublicKey()
    {
        var key = configuration["Push:VapidPublicKey"];
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    public async Task RegisterAsync(
        int authUserId,
        string endpoint,
        string p256dh,
        string auth,
        string? userAgent,
        CancellationToken ct)
    {
        endpoint = (endpoint ?? string.Empty).Trim();
        p256dh = (p256dh ?? string.Empty).Trim();
        auth = (auth ?? string.Empty).Trim();
        if (authUserId <= 0 || endpoint.Length == 0 || p256dh.Length == 0 || auth.Length == 0)
            throw new ArgumentException("Abonnement push incomplet.");

        var now = DateTime.UtcNow;
        var existing = await db.PushSubscriptions
            .FirstOrDefaultAsync(s => s.Endpoint == endpoint, ct);
        if (existing is null)
        {
            db.PushSubscriptions.Add(new Auth.Domain.Entities.PushSubscription
            {
                Id = Guid.NewGuid(),
                AuthUserId = authUserId,
                Endpoint = endpoint,
                P256dh = p256dh,
                Auth = auth,
                UserAgent = Truncate(userAgent, 512),
                CreatedAt = now,
                LastSeenAt = now,
            });
        }
        else
        {
            existing.AuthUserId = authUserId;
            existing.P256dh = p256dh;
            existing.Auth = auth;
            existing.UserAgent = Truncate(userAgent, 512);
            existing.LastSeenAt = now;
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task UnregisterAsync(int authUserId, string endpoint, CancellationToken ct)
    {
        endpoint = (endpoint ?? string.Empty).Trim();
        if (authUserId <= 0 || endpoint.Length == 0)
            return;

        var rows = await db.PushSubscriptions
            .Where(s => s.AuthUserId == authUserId && s.Endpoint == endpoint)
            .ToListAsync(ct);
        if (rows.Count == 0)
            return;
        db.PushSubscriptions.RemoveRange(rows);
        await db.SaveChangesAsync(ct);
    }

    public async Task NotifyAsync(DesktopPushNotifyRequest request, CancellationToken ct)
    {
        var title = DesktopPushPayloadSanitizer.SanitizeTitle(request.Title);
        var body = DesktopPushPayloadSanitizer.SanitizeBody(request.Body);
        var tag = DesktopPushPayloadSanitizer.SanitizeTag(request.Tag);
        var url = DesktopPushPayloadSanitizer.SanitizeUrl(request.Url);

        var userIds = await ResolveUserIdsAsync(request, ct);
        if (userIds.Count == 0)
            return;

        var now = DateTime.UtcNow;
        foreach (var userId in userIds)
        {
            db.PushInboxItems.Add(new PushInboxItem
            {
                Id = Guid.NewGuid(),
                AuthUserId = userId,
                Title = title,
                Body = body,
                Tag = tag,
                Url = url,
                IsRead = false,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(ct);
        await TrimInboxAsync(userIds, ct);

        var vapid = TryCreateVapidDetails();
        if (vapid is null)
        {
            logger.LogWarning("Desktop push skipped: VAPID keys are not configured.");
            return;
        }

        var subscriptions = await db.PushSubscriptions
            .Where(s => userIds.Contains(s.AuthUserId))
            .ToListAsync(ct);
        if (subscriptions.Count == 0)
            return;

        var payload = JsonSerializer.Serialize(new { title, body, tag, url });
        var client = new WebPushClient();
        var stale = new List<Auth.Domain.Entities.PushSubscription>();

        foreach (var sub in subscriptions)
        {
            try
            {
                await client.SendNotificationAsync(
                    new WebPush.PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth),
                    payload,
                    vapid,
                    ct);
            }
            catch (WebPushException ex) when (ex.StatusCode is HttpStatusCode.Gone or HttpStatusCode.NotFound)
            {
                stale.Add(sub);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Web Push failed for AuthUserId={AuthUserId}", sub.AuthUserId);
            }
        }

        if (stale.Count > 0)
        {
            db.PushSubscriptions.RemoveRange(stale);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<PushInboxRecord>> ListInboxAsync(int authUserId, int take, CancellationToken ct)
    {
        var limit = Math.Clamp(take, 1, 100);
        return await db.PushInboxItems.AsNoTracking()
            .Where(x => x.AuthUserId == authUserId)
            .OrderByDescending(x => x.CreatedAt)
            .Take(limit)
            .Select(x => new PushInboxRecord(x.Id, x.Title, x.Body, x.Tag, x.Url, x.IsRead, x.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task MarkInboxReadAsync(int authUserId, Guid id, CancellationToken ct)
    {
        var row = await db.PushInboxItems
            .FirstOrDefaultAsync(x => x.Id == id && x.AuthUserId == authUserId, ct);
        if (row is null)
            return;
        row.IsRead = true;
        await db.SaveChangesAsync(ct);
    }

    private async Task<List<int>> ResolveUserIdsAsync(DesktopPushNotifyRequest request, CancellationToken ct)
    {
        var ids = request.AuthUserIds.Where(id => id > 0).Distinct().ToList();
        var subjects = request.SubjectIds.Where(g => g != Guid.Empty).Distinct().ToList();
        if (subjects.Count > 0)
        {
            var fromSubjects = await db.Users.AsNoTracking()
                .Where(u => subjects.Contains(u.SubjectId))
                .Select(u => u.Id)
                .ToListAsync(ct);
            ids.AddRange(fromSubjects);
        }

        return ids.Distinct().ToList();
    }

    private async Task TrimInboxAsync(IReadOnlyCollection<int> userIds, CancellationToken ct)
    {
        foreach (var userId in userIds)
        {
            var extra = await db.PushInboxItems
                .Where(x => x.AuthUserId == userId)
                .OrderByDescending(x => x.CreatedAt)
                .Skip(50)
                .ToListAsync(ct);
            if (extra.Count == 0)
                continue;
            db.PushInboxItems.RemoveRange(extra);
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync(ct);
    }

    private VapidDetails? TryCreateVapidDetails()
    {
        var publicKey = configuration["Push:VapidPublicKey"];
        var privateKey = configuration["Push:VapidPrivateKey"];
        var subject = configuration["Push:VapidSubject"];
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
            return null;
        if (string.IsNullOrWhiteSpace(subject))
            subject = "mailto:admin@kyntus.ma";
        return new VapidDetails(subject.Trim(), publicKey.Trim(), privateKey.Trim());
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
