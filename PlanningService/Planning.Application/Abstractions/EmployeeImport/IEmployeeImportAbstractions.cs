using Microsoft.AspNetCore.Http;
using Planning.Application.DTOs;

namespace Planning.Application.Abstractions.EmployeeImport;

public sealed record EmployeeImportSourceFile(string FileName, byte[] Content, string? ContentType);

public interface IEmployeeImportService
{
    Task<EmployeeImportAnalyzeResponse> AnalyzeAsync(IFormFile file, CancellationToken ct = default);
    Task<EmployeeImportRevalidateOrgResponse> RevalidateOrgAsync(
        EmployeeImportRevalidateOrgRequest request,
        CancellationToken ct = default);
    Task<EmployeeImportPreviewResponse> PreviewAsync(
        EmployeeImportPreviewRequest request,
        CancellationToken ct = default);
    Task<EmployeeImportReportDto> ExecuteAsync(
        EmployeeImportExecuteRequest request,
        string? startedByEmail,
        CancellationToken ct = default);
    Task<List<EmployeeImportJobSummaryDto>> GetHistoryAsync(int take = 50, CancellationToken ct = default);
    Task<EmployeeImportReportDto?> GetJobReportAsync(Guid jobId, CancellationToken ct = default);
    Task<EmployeeImportSourceFile?> GetJobSourceFileAsync(Guid jobId, CancellationToken ct = default);
    Task<EmployeeImportRollbackResultDto> RollbackJobAsync(Guid jobId, CancellationToken ct = default);
    Task<EmployeeImportRollbackResultDto> RollbackJobByStartedAtAsync(
        DateTime startedAt,
        string? fileNameContains,
        CancellationToken ct = default);
    Task<EmployeeImportPlacementRestoreResultDto> RestorePlacementsAfterImportAsync(
        Guid jobId,
        bool dryRun,
        CancellationToken ct = default);
    Task<byte[]> BuildTemplateAsync(CancellationToken ct = default);
}

public interface IEmployeeImportConfigService
{
    Task<List<EmployeeImportFieldConfigDto>> GetConfigAsync(CancellationToken ct = default);
    Task<List<EmployeeImportFieldConfigDto>> UpdateConfigAsync(
        UpdateEmployeeImportConfigRequest request,
        CancellationToken ct = default);
    Task EnsureSeedAsync(CancellationToken ct = default);
    Task<List<FieldMatchTarget>> GetActiveFieldTargetsAsync(CancellationToken ct = default);
}

public interface IEmployeeFieldService
{
    Task EnsureSeedAsync(CancellationToken ct = default);
    Task<List<EmployeeImportFieldConfigDto>> GetAllAsync(bool enabledOnly = false, CancellationToken ct = default);
    Task<EmployeeImportFieldConfigDto> CreateCustomFieldAsync(CreateEmployeeFieldRequest request, CancellationToken ct = default);
    Task<EmployeeImportFieldConfigDto?> UpdateFieldAsync(string fieldKey, UpdateEmployeeFieldRequest request, CancellationToken ct = default);
    Task<bool> DeleteCustomFieldAsync(string fieldKey, CancellationToken ct = default);
    Task<List<EmployeeImportMappingItemDto>> ResolveImportMappingsAsync(
        IReadOnlyList<EmployeeImportMappingItemDto> mappings,
        IReadOnlyList<string> headers,
        CancellationToken ct = default);
    Task UpsertCustomFieldsAsync(
        int userId,
        Dictionary<string, string?> values,
        bool isCreate,
        CancellationToken ct = default);
    Task<Dictionary<int, Dictionary<string, string?>>> LoadCustomFieldsForUsersAsync(
        IReadOnlyList<int> userIds,
        CancellationToken ct = default);
    Task ValidateCustomFieldsForCreateAsync(
        Dictionary<string, string?> values,
        CancellationToken ct = default);
}

public interface IEmployeeDocumentFieldQueryService
{
    Task<List<EmployeeVariableMatchDto>> MatchVariablesAsync(
        IReadOnlyList<EmployeeVariableMatchSourceDto> sources,
        CancellationToken ct = default);

    Task<EmployeeDocumentFieldValuesDto?> GetFieldValuesAsync(
        Guid employeeGuid,
        IReadOnlyList<string>? includeKeys,
        CancellationToken ct = default);
}
