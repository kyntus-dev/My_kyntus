namespace Planning.Domain.Entities;

/// <summary>État d'une journée dans le planning IT.</summary>
public static class PlanningItStatuts
{
    /// <summary>Journée travaillée : l'heure d'arrivée sert de référence au pointage.</summary>
    public const string Travail = "Travail";
    /// <summary>Jour non travaillé (case grise du modèle Excel).</summary>
    public const string Repos = "Repos";
    /// <summary>Congé / absence prévue (case orange du modèle Excel).</summary>
    public const string Conge = "Conge";

    public static bool EstValide(string? s) => s is Travail or Repos or Conge;
}

/// <summary>
/// Horaire saisi par un membre de la cellule IT pour une journée (planning IT partagé).
/// Une ligne par collaborateur et par jour ; elle reste modifiable à tout moment.
/// </summary>
public class PlanningItHoraire
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Jour { get; set; }
    public string Statut { get; set; } = PlanningItStatuts.Travail;
    /// <summary>Heure d'arrivée prévue (journée travaillée).</summary>
    public TimeOnly? Arrivee { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
}

/// <summary>Commentaire d'un membre IT pour une semaine (colonne « Commentaire » du modèle).</summary>
public class PlanningItCommentaire
{
    public long Id { get; set; }
    public int UserId { get; set; }
    /// <summary>Lundi de la semaine.</summary>
    public DateOnly Semaine { get; set; }
    public string Texte { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
}
