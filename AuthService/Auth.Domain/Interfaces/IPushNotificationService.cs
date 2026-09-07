namespace Auth.Domain.Interfaces;

public sealed record DesktopPushNotifyRequest(
    IReadOnlyList<int> AuthUserIds,
    IReadOnlyList<Guid> SubjectIds,
    string Title,
    string Body,
    string Tag,
    string Url);

public sealed record PushInboxRecord(
    Guid Id,
    string Title,
    string Body,
    string Tag,
    string Url,
    bool IsRead,
    DateTime CreatedAt);

public interface IPushNotificationService
{
    string? GetVapidPublicKey();
    Task RegisterAsync(int authUserId, string endpoint, string p256dh, string auth, string? userAgent, CancellationToken ct);
    Task UnregisterAsync(int authUserId, string endpoint, CancellationToken ct);
    Task NotifyAsync(DesktopPushNotifyRequest request, CancellationToken ct);
    Task<IReadOnlyList<PushInboxRecord>> ListInboxAsync(int authUserId, int take, CancellationToken ct);
    Task MarkInboxReadAsync(int authUserId, Guid id, CancellationToken ct);
}
