using Planning.Application.DTOs.Planning;
namespace Planning.Application.Abstractions;

public interface IPlanningService
{
    // -- CRUD Planning --
    Task<WeeklyPlanningResponseDto> CreatePlanningAsync(CreateWeeklyPlanningDto dto);
    Task<WeeklyPlanningResponseDto?> GetPlanningByIdAsync(int id);
    Task<IEnumerable<WeeklyPlanningResponseDto>> GetPlanningsBySubServiceAsync(int subServiceId);
    Task DeletePlanningAsync(int id);

    // -- Génération ancienne (compatibilité) --
    Task<WeeklyPlanningResponseDto> GeneratePlanningAsync(GeneratePlanningDto dto);
    Task AutoAssignSaturdayGroupsAsync(int subServiceId);

    // -- Config shifts : template + snapshot --
    Task<WeekShiftConfigResponseDto> SaveShiftConfigAsync(SaveShiftConfigDto dto);
    Task<WeekShiftConfigResponseDto?> GetShiftConfigAsync(int subServiceId, string weekCode);
    Task<WeekShiftConfigResponseDto?> GetShiftTemplateAsync(int subServiceId);
    Task<WeekShiftConfigResponseDto> SaveShiftTemplateAsync(SaveShiftConfigDto dto);
    Task<ShiftConfigStatusResponseDto> GetShiftConfigStatusAsync();
    Task EnsureWeekSnapshotAsync(int subServiceId, string weekCode, DateOnly weekStartDate, bool forceRefresh = false);

    // -- Modes de shifts hebdomadaires --
    Task<WeeklyShiftModePlanDto> GetWeeklyShiftModePlanAsync(int subServiceId, string weekCode, DateOnly weekStartDate);
    Task<WeeklyShiftModePlanDto> SaveWeeklyShiftModePlanAsync(SaveWeeklyShiftModePlanDto dto);
    Task NotifyModeConfigPendingAsync(
        int planningUserId, int authUserId, string weekCode, string subServiceName, string message);

    // -- Génération depuis config / auto --
    Task<WeeklyPlanningResponseDto> GeneratePlanningFromConfigAsync(GeneratePlanningFromConfigDto dto);
    Task<AutoGenerateWeekResultDto> AutoGenerateWeekAsync(
        string? weekCode = null, bool forceDraftRefresh = false, int? actorUserId = null);
    Task EnsureViewerCanManageSubServiceAsync(int viewerUserId, int subServiceId);
    Task<AutoGenerateSettingsDto> GetAutoGenerateSettingsAsync();
    Task<AutoGenerateSettingsDto> SaveAutoGenerateSettingsAsync(AutoGenerateSettingsDto dto, int? updatedByUserId);

    // -- Validation RH/Admin --
    Task<PlanningWeekListDto> GetWeekOverviewAsync(string weekCode, int? viewerUserId = null);
    Task<PlanningWeekListDto> GetWeekOverviewForManagedCellsAsync(
        string weekCode,
        int viewerUserId,
        IReadOnlyCollection<string> primeCelluleIds) =>
        GetWeekOverviewAsync(weekCode, viewerUserId);
    Task RecordConsultationAsync(int planningId, int userId);
    Task<bool> HasConsultedAsync(int planningId, int userId);

    // -- Publication --
    Task<WeeklyPlanningResponseDto> PublishPlanningAsync(int planningId, int validatorId);
    /// <summary>
    /// Notification soft après régénération d'un planning déjà Published (statut inchangé).
    /// Agents + superviseurs + alerte RH.
    /// </summary>
    Task NotifyPlanningRepublishedAsync(int planningId, string? reason = null);

    // -- Override manager --
    Task<DayAssignmentDto> OverrideShiftAsync(OverrideShiftDto dto);
    Task<DayAssignmentDto> OverrideBreakAsync(OverrideBreakDto dto);
    Task<MyPlanningDto?> GetMyCurrentPlanningAsync(int userId);

    // -- Samedi --
    Task SetSaturdayGroupAsync(SetSaturdayGroupDto dto);
    Task SetSaturdayWorkModeAsync(SetSaturdayWorkModeDto dto);
    Task SetEmployeeSpecialCaseAsync(SetEmployeeSpecialCaseDto dto);

    Task SetEmployeePlateauTrainingAsync(SetEmployeePlateauTrainingDto dto);
    Task<SaturdayBalanceDto> GetSaturdayBalanceAsync(int subServiceId);
    /// <summary>Crée/actualise une notification superviseur si déséquilibre (idempotent).</summary>
    Task<int> NotifySaturdayImbalanceAsync(int subServiceId, int authUserId);
    Task<IEnumerable<object>> GetSaturdayGroupsAsync(int subServiceId);
    Task SyncNewEmployeesAsync();

    // -- Vue employé --
    Task<MyPlanningDto?> GetMyPlanningAsync(int userId, string weekCode);
    Task<IEnumerable<MyPlanningDto>> GetMyPlanningHistoryAsync(int userId);
    Task<IReadOnlyList<MyPlanningDto>> GetAgentPlanningHistoryAsync(
        int planningUserId, DateOnly? from, DateOnly? to);
    Task<PlanningCommentDto> SaveCommentAsync(SavePlanningCommentDto dto);
    Task DeleteCommentAsync(int planningId, int userId);
    Task<IEnumerable<PlanningCommentDto>> GetCommentsAsync(int planningId);
    Task<List<SaturdayHistoryResponseDto>> GetSaturdayHistoryAsync(int subServiceId, string weekCode);
    Task<List<SaturdayYtdDto>> GetSaturdayYtdAsync(int subServiceId, int year);
    Task SaveSaturdayHistoryAsync(SetSaturdayHistoryDto dto, bool isManual);
    Task<DayAssignmentDto> OverrideSaturdayShiftAsync(OverrideSaturdayDto dto);
    Task SetSaturdayOffAsync(int weeklyPlanningId, int userId);

    Task<IReadOnlyList<EquipePlanningSummaryDto>> GetEquipePlanningsByAuthUserIdAsync(int authUserId);

    // -- Notifications --
    Task<IEnumerable<PlanningNotificationDto>> GetMyNotificationsAsync(int authUserId);
    Task MarkNotificationReadAsync(int id, int authUserId);
    Task MarkAllNotificationsReadAsync(int authUserId);
}
