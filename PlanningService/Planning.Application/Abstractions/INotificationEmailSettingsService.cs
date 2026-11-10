using Planning.Application.NotificationEmail;

namespace Planning.Application.Abstractions;

/// <summary>Préférence « notifications par e-mail » de chaque utilisateur et état de la file d'envoi.</summary>
public interface INotificationEmailSettingsService
{
    Task<MyNotificationEmailPreferenceDto?> GetMineAsync(NotificationEmailViewer viewer, CancellationToken ct = default);

    Task<MyNotificationEmailPreferenceDto?> SetMineAsync(NotificationEmailViewer viewer, bool emailEnabled, CancellationToken ct = default);

    Task<NotificationEmailStatusDto> GetStatusAsync(CancellationToken ct = default);

    /// <summary>Remet en file les e-mails abandonnés des 7 derniers jours (après correction du serveur mail).</summary>
    Task<int> RetryFailedAsync(CancellationToken ct = default);
}
