using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Planning.Application.Abstractions;
using Planning.Application.Abstractions.EmployeeImport;
using Planning.Application.DTOs;
using Planning.Infrastructure.Services;
using Planning.Infrastructure.Services.EmployeeImport;

namespace Planning.UnitTests;

public class ImportExecutionJournalTests
{
    [Fact]
    public async Task CompensateAsync_reverses_org_and_user_entries_in_lifo_order()
    {
        var userService = new FakeImportUserService();
        var directoryOrg = new FakeDirectoryOrgWriteClient();
        var orgMirror = new FakeOrgMirrorService();
        var journal = CreateJournal(userService, directoryOrg, orgMirror);

        journal.RecordOrgCreated(new OrgNodeCreatedReportDto
        {
            NodeType = "pole",
            Name = "Pôle A",
            DirectoryNodeId = "pole-1"
        });
        journal.RecordOrgCreated(new OrgNodeCreatedReportDto
        {
            NodeType = "cellule",
            Name = "Cellule B",
            Pole = "Pôle A",
            DirectoryNodeId = "cell-1"
        });
        journal.RecordUserCreated(10, Guid.NewGuid(), authUserId: 99);
        journal.RecordUserCreated(11, Guid.NewGuid(), authUserId: 100);

        await journal.CompensateAsync();

        Assert.Equal([11, 10], userService.RolledBackCreatedUserIds);
        Assert.Equal(["cell-1", "pole-1"], directoryOrg.DeletedNodeIds);
        Assert.True(orgMirror.SyncCalled);
    }

    [Fact]
    public async Task RollbackLastUserChangeAsync_compensates_only_last_user_entry()
    {
        var userService = new FakeImportUserService();
        var journal = CreateJournal(userService, new FakeDirectoryOrgWriteClient(), new FakeOrgMirrorService());

        journal.RecordUserCreated(1, Guid.NewGuid(), null);
        journal.RecordUserCreated(2, Guid.NewGuid(), null);

        await journal.RollbackLastUserChangeAsync();

        Assert.Equal([2], userService.RolledBackCreatedUserIds);
    }

    [Fact]
    public async Task Commit_clears_entries_without_compensation()
    {
        var userService = new FakeImportUserService();
        var journal = CreateJournal(userService, new FakeDirectoryOrgWriteClient(), new FakeOrgMirrorService());

        journal.RecordUserCreated(1, Guid.NewGuid(), null);
        journal.Commit();
        await journal.CompensateAsync();

        Assert.Empty(userService.RolledBackCreatedUserIds);
    }

    [Fact]
    public async Task CompensateAsync_restores_updated_user_snapshot()
    {
        var userService = new FakeImportUserService();
        var journal = CreateJournal(userService, new FakeDirectoryOrgWriteClient(), new FakeOrgMirrorService());
        var previous = new UpdateUserDto
        {
            Email = "old@test.ma",
            FirstName = "Old",
            LastName = "Name",
            RoleId = 2,
            IsActive = true,
            HireDate = DateTime.UtcNow.Date,
            Level = 1
        };

        journal.RecordUserUpdated(5, previous, new Dictionary<string, string?> { ["badge"] = "A1" });
        await journal.CompensateAsync();

        var rollback = Assert.Single(userService.RolledBackUpdatedUsers);
        Assert.Equal(5, rollback.UserId);
        Assert.Equal("old@test.ma", rollback.PreviousState.Email);
        Assert.Equal("A1", rollback.PreviousCustomFields["badge"]);
    }

    private static ImportExecutionJournal CreateJournal(
        IUserService userService,
        IDirectoryOrgWriteClient directoryOrg,
        IPlanningOrgMirrorService orgMirror) =>
        new(
            userService,
            directoryOrg,
            orgMirror,
            new HttpContextAccessor(),
            NullLogger<ImportExecutionJournal>.Instance);

