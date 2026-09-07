using Prime.Application.DTOs;
using Prime.Domain.Entities;

namespace Prime.Application.Abstractions;

public interface IServicePrimeIndicatorsAppService
{
    Task<IReadOnlyList<ServicePrimeIndicatorDto>> GetAsync(
        string serviceId,
        string supervisorUserId,
        string? employeeId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ServicePrimeIndicatorDto>> PutAsync(
        string serviceId,
        string supervisorUserId,
        PutServicePrimeIndicatorsRequest body,
        CancellationToken ct = default);

    Task<ServicePrimeIndicatorAgentOverridesResponse> GetAgentOverridesAsync(
        string serviceId,
        string supervisorUserId,
        CancellationToken ct = default);

    Task<ServicePrimeIndicatorAgentOverridesResponse> PutAgentOverridesAsync(
        string serviceId,
        string supervisorUserId,
        PutServicePrimeIndicatorAgentOverridesRequest body,
        CancellationToken ct = default);
}

public interface IPrimePeriodRecapReportsAppService
{
    Task<FileExportResultDto> DownloadPeriodRecapAsync(
        string period,
        string actingUserId,
        CancellationToken ct = default);
}

public interface IPrimeFicheImportAppService
{
    Task<(bool Ok, string? Error, ImportReadyFicheResponseDto? Result)> ImportAsync(
        ImportReadyFicheRequest body,
        CancellationToken ct = default);

    Task<IReadOnlyList<PrimeHistoricalFicheListItemDto>> ListHistoricalAsync(
        string supervisorUserId,
        string? period,
        string? role,
        CancellationToken ct = default);

    Task<(bool Ok, string? Error, PrimeHistoricalFicheDetailSnapshotDto? Result)> GetHistoricalDetailSnapshotAsync(
        Guid id,
        string supervisorUserId,
        string? role,
        CancellationToken ct = default);
}

public sealed record PrimeHealthStatusDto(string Status, string? Mode, string? Database, string? Error);

public interface IPrimeCoreQueryAppService
{
    Task<PrimeHealthStatusDto> GetHealthAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Department>> GetLegacyDepartmentsAsync(CancellationToken ct = default);
    Task<OperationalOrgTreeDto> GetOperationalDepartmentsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Employee>> GetLegacyEmployeesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PrimeResult>> GetPrimeResultsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PrimeResult>> GetMyPrimeResultsAsync(string employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<PrimeType>> GetPrimeTypesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PrimeRule>> GetPrimeRulesAsync(CancellationToken ct = default);
    Task<object> GetDashboardStatsAsync(CancellationToken ct = default);
}

public sealed record AllowanceUserContextDto(
    string UserId,
    string Role,
    string? BusinessDepartmentId,
    string? BusinessDepartmentKind,
    bool IsSupportDepartmentManager,
    bool IsOperationalDepartmentManager,
    string? ManagedDepartmentId,
    string? ManagedDepartmentKind,
    string? ManagedDepartmentName,
    string? ManagedDepartmentCode,
    int DirectReportCount);

public interface IAllowanceQueryAppService
{
    Task<IReadOnlyList<BusinessDepartmentMirrorDto>> ListBusinessDepartmentsAsync(CancellationToken ct = default);
    Task<AllowanceUserContextDto> GetMyContextAsync(string userId, string role, CancellationToken ct = default);
    Task<IReadOnlyList<object>> GetTeamAsync(string userId, CancellationToken ct = default);
}
