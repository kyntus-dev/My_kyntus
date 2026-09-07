namespace Auth.Application.DTOs;

public sealed class PushSubscriptionRequestDto
{
    public string Endpoint { get; set; } = string.Empty;
    public string P256dh { get; set; } = string.Empty;
    public string Auth { get; set; } = string.Empty;
    public string? UserAgent { get; set; }
}

public sealed class UnregisterPushSubscriptionDto
{
    public string Endpoint { get; set; } = string.Empty;
}

public sealed class DesktopPushNotifyDto
{
    public int? AuthUserId { get; set; }
    public Guid? SubjectId { get; set; }
    public List<int>? AuthUserIds { get; set; }
    public List<Guid>? SubjectIds { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Tag { get; set; } = "kyntus";
    public string Url { get; set; } = "/";
}

public sealed class PushInboxEntryDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Tag { get; set; } = string.Empty;
    public string Url { get; set; } = "/";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class VapidPublicKeyDto
{
    public string PublicKey { get; set; } = string.Empty;
}
