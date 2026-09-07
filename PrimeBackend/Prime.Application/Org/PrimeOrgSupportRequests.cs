using MediatR;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Domain.Entities;

namespace Prime.Application.Org;

public record GetServicePrimeIndicatorsQuery(string ServiceId, string SupervisorUserId, string? EmployeeId = null)
    : IRequest<IReadOnlyList<ServicePrimeIndicatorDto>>;

public sealed class GetServicePrimeIndicatorsQueryHandler(IServicePrimeIndicatorsAppService indicators)
    : IRequestHandler<GetServicePrimeIndicatorsQuery, IReadOnlyList<ServicePrimeIndicatorDto>>
{
    public Task<IReadOnlyList<ServicePrimeIndicatorDto>> Handle(GetServicePrimeIndicatorsQuery request, CancellationToken ct) =>
        indicators.GetAsync(request.ServiceId, request.SupervisorUserId, request.EmployeeId, ct);
}

public record PutServicePrimeIndicatorsCommand(string ServiceId, string SupervisorUserId, PutServicePrimeIndicatorsRequest Body)
    : IRequest<IReadOnlyList<ServicePrimeIndicatorDto>>;

public sealed class PutServicePrimeIndicatorsCommandHandler(IServicePrimeIndicatorsAppService indicators)
    : IRequestHandler<PutServicePrimeIndicatorsCommand, IReadOnlyList<ServicePrimeIndicatorDto>>
{
    public Task<IReadOnlyList<ServicePrimeIndicatorDto>> Handle(PutServicePrimeIndicatorsCommand request, CancellationToken ct) =>
        indicators.PutAsync(request.ServiceId, request.SupervisorUserId, request.Body, ct);
}

public record GetServicePrimeIndicatorAgentOverridesQuery(string ServiceId, string SupervisorUserId)
    : IRequest<ServicePrimeIndicatorAgentOverridesResponse>;

public sealed class GetServicePrimeIndicatorAgentOverridesQueryHandler(IServicePrimeIndicatorsAppService indicators)
    : IRequestHandler<GetServicePrimeIndicatorAgentOverridesQuery, ServicePrimeIndicatorAgentOverridesResponse>
{
    public Task<ServicePrimeIndicatorAgentOverridesResponse> Handle(
        GetServicePrimeIndicatorAgentOverridesQuery request,
        CancellationToken ct) =>
        indicators.GetAgentOverridesAsync(request.ServiceId, request.SupervisorUserId, ct);
}

public record PutServicePrimeIndicatorAgentOverridesCommand(
    string ServiceId,
    string SupervisorUserId,
    PutServicePrimeIndicatorAgentOverridesRequest Body)
    : IRequest<ServicePrimeIndicatorAgentOverridesResponse>;

public sealed class PutServicePrimeIndicatorAgentOverridesCommandHandler(IServicePrimeIndicatorsAppService indicators)
    : IRequestHandler<PutServicePrimeIndicatorAgentOverridesCommand, ServicePrimeIndicatorAgentOverridesResponse>
{
    public Task<ServicePrimeIndicatorAgentOverridesResponse> Handle(
        PutServicePrimeIndicatorAgentOverridesCommand request,
        CancellationToken ct) =>
        indicators.PutAgentOverridesAsync(request.ServiceId, request.SupervisorUserId, request.Body, ct);
}

public record DownloadPeriodRecapReportQuery(string Period, string ActingUserId) : IRequest<FileExportResultDto>;

public sealed class DownloadPeriodRecapReportQueryHandler(IPrimePeriodRecapReportsAppService reports)
    : IRequestHandler<DownloadPeriodRecapReportQuery, FileExportResultDto>
{
    public Task<FileExportResultDto> Handle(DownloadPeriodRecapReportQuery request, CancellationToken ct) =>
        reports.DownloadPeriodRecapAsync(request.Period, request.ActingUserId, ct);
}

public record ImportPrimeFicheCommand(ImportReadyFicheRequest Body) : IRequest<ImportReadyFicheResponseDto>;

