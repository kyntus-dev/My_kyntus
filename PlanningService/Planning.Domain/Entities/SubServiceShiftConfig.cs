using Planning.Domain.Enums;

namespace Planning.Domain.Entities;

public class SubServiceShiftConfig
{
    public int Id { get; set; }

    // -- Contexte --------------------------------------
    public int SubServiceId { get; set; }
    public SubService SubService { get; set; } = null!;

    /// <summary>Profil de mode (null = mono-mode historique / mode par défaut implicite).</summary>
    public int? ShiftModeProfileId { get; set; }
    public ShiftModeProfile? ShiftModeProfile { get; set; }

    /// <summary>null = template permanent ; ISO week code = snapshot semaine.</summary>
    public string? WeekCode { get; set; }
    public DateOnly? WeekStartDate { get; set; }

    /// <summary>true = modèle éditable ; false = snapshot généré pour une semaine.</summary>
    public bool IsTemplate { get; set; }

    /// <summary>
    /// true = créneau samedi dédié ; false = Lun–Ven (et samedi par défaut, quota moitié).
    /// </summary>
    public bool AppliesToSaturday { get; set; }

    // -- Définition du shift ---------------------------
    public string Label { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public int WorkHours { get; set; } = 8;

    /// <summary>Opening / Closing / Standard — déduit du StartTime à la sauvegarde.</summary>
    public ShiftKind ShiftKind { get; set; } = ShiftKind.Standard;

    // -- Pause déjeuner --------------------------------
    public TimeOnly BreakRangeStart { get; set; }
    public TimeOnly BreakRangeEnd { get; set; }
    public int BreakDurationMinutes { get; set; } = 60;

    /// <summary>JSON des heures de début de pause (max 3), ex. ["12:00","12:30","13:00"].</summary>
    public string? BreakSlotsJson { get; set; }

    /// <summary>Si true : extrêmes [start+3h, start+5h] et ouverture tôt/tard selon le plateau.</summary>
    public bool IsCriticalCell { get; set; }

    // -- Quota -----------------------------------------
    public int RequiredCount { get; set; }
    public decimal Percentage { get; set; }

    // -- Règle présence --------------------------------
    public int MinPresencePercent { get; set; } = 70;

    // -- Ordre affichage -------------------------------
    public int DisplayOrder { get; set; }

    // -- Audit -----------------------------------------
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public TimeOnly EndTime =>
        StartTime.AddHours(WorkHours + (BreakDurationMinutes / 60.0));
}
