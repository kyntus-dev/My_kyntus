using Formation.Domain.Enums;

namespace Formation.Domain.Entities;

public class InitialTrainingPath
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public DateTime DateDebut { get; set; }
    public DateTime DateFinPrevue { get; set; }
    public InitialTrainingStatus Status { get; set; } = InitialTrainingStatus.EnCours;
    public decimal? QuizScore { get; set; }
    public bool? QuizPassed { get; set; }
    public string? QuizRecordedBy { get; set; }
    public string? FormateurComment { get; set; }
    public DateTime? FormateurValidatedAt { get; set; }
    public Guid? FormateurValidatedBy { get; set; }
    public DateTime? RhValidatedAt { get; set; }
    public Guid? RhValidatedBy { get; set; }
    public string? OrgDepartmentId { get; set; }
    public string? OrgPoleId { get; set; }
    public string? OrgCelluleId { get; set; }
    public string? OrgServiceId { get; set; }
    public string? OrgDepartmentName { get; set; }
    public string? OrgPoleName { get; set; }
    public string? OrgCelluleName { get; set; }
    public string? OrgServiceName { get; set; }
    public string? RejectedBy { get; set; }
    public string? RejectReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<InitialTrainingQuizResult> QuizResults { get; set; } = new List<InitialTrainingQuizResult>();
    public ICollection<FormationDocumentChecklistItem> DocumentChecklistItems { get; set; } = new List<FormationDocumentChecklistItem>();
}