    private sealed class FakeImportUserService : IUserService
    {
        public Task<List<UserDto>> GetUsersForSupervisorAsync(int authUserId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> CanSupervisorAccessUserAsync(int authUserId, int targetUserId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<bool> CanSupervisorAccessSubServiceAsync(int authUserId, int subServiceId, CancellationToken ct = default)
            => throw new NotSupportedException();
        public List<int> RolledBackCreatedUserIds { get; } = [];
        public List<(int UserId, UpdateUserDto PreviousState, Dictionary<string, string?> PreviousCustomFields)> RolledBackUpdatedUsers { get; } = [];

        public Task RollbackImportCreatedUserAsync(int planningUserId, CancellationToken ct = default)
        {
            RolledBackCreatedUserIds.Add(planningUserId);
            return Task.CompletedTask;
        }

        public Task RollbackImportUpdatedUserAsync(
            int planningUserId,
            UpdateUserDto previousState,
            Dictionary<string, string?> previousCustomFields,
            CancellationToken ct = default)
        {
            RolledBackUpdatedUsers.Add((planningUserId, previousState, previousCustomFields));
            return Task.CompletedTask;
        }

        public Task RestoreSubServicePlacementAsync(int planningUserId, int subServiceId, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<List<UserDto>> GetAllUsersAsync() => Task.FromResult(new List<UserDto>());
        public Task<List<UserDto>> GetUsersBySubServiceAsync(int subServiceId) => Task.FromResult(new List<UserDto>());
        public Task<UserDto?> GetUserByIdAsync(int id) => Task.FromResult<UserDto?>(null);
        public Task<UserDto?> GetUserByGuidAsync(Guid employeeGuid) => Task.FromResult<UserDto?>(null);
        public Task<UserDto> CreateUserAsync(CreateUserDto dto) => throw new NotImplementedException();
        public Task<UserDto> CreateUserFromImportAsync(CreateUserFromImportDto dto) => throw new NotImplementedException();
        public Task<ResetPasswordResultDto?> ResetPasswordAsync(int userId, CancellationToken ct = default) =>
            Task.FromResult<ResetPasswordResultDto?>(null);
        public Task<ResetPasswordsByGroupResultDto> ResetPasswordsByGroupAsync(
            ResetPasswordsByGroupRequest request,
            CancellationToken ct = default) =>
            Task.FromResult(new ResetPasswordsByGroupResultDto());
        public Task<IReadOnlyList<ImportChunkCreateResultDto>> CreateUsersFromImportChunkAsync(
            IReadOnlyList<ImportChunkCreateItemDto> items,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ImportChunkCreateResultDto>>(Array.Empty<ImportChunkCreateResultDto>());
        public Task<UserDto?> UpdateUserAsync(int id, UpdateUserDto dto) => throw new NotImplementedException();
        public Task<bool> DeleteUserAsync(int id) => throw new NotImplementedException();
        public Task SyncMissingAuthUsersAsync() => Task.CompletedTask;
        public Task SyncUserRoleToAuthAsync(int userId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<UserDto?> GetUserByAuthIdAsync(int authUserId) => Task.FromResult<UserDto?>(null);
        public Task<UserDto?> GetUserByEmailAsync(string email) => Task.FromResult<UserDto?>(null);
        public Task<UserDto?> GetOrLinkUserForAuthAsync(int authUserId, string? email) => Task.FromResult<UserDto?>(null);
        public Task<UserDto?> GetOrEnsureUserForAuthAsync(
            int authUserId,
            string? email,
            string? authRole,
            Guid? subjectId,
            CancellationToken ct = default) => Task.FromResult<UserDto?>(null);
        public Task<bool> IsEmailUniqueAsync(string email, int? excludeId = null) => Task.FromResult(true);
        public Task SyncAllEmployesToCongeAsync() => Task.CompletedTask;
        public Task<List<UserDto>> GetManagersBySubServiceAsync(int subServiceId, CancellationToken ct = default) =>
            Task.FromResult(new List<UserDto>());
        public Task<SetNewEmployeeStatusResultDto?> SetNewEmployeeStatusAsync(int id, SetNewEmployeeDto dto, CancellationToken ct = default) =>
            Task.FromResult<SetNewEmployeeStatusResultDto?>(null);

        public Task<UserDto?> UpdateContractualLevelAsync(
            int targetUserId,
            int level,
            Guid actorSubjectId,
            string actorRole,
            CancellationToken ct = default) =>
            Task.FromResult<UserDto?>(null);

        public Task<bool> ExitAfterInitialTrainingRejectionAsync(
            Guid employeeGuid,
            string reason,
            CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task<bool> CompleteInitialTrainingAsync(
            Guid employeeGuid,
            int niveauExpertiseMetier,
            DateOnly productionStartDate,
            CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private sealed class FakeDirectoryOrgWriteClient : IDirectoryOrgWriteClient
    {
        public List<string> DeletedNodeIds { get; } = [];

        public Task DeletePoleAsync(string nodeId, CancellationToken ct = default)
        {
            DeletedNodeIds.Add(nodeId);
            return Task.CompletedTask;
        }

        public Task DeleteCelluleAsync(string poleId, string nodeId, CancellationToken ct = default)
        {
            DeletedNodeIds.Add(nodeId);
            return Task.CompletedTask;
        }

        public Task DeleteServiceAsync(string celluleId, string nodeId, CancellationToken ct = default)
        {
            DeletedNodeIds.Add(nodeId);
            return Task.CompletedTask;
        }

        public Task<string> CreatePoleAsync(string name, Guid businessDepartmentId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<Guid> CreateOperationalDepartmentAsync(string? code, string name, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<DirectoryOperationalDepartmentJson>> GetOperationalDepartmentsAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetEmployeeOperationalBusinessDepartmentIdsAsync(CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<string> CreateCelluleAsync(string poleDirectoryId, string name, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<string> CreateServiceAsync(string celluleDirectoryId, string name, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<bool> AssignChefDeProjetAsync(string poleDirectoryId, Guid employeeGuid, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<bool> AssignSuperviseurAsync(string celluleDirectoryId, Guid employeeGuid, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<bool> AssignReferentTechniqueAsync(string serviceDirectoryId, Guid employeeGuid, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<DirectoryUndoImportWindowResult> UndoImportAssignmentWindowAsync(
            IReadOnlyList<Guid> employeeIds,
            DateTime windowStart,
            DateTime windowEnd,
            CancellationToken ct = default) =>
            Task.FromResult(new DirectoryUndoImportWindowResult());

        public Task<IReadOnlyList<Guid>> GetEmployeeIdsWithAssignmentsAfterAsync(
            IReadOnlyList<Guid> employeeIds,
            DateTime after,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());
    }

    private sealed class FakeOrgMirrorService : IPlanningOrgMirrorService
    {
        public bool SyncCalled { get; private set; }

        public Task<int> SyncFromPrimeTreeAsync(IReadOnlyList<PrimeOrgPoleMirrorDto> poles, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<int> SyncFromDirectoryOverviewAsync(string? authorizationHeader, CancellationToken ct = default)
        {
            SyncCalled = true;
            return Task.FromResult(0);
        }

        public Task<int> SyncEmployeeSubServicesFromDirectoryOverviewAsync(string? authorizationHeader, CancellationToken ct = default) =>
            Task.FromResult(0);

        public Task<EmployeeImportOrgOverview?> GetDirectoryOverviewAsync(string? authorizationHeader, CancellationToken ct = default) =>
            Task.FromResult<EmployeeImportOrgOverview?>(null);
    }
}

public class EmployeeImportStructuralValidationTests
{
    [Fact]
    public void ValidateStructuralPreconditions_rejects_unmapped_email()
    {
        var parsed = new ParsedImportFile(["Nom"], [["Dupont"]]);
        var columnMap = new Dictionary<int, string> { [0] = "lastName" };
        var request = new EmployeeImportExecuteRequest
        {
            ImportSessionId = Guid.NewGuid(),
            Mappings = [],
            ConfirmOrgProvision = false,
            ApprovedOrgCreations = [],
            AcceptedFuzzyMatches = []
        };
        var snapshot = new EmployeeImportOrgSnapshot
        {
            Roles = [new Planning.Domain.Entities.Role { Id = 1, Name = "Pilote" }]
        };

        var ex = Assert.Throws<TargetInvocationException>(() =>
            EmployeeImportExecutorTestHooks.ValidateStructuralPreconditions(
                parsed, columnMap, request, snapshot));

        var inner = Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("email", inner.Message, StringComparison.OrdinalIgnoreCase);
    }
}

internal static class EmployeeImportExecutorTestHooks
{
    public static void ValidateStructuralPreconditions(
        ParsedImportFile parsed,
        Dictionary<int, string> columnToField,
        EmployeeImportExecuteRequest request,
        EmployeeImportOrgSnapshot orgSnapshot)
    {
        var method = typeof(EmployeeImportExecutor).GetMethod(
            "ValidateStructuralPreconditions",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        method.Invoke(null, [parsed, columnToField, request, orgSnapshot]);
    }
}
