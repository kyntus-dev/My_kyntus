namespace Planning.Domain.Entities;

/// <summary>
/// v87 — Historique des indicateurs de l'accueil : la valeur d'un indicateur, telle qu'une personne l'a vue
/// sur son propre accueil (calculée avec ses droits), une fois par jour. Sert uniquement aux tendances
/// ▲/▼ et aux courbes de son accueil ; personne d'autre ne la lit.
/// </summary>
public class IndicateurAccueilJour
{
    public long Id { get; set; }
    /// <summary>Identité de la personne (sujet du jeton).</summary>
    public Guid SubjectId { get; set; }
    public DateOnly Jour { get; set; }
    /// <summary>Clé de l'indicateur (dérivée de son libellé, 60 caractères au plus).</summary>
    public string Cle { get; set; } = string.Empty;
    public decimal Valeur { get; set; }
    public DateTime MisAJourLe { get; set; } = DateTime.UtcNow;
}
