namespace Prime.Infrastructure.Persistence;

/// <summary>Synthèse globale PRIME par période et périmètre (service, cellule ou pôle).</summary>
public class GlobalPoolScopeSynthesisEntity
{
    public Guid Id { get; set; }
    public string Period { get; set; } = "";
    /// <summary>Service | Cellule | Pole</summary>
    public string ScopeType { get; set; } = "";
    public string ScopeId { get; set; } = "";
    public string ScopeDisplayName { get; set; } = "";

    public byte[]? ExcelContent { get; set; }
    public string? FileName { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public string? GeneratedByUserId { get; set; }

    public DateTimeOffset? ManagerApprovedAt { get; set; }
    public string? ManagerApprovedByUserId { get; set; }
    public DateTimeOffset? RhApprovedAt { get; set; }
    public string? RhApprovedByUserId { get; set; }
    public DateTimeOffset? ComptaAckAt { get; set; }
    public string? ComptaAckByUserId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<GlobalPoolSynthesisLineEntity> Lines { get; set; } = new List<GlobalPoolSynthesisLineEntity>();
    public ICollection<GlobalPoolApprovalEntity> GlobalPoolApprovals { get; set; } = new List<GlobalPoolApprovalEntity>();
}

/// <summary>Ligne employé figée dans une synthèse globale (pool RH / Manager).</summary>
public class GlobalPoolSynthesisLineEntity
{
    public Guid Id { get; set; }
    public Guid ScopeSynthesisId { get; set; }
    public GlobalPoolScopeSynthesisEntity ScopeSynthesis { get; set; } = null!;
    public Guid FicheId { get; set; }
    public string EmployeeId { get; set; } = "";
    public string ServiceId { get; set; } = "";
    public decimal? PrimeAmount { get; set; }
    public decimal? ChallengeAmount { get; set; }
    public decimal? TotalAmount { get; set; }
    /// <summary>PendingReview | Approved | LineRejected (dérivé de la décision RH)</summary>
    public string LineStatus { get; set; } = "PendingReview";
    /// <summary>Pending | Approved | Rejected</summary>
    public string RhDecision { get; set; } = "Pending";
    public string? RhDecidedByUserId { get; set; }
    public DateTimeOffset? RhDecidedAt { get; set; }
    public string? RhRejectionReason { get; set; }
    /// <summary>Pending | Approved | Rejected</summary>
    public string ManagerDecision { get; set; } = "Pending";
    public string? ManagerDecidedByUserId { get; set; }
    public DateTimeOffset? ManagerDecidedAt { get; set; }
    public string? ManagerRejectionReason { get; set; }
    public string? RejectedByUserId { get; set; }
    public string? RejectedByRole { get; set; }
    public DateTimeOffset? RejectedAt { get; set; }
    public string? RejectionReason { get; set; }

    /// <summary>Paiement par employé (comptable) : Unpaid | Paid</summary>
    public string PaymentStatus { get; set; } = "Unpaid";
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaidByUserId { get; set; }
    public string? PaymentReference { get; set; }

    public int AbsenceDayCount { get; set; }
    public decimal SanctionAmount { get; set; }
    public decimal RegularizationAmount { get; set; }
    public decimal? NetPayableAmount { get; set; }
    public DateTimeOffset? AbsenceComputedAt { get; set; }
    public string? RegularizationUpdatedByUserId { get; set; }
    public DateTimeOffset? RegularizationUpdatedAt { get; set; }

    public ICollection<GlobalPoolSynthesisLineHistoryEntity> History { get; set; } =
        new List<GlobalPoolSynthesisLineHistoryEntity>();
}

public class GlobalPoolSynthesisLineHistoryEntity
{
    public Guid Id { get; set; }
    public Guid LineId { get; set; }
    public GlobalPoolSynthesisLineEntity Line { get; set; } = null!;
    public DateTimeOffset At { get; set; }
    /// <summary>Approved | LineRejected | Paid | Unpaid</summary>
    public string Action { get; set; } = "";
    public string ActorUserId { get; set; } = "";
    public string ActorRole { get; set; } = "";
    public string? Comment { get; set; }
}
