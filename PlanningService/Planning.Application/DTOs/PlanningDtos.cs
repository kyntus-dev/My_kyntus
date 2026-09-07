namespace Planning.Application.DTOs.Planning;

// -- Cr�er un planning --
public class CreateWeeklyPlanningDto
{
    public int SubServiceId { get; set; }
    public string WeekCode { get; set; } = string.Empty;
    public DateOnly WeekStartDate { get; set; }
    public int TotalEffectif { get; set; }
}

// -- G�n�rer le planning automatiquement --
public class GeneratePlanningDto
{
    public int WeeklyPlanningId { get; set; }
    public int TotalEffectif { get; set; }
}

// -- Override manuel d'un shift par le manager --
public class OverrideShiftDto
{
    public int ShiftAssignmentId { get; set; }
    public int NewShiftId { get; set; }                    // ancien syst�me (gard� pour compatibilit�)
    public int NewSubServiceShiftConfigId { get; set; }    // ? nouveau syst�me
}

// -- Configurer groupe samedi --
public class SetSaturdayGroupDto
{
    public int UserId { get; set; }
    public int GroupNumber { get; set; }
    public bool IsNewEmployee { get; set; } = false;
}

/// <summary>
/// Mode samedi par employé. SaturdayWorkMode null = défaut selon Niveau ;
/// 1 = tous les samedis 4h ; 2 = alternance 8h. GroupNumber requis si mode alternance.
/// </summary>
public class SetSaturdayWorkModeDto
{
    public int UserId { get; set; }
    public int? SaturdayWorkMode { get; set; }
    public int? GroupNumber { get; set; }
    /// <summary>Auth userId du superviseur (pour notification déséquilibre).</summary>
    public int? AuthUserId { get; set; }
}

/// <summary>Tag cas particulier (exclusion pauses extrêmes +3h/+5h).</summary>
public class SetEmployeeSpecialCaseDto
{
    public int UserId { get; set; }
    public bool IsSpecialCase { get; set; }
    /// <summary>Obligatoire si IsSpecialCase (ex. diabétique, expatrié).</summary>
    public string? Description { get; set; }
}

/// <summary>Ticket formation plateau : jamais ouverture/fermeture.</summary>
public class SetEmployeePlateauTrainingDto
{
    public int UserId { get; set; }
    public bool IsPlateauTraining { get; set; }
}

public record SaturdayEmployeeModeDto(
    int UserId,
    Guid Guid,
    string FullName,
    int Level,
    int? SaturdayWorkMode,
    int EffectiveMode,
    int GroupNumber,
    bool IsSpecialCase = false,
    string? SpecialCaseDescription = null,
    bool IsPlateauTraining = false
);

public record SaturdayBalanceDto(
    int SubServiceId,
    int AlwaysOnCount,
    int Group1Count,
    int Group2Count,
    int ProjectedSaturdayGroup1,
    int ProjectedSaturdayGroup2,
    bool IsImbalanced,
    int ImbalanceDelta,
    IReadOnlyList<SaturdayEmployeeModeDto> Employees,
    DateOnly Group1SaturdayDate,
    DateOnly Group2SaturdayDate
);

public record SetSaturdayHistoryDto(
    int SubServiceId,
    string WeekCode,
    List<SaturdayHistoryEntryDto> Entries
);

public record SaturdayHistoryEntryDto(
    int UserId,
    bool WorkedSaturday
);

public record SaturdayHistoryResponseDto(
    int UserId,
    string FullName,
    string WeekCode,
    bool WorkedSaturday,
    bool IsManualEntry
);

// -- Réponse planning complet --
public class WeeklyPlanningResponseDto
{
    public int Id { get; set; }
    public string WeekCode { get; set; } = string.Empty;
    public DateOnly WeekStartDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int TotalEffectif { get; set; }
    public int SaturdayGroupId { get; set; }
    public string SubServiceName { get; set; } = string.Empty;
    public List<ShiftConfigResponseDto> ShiftConfigs { get; set; } = new();
    public int SubServiceId { get; set; }
    public List<EmployeePlanningDto> Assignments { get; set; } = new();
    public CoverageReportDto? CoverageReport { get; set; }
}