public sealed class ImportPrimeFicheCommandHandler(IPrimeFicheImportAppService import)
    : IRequestHandler<ImportPrimeFicheCommand, ImportReadyFicheResponseDto>
{
    public async Task<ImportReadyFicheResponseDto> Handle(ImportPrimeFicheCommand request, CancellationToken ct)
    {
        var (ok, err, result) = await import.ImportAsync(request.Body, ct);
        if (!ok || result is null)
            throw new InvalidOperationException(err ?? "Import impossible.");
        return result;
    }
}

public record ListHistoricalPrimeFichesQuery(string SupervisorUserId, string? Period, string? Role)
    : IRequest<IReadOnlyList<PrimeHistoricalFicheListItemDto>>;

public sealed class ListHistoricalPrimeFichesQueryHandler(IPrimeFicheImportAppService import)
    : IRequestHandler<ListHistoricalPrimeFichesQuery, IReadOnlyList<PrimeHistoricalFicheListItemDto>>
{
    public Task<IReadOnlyList<PrimeHistoricalFicheListItemDto>> Handle(ListHistoricalPrimeFichesQuery request, CancellationToken ct) =>
        import.ListHistoricalAsync(request.SupervisorUserId, request.Period, request.Role, ct);
}

public record GetHistoricalPrimeFicheDetailQuery(Guid Id, string SupervisorUserId, string? Role)
    : IRequest<PrimeHistoricalFicheDetailSnapshotDto>;

public sealed class GetHistoricalPrimeFicheDetailQueryHandler(IPrimeFicheImportAppService import)
    : IRequestHandler<GetHistoricalPrimeFicheDetailQuery, PrimeHistoricalFicheDetailSnapshotDto>
{
    public async Task<PrimeHistoricalFicheDetailSnapshotDto> Handle(GetHistoricalPrimeFicheDetailQuery request, CancellationToken ct)
    {
        var (ok, err, result) = await import.GetHistoricalDetailSnapshotAsync(
            request.Id, request.SupervisorUserId, request.Role, ct);
        if (!ok || result is null)
            throw new InvalidOperationException(err ?? "Lecture impossible.");
        return result;
    }
}

public record GetPrimeHealthQuery : IRequest<PrimeHealthStatusDto>;

public sealed class GetPrimeHealthQueryHandler(IPrimeCoreQueryAppService core)
    : IRequestHandler<GetPrimeHealthQuery, PrimeHealthStatusDto>
{
    public Task<PrimeHealthStatusDto> Handle(GetPrimeHealthQuery request, CancellationToken ct) =>
        core.GetHealthAsync(ct);
}

public record GetLegacyDepartmentsQuery : IRequest<IReadOnlyList<Department>>;

public sealed class GetLegacyDepartmentsQueryHandler(IPrimeCoreQueryAppService core)
    : IRequestHandler<GetLegacyDepartmentsQuery, IReadOnlyList<Department>>
{
    public Task<IReadOnlyList<Department>> Handle(GetLegacyDepartmentsQuery request, CancellationToken ct) =>
        core.GetLegacyDepartmentsAsync(ct);
}

public record GetOperationalDepartmentsQuery : IRequest<OperationalOrgTreeDto>;

public sealed class GetOperationalDepartmentsQueryHandler(IPrimeCoreQueryAppService core)
    : IRequestHandler<GetOperationalDepartmentsQuery, OperationalOrgTreeDto>
{
    public Task<OperationalOrgTreeDto> Handle(GetOperationalDepartmentsQuery request, CancellationToken ct) =>
        core.GetOperationalDepartmentsAsync(ct);
}

public record GetLegacyEmployeesQuery : IRequest<IReadOnlyList<Employee>>;

public sealed class GetLegacyEmployeesQueryHandler(IPrimeCoreQueryAppService core)
    : IRequestHandler<GetLegacyEmployeesQuery, IReadOnlyList<Employee>>
{
    public Task<IReadOnlyList<Employee>> Handle(GetLegacyEmployeesQuery request, CancellationToken ct) =>
        core.GetLegacyEmployeesAsync(ct);
}

