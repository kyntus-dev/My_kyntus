using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Enums;
using MediatR;

namespace EmployeeDirectory.Application.Org;

public record GetOrgOverviewQuery : IRequest<OrgOverviewDto>;

public sealed class GetOrgOverviewQueryHandler(IDirectoryReadService read)
    : IRequestHandler<GetOrgOverviewQuery, OrgOverviewDto>
{
    public Task<OrgOverviewDto> Handle(GetOrgOverviewQuery request, CancellationToken ct) =>
        read.GetOrgOverviewAsync(ct);
}

public record GetAssignmentsAsOfQuery(DateTime Date) : IRequest<OrgAssignmentAsOfDto>;

public sealed class GetAssignmentsAsOfQueryHandler(IDirectoryReadService read)
    : IRequestHandler<GetAssignmentsAsOfQuery, OrgAssignmentAsOfDto>
{
    public Task<OrgAssignmentAsOfDto> Handle(GetAssignmentsAsOfQuery request, CancellationToken ct) =>
        read.GetAssignmentsAsOfAsync(request.Date, ct);
}

public record CreatePoleCommand(string Name, Guid BusinessDepartmentId) : IRequest<string>;

public sealed class CreatePoleCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<CreatePoleCommand, string>
{
    public Task<string> Handle(CreatePoleCommand request, CancellationToken ct) =>
        write.CreatePoleAsync(request.Name, request.BusinessDepartmentId, ct);
}

public record AttachPoleToDepartmentCommand(string PoleId, Guid BusinessDepartmentId) : IRequest<bool>;

public sealed class AttachPoleToDepartmentCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<AttachPoleToDepartmentCommand, bool>
{
    public Task<bool> Handle(AttachPoleToDepartmentCommand request, CancellationToken ct) =>
        write.AttachPoleToBusinessDepartmentAsync(request.PoleId, request.BusinessDepartmentId, ct);
}

public record CreateCelluleCommand(string PoleId, string Name) : IRequest<string>;

public sealed class CreateCelluleCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<CreateCelluleCommand, string>
{
    public Task<string> Handle(CreateCelluleCommand request, CancellationToken ct) =>
        write.CreateCelluleAsync(request.PoleId, request.Name, ct);
}

public record CreateServiceCommand(string CelluleId, string Name) : IRequest<string>;

public sealed class CreateServiceCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<CreateServiceCommand, string>
{
    public Task<string> Handle(CreateServiceCommand request, CancellationToken ct) =>
        write.CreateServiceAsync(request.CelluleId, request.Name, ct);
}

public record RenameOrgNodeCommand(OrgNodeLevel Level, string NodeId, string Name) : IRequest<bool>;

public sealed class RenameOrgNodeCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<RenameOrgNodeCommand, bool>
{
    public Task<bool> Handle(RenameOrgNodeCommand request, CancellationToken ct) =>
        write.RenameOrgNodeAsync(request.Level, request.NodeId, request.Name, ct);
}

public record DeleteOrgNodeCommand(OrgNodeLevel Level, string NodeId) : IRequest<bool>;

public sealed class DeleteOrgNodeCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<DeleteOrgNodeCommand, bool>
{
    public Task<bool> Handle(DeleteOrgNodeCommand request, CancellationToken ct) =>
        write.DeleteOrgNodeAsync(request.Level, request.NodeId, ct);
}

public record AssignStructureRoleCommand(
    string Kind,
    string NodeId,
    Guid EmployeeId,
    Guid? ChangedBy,
    string? Reason,
    IReadOnlyList<Guid>? RevokeEmployeeIds = null,
    bool ForceTenureOverride = false)
    : IRequest<StructuralRoleAssignmentResult>;

public sealed class AssignStructureRoleCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<AssignStructureRoleCommand, StructuralRoleAssignmentResult>
{
    public Task<StructuralRoleAssignmentResult> Handle(AssignStructureRoleCommand request, CancellationToken ct) =>
        write.AssignStructureRoleAsync(
            request.Kind,
            request.NodeId,
            request.EmployeeId,
            request.ChangedBy,
            request.Reason,
            request.RevokeEmployeeIds,
            request.ForceTenureOverride,
            ct);
}