/// <summary>Liste légère pour Planning Équipe (superviseur) — sans grille ni coverage.</summary>
public class EquipePlanningSummaryDto
{
    public int Id { get; set; }
    public string WeekCode { get; set; } = string.Empty;
    public DateOnly WeekStartDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int SubServiceId { get; set; }
    public string SubServiceName { get; set; } = string.Empty;
    public int EmployeeCount { get; set; }
    /// <summary>UserIds distincts présents dans les ShiftAssignments du planning.</summary>
    public List<int> AssignedUserIds { get; set; } = new();
}

public class CoverageReportDto
{
    public bool HasUnderstaffing { get; set; }
    public bool HasLevelBalanceAnomaly { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<PlanningAnomalyDto> LevelBalanceAnomalies { get; set; } = new();
    public List<CoverageDayShiftDto> Items { get; set; } = new();
    public List<DaySynthesisDto> DaySynthesis { get; set; } = new();

    /// <summary>Min disponibilité plateau observée sur la semaine (Lun–Ven).</summary>
    public decimal PlateauAvailabilityPercent { get; set; } = 100;
    /// <summary>Cible présence min cellule.</summary>
    public int PlateauAvailabilityTargetPercent { get; set; } = 70;
    /// <summary>% créneaux sans anomalie débutant seul.</summary>
    public decimal LevelBalancePercent { get; set; } = 100;
    /// <summary>% employés sans même shift deux jours travaillés d'affilée (KPI, hors max 2×).</summary>
    public decimal RotationCompliancePercent { get; set; } = 100;
    public int RotationViolatorsCount { get; set; }
    public int RotationEmployeesCount { get; set; }

    /// <summary>Nombre de pauses à +3h ou +5h du start (cas extrêmes) sur la semaine.</summary>
    public int ExtremeBreakCount { get; set; }
    /// <summary>Pauses du dernier palier uniquement.</summary>
    public int ExtremeTierBreakCount { get; set; }

    /// <summary>
    /// % employés dont la charge de cas extrêmes est équitable vs collègues même niveau
    /// (écart max−min ≤ 1 dans le niveau).
    /// </summary>
    public decimal ExtremeRotationCompliancePercent { get; set; } = 100;
    public int ExtremeRotationViolatorsCount { get; set; }
    public int ExtremeRotationEmployeesCount { get; set; }
}

public class PlanningAnomalyDto
{
    public string Code { get; set; } = "LEVEL_BALANCE";
    public string Severity { get; set; } = "Warning";
    public DateOnly Date { get; set; }
    public string Day { get; set; } = string.Empty;
    public int ShiftConfigId { get; set; }
    public string ShiftLabel { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    /// <summary>True si aucun Confirmé/Expert présentable ce jour (congés / effectif).</summary>
    public bool IsForced { get; set; }
}

public class DaySynthesisDto
{
    public DateOnly Date { get; set; }
    public string Day { get; set; } = string.Empty;
    public List<DaySynthesisShiftDto> Shifts { get; set; } = new();
    public int LeaveCount { get; set; }
    public int HolidayCount { get; set; }
    public int PresentCount { get; set; }
    public int SaturdayBeginners { get; set; }
    public int SaturdaySeniors { get; set; }
    public bool HasAnyAnomaly { get; set; }

    /// <summary>Min disponibilité plateau du jour.</summary>
    public decimal PlateauAvailabilityPercent { get; set; } = 100;
    public decimal LevelBalancePercent { get; set; } = 100;
    public decimal RotationCompliancePercent { get; set; } = 100;

    /// <summary>Nombre de pauses à +3h ou +5h du start (cas extrêmes) ce jour.</summary>
    public int ExtremeBreakCount { get; set; }
    /// <summary>Pauses du dernier palier uniquement ce jour.</summary>
    public int ExtremeTierBreakCount { get; set; }

    /// <summary>Disponibilité plateau par créneau 5 min (pour diagramme jour).</summary>
    public List<DayAvailabilityPointDto> AvailabilityTimeline { get; set; } = new();

