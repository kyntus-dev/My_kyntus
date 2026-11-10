using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;
using Planning.Application.PilotPlanning;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.NotificationEmail;

public sealed class NotificationEmailQueue(
    AppDbContext db,
    IPlanningEmailReadiness readiness,
    IOptions<NotificationEmailOptions> options,
    ILogger<NotificationEmailQueue> logger) : INotificationEmailQueue
{
    private const int MaxCategoryLength = 40;
    private const int MaxDedupLength = 200;

    public async Task<bool> IsEnabledAsync(CancellationToken ct = default) =>
        options.Value.Enabled && await readiness.IsReadyAsync(ct);

    public async Task<int> EnqueueNotificationAsync(
        NotificationEmailRecipients recipients,
        NotificationEmailContent content,
        CancellationToken ct = default)
    {
        if (recipients.IsEmpty || string.IsNullOrWhiteSpace(content.Body))
            return 0;
        if (!await IsEnabledAsync(ct))
            return 0;

        var users = await ResolveRecipientsAsync(recipients, ct);
        if (users.Count == 0)
            return 0;

        var userIds = users.Select(u => u.Id).ToList();
        var optedOut = await db.NotificationEmailPreferences.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId) && !p.EmailEnabled)
            .Select(p => p.UserId)
            .ToListAsync(ct);
        var optedOutSet = optedOut.ToHashSet();

        var link = NotificationEmailComposer.BuildLink(options.Value.PublicAppUrl, content.Route);
        var subject = NotificationEmailComposer.Subject(content.Title);
        var category = Truncate(content.Source.Trim().ToLowerInvariant(), MaxCategoryLength);

        var candidates = new List<EmailOutboxMessage>();
        foreach (var user in users)
        {
            if (optedOutSet.Contains(user.Id))
                continue;
            if (!PilotPlanningEmailComposer.IsValidPersonalEmail(user.Email))
            {
                logger.LogInformation("Notification e-mail ignorée : utilisateur {UserId} sans adresse professionnelle valide.", user.Id);
                continue;
            }

            candidates.Add(new EmailOutboxMessage
            {
                ToEmail = user.Email.Trim(),
                RecipientUserId = user.Id,
                Category = category,
                Subject = subject,
                HtmlBody = NotificationEmailComposer.BuildHtml(user.FirstName, content.Source, content.Title, content.Body, link),
                DedupKey = DedupKey(content.DedupKey, user.Id),
            });
        }

        return await AddNewAsync(candidates, ct);
    }

    public async Task<bool> EnqueueDirectAsync(
        int recipientUserId,
        string toEmail,
        string category,
        string subject,
        string htmlBody,
        string? dedupKey = null,
        CancellationToken ct = default)
    {
        if (!PilotPlanningEmailComposer.IsValidPersonalEmail(toEmail))
            return false;
        if (!await IsEnabledAsync(ct))
            return false;

        var added = await AddNewAsync(
        [
            new EmailOutboxMessage
            {
                ToEmail = toEmail.Trim(),
                RecipientUserId = recipientUserId,
                Category = Truncate(category.Trim().ToLowerInvariant(), MaxCategoryLength),
                Subject = Truncate(subject.Trim(), NotificationEmailComposer.MaxSubjectLength),
                HtmlBody = htmlBody,
                DedupKey = dedupKey is null ? null : Truncate(dedupKey, MaxDedupLength),
            },
        ], ct);
        return added > 0;
    }

    private async Task<List<User>> ResolveRecipientsAsync(NotificationEmailRecipients r, CancellationToken ct)
    {
        var ids = r.UserIds?.Where(id => id > 0).Distinct().ToList() ?? [];
        var guids = r.EmployeeIds?.Where(g => g != Guid.Empty).Distinct().ToList() ?? [];
        var authIds = r.AuthUserIds?.Where(id => id > 0).Distinct().ToList() ?? [];
        var roles = r.Roles?
            .Select(x => x.Trim().ToLower())
            .Where(x => x.Length > 0)
            .Distinct()
            .ToList() ?? [];

        var query = db.Users.AsNoTracking().Where(u => u.IsActive);
        return await query
            .Where(u => ids.Contains(u.Id)
                        || guids.Contains(u.Guid)
                        || (u.AuthUserId != null && authIds.Contains(u.AuthUserId.Value))
                        || (u.Role != null && roles.Contains(u.Role.Name.ToLower())))
            .OrderBy(u => u.Id)
            .ToListAsync(ct);
    }

    /// <summary>Ajoute les e-mails dont la clé d'idempotence n'existe pas encore.</summary>
    private async Task<int> AddNewAsync(List<EmailOutboxMessage> candidates, CancellationToken ct)
    {
        if (candidates.Count == 0)
            return 0;

        var keys = candidates.Where(c => c.DedupKey != null).Select(c => c.DedupKey!).ToList();
        if (keys.Count > 0)
        {
            var existing = await db.EmailOutboxMessages.AsNoTracking()
                .Where(m => m.DedupKey != null && keys.Contains(m.DedupKey))
                .Select(m => m.DedupKey!)
                .ToListAsync(ct);
            var seen = existing.ToHashSet(StringComparer.Ordinal);
            candidates = candidates
                .Where(c => c.DedupKey is null || seen.Add(c.DedupKey))
                .ToList();
        }

        if (candidates.Count == 0)
            return 0;

        db.EmailOutboxMessages.AddRange(candidates);
        try
        {
            await db.SaveChangesAsync(ct);
            return candidates.Count;
        }
        catch (DbUpdateException ex)
        {
            // Événement rejoué en parallèle : la clé unique a déjà été insérée ailleurs.
            foreach (var c in candidates)
                db.Entry(c).State = EntityState.Detached;
            logger.LogWarning(ex, "Mise en file e-mail ignorée (doublon concurrent probable).");
            return 0;
        }
    }

    private static string? DedupKey(string? eventKey, int userId) =>
        string.IsNullOrWhiteSpace(eventKey) ? null : Truncate($"{eventKey.Trim()}:u{userId}", MaxDedupLength);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
