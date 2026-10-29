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
    ILogger<NotificationEmailRequestedConsumer> logger) : IConsumer<NotificationEmailRequestedMessage>
{
    public async Task Consume(ConsumeContext<NotificationEmailRequestedMessage> context)
    {
        var msg = context.Message;
        if (string.IsNullOrWhiteSpace(msg.Body) || msg.RequestId == Guid.Empty)
        {
            logger.LogWarning("Demande d'e-mail de notification incomplète ({Source}).", msg.Source);
            return;
        }

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
}