    /// <summary>Timelines par mode métier (cellules multi-modes).</summary>
    public List<DayModeAvailabilityDto> AvailabilityByMode { get; set; } = new();
}

public class DayAvailabilityPointDto
{
    public string Time { get; set; } = string.Empty;
    public int PresentCount { get; set; }
    public int OnBreakCount { get; set; }
    public int AvailableCount { get; set; }
    public decimal AvailabilityPercent { get; set; }
}

public class DaySynthesisShiftDto
{
    public int ShiftConfigId { get; set; }
    public string ShiftLabel { get; set; } = string.Empty;
    public string ShiftKind { get; set; } = "Standard";
    public int? ShiftModeProfileId { get; set; }
    public string? ShiftModeTitle { get; set; }
    public int AssignedCount { get; set; }
    public int RequiredCount { get; set; }
    public int Delta { get; set; }
    public int BeginnerCount { get; set; }
    public int SeniorCount { get; set; }
    public bool IsUnderstaffed { get; set; }
    public bool HasLevelBalanceAnomaly { get; set; }
}

public class CoverageDayShiftDto
{
    public DateOnly Date { get; set; }
    public string Day { get; set; } = string.Empty;
    public int ShiftConfigId { get; set; }
    public string ShiftLabel { get; set; } = string.Empty;
    public string ShiftKind { get; set; } = "Standard";
    public int RequiredCount { get; set; }
    public int AssignedCount { get; set; }
    public int MinPresencePercent { get; set; }
    public decimal PresencePercent { get; set; }
    public bool IsUnderstaffed { get; set; }
    public bool HasLevelBalanceAnomaly { get; set; }
}

public record SaturdayYtdDto(
    int UserId,
    string FullName,
    int WorkedCount,
    int OffCount,
    int TotalWeeksRecorded,
    decimal WorkedPercent
);

// ? CORRIG� � WeeklyPlanningId + UserId ajout�s pour cr�er une assignation quand l'employ� est OFF
public class OverrideSaturdayDto
{
    public int ShiftAssignmentId { get; set; }          // 0 si l'employ� est actuellement OFF
    public int NewSubServiceShiftConfigId { get; set; }
    public int WeeklyPlanningId { get; set; }           // requis quand ShiftAssignmentId = 0
    public int UserId { get; set; }                     // requis quand ShiftAssignmentId = 0
}

// -- Config shift (% et count) --
public class ShiftConfigResponseDto
{
    public int ShiftId { get; set; }
    public string ShiftLabel { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string ShiftKind { get; set; } = "Standard";
    public int RequiredCount { get; set; }
    public decimal Percentage { get; set; }
    public List<string> BreakSlots { get; set; } = new();
    public int BreakDurationMinutes { get; set; } = 60;
    public bool IsCriticalCell { get; set; }
    public int? ShiftModeProfileId { get; set; }
    public string? ShiftModeTitle { get; set; }
}

// -- Shift d'un jour --
public class DayAssignmentDto
{
    public int AssignmentId { get; set; }
    public string Day { get; set; } = string.Empty;
    public DateOnly AssignedDate { get; set; }
    public string ShiftLabel { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public bool IsSaturday { get; set; }
    public bool IsManagerOverride { get; set; }
    /// <summary>True si le créneau vient d'une demande exceptionnelle appliquée.</summary>
    public bool IsExceptionalRequest { get; set; }
    public bool IsReinforcement { get; set; }
    public string EndTime { get; set; } = string.Empty;
    public string? BreakTime { get; set; }
    public bool IsOnLeave { get; set; }
    public bool IsHalfDaySaturday { get; set; }
    public bool IsHoliday { get; set; } = false;
    public string HolidayName { get; set; } = string.Empty;
    public int SaturdaySlot { get; set; }
    public string? AbsenceType { get; set; } // ? AJOUTER
    public string SlotLabel { get; set; } = string.Empty;
    public int? ShiftModeProfileId { get; set; }
    public string? ShiftModeTitle { get; set; }
    public bool IsModeOverride { get; set; }
}

// -- Vue employ� (son propre planning) --
public class MyPlanningDto
{
    public int WeeklyPlanningId { get; set; }
    public string WeekCode { get; set; } = string.Empty;
    public DateOnly WeekStartDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string SubServiceName { get; set; } = string.Empty;
    public List<DayAssignmentDto> Days { get; set; } = new();
}

/// <summary>Ancien DTO résumé — préférer MyPlanningDto (vue jours).</summary>
public class AgentPlanningHistoryItemDto
{
    public int WeeklyPlanningId { get; set; }
    public string WeekCode { get; set; } = string.Empty;
    public DateOnly WeekStartDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public int SubServiceId { get; set; }
    public string SubServiceName { get; set; } = string.Empty;
    public int WorkedDays { get; set; }
    public int LeaveDays { get; set; }
    public int HolidayDays { get; set; }
    public int OffSaturdayCount { get; set; }
}

// -- Notification de publication de planning (persistée) --
public class PlanningNotificationDto
{
    public int Id { get; set; }
    public int? WeeklyPlanningId { get; set; }
    public string WeekCode { get; set; } = string.Empty;
    public string SubServiceName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    /// <summary>Route SPA pour le clic (dérivée du type de notification).</summary>
    public string? DeepLink { get; set; }
}

// -- Sauvegarder la config shifts (template si WeekCode vide) --
public class SaveShiftConfigDto
{
    public int SubServiceId { get; set; }
    /// <summary>Vide = sauvegarde du modèle permanent.</summary>
    public string? WeekCode { get; set; }
    public DateOnly? WeekStartDate { get; set; }
    /// <summary>Extrêmes pause +3h/+5h (plafond métier) et ouverture bidirectionnelle.</summary>
    public bool IsCriticalCell { get; set; }
    /// <summary>Présence min plateau de toute la cellule (défaut 70). Mono-mode uniquement.</summary>
    public int MinPresencePercent { get; set; } = 70;
    /// <summary>Active les profils multi-modes pour cette cellule.</summary>
    public bool MultiShiftModesEnabled { get; set; }
    /// <summary>Profils quand MultiShiftModesEnabled ; sinon ignorer et utiliser Shifts.</summary>
    public List<ShiftModeProfileSaveDto> Modes { get; set; } = new();
    public List<ShiftConfigItemDto> Shifts { get; set; } = new();
    /// <summary>Si true, SaturdayShifts remplace le fallback « moitié weekday » le samedi.</summary>
    public bool UseDistinctSaturdayShifts { get; set; }
    public List<ShiftConfigItemDto> SaturdayShifts { get; set; } = new();
}

public class ShiftModeProfileSaveDto
{
    public int? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public int MinPresencePercent { get; set; } = 70;
    public bool IsCriticalCell { get; set; }
    public List<ShiftConfigItemDto> Shifts { get; set; } = new();
    public List<ShiftConfigItemDto> SaturdayShifts { get; set; } = new();
}

public class ShiftModeProfileDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public int MinPresencePercent { get; set; }
    public bool IsCriticalCell { get; set; }
    public List<ShiftConfigResponseNewDto> Shifts { get; set; } = new();
    public List<ShiftConfigResponseNewDto> SaturdayShifts { get; set; } = new();
}

// -- Un shift dans la config --
public class ShiftConfigItemDto
{
    public string Label { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public int WorkHours { get; set; } = 8;
    public int BreakDurationMinutes { get; set; } = 60;
    public string? BreakRangeStart { get; set; }
    public string? BreakRangeEnd { get; set; }
    /// <summary>Heures de début de pause cochées par RH (fenêtre autorisée du shift).</summary>
    public List<string>? BreakSlots { get; set; }
    public int RequiredCount { get; set; }
    /// <summary>Pourcentage du groupe mode (ou cellule). Prioritaire sur RequiredCount en multi-mode.</summary>
    public decimal? Percentage { get; set; }
    /// <summary>Ignoré à la sauvegarde — utiliser SaveShiftConfigDto.MinPresencePercent (niveau cellule).</summary>
    public int MinPresencePercent { get; set; } = 70;
    public int DisplayOrder { get; set; }
    /// <summary>Optionnel — sinon déduit (StartTime min=Opening, max=Closing).</summary>
    public string? ShiftKind { get; set; }
    public bool AppliesToSaturday { get; set; }
}

// -- Réponse après sauvegarde config --
public class ShiftConfigResponseNewDto
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int WorkHours { get; set; }
    public string BreakRangeStart { get; set; } = string.Empty;
    public string BreakRangeEnd { get; set; } = string.Empty;
    public int BreakDurationMinutes { get; set; }
    public List<string> BreakSlots { get; set; } = new();
    public bool IsCriticalCell { get; set; }
    public int RequiredCount { get; set; }
    public decimal Percentage { get; set; }
    public int MinPresencePercent { get; set; }
    public int DisplayOrder { get; set; }
    public string ShiftKind { get; set; } = "Standard";
    public int? ShiftModeProfileId { get; set; }
    public bool AppliesToSaturday { get; set; }
}

// -- Statut modèle shift par service (vue arbre RH) --
public class ShiftConfigStatusItemDto
{
    public int SubServiceId { get; set; }
    public string SubServiceName { get; set; } = string.Empty;
    public string? PrimeServiceId { get; set; }
    public bool HasTemplate { get; set; }
    public int ShiftCount { get; set; }
    public int TemplateEffectif { get; set; }
    public int ActiveEmployeeCount { get; set; }
}

public class ShiftConfigStatusResponseDto
{
    public List<ShiftConfigStatusItemDto> Items { get; set; } = new();
    public int ConfiguredCount { get; set; }
    public int TotalCount { get; set; }
}

// -- Réponse config complète (template ou snapshot) --
public class WeekShiftConfigResponseDto
{
    public int SubServiceId { get; set; }
    public string SubServiceName { get; set; } = string.Empty;
    public string WeekCode { get; set; } = string.Empty;
    public DateOnly WeekStartDate { get; set; }
    public bool IsTemplate { get; set; }
    public bool IsCriticalCell { get; set; }
    /// <summary>Présence min plateau de toute la cellule.</summary>
    public int MinPresencePercent { get; set; } = 70;
    public bool MultiShiftModesEnabled { get; set; }
    public List<ShiftModeProfileDto> Modes { get; set; } = new();
    public int TotalEffectif { get; set; }
    public List<ShiftConfigResponseNewDto> Shifts { get; set; } = new();
    public bool UseDistinctSaturdayShifts { get; set; }
    public List<ShiftConfigResponseNewDto> SaturdayShifts { get; set; } = new();
}

public class WeeklyShiftModePlanDto
{
    public int Id { get; set; }
    public int SubServiceId { get; set; }
    public string SubServiceName { get; set; } = string.Empty;
    public string WeekCode { get; set; } = string.Empty;
    public DateOnly WeekStartDate { get; set; }
    public bool IsValidated { get; set; }
    public bool IsLocked { get; set; }
    public DateTime? ValidatedAt { get; set; }
    /// <summary>True si aucun plan persisté : lignes recopiées de la dernière semaine validée.</summary>
    public bool IsCopiedPreview { get; set; }
    public string? SourceWeekCode { get; set; }
    public bool IsSupervisorSaved { get; set; }
    public DateTime? DeadlineLocal { get; set; }
    public bool DeadlinePassed { get; set; }
    public List<ShiftModeProfileDto> AvailableModes { get; set; } = new();
    public List<WeeklyEmployeeShiftModeDto> Employees { get; set; } = new();
}

public class WeeklyEmployeeShiftModeDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int Level { get; set; }
    public int? SaturdayWorkMode { get; set; }
    public int? ShiftModeProfileId { get; set; }
    public string? ShiftModeTitle { get; set; }
}

public class SaveWeeklyShiftModePlanDto
{
    public int SubServiceId { get; set; }
    public string WeekCode { get; set; } = string.Empty;
    public DateOnly WeekStartDate { get; set; }
    public int? ActorUserId { get; set; }
    public List<WeeklyEmployeeShiftModeItemDto> Employees { get; set; } = new();
}

public class WeeklyEmployeeShiftModeItemDto
{
    public int UserId { get; set; }
    public int ShiftModeProfileId { get; set; }
}

public class DayModeAvailabilityDto
{
    public int ShiftModeProfileId { get; set; }
    public string ShiftModeTitle { get; set; } = string.Empty;
    public int TargetPercent { get; set; }
    public decimal PlateauAvailabilityPercent { get; set; } = 100;
    public List<DayAvailabilityPointDto> AvailabilityTimeline { get; set; } = new();
}

// -- Générer le planning depuis la config --
public class GeneratePlanningFromConfigDto
{
    public int SubServiceId { get; set; }
    public string? WeekCode { get; set; }
    public int WeeklyPlanningId { get; set; }

