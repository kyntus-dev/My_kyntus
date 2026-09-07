namespace Planning.Domain.Entities;

/// <summary>Singleton — paramètres de génération automatique des plannings.</summary>
public class PlanningAutoGenerateSettings
{
    public const string SingletonId = "default";

    public string Id { get; set; } = SingletonId;
    public bool Enabled { get; set; } = true;
    /// <summary>0=Sunday … 4=Thursday … 6=Saturday</summary>
    public int DayOfWeek { get; set; } = (int)System.DayOfWeek.Thursday;
    public int HourLocal { get; set; } = 6;
    public int MinuteLocal { get; set; } = 0;
    public string TimeZone { get; set; } = "Africa/Casablanca";
    /// <summary>NextWeek | CurrentWeek</summary>
    public string Target { get; set; } = "NextWeek";
    public DateTime? LastRunAt { get; set; }
    public string? LastRunWeekCode { get; set; }
    /// <summary>Date locale du dernier rappel J-1 demandes pending (anti-doublon).</summary>
    public DateOnly? LastPendingJ1ReminderDate { get; set; }
    /// <summary>WeekCode du dernier rappel RH phase validation.</summary>
    public string? LastValidationReminderWeekCode { get; set; }
    /// <summary>Adresse RH « De : » des mails planning (prioritaire sur Smtp:From si valide).</summary>
    public string? SenderFromEmail { get; set; }
    /// <summary>Nom affiché optionnel (ex. Kyntus Planning).</summary>
    public string? SenderDisplayName { get; set; }
    /// <summary>Hôte SMTP (prioritaire sur Smtp:Host env si renseigné).</summary>
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
    public bool SmtpEnableSsl { get; set; } = true;
    /// <summary>Smtp | ApiKey</summary>
    public string MailSendMode { get; set; } = "Smtp";
    /// <summary>Brevo | SendGrid</summary>
    public string MailApiProvider { get; set; } = "Brevo";
    public string? MailApiKey { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }
}
