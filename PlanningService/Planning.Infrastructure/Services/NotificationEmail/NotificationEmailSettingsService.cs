using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;
using Planning.Application.PilotPlanning;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.NotificationEmail;

public sealed class NotificationEmailSettingsService(
    AppDbContext db,
    IPlanningEmailReadiness readiness,
    IOptions<NotificationEmailOptions> options) : INotificationEmailSettingsService
{
    public async Task<MyNotificationEmailPreferenceDto?> GetMineAsync(NotificationEmailViewer viewer, CancellationToken ct = default)
    {
        var user = await FindUserAsync(viewer, ct);
        if (user is null)
            return null;
        var pref = await db.NotificationEmailPreferences.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        return await ToDtoAsync(user, pref?.EmailEnabled ?? true, ct);
    }

    public async Task<MyNotificationEmailPreferenceDto?> SetMineAsync(
        NotificationEmailViewer viewer,
        bool emailEnabled,
        CancellationToken ct = default)
    {
        var user = await FindUserAsync(viewer, ct);
        if (user is null)
            return null;

        var pref = await db.NotificationEmailPreferences.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (pref is null)
        {
            pref = new NotificationEmailPreference { UserId = user.Id };
            db.NotificationEmailPreferences.Add(pref);
        }
        pref.EmailEnabled = emailEnabled;
        pref.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(user, emailEnabled, ct);
    }

    public async Task<NotificationEmailStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var dayAgo = now.AddHours(-24);
        var weekAgo = now.AddDays(-7);
        var opts = options.Value;

        var failures = await db.EmailOutboxMessages.AsNoTracking()
            .Where(m => m.Status == EmailOutboxStatus.Failed && m.CreatedAt >= weekAgo)
            .OrderByDescending(m => m.Id)
            .Take(10)
            .Select(m => new NotificationEmailFailureDto
            {
                Id = m.Id,
                To = m.ToEmail,
                Category = m.Category,
                Subject = m.Subject,
                Error = m.LastError,
                Attempts = m.Attempts,
                CreatedAt = m.CreatedAt,
            })
            .ToListAsync(ct);

        return new NotificationEmailStatusDto
        {
            Enabled = opts.Enabled,
            MailConfigured = await readiness.IsReadyAsync(ct),
            MaxPerMinute = opts.EffectiveMaxPerMinute,
            PublicLinkConfigured = NotificationEmailComposer.BuildLink(opts.PublicAppUrl, "/") is not null,
            Pending = await db.EmailOutboxMessages.CountAsync(m => m.Status == EmailOutboxStatus.Pending, ct),
            SentLast24h = await db.EmailOutboxMessages
                .CountAsync(m => m.Status == EmailOutboxStatus.Sent && m.SentAt >= dayAgo, ct),
            FailedLast7Days = await db.EmailOutboxMessages
                .CountAsync(m => m.Status == EmailOutboxStatus.Failed && m.CreatedAt >= weekAgo, ct),
            LastSentAt = await db.EmailOutboxMessages
                .Where(m => m.Status == EmailOutboxStatus.Sent)
                .MaxAsync(m => m.SentAt, ct),
            RecentFailures = failures,
        };
    }

    public async Task<int> RetryFailedAsync(CancellationToken ct = default)
    {
        var weekAgo = DateTime.UtcNow.AddDays(-7);
        var failed = await db.EmailOutboxMessages
            .Where(m => m.Status == EmailOutboxStatus.Failed && m.CreatedAt >= weekAgo)
            .ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var m in failed)
        {
            m.Status = EmailOutboxStatus.Pending;
            m.Attempts = 0;
            m.NextAttemptAt = now;
            // Repart pour un nouveau délai d'expiration.
            m.CreatedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return failed.Count;
    }

    private async Task<MyNotificationEmailPreferenceDto> ToDtoAsync(User user, bool enabled, CancellationToken ct) => new()
    {
        EmailEnabled = enabled,
        EmailAddress = PilotPlanningEmailComposer.IsValidPersonalEmail(user.Email) ? user.Email.Trim() : null,
        MailConfigured = options.Value.Enabled && await readiness.IsReadyAsync(ct),
    };

    private async Task<User?> FindUserAsync(NotificationEmailViewer viewer, CancellationToken ct)
    {
        if (viewer.AuthUserId is int authId && authId > 0)
        {
            var byAuth = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.AuthUserId == authId, ct);
            if (byAuth is not null)
                return byAuth;
        }
        if (viewer.SubjectId is Guid subject && subject != Guid.Empty)
        {
            var bySubject = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Guid == subject, ct);
            if (bySubject is not null)
                return bySubject;
        }
        var email = viewer.Email?.Trim().ToLower();
        if (string.IsNullOrEmpty(email))
            return null;
        return await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);
    }
}
