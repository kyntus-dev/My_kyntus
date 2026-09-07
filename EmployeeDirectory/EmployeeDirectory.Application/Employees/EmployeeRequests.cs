using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using MediatR;

namespace EmployeeDirectory.Application.Employees;

public record ListEmployeesQuery(string? Role, string? PoleId) : IRequest<IReadOnlyList<EmployeeDto>>;

public sealed class ListEmployeesQueryHandler(IDirectoryReadService read)
    : IRequestHandler<ListEmployeesQuery, IReadOnlyList<EmployeeDto>>
{
    public Task<IReadOnlyList<EmployeeDto>> Handle(ListEmployeesQuery request, CancellationToken ct) =>
        read.GetEmployeesAsync(request.Role, request.PoleId, ct);
}

public record CheckEmployeeEmailQuery(string Email, Guid? ExcludeId) : IRequest<bool>;

public sealed class CheckEmployeeEmailQueryHandler(IDirectoryReadService read)
    : IRequestHandler<CheckEmployeeEmailQuery, bool>
{
    public Task<bool> Handle(CheckEmployeeEmailQuery request, CancellationToken ct) =>
        read.IsEmailUsedAsync(request.Email, request.ExcludeId, ct);
}

public record GetEmployeeByIdQuery(Guid Id) : IRequest<EmployeeDto?>;

public sealed class GetEmployeeByIdQueryHandler(IDirectoryReadService read)
    : IRequestHandler<GetEmployeeByIdQuery, EmployeeDto?>
{
    public Task<EmployeeDto?> Handle(GetEmployeeByIdQuery request, CancellationToken ct) =>
        read.GetEmployeeByIdAsync(request.Id, ct);
}

public record GetEmployeeByEmailQuery(string Email) : IRequest<EmployeeDto?>;

public sealed class GetEmployeeByEmailQueryHandler(IDirectoryReadService read)
    : IRequestHandler<GetEmployeeByEmailQuery, EmployeeDto?>
{
    public Task<EmployeeDto?> Handle(GetEmployeeByEmailQuery request, CancellationToken ct) =>
        read.GetEmployeeByEmailAsync(request.Email, ct);
}

public record CreateEmployeeCommand(CreateEmployeeRequest Body, Guid? ChangedBy) : IRequest<EmployeeDto>;

public sealed class CreateEmployeeCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<CreateEmployeeCommand, EmployeeDto>
{
    public Task<EmployeeDto> Handle(CreateEmployeeCommand request, CancellationToken ct) =>
        write.CreateEmployeeAsync(request.Body, request.ChangedBy, ct);
}

public record BulkCreateEmployeesCommand(BulkCreateEmployeesRequest Body, Guid? ChangedBy)
    : IRequest<IReadOnlyList<BulkCreateEmployeeResult>>;

public sealed class BulkCreateEmployeesCommandHandler(IMediator mediator)
    : IRequestHandler<BulkCreateEmployeesCommand, IReadOnlyList<BulkCreateEmployeeResult>>
{
    public async Task<IReadOnlyList<BulkCreateEmployeeResult>> Handle(
        BulkCreateEmployeesCommand request,
        CancellationToken ct)
    {
        var results = new List<BulkCreateEmployeeResult>();

        foreach (var item in request.Body.Items)
        {
            try
            {
                var created = await mediator.Send(new CreateEmployeeCommand(item, request.ChangedBy), ct);
                Guid? employeeId = Guid.TryParse(created.Id, out var parsedId) ? parsedId : null;
                results.Add(new BulkCreateEmployeeResult(item.Email, true, employeeId, null));
            }
            catch (InvalidOperationException ex)
            {
                results.Add(new BulkCreateEmployeeResult(item.Email, false, null, ex.Message));
            }
            catch (Exception ex)
            {
                results.Add(new BulkCreateEmployeeResult(item.Email, false, null, ex.Message));
            }
        }

        return results;
    }
}

public record UpdateEmployeeCommand(Guid Id, UpdateEmployeeRequest Body, Guid? ChangedBy) : IRequest<EmployeeDto?>;

