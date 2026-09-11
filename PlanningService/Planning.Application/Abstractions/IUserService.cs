namespace Planning.Application.Abstractions;

using Planning.Application.DTOs;

public interface IUserService
{
    Task<List<UserDto>> GetAllUsersAsync();
    Task<List<UserDto>> GetUsersForSupervisorAsync(
        int authUserId,
        CancellationToken ct = default);
    Task<bool> CanSupervisorAccessUserAsync(
        int authUserId,
        int targetUserId,
        CancellationToken ct = default);
    Task<bool> CanSupervisorAccessSubServiceAsync(
        int authUserId,
        int subServiceId,
        CancellationToken ct = default);
    Task<List<UserDto>> GetUsersBySubServiceAsync(int subServiceId);
    Task<UserDto?> GetUserByIdAsync(int id);
    Task<UserDto?> GetUserByGuidAsync(Guid employeeGuid);
    Task<UserDto> CreateUserAsync(CreateUserDto dto);
    Task<UserDto> CreateUserFromImportAsync(CreateUserFromImportDto dto);
    Task<ResetPasswordResultDto?> ResetPasswordAsync(int userId, CancellationToken ct = default);
    Task<ResetPasswordsByGroupResultDto> ResetPasswordsByGroupAsync(
        ResetPasswordsByGroupRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Insert Planning + Auth batch pour un chunk déjà créé dans Directory (sans check-email Directory).
    /// </summary>
    Task<IReadOnlyList<ImportChunkCreateResultDto>> CreateUsersFromImportChunkAsync(
        IReadOnlyList<ImportChunkCreateItemDto> items,
        CancellationToken ct = default);

    Task<UserDto?> UpdateUserAsync(int id, UpdateUserDto dto);
    Task<bool> DeleteUserAsync(int id);
    Task SyncMissingAuthUsersAsync();

    /// <summary>
    /// Pousse le rôle Planning courant vers Auth (JWT).
    /// À appeler après une projection d'affectation structurelle Directory → Planning.
    /// </summary>
    Task SyncUserRoleToAuthAsync(int userId, CancellationToken ct = default);

    Task<UserDto?> GetUserByAuthIdAsync(int authUserId);
    Task<UserDto?> GetUserByEmailAsync(string email);
    Task<UserDto?> GetOrLinkUserForAuthAsync(int authUserId, string? email);
    /// <summary>
    /// Résout l'employé Planning lié au JWT Auth ; crée une fiche minimale si absente
    /// (ex. compte RH seed Auth sans seed Planning).
    /// </summary>
    Task<UserDto?> GetOrEnsureUserForAuthAsync(
        int authUserId,
        string? email,
        string? authRole,
        Guid? subjectId,
        CancellationToken ct = default);
    Task<bool> IsEmailUniqueAsync(string email, int? excludeId = null);
    Task SyncAllEmployesToCongeAsync();
    Task<List<UserDto>> GetManagersBySubServiceAsync(int subServiceId, CancellationToken ct = default);
    Task<SetNewEmployeeStatusResultDto?> SetNewEmployeeStatusAsync(
        int id,
        SetNewEmployeeDto dto,
        CancellationToken ct = default);

    Task RollbackImportCreatedUserAsync(int planningUserId, CancellationToken ct = default);

    Task RollbackImportUpdatedUserAsync(
        int planningUserId,
        UpdateUserDto previousState,
        Dictionary<string, string?> previousCustomFields,
        CancellationToken ct = default);

    /// <summary>Change uniquement le rattachement cellule/service (sans toucher groupes samedi ni périmètres managés).</summary>
    Task RestoreSubServicePlacementAsync(int planningUserId, int subServiceId, CancellationToken ct = default);

    Task<UserDto?> UpdateContractualLevelAsync(
        int targetUserId,
        int level,
        Guid actorSubjectId,
        string actorRole,
        CancellationToken ct = default);

    /// <summary>
    /// Sortie complète après rejet de formation initiale : désactivation, date de sortie, sync Directory / Auth.
    /// </summary>
    Task<bool> ExitAfterInitialTrainingRejectionAsync(
        Guid employeeGuid,
        string reason,
        CancellationToken ct = default);

    /// <summary>
    /// Passage en production après validation RH Formation : clear EnFormation, expertise, sync Directory.
    /// </summary>
    Task<bool> CompleteInitialTrainingAsync(
        Guid employeeGuid,
        int niveauExpertiseMetier,
        DateOnly productionStartDate,
        CancellationToken ct = default);
}
