using Planning.Domain.Enums;
using Planning.Domain.Entities;

public class WeeklyPlanning
{
    public int Id { get; set; }
    public int SubServiceId { get; set; }
    public SubService SubService { get; set; }
    public string WeekCode { get; set; }
    public DateOnly WeekStartDate { get; set; }
    public PlanningStatus Status { get; set; } = PlanningStatus.Draft;

    // ? NOUVEAU — effectif total voulu par le manager (Option B)
    public int TotalEffectif { get; set; } = 0;

    // ? NOUVEAU — groupe samedi actif cette semaine (1 ou 2)
    public int SaturdayGroupId { get; set; } = 1;

    public int? ValidatedBy { get; set; }
    public User? Validator { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// v51 : raison du dernier echec de generation (modes non saisis par le superviseur, cellule
    /// vide...). Null apres une generation reussie. Affichee dans la liste et la vue du planning
    /// pour qu'une grille vide ne soit jamais muette.
    /// </summary>
    public string? GenerationErreur { get; set; }
    public DateTime? GenerationErreurLe { get; set; }

    public ICollection<ShiftAssignment> ShiftAssignments { get; set; }
    public ICollection<WeeklyShiftConfig> WeeklyShiftConfigs { get; set; }
}