public record GetPrimeResultsQuery : IRequest<IReadOnlyList<PrimeResult>>;

public sealed class GetPrimeResultsQueryHandler(IPrimeCoreQueryAppService core)
    : IRequestHandler<GetPrimeResultsQuery, IReadOnlyList<PrimeResult>>
{
    public Task<IReadOnlyList<PrimeResult>> Handle(GetPrimeResultsQuery request, CancellationToken ct) =>
        core.GetPrimeResultsAsync(ct);
}

public record GetMyPrimeResultsQuery(string EmployeeId) : IRequest<IReadOnlyList<PrimeResult>>;

public sealed class GetMyPrimeResultsQueryHandler(IPrimeCoreQueryAppService core)
    : IRequestHandler<GetMyPrimeResultsQuery, IReadOnlyList<PrimeResult>>
{
    public Task<IReadOnlyList<PrimeResult>> Handle(GetMyPrimeResultsQuery request, CancellationToken ct) =>
        core.GetMyPrimeResultsAsync(request.EmployeeId, ct);
}

public record GetPrimeTypesQuery : IRequest<IReadOnlyList<PrimeType>>;

public sealed class GetPrimeTypesQueryHandler(IPrimeCoreQueryAppService core)
    : IRequestHandler<GetPrimeTypesQuery, IReadOnlyList<PrimeType>>
{
    public Task<IReadOnlyList<PrimeType>> Handle(GetPrimeTypesQuery request, CancellationToken ct) =>
        core.GetPrimeTypesAsync(ct);
}

public record GetPrimeRulesQuery : IRequest<IReadOnlyList<PrimeRule>>;

public sealed class GetPrimeRulesQueryHandler(IPrimeCoreQueryAppService core)
    : IRequestHandler<GetPrimeRulesQuery, IReadOnlyList<PrimeRule>>
{
    public Task<IReadOnlyList<PrimeRule>> Handle(GetPrimeRulesQuery request, CancellationToken ct) =>
        core.GetPrimeRulesAsync(ct);
}

public record GetPrimeDashboardStatsQuery : IRequest<object>;

public sealed class GetPrimeDashboardStatsQueryHandler(IPrimeCoreQueryAppService core)
    : IRequestHandler<GetPrimeDashboardStatsQuery, object>
{
    public Task<object> Handle(GetPrimeDashboardStatsQuery request, CancellationToken ct) =>
        core.GetDashboardStatsAsync(ct);
}

public record ListAllowanceBusinessDepartmentsQuery : IRequest<IReadOnlyList<BusinessDepartmentMirrorDto>>;

public sealed class ListAllowanceBusinessDepartmentsQueryHandler(IAllowanceQueryAppService allowance)
    : IRequestHandler<ListAllowanceBusinessDepartmentsQuery, IReadOnlyList<BusinessDepartmentMirrorDto>>
{
    public Task<IReadOnlyList<BusinessDepartmentMirrorDto>> Handle(ListAllowanceBusinessDepartmentsQuery request, CancellationToken ct) =>
        allowance.ListBusinessDepartmentsAsync(ct);
}

public record GetAllowanceMyContextQuery(string UserId, string Role) : IRequest<AllowanceUserContextDto>;

public sealed class GetAllowanceMyContextQueryHandler(IAllowanceQueryAppService allowance)
    : IRequestHandler<GetAllowanceMyContextQuery, AllowanceUserContextDto>
{
    public Task<AllowanceUserContextDto> Handle(GetAllowanceMyContextQuery request, CancellationToken ct) =>
        allowance.GetMyContextAsync(request.UserId, request.Role, ct);
}

public record GetAllowanceTeamQuery(string UserId) : IRequest<IReadOnlyList<object>>;

public sealed class GetAllowanceTeamQueryHandler(IAllowanceQueryAppService allowance)
    : IRequestHandler<GetAllowanceTeamQuery, IReadOnlyList<object>>
{
    public Task<IReadOnlyList<object>> Handle(GetAllowanceTeamQuery request, CancellationToken ct) =>
        allowance.GetTeamAsync(request.UserId, ct);
}
