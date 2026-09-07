using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using Kyntus.Iam;
using MediatR;

namespace EmployeeDirectory.Application.Iam;

public record GetEffectivePermissionsQuery(Guid SubjectId, string Role) : IRequest<EffectivePermissionsDto>;

public sealed class GetEffectivePermissionsQueryHandler(IIamReadService iam)
    : IRequestHandler<GetEffectivePermissionsQuery, EffectivePermissionsDto>
{
    public Task<EffectivePermissionsDto> Handle(GetEffectivePermissionsQuery request, CancellationToken ct) =>
        iam.GetEffectivePermissionsAsync(request.SubjectId, request.Role, ct);
}

public record EvaluatePolicyCommand(Guid SubjectId, string Role, string Action, string ResourceType, string? ResourceId)
    : IRequest<PolicyDecision>;

public sealed class EvaluatePolicyCommandHandler(IPolicyEvaluator evaluator)
    : IRequestHandler<EvaluatePolicyCommand, PolicyDecision>
{
    public Task<PolicyDecision> Handle(EvaluatePolicyCommand request, CancellationToken ct) =>
        evaluator.EvaluateAsync(
            new PolicyRequest(request.SubjectId, request.Role, request.Action, request.ResourceType, request.ResourceId),
            ct);
}

public record GetIamCatalogQuery() : IRequest<IamCatalogDto>;

public sealed class GetIamCatalogQueryHandler(IIamAdminService iam)
    : IRequestHandler<GetIamCatalogQuery, IamCatalogDto>
{
    public Task<IamCatalogDto> Handle(GetIamCatalogQuery request, CancellationToken ct) =>
        iam.GetCatalogAsync(ct);
}

public record ListIamPermissionsQuery(string? Role) : IRequest<IReadOnlyList<IamPermissionRowDto>>;

public sealed class ListIamPermissionsQueryHandler(IIamAdminService iam)
    : IRequestHandler<ListIamPermissionsQuery, IReadOnlyList<IamPermissionRowDto>>
{
    public Task<IReadOnlyList<IamPermissionRowDto>> Handle(ListIamPermissionsQuery request, CancellationToken ct) =>
        iam.ListAsync(request.Role, ct);
}

public record GetIamMatrixQuery(string Role, string? Scope = null) : IRequest<IamMatrixDto>;

public sealed class GetIamMatrixQueryHandler(IIamAdminService iam)
    : IRequestHandler<GetIamMatrixQuery, IamMatrixDto>
{
    public Task<IamMatrixDto> Handle(GetIamMatrixQuery request, CancellationToken ct) =>
        iam.GetMatrixAsync(request.Role, request.Scope, ct);
}

public record UpsertIamPermissionCommand(UpsertIamPermissionRequest Body) : IRequest<IamPermissionRowDto>;

public sealed class UpsertIamPermissionCommandHandler(IIamAdminService iam)
    : IRequestHandler<UpsertIamPermissionCommand, IamPermissionRowDto>
{
    public Task<IamPermissionRowDto> Handle(UpsertIamPermissionCommand request, CancellationToken ct) =>
        iam.UpsertAsync(request.Body, ct);
}

public record UpsertIamPermissionsBulkCommand(IReadOnlyList<UpsertIamPermissionRequest> Items) : IRequest<int>;

public sealed class UpsertIamPermissionsBulkCommandHandler(IIamAdminService iam)
    : IRequestHandler<UpsertIamPermissionsBulkCommand, int>
{
    public Task<int> Handle(UpsertIamPermissionsBulkCommand request, CancellationToken ct) =>
        iam.UpsertBulkAsync(request.Items, ct);
}

public record GetLeaveWorkflowQuery() : IRequest<LeaveWorkflowDto>;

public sealed class GetLeaveWorkflowQueryHandler(IIamReadService iam)
    : IRequestHandler<GetLeaveWorkflowQuery, LeaveWorkflowDto>
{
    public async Task<LeaveWorkflowDto> Handle(GetLeaveWorkflowQuery request, CancellationToken ct)
    {
        var keys = await iam.GetEffectivePermissionsAsync(Guid.Empty, "RH", ct);
        var required = keys.Permissions.Any(p =>
            p.StartsWith("conges.validation.Validate:", StringComparison.OrdinalIgnoreCase));
        return new LeaveWorkflowDto(required);
    }
}
