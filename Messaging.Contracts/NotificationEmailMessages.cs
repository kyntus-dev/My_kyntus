namespace Kyntus.Messaging.Contracts;

/// <summary>
/// Demande d'envoi par e-mail d'une notification affichée dans l'application (Prime, Parrainage,
/// Documentation…). Consommé par Planning, qui détient la configuration d'envoi : adresse
/// professionnelle du destinataire, préférence « e-mails » respectée, cadence limitée.
/// </summary>
public record NotificationEmailRequestedMessage
{
    /// <summary>Identifiant de l'événement : rejouer le message ne renvoie pas l'e-mail.</summary>
    public Guid RequestId { get; init; } = Guid.NewGuid();
    /// <summary>Module d'origine : prime, parrainage, documentation…</summary>
    public string Source { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    /// <summary>Écran à ouvrir dans l'application (ex. « /prime »), facultatif.</summary>
    public string? Route { get; init; }
    /// <summary>Destinataires par GUID employé (Directory / Planning).</summary>
    public IReadOnlyList<Guid> EmployeeIds { get; init; } = [];
    /// <summary>Destinataires par identifiant de compte Auth.</summary>
    public IReadOnlyList<int> AuthUserIds { get; init; } = [];
    /// <summary>Destinataires par rôle (ex. « RH », « Admin »).</summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
