namespace Planning.Domain.Entities;

using System.ComponentModel.DataAnnotations;

public class Floor
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; }

    public int FloorNumber { get; set; }

    public string? Description { get; set; }

    [MaxLength(64)]
    public string? PrimePoleId { get; set; }

    /// <summary>
    /// Nœud supprimé dans l'Organisation RH : masqué des listes Planning, mais conservé
    /// (avec ses plannings) pour l'historique.
    /// </summary>
    public bool IsArchived { get; set; }

    public ICollection<Service> Services { get; set; }
}
