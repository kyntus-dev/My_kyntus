using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Kyntus.Messaging.Contracts;
using Planning.Application.Abstractions;
using Planning.Domain.Enums;
using Planning.Application.NotificationEmail;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;

namespace Planning.Infrastructure.Services.NotificationEmail;

/// <summary>
/// Deux tâches, appelées en boucle par <see cref="NotificationEmailHostedService"/> :
/// 1. convertir les nouvelles notifications stockées (Planning, Formation, demandes, contrats) en e-mails ;
/// 2. envoyer les e-mails en attente, sans dépasser la cadence autorisée, avec nouvelles tentatives.
/// </summary>
public sealed class EmailOutboxProcessor(
    AppDbContext db,
    INotificationEmailQueue queue,
    IPlanningEmailSender sender,
    IOptions<NotificationEmailOptions> options,
    ILogger<EmailOutboxProcessor> logger)
{
    public const int MaxAttempts = 6;
    private const int HarvestBatch = 500;
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(3),
    ];

    private static readonly string[] ContractRecipientRoles = ["RH", "Admin", "Manager"];

    /// <summary>Convertit les notifications créées depuis le dernier passage. Renvoie le nombre d'e-mails mis en file.</summary>
    public async Task<int> HarvestAsync(CancellationToken ct = default)
    {
        var cursor = await db.NotificationEmailCursors
            .FirstOrDefaultAsync(c => c.Id == NotificationEmailCursor.SingletonId, ct);
        if (cursor is null)
        {
            // Premier démarrage : on part des notifications existantes, l'historique n'est pas envoyé.
            db.NotificationEmailCursors.Add(new NotificationEmailCursor
            {
                LastPlanningNotificationId = await db.PlanningNotifications.MaxAsync(n => (int?)n.Id, ct) ?? 0,
                LastContractNotificationId = await db.ContractNotifications.MaxAsync(n => (int?)n.Id, ct) ?? 0,
            });
            await db.SaveChangesAsync(ct);
            return 0;
        }

        // Envoi coupé ou non configuré : on avance quand même le curseur, pour ne pas envoyer
        // d'un coup des notifications anciennes le jour où le serveur mail sera renseigné.
        var enabled = await queue.IsEnabledAsync(ct);
        var queued = 0;

        var planningNotifications = await db.PlanningNotifications.AsNoTracking()
            .Where(n => n.Id > cursor.LastPlanningNotificationId)
            .OrderBy(n => n.Id)
            .Take(HarvestBatch)
            .ToListAsync(ct);
        // Planning encore en brouillon : les agents ne reçoivent rien avant la validation du
        // superviseur ; la publication leur envoie ensuite leur planning complet par e-mail.
        var planningIds = planningNotifications.Where(n => n.WeeklyPlanningId != null)
            .Select(n => n.WeeklyPlanningId!.Value).Distinct().ToList();
        var brouillons = planningIds.Count == 0
            ? new HashSet<int>()
            : (await db.WeeklyPlannings.AsNoTracking()
                .Where(p => planningIds.Contains(p.Id) && p.Status == PlanningStatus.Draft)
                .Select(p => p.Id).ToListAsync(ct)).ToHashSet();
        var destinataires = planningNotifications.Where(n => n.WeeklyPlanningId is int id && brouillons.Contains(id))
            .Select(n => n.UserId).Distinct().ToList();
        var roles = destinataires.Count == 0
            ? new Dictionary<int, string>()
            : await db.Users.AsNoTracking().Where(u => destinataires.Contains(u.Id))
                .Select(u => new { u.Id, Role = u.Role.Name }).ToDictionaryAsync(x => x.Id, x => x.Role, ct);

        foreach (var n in planningNotifications)
        {
            var retenu = n.WeeklyPlanningId is int pid && brouillons.Contains(pid)
                         && EstAgent(roles.GetValueOrDefault(n.UserId));
            if (enabled && !retenu && !PlanningNotificationEmailRules.IsCoveredByPlanningEmail(n.WeeklyPlanningId, n.Message))
            {
                var (source, title) = PlanningNotificationEmailRules.Classify(n.WeekCode, n.SubServiceName);
                queued += await queue.EnqueueNotificationAsync(
                    new NotificationEmailRecipients(UserIds: [n.UserId]),
                    new NotificationEmailContent(source, title, n.Message, DedupKey: $"pn:{n.Id}"),
                    ct);
            }
            cursor.LastPlanningNotificationId = n.Id;
        }

        var contractNotifications = await db.ContractNotifications.AsNoTracking()
            .Where(n => n.Id > cursor.LastContractNotificationId)
            .OrderBy(n => n.Id)
            .Take(HarvestBatch)
            .ToListAsync(ct);
        foreach (var n in contractNotifications)
        {
            if (enabled)
            {
                queued += await queue.EnqueueNotificationAsync(
                    new NotificationEmailRecipients(Roles: ContractRecipientRoles),
                    new NotificationEmailContent(
                        NotificationEmailSources.Contract,
                        $"Contrat — {ContractTypeLabel(n.Type)}",
                        n.Message,
                        Route: $"/contracts/{n.ContractId}",
                        DedupKey: $"cn:{n.Id}"),
                    ct);
            }
            cursor.LastContractNotificationId = n.Id;
        }

        if (planningNotifications.Count > 0 || contractNotifications.Count > 0)
        {
            cursor.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return queued;
    }

    /// <summary>Agent (pilote ou cellule IT) : destinataire du planning, pas son valideur.</summary>
    internal static bool EstAgent(string? role) =>
        KyntusRoleNames.IsPilote(role) || KyntusRoleNames.IsEquipeIt(role);

    /// <summary>Envoie les e-mails échus dans la limite de la cadence. Renvoie le nombre d'e-mails envoyés.</summary>
    public async Task<int> SendDueAsync(CancellationToken ct = default)
    {
        var opts = options.Value;
        var now = DateTime.UtcNow;

        await ExpireStaleAsync(now, opts.ExpireAfterHours, ct);
        if (!opts.Enabled)
            return 0;

        var sentLastMinute = await db.EmailOutboxMessages
            .CountAsync(m => m.Status == EmailOutboxStatus.Sent && m.SentAt > now.AddMinutes(-1), ct);
        var budget = opts.EffectiveMaxPerMinute - sentLastMinute;
        if (budget <= 0)
            return 0;

        var due = await db.EmailOutboxMessages
            .Where(m => m.Status == EmailOutboxStatus.Pending && m.NextAttemptAt <= now)
            .OrderBy(m => m.NextAttemptAt)
            .ThenBy(m => m.Id)
            .Take(budget)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var message in due)
        {
            try
            {
                await sender.SendRequiredHtmlAsync(message.ToEmail, message.Subject, message.HtmlBody, ct);
                message.Status = EmailOutboxStatus.Sent;
                message.SentAt = DateTime.UtcNow;
                message.LastError = null;
                sent++;
            }
            catch (MailNotConfiguredException)
            {
                // Rien n'est consommé : les e-mails partiront quand l'envoi sera configuré (ou expireront).
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                RecordFailure(message, ex, DateTime.UtcNow);
                logger.LogWarning(
                    "E-mail {Id} vers {To} non envoyé (tentative {Attempt}) : {Error}",
                    message.Id, message.ToEmail, message.Attempts, message.LastError);
                if (IsThrottling(ex))
                    break; // le serveur demande de ralentir : on reprendra au prochain passage
            }
        }

        await db.SaveChangesAsync(ct);
        return sent;
    }

    public static void RecordFailure(EmailOutboxMessage message, Exception ex, DateTime now)
    {
        message.Attempts++;
        var error = ex.Message.Trim();
        message.LastError = error.Length <= 1000 ? error : error[..1000];
        if (message.Attempts >= MaxAttempts)
        {
            message.Status = EmailOutboxStatus.Failed;
            return;
        }
        message.NextAttemptAt = now + Backoff[Math.Min(message.Attempts - 1, Backoff.Length - 1)];
    }

    public static bool IsThrottling(Exception ex)
    {
        var text = ex.Message;
        return text.Contains("4.3.2", StringComparison.Ordinal)
               || text.Contains("4.7.0", StringComparison.Ordinal)
               || text.Contains("throttl", StringComparison.OrdinalIgnoreCase)
               || text.Contains("too many", StringComparison.OrdinalIgnoreCase)
               || text.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
               || text.Contains("429", StringComparison.Ordinal);
    }

    private async Task ExpireStaleAsync(DateTime now, int expireAfterHours, CancellationToken ct)
    {
        var limit = now.AddHours(-Math.Max(1, expireAfterHours));
        var stale = await db.EmailOutboxMessages
            .Where(m => m.Status == EmailOutboxStatus.Pending && m.CreatedAt < limit)
            .Take(500)
            .ToListAsync(ct);
        if (stale.Count == 0)
            return;
        foreach (var m in stale)
        {
            m.Status = EmailOutboxStatus.Skipped;
            m.LastError ??= "Non envoyé dans le délai prévu.";
        }
        await db.SaveChangesAsync(ct);
        logger.LogWarning("{Count} e-mail(s) abandonné(s) : non envoyés après {Hours} h.", stale.Count, expireAfterHours);
    }

    private static string ContractTypeLabel(NotificationType type) => type switch
    {
        NotificationType.MiParcoursCDD => "mi-parcours CDD",
        NotificationType.MiParcoursPeriodeEssai => "mi-parcours période d'essai",
        NotificationType.AvantFinCDD => "fin de CDD proche",
        NotificationType.AvantFinPeriodeEssai => "fin de période d'essai proche",
        NotificationType.AvantFinStage => "fin de stage proche",
        NotificationType.AvantFinInterim => "fin d'intérim proche",
        _ => "alerte",
    };
}
