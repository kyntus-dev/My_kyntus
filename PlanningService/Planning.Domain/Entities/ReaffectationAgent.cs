namespace Planning.Domain.Entities;

/// <summary>Historique des changements de service d'un agent (v21) : de « Ancien » vers « Nouveau ».</summary>
public class ReaffectationAgent
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public int? AncienSubServiceId { get; set; }
    /// <summary>null : l'agent quitte la production (aucun service), v26.</summary>
    public int? NouveauSubServiceId { get; set; }
    public string? AncienLibelle { get; set; }
    public string NouveauLibelle { get; set; } = string.Empty;
    public string? Motif { get; set; }
    /// <summary>Changement de poste (v26) : rôle avant / après, null si inchangé.</summary>
    public string? AncienRole { get; set; }
    public string? NouveauRole { get; set; }
    /// <summary>« Réaffectation » (écran dédié) ou « Fiche employé » (modification de la fiche).</summary>
    public string Origine { get; set; } = "Réaffectation";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public string? CreatedByNom { get; set; }
}
