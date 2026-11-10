namespace Planning.Domain.Entities;

public enum EmailOutboxStatus
{
    Pending = 0,
    Sent = 1,
    /// <summary>Abandonné après toutes les tentatives.</summary>
    Failed = 2,
    /// <summary>Non envoyé (adresse invalide, envoi coupé, trop ancien).</summary>
    Skipped = 3,
}

/// <summary>
/// File d'envoi des e-mails (plannings publiés et notifications de l'application).
/// Chaque e-mail est enregistré avant l'envoi : rien n'est perdu si le serveur mail est
/// indisponible, et la cadence reste compatible avec les limites de Microsoft 365.
/// </summary>
public class EmailOutboxMessage
{
    public long Id { get; set; }
    /// <summary>Adresse professionnelle du destinataire.</summary>
    public string ToEmail { get; set; } = string.Empty;
    /// <summary>Id Planning du destinataire (null si l'adresse vient d'ailleurs).</summary>
    public int? RecipientUserId { get; set; }
    /// <summary>« planning » ou la source de la notification (conge, formation, prime…).</summary>
    public string Category { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    /// <summary>Clé d'idempotence : un même événement ne produit qu'un e-mail par destinataire.</summary>
    public string? DedupKey { get; set; }
    public EmailOutboxStatus Status { get; set; } = EmailOutboxStatus.Pending;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SentAt { get; set; }
}

/// <summary>Choix de l'utilisateur : recevoir aussi ses notifications par e-mail (oui par défaut).</summary>
public class NotificationEmailPreference
{
    /// <summary>Id Planning de l'utilisateur.</summary>
    public int UserId { get; set; }
    public bool EmailEnabled { get; set; } = true;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Dernières notifications déjà converties en e-mail. Initialisé sur les notifications existantes
/// au premier démarrage : l'historique n'est jamais envoyé.
/// </summary>
public class NotificationEmailCursor
{
    public const string SingletonId = "default";
    public string Id { get; set; } = SingletonId;
    public int LastPlanningNotificationId { get; set; }
    public int LastContractNotificationId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
