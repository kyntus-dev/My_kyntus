using Conge.Domain.Entities;

namespace Conge.Application.Abstractions;

public sealed record DirectoryEmployeeRecord(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string Role,
    Guid? SuperviseurId,
    Guid? ParentId,
    string? ServiceId,
    string? PoleId,
    string? CelluleId,
    Guid? BusinessDepartmentId,
    DateTime? HireDate,
    bool EstMineur);

public interface IDirectoryEmployeeLookup
{
    Task<DirectoryEmployeeRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<DirectoryEmployeeRecord?> GetByEmailAsync(string email, CancellationToken ct = default);
}

public sealed class NullDirectoryEmployeeLookup : IDirectoryEmployeeLookup
{
    public Task<DirectoryEmployeeRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => Task.FromResult<DirectoryEmployeeRecord?>(null);

    public Task<DirectoryEmployeeRecord?> GetByEmailAsync(string email, CancellationToken ct = default)
        => Task.FromResult<DirectoryEmployeeRecord?>(null);
}

public sealed record EmployeSnapshotEnsureRequest(
    Guid RequestedId,
    string? Email,
    string? Role,
    Guid? ActorSubjectId,
    bool AllowJwtStub);

public interface IEmployeSnapshotEnsure
{
    Task<EmployeSnapshot?> EnsureAsync(EmployeSnapshotEnsureRequest request, CancellationToken ct = default);
}