    /// <summary>
    /// Si renseigné : ne régénère que les jours &gt;= cette date (jours antérieurs figés).
    /// Si null et planning Published : calculé via cutoff 15h.
    /// Si null et Draft : semaine entière (génération initiale).
    /// </summary>
    public DateOnly? RegenerateFromDate { get; set; }

    /// <summary>Motif pour les notifications de republication soft.</summary>
    public string? RepublishReason { get; set; }
}

public class PlanningWeekItemDto
{
    public int SubServiceId { get; set; }
    public string? PrimeServiceId { get; set; }
    public string SubServiceName { get; set; } = string.Empty;
    public string OrgLabel { get; set; } = string.Empty;
    public int? PlanningId { get; set; }
    public string? Status { get; set; }
    public int TotalEffectif { get; set; }
    public bool HasTemplate { get; set; }
    public bool CoverageOk { get; set; } = true;
    public bool HasConsulted { get; set; }
    /// <summary>
    /// Nombre d’affectations modifiées manuellement (IsManagerOverride),
    /// hors demandes exceptionnelles / renforts — même règle que le badge « Modifié manuellement ».
    /// </summary>
    public int ManualModificationCount { get; set; }
    /// <summary>UserIds distincts affectés (si planning généré).</summary>
    public List<int> AssignedUserIds { get; set; } = new();
}

public class PlanningWeekListDto
{
    public string WeekCode { get; set; } = string.Empty;
    public DateOnly WeekStartDate { get; set; }
    public List<PlanningWeekItemDto> Items { get; set; } = new();
}

public class AutoGenerateSettingsDto
{
    public bool Enabled { get; set; } = true;
    public int DayOfWeek { get; set; } = 4;
    public int HourLocal { get; set; } = 6;
    public int MinuteLocal { get; set; }
    public string TimeZone { get; set; } = "Africa/Casablanca";
    public string Target { get; set; } = "NextWeek";
    public DateTime? LastRunAt { get; set; }
    public string? LastRunWeekCode { get; set; }
    public string? SenderFromEmail { get; set; }
    public string? SenderDisplayName { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUser { get; set; }
    /// <summary>Uniquement en écriture. Jamais renvoyé par GET.</summary>
    public string? SmtpPassword { get; set; }
    public bool SmtpEnableSsl { get; set; } = true;
    public bool SmtpPasswordSet { get; set; }
    /// <summary>Smtp | ApiKey</summary>
    public string MailSendMode { get; set; } = "Smtp";
    /// <summary>Brevo | SendGrid</summary>
    public string MailApiProvider { get; set; } = "Brevo";
    /// <summary>Uniquement en écriture. Jamais renvoyé par GET.</summary>
    public string? MailApiKey { get; set; }
    public bool MailApiKeySet { get; set; }
}

public class TestPlanningEmailRequest
{
    public string? To { get; set; }
}

public class AutoGenerateWeekResultDto
{
    public string WeekCode { get; set; } = string.Empty;
    public int Created { get; set; }
    public int Skipped { get; set; }
    public int Errors { get; set; }
    public List<string> Messages { get; set; } = new();
}

public class OverrideBreakDto
{
    public int ShiftAssignmentId { get; set; }
    public string NewBreakTime { get; set; } = string.Empty;
}

// -- Sauvegarder un commentaire --
public class SavePlanningCommentDto
{
    public int WeeklyPlanningId { get; set; }
    public int UserId { get; set; }
    public string Comment { get; set; } = string.Empty;
    public int CreatedBy { get; set; }
}

// -- Réponse commentaire --
public class PlanningCommentDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class EmployeePlanningDto
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsNewEmployee { get; set; }
    public int Level { get; set; }
    public List<DayAssignmentDto> Days { get; set; } = new();
    public string? ManagerComment { get; set; }
    /// <summary>Cas particulier — visible RH / superviseur uniquement.</summary>
    public bool IsSpecialCase { get; set; }
    public string? SpecialCaseDescription { get; set; }
    /// <summary>Formation plateau — visible RH / superviseur uniquement.</summary>
    public bool IsPlateauTraining { get; set; }
    public int? IdTechnicien { get; set; }
    public string? HtelCode { get; set; }
    /// <summary>Libellé HTEL (NOM Prénom) — export planning uniquement.</summary>
    public string? HtelTechnicienName { get; set; }
}