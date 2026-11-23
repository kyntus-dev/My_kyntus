using Kyntus.Messaging.Contracts;
using MassTransit;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;

namespace Planning.Infrastructure.Messaging.Consumers;

/// <summary>
/// Notification d'un autre module (Prime, Parrainage, Documentation…) à doubler par e-mail.
/// </summary>
public sealed class NotificationEmailRequestedConsumer(
    INotificationEmailQueue queue,
    ILogger<NotificationEmailRequestedConsumer> logger,
    IAppNotifier? notifier = null) : IConsumer<NotificationEmailRequestedMessage>
{
    public async Task Consume(ConsumeContext<NotificationEmailRequestedMessage> context)
    {
        var msg = context.Message;
        if (string.IsNullOrWhiteSpace(msg.Body) || msg.RequestId == Guid.Empty)
        {
            logger.LogWarning("Demande d'e-mail de notification incomplète ({Source}).", msg.Source);
            return;
        }

        // v62 : demande de document traitée (validée, refusée, disponible, annulée) → aussi dans la cloche
        // du collaborateur, avec lien vers le suivi de ses demandes. Les pilotes la voient déjà dans la
        // cloche Documentation ; RH et Admin y voient déjà les demandes en attente.
        if (notifier is not null && DocumentPourCollaborateur(msg))
            await notifier.NotifierAsync(
                new NotificationEmailRecipients(EmployeeIds: msg.EmployeeIds, AuthUserIds: msg.AuthUserIds),
                new AppNotification("documentation",
                    string.IsNullOrWhiteSpace(msg.Title) ? "Demande de document" : msg.Title.Trim(),
                    msg.Body.Trim(),
                    LienDocument(msg.Route),
                    $"DOC-{msg.RequestId:N}"),
                rolesExclus: ["Pilote"],
                ct: context.CancellationToken);

        var queued = await queue.EnqueueNotificationAsync(
            new NotificationEmailRecipients(
                EmployeeIds: msg.EmployeeIds,
                AuthUserIds: msg.AuthUserIds,
                Roles: msg.Roles),
            new NotificationEmailContent(
                string.IsNullOrWhiteSpace(msg.Source) ? "notification" : msg.Source,
                string.IsNullOrWhiteSpace(msg.Title) ? NotificationEmailSources.Label(msg.Source) : msg.Title,
                msg.Body,
                msg.Route,
                DedupKey: $"req:{msg.RequestId:N}"),
            context.CancellationToken);

        logger.LogDebug("Notification {Source} : {Count} e-mail(s) mis en file.", msg.Source, queued);
    }

    /// <summary>v62 : message Documentation adressé à des collaborateurs nommés (pas à un rôle).</summary>
    public static bool DocumentPourCollaborateur(NotificationEmailRequestedMessage msg) =>
        string.Equals(msg.Source?.Trim(), "documentation", StringComparison.OrdinalIgnoreCase)
        && (msg.EmployeeIds.Count > 0 || msg.AuthUserIds.Count > 0);

    /// <summary>v62 : « /documentation » → suivi des demandes du collaborateur.</summary>
    public static string LienDocument(string? route)
    {
        var r = (route ?? string.Empty).Trim().TrimEnd('/');
        return r.Length == 0 || string.Equals(r, "/documentation", StringComparison.OrdinalIgnoreCase)
            ? "/documentation/tracking"
            : r;
    }
}
