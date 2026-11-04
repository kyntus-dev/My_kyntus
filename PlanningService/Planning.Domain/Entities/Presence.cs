namespace Planning.Domain.Entities;

/// <summary>
/// Saisie d'un responsable sur une journée d'un agent (tableau des présences) : motif d'absence,
/// autorisation de retard, commentaire. Une ligne par agent et par jour ; sans ligne, la journée
/// est calculée à partir du pointage.
/// </summary>
public class PresenceJour
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Jour { get; set; }
    /// <summary>Code imposé (P, ABS N, AM…) ; null = code calculé depuis le pointage.</summary>
    public string? Code { get; set; }
    /// <summary>Retard d'arrivée autorisé par le responsable : il ne compte pas dans les retards.</summary>
    public bool RetardAutorise { get; set; }
    /// <summary>Dépassement de pause autorisé : il ne compte pas dans les retards.</summary>
    public bool RetardPauseAutorise { get; set; }
    /// <summary>Motif de l'autorisation (« prévenu la veille », « rendez-vous médical »…).</summary>
    public string? MotifAutorisation { get; set; }
    public string? Commentaire { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
    /// <summary>Identifiant lisible de l'auteur (repris dans « Commentaire Présence » de l'export paie).</summary>
    public string? UpdatedByNom { get; set; }
}
