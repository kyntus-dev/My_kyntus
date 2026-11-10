namespace Planning.Application.NotificationEmail;

/// <summary>Section de configuration « NotificationEmail » (variables KYNTUS_NOTIFICATION_EMAIL_* / KYNTUS_PUBLIC_APP_URL).</summary>
public sealed class NotificationEmailOptions
{
    public const string SectionName = "NotificationEmail";

    /// <summary>Coupe tous les e-mails (plannings et notifications) sans toucher à la configuration SMTP.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// E-mails envoyés par minute au maximum. Microsoft 365 limite une boîte à 30 messages par minute :
    /// 25 laisse une marge pour les autres envois de la même boîte.
    /// </summary>
    public int MaxPerMinute { get; set; } = 25;

    /// <summary>Adresse de l'application pour le bouton « Ouvrir dans My Kyntus » (ex. http://10.10.10.25:8200).</summary>
    public string? PublicAppUrl { get; set; }

    /// <summary>Un e-mail non envoyé après ce délai est abandonné (pas d'envoi de notifications périmées).</summary>
    public int ExpireAfterHours { get; set; } = 72;

    public int EffectiveMaxPerMinute => Math.Clamp(MaxPerMinute, 1, 600);
}
