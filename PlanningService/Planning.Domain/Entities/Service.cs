using Planning.Domain.Entities;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Service
{
    public int Id { get; set; }

    [ForeignKey("Floor")]
    public int FloorId { get; set; }
    public Floor Floor { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; }

    [Required]
    [MaxLength(20)]
    public string Code { get; set; }

    [MaxLength(64)]
    public string? PrimeCelluleId { get; set; }

    /// <summary>
    /// Nœud supprimé dans l'Organisation RH : masqué des listes Planning, mais conservé
    /// (avec ses plannings) pour l'historique.
    /// </summary>
    public bool IsArchived { get; set; }

    public ICollection<SubService> SubServices { get; set; }
    // 🆕 AJOUTER cette ligne dans Service.cs
    public ICollection<UserManagedService> ManagedByUsers { get; set; } = new List<UserManagedService>();
}
