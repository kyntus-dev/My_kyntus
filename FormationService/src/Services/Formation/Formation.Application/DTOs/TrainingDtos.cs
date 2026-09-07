using Formation.Domain;
using Formation.Domain.Enums;

namespace Formation.Application.DTOs;

public sealed record TrainingSessionDto(
    Guid Id,
    string Title,
    string Description,
    TrainingSessionType Type,
    AnimatorKind AnimatorKind,
    Guid? AnimatorUserId,
    string? ExternalAnimatorName,
    string? ExternalAnimatorOrganization,
    string? ExternalAnimatorEmail,
    string? ExternalAnimatorPhone,
    DateTime PlannedStart,
    DateTime PlannedEnd,
    int Capacity,
    TrainingSessionStatus Status,
    int AssignmentCount,
    Guid? ProgramId = null,
    int SequenceNumber = 1,
    bool HasReport = false,
    Guid? QuizId = null,
    string? QuizStatus = null,
    Guid? CatalogItemId = null,
    string? LearningGateMode = null,
    bool? CanMarkAttendance = null,
    bool? CanUploadReport = null,
    string? AttendanceBlockedReason = null,
    string? ReportBlockedReason = null);

public sealed record TrainingAssignmentDto(
    Guid Id,
    Guid SessionId,
    Guid EmployeeId,
    string EmployeeName,
    TrainingAssignmentStatus Status,
    string Attendance);

/// <summary>Session continue où l'employé est bénéficiaire (Mes formations).</summary>
public sealed record MyAssignedTrainingSessionDto(
    Guid SessionId,
    Guid AssignmentId,
    string Title,
    DateTime PlannedStart,
    DateTime PlannedEnd,
    TrainingSessionStatus Status,
    string Attendance,
    Guid? QuizId = null,
    string? QuizStatus = null,
    bool CanTakeQuiz = false,
    Guid? AttemptId = null,
    bool AttemptGraded = false,
    decimal? FinalScore = null,
    bool? Passed = null,
    Guid? CatalogItemId = null,
    decimal CatalogProgressPercent = 0m,
    int RequiredLessonsDone = 0,
    int RequiredLessonsTotal = 0,
    string? QuizBlockedReason = null,
    bool AllowMultipleAttempts = false);

public sealed record InitialTrainingQuizResultDto(
    Guid Id,
    string Title,
    decimal Score,
    bool Passed,
    string? RecordedBy,
    DateTime RecordedAt);

public sealed record InitialTrainingPathDto(
    Guid Id,
    Guid EmployeeId,
    string EmployeeName,
    DateTime DateDebut,
    DateTime DateFinPrevue,
    InitialTrainingStatus Status,
    bool HasQuizResult,
    DateTime? FormateurValidatedAt,
    DateTime? RhValidatedAt,
    string? RejectedBy,
    string? RejectReason,
    IReadOnlyList<InitialTrainingQuizResultDto> QuizResults,
    decimal QuizSuccessRate,
    int DocumentsReceivedCount = 0,
    int DocumentsTotalCount = 0,
    IReadOnlyList<string>? MissingDocumentTitles = null,
    int? DaysUntilEnd = null,
    Guid? FormateurValidatedBy = null,
    Guid? RhValidatedBy = null,
    string? OrgDepartmentId = null,
    string? OrgPoleId = null,
    string? OrgCelluleId = null,
    string? OrgServiceId = null,
    string? OrgDepartmentName = null,
    string? OrgPoleName = null,
    string? OrgCelluleName = null,
    string? OrgServiceName = null);

public sealed record FormationDocumentDefinitionDto(
    Guid Id,
    string Title,
    int SortOrder,
    bool IsActive,
    DateTime CreatedAt);