public record ReconcileEmployeeStructuralAssignmentsCommand(
    string Kind,
    Guid EmployeeId,
    IReadOnlyList<string> NodeIds,
    string PrimaryNodeId,
    Guid? ChangedBy,
    string? Reason)
    : IRequest<StructuralAssignmentsReconcileResult>;

public sealed class ReconcileEmployeeStructuralAssignmentsCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<ReconcileEmployeeStructuralAssignmentsCommand, StructuralAssignmentsReconcileResult>
{
    public Task<StructuralAssignmentsReconcileResult> Handle(
        ReconcileEmployeeStructuralAssignmentsCommand request,
        CancellationToken ct) =>
        write.ReconcileEmployeeStructuralAssignmentsAsync(
            request.Kind,
            request.EmployeeId,
            request.NodeIds,
            request.PrimaryNodeId,
            request.ChangedBy,
            request.Reason,
            ct);
}

public record RemoveStructureAssignmentCommand(
    string Kind,
    string NodeId,
    Guid EmployeeId,
    Guid? ChangedBy,
    string? Reason) : IRequest<bool>;

public sealed class RemoveStructureAssignmentCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<RemoveStructureAssignmentCommand, bool>
{
    public Task<bool> Handle(RemoveStructureAssignmentCommand request, CancellationToken ct) =>
        write.RemoveStructureAssignmentAsync(
            request.Kind, request.NodeId, request.EmployeeId, request.ChangedBy, request.Reason, ct);
}

public record RemoveStructurePilotCommand(string ServiceId, Guid EmployeeId, Guid? ChangedBy) : IRequest<bool>;

public sealed class RemoveStructurePilotCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<RemoveStructurePilotCommand, bool>
{
    public Task<bool> Handle(RemoveStructurePilotCommand request, CancellationToken ct) =>
        write.RemoveStructurePilotAsync(request.ServiceId, request.EmployeeId, request.ChangedBy, null, ct);
}

public record ClearStructureRoleCommand(string Kind, string NodeId, Guid? ChangedBy) : IRequest<Unit>;

public sealed class ClearStructureRoleCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<ClearStructureRoleCommand, Unit>
{
    public async Task<Unit> Handle(ClearStructureRoleCommand request, CancellationToken ct)
    {
        await write.ClearStructureRoleAsync(request.Kind, request.NodeId, request.ChangedBy, null, ct);
        return Unit.Value;
    }
}

public record GetEmployeeIdsWithAssignmentsAfterQuery(
    IReadOnlyList<Guid> EmployeeIds,
    DateTime After) : IRequest<IReadOnlyList<Guid>>;

public sealed class GetEmployeeIdsWithAssignmentsAfterQueryHandler(IDirectoryReadService read)
    : IRequestHandler<GetEmployeeIdsWithAssignmentsAfterQuery, IReadOnlyList<Guid>>
{
    public Task<IReadOnlyList<Guid>> Handle(
        GetEmployeeIdsWithAssignmentsAfterQuery request,
        CancellationToken ct) =>
        read.GetEmployeeIdsWithAssignmentsAfterAsync(request.EmployeeIds, request.After, ct);
}

public record UndoImportAssignmentWindowCommand(
    UndoImportAssignmentWindowRequest Body,
    Guid? ChangedBy) : IRequest<UndoImportAssignmentWindowResult>;

public sealed class UndoImportAssignmentWindowCommandHandler(IDirectoryWriteService write)
    : IRequestHandler<UndoImportAssignmentWindowCommand, UndoImportAssignmentWindowResult>
{
    public Task<UndoImportAssignmentWindowResult> Handle(
        UndoImportAssignmentWindowCommand request,
        CancellationToken ct) =>
        write.UndoImportAssignmentWindowAsync(request.Body, request.ChangedBy, ct);
}