public sealed class UpdateEmployeeCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<UpdateEmployeeCommand, EmployeeDto?>
{
    public Task<EmployeeDto?> Handle(UpdateEmployeeCommand request, CancellationToken ct) =>
        write.UpdateEmployeeAsync(request.Id, request.Body, request.ChangedBy, ct);
}

public record ClearOrgPlacementCommand(Guid Id, ClearOrgPlacementRequest Body, Guid? ChangedBy)
    : IRequest<EmployeeDto?>;

public sealed class ClearOrgPlacementCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<ClearOrgPlacementCommand, EmployeeDto?>
{
    public Task<EmployeeDto?> Handle(ClearOrgPlacementCommand request, CancellationToken ct) =>
        write.ClearOrgPlacementAsync(request.Id, request.Body, request.ChangedBy, ct);
}

public record DeleteEmployeeCommand(Guid Id, Guid? ChangedBy) : IRequest<bool>;

public sealed class DeleteEmployeeCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<DeleteEmployeeCommand, bool>
{
    public Task<bool> Handle(DeleteEmployeeCommand request, CancellationToken ct) =>
        write.DeleteEmployeeAsync(request.Id, request.ChangedBy, ct);
}

public record SetAuthSubjectCommand(Guid EmployeeId, Guid AuthSubjectId) : IRequest<bool>;

public sealed class SetAuthSubjectCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<SetAuthSubjectCommand, bool>
{
    public Task<bool> Handle(SetAuthSubjectCommand request, CancellationToken ct) =>
        write.SetAuthSubjectIdAsync(request.EmployeeId, request.AuthSubjectId, ct);
}

public record GetAssignmentHistoryQuery(Guid EmployeeId) : IRequest<IReadOnlyList<AssignmentHistoryEntryDto>>;

public sealed class GetAssignmentHistoryQueryHandler(IDirectoryReadService read)
    : IRequestHandler<GetAssignmentHistoryQuery, IReadOnlyList<AssignmentHistoryEntryDto>>
{
    public Task<IReadOnlyList<AssignmentHistoryEntryDto>> Handle(GetAssignmentHistoryQuery request, CancellationToken ct) =>
        read.GetAssignmentHistoryAsync(request.EmployeeId, ct);
}

public record GetPilotRotationHistoryQuery(Guid EmployeeId) : IRequest<IReadOnlyList<PilotRotationHistoryEntryDto>>;

public sealed class GetPilotRotationHistoryQueryHandler(IPilotRotationTenureService tenure)
    : IRequestHandler<GetPilotRotationHistoryQuery, IReadOnlyList<PilotRotationHistoryEntryDto>>
{
    public Task<IReadOnlyList<PilotRotationHistoryEntryDto>> Handle(GetPilotRotationHistoryQuery request, CancellationToken ct) =>
        tenure.GetRotationHistoryAsync(request.EmployeeId, ct);
}

public record ListPilotRotationsQuery(
    string? ServiceId,
    DateTime? From,
    DateTime? To,
    int? MinRotations,
    int? MaxRotations,
    string? Sort) : IRequest<IReadOnlyList<PilotRotationSummaryDto>>;

public sealed class ListPilotRotationsQueryHandler(IPilotRotationTenureService tenure)
    : IRequestHandler<ListPilotRotationsQuery, IReadOnlyList<PilotRotationSummaryDto>>
{
    public Task<IReadOnlyList<PilotRotationSummaryDto>> Handle(ListPilotRotationsQuery request, CancellationToken ct) =>
        tenure.ListRotationSummariesAsync(
            request.ServiceId,
            request.From,
            request.To,
            request.MinRotations,
            request.MaxRotations,
            request.Sort,
            ct);
}

public record GetPilotRotationEligibilityQuery(Guid EmployeeId, string TargetServiceId)
    : IRequest<PilotRotationEligibilityDto>;

public sealed class GetPilotRotationEligibilityQueryHandler(IPilotRotationTenureService tenure)
    : IRequestHandler<GetPilotRotationEligibilityQuery, PilotRotationEligibilityDto>
{
    public Task<PilotRotationEligibilityDto> Handle(GetPilotRotationEligibilityQuery request, CancellationToken ct) =>
        tenure.GetEligibilityAsync(request.EmployeeId, request.TargetServiceId, ct);
}
