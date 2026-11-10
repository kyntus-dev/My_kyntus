namespace Planning.Application.NotificationEmail;

/// <summary>Identité de l'utilisateur connecté, telle que lue dans le jeton.</summary>
public sealed record NotificationEmailViewer(int? AuthUserId, Guid? SubjectId, string? Email);

public sealed class MyNotificationEmailPreferenceDto
{
    /// <summary>Recevoir aussi ses notifications par e-mail.</summary>
    public bool EmailEnabled { get; set; } = true;
    /// <summary>Adresse professionnelle utilisée (null si le compte n'en a pas de valide).</summary>
    public string? EmailAddress { get; set; }
    /// <summary>Envoi d'e-mails configuré sur le serveur.</summary>
    public bool MailConfigured { get; set; }
}

public sealed class UpdateNotificationEmailPreferenceRequest
{
    public bool EmailEnabled { get; set; } = true;
}

public sealed class NotificationEmailFailureDto
{
    public long Id { get; set; }
    public string To { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string? Error { get; set; }
    public int Attempts { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>État de la file d'envoi, pour l'écran de configuration (Admin / RH).</summary>
public sealed class NotificationEmailStatusDto
{
    public bool Enabled { get; set; }
    public bool MailConfigured { get; set; }
    public int MaxPerMinute { get; set; }
    public bool PublicLinkConfigured { get; set; }
    public int Pending { get; set; }
    public int SentLast24h { get; set; }
    public int FailedLast7Days { get; set; }
    public DateTime? LastSentAt { get; set; }
    public IReadOnlyList<NotificationEmailFailureDto> RecentFailures { get; set; } = [];
}
