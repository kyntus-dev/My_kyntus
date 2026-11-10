using Planning.Application.NotificationEmail;

namespace Planning.Application.Abstractions;

/// <summary>
/// Met les e-mails en file d'envoi (table EmailOutboxMessages). L'envoi réel est fait en arrière-plan,
/// à cadence limitée, avec nouvelles tentatives.
/// </summary>
public interface INotificationEmailQueue
{
    /// <summary>Envoi actif (option non coupée) et moyen d'envoi configuré (SMTP ou clé API).</summary>
    Task<bool> IsEnabledAsync(CancellationToken ct = default);

    /// <summary>
    /// Notification de l'application, doublée par e-mail sur l'adresse professionnelle de chaque
    /// destinataire actif, sauf s'il a désactivé ces e-mails. Renvoie le nombre d'e-mails mis en file.
    /// </summary>
    Task<int> EnqueueNotificationAsync(
        NotificationEmailRecipients recipients,
        NotificationEmailContent content,
        CancellationToken ct = default);

    /// <summary>E-mail déjà composé (planning de l'agent) : toujours envoyé, sans tenir compte de la préférence.</summary>
    Task<bool> EnqueueDirectAsync(
        int recipientUserId,
        string toEmail,
        string category,
        string subject,
        string htmlBody,
        string? dedupKey = null,
        CancellationToken ct = default);
}
