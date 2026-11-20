namespace Planning.Domain.Entities;

/// <summary>
/// v63 — Historique d'une synchronisation de fiches depuis le fichier RH de référence : une ligne par
/// champ modifié (valeur avant / après). Sert de traçabilité et permet d'annuler une synchronisation
/// sans toucher aux valeurs modifiées depuis par quelqu'un d'autre.
/// Champs spéciaux : « __creation__ » (fiche créée), « __desactivation__ » (compte désactivé).
/// </summary>
public class FicheReferenceModification
{
    public long Id { get; set; }
    public Guid ImportId { get; set; }
    public DateTime ImportLe { get; set; } = DateTime.UtcNow;
    public string? Fichier { get; set; }
    public string? Auteur { get; set; }
    public int UserId { get; set; }
    public string? Agent { get; set; }
    public string Champ { get; set; } = string.Empty;
    public string? AncienneValeur { get; set; }
    public string? NouvelleValeur { get; set; }
    public DateTime? AnnuleeLe { get; set; }
}
