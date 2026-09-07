namespace Prime.Application.DTOs;

public sealed class CelluleDraftGlobalPoolStateDto
{
    public Guid DraftId { get; init; }
    public string CelluleId { get; init; } = "";
    public string Period { get; init; } = "";
    public bool HasFile { get; init; }
    public string? FileName { get; init; }
    public DateTimeOffset? UploadedAt { get; init; }
    public DateTimeOffset? ManagerApprovedAt { get; init; }
    public DateTimeOffset? RhApprovedAt { get; init; }
    public DateTimeOffset? ComptaAckAt { get; init; }
    public bool PoolDistributionUnlocked { get; init; }
}

/// <summary>File global PRIME — entrée de file d’attente (Manager / RH / Comptable) avec identifiant superviseur pour compat API historique.</summary>
public sealed class GlobalPoolInboxItemDto
{
    public Guid DraftId { get; init; }
    public string SupervisorUserId { get; init; } = "";
    public string CelluleId { get; init; } = "";
    public string Period { get; init; } = "";
    public bool HasFile { get; init; }
    public string? FileName { get; init; }
    public DateTimeOffset? UploadedAt { get; init; }
    public DateTimeOffset? ManagerApprovedAt { get; init; }
    public DateTimeOffset? RhApprovedAt { get; init; }
    public DateTimeOffset? ComptaAckAt { get; init; }
    public bool PoolDistributionUnlocked { get; init; }
    /// <summary>Vrai si l’utilisateur connecté a encore une action attendue sur ce brouillon.</summary>
    public bool PendingActionForUser { get; init; }
    /// <summary>Présent uniquement lorsque le workflow global configurable est actif : état par étape.</summary>
    public List<GlobalPoolInboxStepStatusDto>? StepStatuses { get; init; }
    /// <summary>Première étape que l’utilisateur (ou Admin) peut valider dans la vague courante.</summary>
    public Guid? SuggestedApproveStepId { get; init; }
}

public sealed class GlobalPoolInboxStepStatusDto
{
    public Guid StepId { get; init; }
    public int SortOrder { get; init; }
    public string ApproverRole { get; init; } = "";
    public bool IsRequired { get; init; }
    public DateTimeOffset? ApprovedAt { get; init; }
}

public sealed class GlobalPoolActingUserRequest
{
    public string UserId { get; set; } = "";
    /// <summary>Rôle revendiqué (si absent : en-tête <c>X-Prime-Role</c>).</summary>
    public string? Role { get; set; }
}

public sealed class GlobalPoolWorkflowStepDto
{
    public Guid Id { get; init; }
    public int SortOrder { get; init; }
    public string ApproverRole { get; init; } = "";
    public bool IsRequired { get; init; }
    public bool IsActive { get; init; }
}

public sealed class GlobalPoolApproveStepRequest
{
    public string UserId { get; set; } = "";
    public Guid StepId { get; set; }
    /// <summary>Rôle revendiqué (si absent : en-tête <c>X-Prime-Role</c>).</summary>
    public string? Role { get; set; }
}

