namespace Planning.Domain.Entities;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class SubService
{
    public int Id { get; set; }

    [ForeignKey("Service")]
    public int ServiceId { get; set; }
    public Service Service { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; }

    [Required]
    [MaxLength(20)]
    public string Code { get; set; }

    [MaxLength(64)]
    public string? PrimeServiceId { get; set; }

    /// <summary>
    /// Nœud supprimé dans l'Organisation RH : masqué des listes Planning, mais conservé
    /// (avec ses plannings) pour l'historique.
    /// </summary>
    public bool IsArchived { get; set; }

    /// <summary>Active la configuration multi-modes de shifts pour cette cellule.</summary>
    public bool MultiShiftModesEnabled { get; set; }

    public ICollection<User> Users { get; set; }
    public ICollection<WeeklyPlanning> WeeklyPlannings { get; set; }
    public ICollection<UserSubService> Managers { get; set; } = new List<UserSubService>();
    public ICollection<ShiftModeProfile> ShiftModeProfiles { get; set; } = new List<ShiftModeProfile>();
}
