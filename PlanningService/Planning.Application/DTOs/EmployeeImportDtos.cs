namespace Planning.Application.DTOs;



public class EmployeeImportFieldConfigDto

{

    public string FieldKey { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public bool IsRequiredOnCreate { get; set; }

    public List<string> Aliases { get; set; } = new();

    public int SortOrder { get; set; }

    public bool IsSystemField { get; set; }

    public string DataType { get; set; } = "text";

    public DateTime? CreatedAt { get; set; }

}



public class CreateEmployeeFieldRequest

{

    public string Label { get; set; } = string.Empty;

    public string? FieldKey { get; set; }

    public string DataType { get; set; } = "text";

    public bool IsRequiredOnCreate { get; set; }

    public bool IsEnabled { get; set; } = true;

    public List<string> Aliases { get; set; } = new();

    public int SortOrder { get; set; } = 100;

}



public class UpdateEmployeeFieldRequest

{

    public string Label { get; set; } = string.Empty;

    public string DataType { get; set; } = "text";

    public bool IsRequiredOnCreate { get; set; }

    public bool IsEnabled { get; set; }

    public List<string> Aliases { get; set; } = new();

    public int SortOrder { get; set; }

}



public class EmployeeImportNewFieldDefinitionDto

{

    public string Label { get; set; } = string.Empty;

    public string DataType { get; set; } = "text";

    public bool IsRequiredOnCreate { get; set; }

}



public class UpdateEmployeeImportConfigRequest

{

    public List<EmployeeImportFieldConfigDto> Fields { get; set; } = new();

}



public class EmployeeImportColumnMappingDto

{

    public int ColumnIndex { get; set; }

    public string SourceHeader { get; set; } = string.Empty;

    public string? SuggestedFieldKey { get; set; }

    public string Confidence { get; set; } = "low";

}



public class EmployeeImportOrgHintDto

{

    public string FieldKey { get; set; } = string.Empty;

    public string SourceValue { get; set; } = string.Empty;

    public string? MatchedValue { get; set; }

    public string Confidence { get; set; } = "low";

    public bool IsNewName { get; set; }

}



public class EmployeeImportResolvedRowDto

{

    public int LineNumber { get; set; }

    public string? Email { get; set; }

    public string? RoleName { get; set; }

    public string RoleConfidence { get; set; } = "low";

    public string? Pole { get; set; }

    public string? Cellule { get; set; }

    public string? Service { get; set; }

    public List<EmployeeImportOrgHintDto> OrgHints { get; set; } = new();

}



public class PendingOrgCreationDto

{

    public string Type { get; set; } = string.Empty;

    public string? Pole { get; set; }

    public string? Cellule { get; set; }

    public string? Service { get; set; }

    public string? OperationalDepartment { get; set; }

    public string ConfirmationLabel { get; set; } = string.Empty;

    public List<int> AffectedLineNumbers { get; set; } = new();

    public bool Approved { get; set; } = true;

}



public class EmployeeImportOrgLineIssueDto

{

    public int LineNumber { get; set; }

    public string? Email { get; set; }

    public string Severity { get; set; } = "warning";

    public string Message { get; set; } = string.Empty;

}



public class AcceptedFuzzyOrgMatchDto

{

    public int LineNumber { get; set; }

    public string FieldKey { get; set; } = string.Empty;

    public string SourceValue { get; set; } = string.Empty;

    public string MatchedValue { get; set; } = string.Empty;

}



public class OrgNodeCreatedReportDto

{

    public string NodeType { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Pole { get; set; }

    public string? Cellule { get; set; }

    public int LocalNodeId { get; set; }

    public string? DirectoryNodeId { get; set; }

}



public class EmployeeImportAnalyzeResponse

{

    public Guid ImportSessionId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public int TotalRows { get; set; }

    public List<string> Headers { get; set; } = new();

    public List<EmployeeImportColumnMappingDto> SuggestedMappings { get; set; } = new();

    public List<Dictionary<string, string?>> PreviewRows { get; set; } = new();

    public List<string> Alerts { get; set; } = new();

    public List<EmployeeImportFieldConfigDto> ActiveFields { get; set; } = new();

    public List<PendingOrgCreationDto> PendingOrgCreations { get; set; } = new();

    public List<EmployeeImportResolvedRowDto> ResolvedRows { get; set; } = new();

    public List<EmployeeImportOrgLineIssueDto> OrgLineIssues { get; set; } = new();

}



public class EmployeeImportMappingItemDto

{

    public int ColumnIndex { get; set; }

    public string? FieldKey { get; set; }

    public string Disposition { get; set; } = "map";

    public EmployeeImportNewFieldDefinitionDto? NewFieldDefinition { get; set; }

}



public class EmployeeImportPreviewRequest

{

    public Guid ImportSessionId { get; set; }

    public List<EmployeeImportMappingItemDto> Mappings { get; set; } = new();

    /// <summary>Index 0-based de la première ligne à inclure dans l'aperçu.</summary>
    public int Skip { get; set; }

    /// <summary>Nombre de lignes d'aperçu (défaut 50, max 200).</summary>
    public int Take { get; set; } = 50;

}



public class EmployeeImportPreviewResponse

{

    public List<Dictionary<string, string?>> PreviewRows { get; set; } = new();

    public List<string> ExtraFieldKeys { get; set; } = new();

    public List<EmployeeImportFieldConfigDto> ActiveFields { get; set; } = new();

    public int TotalRows { get; set; }

    public int Skip { get; set; }