public sealed class UpsertGlobalPoolWorkflowStepRequest
{
    public int SortOrder { get; set; }
    public string ApproverRole { get; set; } = "";
    public bool IsRequired { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public sealed class GlobalPoolReadinessDto
{
    public string Period { get; init; } = "";
    public List<GlobalPoolServiceReadinessDto> Services { get; init; } = [];
    public List<GlobalPoolCelluleReadinessDto> Cellules { get; init; } = [];
    public List<GlobalPoolPoleReadinessDto> Poles { get; init; } = [];
}

public sealed class GlobalPoolServiceReadinessDto
{
    public string ServiceId { get; init; } = "";
    public string ServiceName { get; init; } = "";
    public string CelluleId { get; init; } = "";
    public string PoleId { get; init; } = "";
    public bool Ready { get; init; }
    public int FichesTotal { get; init; }
    public int FichesValidated { get; init; }
    public string? BlockingReason { get; init; }
}

public sealed class GlobalPoolCelluleReadinessDto
{
    public string CelluleId { get; init; } = "";
    public string CelluleName { get; init; } = "";
    public string PoleId { get; init; } = "";
    public bool Ready { get; init; }
    public int ServicesReady { get; init; }
    public int ServicesTotal { get; init; }
    public string? BlockingReason { get; init; }
}

public sealed class GlobalPoolPoleReadinessDto
{
    public string PoleId { get; init; } = "";
    public string PoleName { get; init; } = "";
    public bool Ready { get; init; }
    public int CellulesReady { get; init; }
    public int CellulesTotal { get; init; }
    public string? BlockingReason { get; init; }
}

public sealed class GlobalPoolScopeSynthesisInboxItemDto
{
    public Guid ScopeSynthesisId { get; init; }
    public string Period { get; init; } = "";
    public string ScopeType { get; init; } = "";
    public string ScopeId { get; init; } = "";
    public string ScopeDisplayName { get; init; } = "";
    public bool HasFile { get; init; }
    public string? FileName { get; init; }
    public DateTimeOffset? GeneratedAt { get; init; }
    public DateTimeOffset? ManagerApprovedAt { get; init; }
    public DateTimeOffset? RhApprovedAt { get; init; }
    public DateTimeOffset? ComptaAckAt { get; init; }
    public bool PoolDistributionUnlocked { get; init; }
    public bool PendingActionForUser { get; init; }
    public List<GlobalPoolInboxStepStatusDto>? StepStatuses { get; init; }
    public Guid? SuggestedApproveStepId { get; init; }
    /// <summary>Etat paiement déduit : Unpaid | Partial | Paid.</summary>
    public string PaymentState { get; init; } = "Unpaid";
    public int PaidLines { get; init; }
    public int TotalLines { get; init; }
    /// <summary>Avancement par rôle (décisions par ligne, indépendamment du rollup périmètre).</summary>
    public int RhDecidedLines { get; init; }
    public int ManagerDecidedLines { get; init; }
    public int ApprovedLines { get; init; }
    public int RejectedLines { get; init; }
}

public sealed class GlobalSynthesisLineDto
{
    public Guid? LineId { get; init; }
    public Guid FicheId { get; init; }
    public string EmployeeId { get; init; } = "";
    public string EmployeeDisplayName { get; init; } = "";
    public string EmployeeRole { get; init; } = "";
    public string PoleId { get; init; } = "";
    public string PoleName { get; init; } = "";
    public string CelluleId { get; init; } = "";
    public string CelluleName { get; init; } = "";
    public string ServiceId { get; init; } = "";
    public string ServiceName { get; init; } = "";
    public decimal? PrimeAmount { get; init; }
    public decimal? ChallengeAmount { get; init; }
    public decimal? TotalAmount { get; init; }
    public int AbsenceDayCount { get; init; }
    public decimal SanctionAmount { get; init; }
    public decimal RegularizationAmount { get; init; }
    public decimal? NetPayableAmount { get; init; }
    public string ValidationStatus { get; init; } = "";
    public string FillingStatus { get; init; } = "";
    public string? LineStatus { get; init; }
    public string? LineRejectionReason { get; init; }
    public string RhDecision { get; init; } = "Pending";
    public string ManagerDecision { get; init; } = "Pending";
    public string? RhRejectionReason { get; init; }
    public string? ManagerRejectionReason { get; init; }
    public string? RejectedByRole { get; init; }
    public string PaymentStatus { get; init; } = "Unpaid";
    public DateTimeOffset? PaidAt { get; init; }
    public string? PaymentReference { get; init; }
    /// <summary>Permet à la page Synthèse de recalculer les montants via les endpoints superviseur.</summary>
    public string SupervisorUserId { get; init; } = "";
    public string TemplateId { get; init; } = "";
}

public sealed class GlobalSynthesisSummaryDto
{
    public int LineCount { get; init; }
    public decimal TotalPrime { get; init; }
    public decimal TotalChallenge { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal TotalSanction { get; init; }
    public decimal TotalRegularization { get; init; }
    public decimal TotalNetPayable { get; init; }
    public int LinesRejected { get; init; }
}

public sealed class GenerateScopeSynthesisRequest
{
    public string UserId { get; set; } = "";
    public string Period { get; set; } = "";
    public string ScopeType { get; set; } = "";
    public string ScopeId { get; set; } = "";
}

public sealed class RejectSynthesisLineRequest
{
    public string UserId { get; set; } = "";
    public string? Role { get; set; }
    public string Reason { get; set; } = "";
}

/// <summary>Suivi superviseur (pilote) : avancement synthèse + paiement par employé pour ses fiches.</summary>
public sealed class SupervisorSynthesisTrackingItemDto
{
    public Guid FicheId { get; init; }
    public string EmployeeId { get; init; } = "";
    public string EmployeeDisplayName { get; init; } = "";
    public string CelluleName { get; init; } = "";
    public string ServiceName { get; init; } = "";
    public string ValidationStatus { get; init; } = "";
    public string? LineStatus { get; init; }
    public string RhDecision { get; init; } = "Pending";
    public string ManagerDecision { get; init; } = "Pending";
    public string? RhRejectionReason { get; init; }
    public string? ManagerRejectionReason { get; init; }
    public string? RejectedByRole { get; init; }
    public string PaymentStatus { get; init; } = "Unpaid";
    public DateTimeOffset? PaidAt { get; init; }
    public bool ManagerApproved { get; init; }
    public bool RhApproved { get; init; }
    public bool PoolDistributionUnlocked { get; init; }
    public Guid? ScopeSynthesisId { get; init; }
    public string? ScopeLabel { get; init; }
}

/// <summary>Vue pilote : sa fiche de prime (aperçu après double validation) + suivi du paiement.</summary>
public sealed class EmployeePrimePaymentTrackingDto
{
    public Guid FicheId { get; init; }
    public string Period { get; init; } = "";
    public string CelluleName { get; init; } = "";
    public string ServiceName { get; init; } = "";
    public decimal? PrimeAmount { get; init; }
    public decimal? ChallengeAmount { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal? NetPayableAmount { get; init; }
    /// <summary>PendingReview | Approved | LineRejected (null si pas encore dans une synthèse).</summary>
    public string? LineStatus { get; init; }
    public string PaymentStatus { get; init; } = "Unpaid";
    public DateTimeOffset? PaidAt { get; init; }
    public string? PaymentReference { get; init; }
    /// <summary>True si la ligne est validée par la RH : la fiche est consultable/téléchargeable.</summary>
    public bool CanViewFiche { get; init; }
}

public sealed class SetSynthesisLinePaymentRequest
{
    public string UserId { get; set; } = "";
    public string? Role { get; set; }
    public bool Paid { get; set; } = true;
    public DateTimeOffset? PaidAt { get; set; }
    public string? Reference { get; set; }
}

public sealed class UpdateSynthesisLineAdjustmentsRequest
{
    public string UserId { get; set; } = "";
    public string? Role { get; set; }
    public decimal RegularizationAmount { get; set; }
}

public sealed class PrimeAbsenceSanctionConfigDto
{
    public int DivisorDays { get; set; } = 26;
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedByUserId { get; set; }
}

public sealed class SavePrimeAbsenceSanctionConfigRequest
{
    public string UserId { get; set; } = "";
    public string? Role { get; set; }
    public int DivisorDays { get; set; } = 26;
}

public sealed class PaySynthesisAllRequest
{
    public string UserId { get; set; } = "";
    public string? Role { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public string? Reference { get; set; }
}

public sealed class GlobalPoolSynthesisLineHistoryDto
{
    public Guid Id { get; init; }
    public Guid LineId { get; init; }
    public DateTimeOffset At { get; init; }
    public string Action { get; init; } = "";
    public string ActorUserId { get; init; } = "";
    public string ActorRole { get; init; } = "";
    public string? ActorDisplayName { get; init; }
    public string? Comment { get; init; }
}

public sealed class GlobalSynthesisLinesResponseDto
{
    public Guid? ScopeSynthesisId { get; init; }
    public bool ValidationReady { get; init; }
    public List<GlobalSynthesisLineDto> Lines { get; init; } = [];
}