public sealed class UpsertFormationDocumentDefinitionRequest
{
    public string Title { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed record FormationDocumentChecklistItemDto(
    Guid Id,
    Guid DefinitionId,
    string Title,
    int SortOrder,
    bool IsReceived,
    DateTime? ReceivedAt,
    string? ReceivedBy,
    string? Note,
    Guid? PathId = null);

public sealed class UpdateChecklistItemRequest
{
    public bool IsReceived { get; set; }
    public string? ReceivedBy { get; set; }
    public string? Note { get; set; }
}

public sealed class MarkTrainingAttendanceRequest
{
    /// <summary>Present | Absent</summary>
    public string Attendance { get; set; } = string.Empty;
    public Guid AnimatorUserId { get; set; }
}

public sealed class CreateTrainingSessionRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public AnimatorKind AnimatorKind { get; set; }
    public Guid? AnimatorUserId { get; set; }
    public string? ExternalAnimatorName { get; set; }
    public string? ExternalAnimatorOrganization { get; set; }
    public string? ExternalAnimatorEmail { get; set; }
    public string? ExternalAnimatorPhone { get; set; }
    public DateTime PlannedStart { get; set; }
    public DateTime PlannedEnd { get; set; }
    public int Capacity { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public bool Publish { get; set; }
}

public sealed class AssignTrainingEmployeesRequest
{
    public IReadOnlyList<AssignTrainingEmployeeItem> Employees { get; set; } = Array.Empty<AssignTrainingEmployeeItem>();
}

public sealed class AssignTrainingEmployeeItem
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
}

public sealed class CreateInitialTrainingPathRequest
{
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime DateDebut { get; set; }
    public DateTime DateFinPrevue { get; set; }
}

public sealed class RecordInitialQuizRequest
{
    public decimal QuizScore { get; set; }
    public bool QuizPassed { get; set; }
    public string? FormateurComment { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
    /// <summary>Titre libre pour la traçabilité (défaut « Quiz »).</summary>
    public string? Title { get; set; }
}

public sealed class AddInitialQuizResultRequest
{
    public string Title { get; set; } = string.Empty;
    public decimal Score { get; set; }
    public string RecordedBy { get; set; } = string.Empty;
}

public sealed class RejectInitialTrainingRequest
{
    public string RejectedBy { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class FormateurValidateOrgRequest
{
    public string? OrgDepartmentId { get; set; }
    public string? OrgPoleId { get; set; }
    public string? OrgCelluleId { get; set; }
    public string? OrgServiceId { get; set; }
    public string? OrgDepartmentName { get; set; }
    public string? OrgPoleName { get; set; }
    public string? OrgCelluleName { get; set; }
    public string? OrgServiceName { get; set; }
}

public sealed class ExtendInitialTrainingRequest
{
    public DateTime DateFinPrevue { get; set; }
}

public sealed record TrainingProgramDto(
    Guid Id,
    string Title,
    string Description,
    TrainingProgramMode Mode,
    int SessionCount,
    AnimatorKind AnimatorKind,
    Guid? AnimatorUserId,
    string? ExternalAnimatorName,
    int Capacity,
    IReadOnlyList<TrainingSessionDto> Sessions);

public sealed record ProgramBeneficiaryProgressDto(
    Guid EmployeeId,
    string EmployeeName,
    bool AttendedInPerson,
    bool ContentCompleted,
    bool QuizPassed,
    bool HasContentTrack,
    bool HasQuizTrack,
    bool IsComplete);

public sealed record TrainingProgramDetailDto(
    Guid Id,
    string Title,
    string Description,
    TrainingProgramMode Mode,
    int SessionCount,
    AnimatorKind AnimatorKind,
    Guid? AnimatorUserId,
    string? ExternalAnimatorName,
    string? ExternalAnimatorOrganization,
    string? ExternalAnimatorEmail,
    string? ExternalAnimatorPhone,
    int Capacity,
    Guid? CatalogItemId,
    Guid? QuizTemplateId,
    string? LearningGateMode,
    IReadOnlyList<TrainingSessionDto> Sessions,
    IReadOnlyList<ProgramBeneficiaryProgressDto> Beneficiaries);

public sealed class CreateTrainingProgramRequest
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TrainingProgramMode Mode { get; set; } = TrainingProgramMode.Single;
    public int SessionCount { get; set; } = 1;
    public AnimatorKind AnimatorKind { get; set; }
    public Guid? AnimatorUserId { get; set; }
    public string? ExternalAnimatorName { get; set; }
    public string? ExternalAnimatorOrganization { get; set; }
    public string? ExternalAnimatorEmail { get; set; }
    public string? ExternalAnimatorPhone { get; set; }
    public int Capacity { get; set; }
    public string CreatedByUserId { get; set; } = string.Empty;
    public bool Publish { get; set; }
    public IReadOnlyList<TrainingProgramSessionSlot> Sessions { get; set; } = Array.Empty<TrainingProgramSessionSlot>();
}

public sealed class TrainingProgramSessionSlot
{
    public DateTime PlannedStart { get; set; }
    public DateTime PlannedEnd { get; set; }
}

public sealed record TrainingSessionReportDto(
    Guid Id,
    Guid SessionId,
    string FileName,
    string ContentType,
    DateTime UploadedAt);

public sealed record TrainingQuizQuestionDto(
    Guid Id,
    int SortOrder,
    TrainingQuizQuestionType Type,
    string Prompt,
    IReadOnlyList<string>? Options,
    int? CorrectOptionIndex,
    decimal Points,
    bool AllowMultiple = false,
    IReadOnlyList<int>? CorrectOptionIndexes = null,
    string? ImageUrl = null,
    string? Explanation = null,
    string? MediaKind = null);

/// <summary>Historique transversal des tentatives quiz d'un collaborateur.</summary>
public sealed record MyQuizAttemptHistoryItemDto(
    Guid AttemptId,
    Guid SessionId,
    string SessionTitle,
    Guid? CatalogItemId,
    string? CatalogTitle,
    int AttemptNumber,
    decimal? Score,
    bool? Passed,
    bool IsGraded,
    DateTime SubmittedAt);

public sealed record TrainingQuizDto(
    Guid Id,
    Guid? SessionId,
    string Title,
    TrainingQuizStatus Status,
    IReadOnlyList<TrainingQuizQuestionDto> Questions,
    string? RejectedReason = null,
    decimal PassThreshold = TrainingQuizDefaults.PassThreshold,
    bool AllowMultipleAttempts = false,
    Guid? TemplateId = null);

public sealed class GradeFreeTextAnswerRequest
{
    public Guid AnimatorUserId { get; set; }
    public Guid QuestionId { get; set; }
    public bool IsCorrect { get; set; }
}

/// <summary>Quiz for employees — hides correct answers.</summary>
public sealed record TrainingQuizForEmployeeDto(
    Guid Id,
    Guid SessionId,
    string Title,
    TrainingQuizStatus Status,
    IReadOnlyList<TrainingQuizQuestionPublicDto> Questions,
    bool AllowMultipleAttempts = false,
    decimal PassThreshold = TrainingQuizDefaults.PassThreshold,
    Guid? CatalogItemId = null,
    Guid? EnrollmentId = null);

public sealed record TrainingQuizQuestionPublicDto(
    Guid Id,
    int SortOrder,
    TrainingQuizQuestionType Type,
    string Prompt,
    IReadOnlyList<string>? Options,
    decimal Points,
    bool AllowMultiple = false,
    string? ImageUrl = null,
    string? MediaKind = null);

public sealed class UpsertTrainingQuizRequest
{
    public string Title { get; set; } = string.Empty;
    /// <summary>Seuil de réussite en % (1–100). Défaut 70.</summary>
    public decimal PassThreshold { get; set; } = TrainingQuizDefaults.PassThreshold;
    public bool AllowMultipleAttempts { get; set; }
    public Guid AnimatorUserId { get; set; }
    public IReadOnlyList<UpsertTrainingQuizQuestionItem> Questions { get; set; } = Array.Empty<UpsertTrainingQuizQuestionItem>();
}

public sealed class UpsertTrainingQuizQuestionItem
{
    /// <summary>Identifiant existant (préserve ImageStoragePath / fichiers à la réécriture).</summary>
    public Guid? Id { get; set; }
    public TrainingQuizQuestionType Type { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public IReadOnlyList<string>? Options { get; set; }
    public int? CorrectOptionIndex { get; set; }
    public bool AllowMultiple { get; set; }
    public IReadOnlyList<int>? CorrectOptionIndexes { get; set; }
    public decimal Points { get; set; } = 1m;
    public string? ImageUrl { get; set; }
    public string? Explanation { get; set; }
}

public sealed class SubmitTrainingQuizAttemptRequest
{
    public Guid AssignmentId { get; set; }
    public Guid EmployeeId { get; set; }
    public IReadOnlyList<TrainingQuizAnswerItem> Answers { get; set; } = Array.Empty<TrainingQuizAnswerItem>();
}

public sealed class TrainingQuizAnswerItem
{
    public Guid QuestionId { get; set; }
    public int? SelectedOptionIndex { get; set; }
    public IReadOnlyList<int>? SelectedOptionIndexes { get; set; }
    public string? FreeText { get; set; }
}

public sealed class GradeTrainingQuizAttemptRequest
{
    public Guid AnimatorUserId { get; set; }
    public decimal? ManualScore { get; set; }
    public bool Passed { get; set; }
    public string? AnimatorComment { get; set; }
}

public sealed class ValidateTrainingQuizRequest
{
    public Guid ActorUserId { get; set; }
}

public sealed class RejectTrainingQuizRequest
{
    public Guid ActorUserId { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public sealed record TrainingQuizAttemptAnswerDetailDto(
    Guid QuestionId,
    int SortOrder,
    TrainingQuizQuestionType Type,
    string Prompt,
    IReadOnlyList<string>? Options,
    int? SelectedOptionIndex,
    IReadOnlyList<int>? SelectedOptionIndexes,
    string? FreeText,
    int? CorrectOptionIndex,
    IReadOnlyList<int>? CorrectOptionIndexes,
    bool AllowMultiple,
    bool? IsCorrect,
    decimal Points,
    string? ImageUrl = null,
    string? Explanation = null,
    string? MediaKind = null);

public sealed record TrainingQuizAttemptDto(
    Guid Id,
    Guid QuizId,
    Guid AssignmentId,
    Guid EmployeeId,
    string EmployeeName,
    decimal? AutoScore,
    decimal? ManualScore,
    decimal? FinalScore,
    bool? Passed,
    bool IsGraded,
    DateTime SubmittedAt,
    string? AnimatorComment,
    IReadOnlyList<TrainingQuizAttemptAnswerDetailDto>? Answers = null,
    int AttemptNumber = 1);

public sealed record FormationDashboardStatsDto(
    int ProgramCount,
    int SessionCount,
    int AssignmentCount,
    int PresentCount,
    double AttendanceRate,
    int QuizCount,
    int QuizzesValidated,
    int GradedAttempts,
    int PassedAttempts,
    double QuizSuccessRate,
    int UpcomingSessions,
    int MissingReports,
    int QuizzesPendingValidation);

public sealed record FormationInitialRiskItemDto(
    Guid PathId,
    Guid EmployeeId,
    string EmployeeName,
    int? DaysUntilEnd,
    int DocumentsReceivedCount,
    int DocumentsTotalCount,
    IReadOnlyList<string> MissingDocumentTitles);

public sealed record FormationInitialDashboardStatsDto(
    int TotalPaths,
    int EnCours,
    int AttenteValidationFormateur,
    int AttenteValidationRh,
    int EnProduction,
    int Rejete,
    int PendingRh,
    double AvgQuizSuccessRate,
    int PathsWithMissingDocs,
    int EndingWithin7Days,
    IReadOnlyList<FormationInitialRiskItemDto> AtRisk);