    public int Take { get; set; }

}



public class EmployeeImportRevalidateOrgRequest
{
    public Guid ImportSessionId { get; set; }
    public List<EmployeeImportMappingItemDto> Mappings { get; set; } = new();
}

public class EmployeeImportRevalidateOrgResponse
{
    public List<PendingOrgCreationDto> PendingOrgCreations { get; set; } = new();
    public List<EmployeeImportResolvedRowDto> ResolvedRows { get; set; } = new();
    public List<EmployeeImportOrgLineIssueDto> OrgLineIssues { get; set; } = new();
}

public class EmployeeImportExecuteRequest

{

    public Guid ImportSessionId { get; set; }

    public List<EmployeeImportMappingItemDto> Mappings { get; set; } = new();

    public bool ConfirmOrgProvision { get; set; }

    public List<PendingOrgCreationDto> ApprovedOrgCreations { get; set; } = new();

    public List<AcceptedFuzzyOrgMatchDto> AcceptedFuzzyMatches { get; set; } = new();

}



public class EmployeeImportRowResultDto

{

    public int LineNumber { get; set; }

    public string? Email { get; set; }

    public string Action { get; set; } = string.Empty;

    public string? Message { get; set; }

    /// <summary>Mot de passe one-shot pour les créations (non stocké en historique durable).</summary>
    public string? TemporaryPassword { get; set; }

    public string? FirstName { get; set; }

    public string? LastName { get; set; }

}



public class EmployeeImportReportDto

{

    public Guid ImportJobId { get; set; }

    public int TotalLignes { get; set; }

    public int ProcessedLignes { get; set; }

    /// <summary>Running | Completed | Failed</summary>
    public string Status { get; set; } = "Completed";

    public string? ErrorMessage { get; set; }

    public int Crees { get; set; }

    public int MisAJour { get; set; }

    public int Ignores { get; set; }

    public int Erreurs { get; set; }

    public DateTime CompletedAt { get; set; }

    public List<EmployeeImportRowResultDto> Lignes { get; set; } = new();

    public List<OrgNodeCreatedReportDto> OrgNodesCreated { get; set; } = new();

    public List<string> OrgNodesSkipped { get; set; } = new();

}



public class EmployeeImportJobSummaryDto

{

    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public bool HasSourceFile { get; set; }

    public int TotalLignes { get; set; }

    public int ProcessedLignes { get; set; }

    public string Status { get; set; } = "Completed";

    public int Crees { get; set; }

    public int MisAJour { get; set; }

    public int Ignores { get; set; }

    public int Erreurs { get; set; }

    public string? StartedByEmail { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

}



public class CreateUserFromImportDto : CreateUserDto

{

    public string? Password { get; set; }

    public bool? IsActiveOnImport { get; set; }

    public Guid? ParentEmployeeId { get; set; }

    public bool? IsNewEmployee { get; set; }

}

/// <summary>Création Planning après Directory bulk (import chunk).</summary>
public sealed class ImportChunkCreateItemDto
{
    public CreateUserFromImportDto Dto { get; set; } = null!;
    public Guid DirectoryEmployeeId { get; set; }
    public string RoleName { get; set; } = string.Empty;
}

public sealed class ImportChunkCreateResultDto
{
    public string Email { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int? PlanningUserId { get; set; }
    public Guid? EmployeeGuid { get; set; }
    public int? AuthUserId { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Mot de passe généré ou fourni — visible une seule fois dans le résultat d'import.</summary>
    public string? TemporaryPassword { get; set; }
}

public class EmployeeImportRollbackResultDto
{
    public Guid JobId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; }
    public int CreatedDeleted { get; set; }
    public int DirectoryAssignmentsDeleted { get; set; }
    public int DirectoryAssignmentsRestored { get; set; }
    public List<string> DeletedEmails { get; set; } = [];
    public string Message { get; set; } = string.Empty;
}

public class EmployeeImportRollbackByTimeRequest
{
    public DateTime StartedAt { get; set; }
    public string? FileNameContains { get; set; }
}

public class EmployeeImportPlacementRestoreItemDto
{
    public string Email { get; set; } = string.Empty;
    public string? CurrentLabel { get; set; }
    public string? PreviousLabel { get; set; }
    public string Action { get; set; } = string.Empty;
}

public class EmployeeImportPlacementRestoreResultDto
{
    public Guid JobId { get; set; }
    public bool DryRun { get; set; }
    public int Restored { get; set; }
    public int SkippedRotation { get; set; }
    public int SkippedAlready { get; set; }
    public int SkippedNoHistory { get; set; }
    public List<EmployeeImportPlacementRestoreItemDto> Items { get; set; } = [];
    public string Message { get; set; } = string.Empty;
}

public class EmployeeVariableMatchSourceDto
{
    public string Name { get; set; } = string.Empty;
    public string? DisplayLabel { get; set; }
    public string? NormalizedName { get; set; }
    public string? RawPlaceholder { get; set; }
}

public class EmployeeVariableMatchRequest
{
    public List<EmployeeVariableMatchSourceDto> Sources { get; set; } = new();
}

public class EmployeeVariableMatchDto
{
    public string SourceName { get; set; } = string.Empty;
    public string? SuggestedFieldKey { get; set; }
    public string? SuggestedFieldLabel { get; set; }
    public double Score { get; set; }
    public string Confidence { get; set; } = "low";
    public bool IsEnabled { get; set; }
    public bool IsSystemField { get; set; }
}

public class EmployeeDocumentFieldValuesDto
{
    public Guid EmployeeGuid { get; set; }
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> UnavailableKeys { get; set; } = [];
}


