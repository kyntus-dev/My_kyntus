namespace Kyntus.Messaging.Contracts;

/// <summary>
/// v64 — Action à inscrire au journal d'actions (Audit et conformité), publiée par un autre service
/// (ex. Employee Directory : droits d'accès par rôle ou par compte) et enregistrée par le Planning.
/// Le rôle Super Admin n'est jamais transmis : il est présenté comme « Admin ».
/// </summary>
public record JournalActionMessage
{
    public DateTime Horodatage { get; init; } = DateTime.UtcNow;
    public Guid? ActeurGuid { get; init; }
    public string? ActeurEmail { get; init; }
    public string? ActeurRole { get; init; }
    public string Module { get; init; } = string.Empty;
    public string Operation { get; init; } = string.Empty;
    public string Entite { get; init; } = string.Empty;
    public string? EntiteId { get; init; }
    public string Element { get; init; } = string.Empty;
    public Dictionary<string, string?>? AnciennesValeurs { get; init; }
    public Dictionary<string, string?>? NouvellesValeurs { get; init; }
    public string? Details { get; init; }
    public string? Source { get; init; }
}
