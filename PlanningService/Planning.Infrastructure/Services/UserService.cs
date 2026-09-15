using System.Security.Claims;
using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Kyntus.Messaging.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Messaging.Publishers;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Security;
using Planning.Application.DTOs;
using Planning.Application.Abstractions;
using Planning.Domain.Entities;

using Planning.Application.Abstractions.EmployeeImport;

namespace Planning.Infrastructure.Services;

file record AuthRegisterResult(int Id, string Email, Guid SubjectId);
public class UserService : IUserService
{
    private sealed record AuthBatchSyncItem(User User, string? DefaultPassword);
    private sealed record AuthBatchSyncResult(string Email, bool Success, int? AuthUserId, string? Message);
    private readonly AppDbContext _context;
    private readonly IEmployePublisher _employePublisher;
    private readonly HttpClient _httpClient;
    private readonly IDirectoryEmployeeEnsureClient _directoryEmployeeEnsure;
    private readonly IDirectoryEmployeeWriteClient _directoryEmployeeWrite;
    private readonly IDirectoryHierarchyClient _directoryHierarchy;
    private readonly IDirectoryOrgWriteClient _directoryOrg;
    private readonly IConfiguration _configuration;
    private readonly ILogger<UserService> _logger;
    private readonly IEmployeeFieldService _fieldService;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IRebacClient _rebac;
    private readonly IPlanningPerimeterResolver _perimeter;

    public UserService(
           AppDbContext context,
           IEmployePublisher employePublisher,
           HttpClient httpClient,
           IDirectoryEmployeeEnsureClient directoryEmployeeEnsure,
           IDirectoryEmployeeWriteClient directoryEmployeeWrite,
           IDirectoryHierarchyClient directoryHierarchy,
           IDirectoryOrgWriteClient directoryOrg,
           IConfiguration configuration,
           ILogger<UserService> logger,
           IEmployeeFieldService fieldService,
           IHttpContextAccessor httpContextAccessor,
           IRebacClient rebac,
           IPlanningPerimeterResolver perimeter)
    {
        _context = context;
        _employePublisher = employePublisher;
        _httpClient = httpClient;
        _directoryEmployeeEnsure = directoryEmployeeEnsure;
        _directoryEmployeeWrite = directoryEmployeeWrite;
        _directoryHierarchy = directoryHierarchy;
        _directoryOrg = directoryOrg;
        _configuration = configuration;
        _logger = logger;
        _fieldService = fieldService;
        _httpContextAccessor = httpContextAccessor;
        _rebac = rebac;
        _perimeter = perimeter;
    }

    public async Task<List<UserDto>> GetAllUsersAsync()
    {
        var users = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.SubService)
                .ThenInclude(ss => ss!.Service)
                    .ThenInclude(s => s.Floor)
            .Include(u => u.ManagedSubServices)
                .ThenInclude(us => us.SubService)
                    .ThenInclude(s => s.Service)
                        .ThenInclude(s => s.Floor)
            .Include(u => u.ManagedServices)
                .ThenInclude(us => us.Service)
                    .ThenInclude(s => s.Floor)
            .ToListAsync();

        var customByUser = await _fieldService.LoadCustomFieldsForUsersAsync(users.Select(u => u.Id).ToList());
        var hrByUser = await LoadHrProfilesAsync(users.Select(u => u.Id).ToList());
        var orgCtx = await LoadOrgNameContextAsync();
        return users.Select(u => ToDto(u, customByUser.GetValueOrDefault(u.Id), orgCtx, hrByUser.GetValueOrDefault(u.Id))).ToList();
    }

    public async Task<List<UserDto>> GetUsersForSupervisorAsync(
        int authUserId,
        CancellationToken ct = default)
    {
        var supervisorId = await _context.Users
            .Where(u => u.AuthUserId == authUserId && u.IsActive)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

        if (supervisorId == 0)
            return [];

        var allowedSubServiceIds =
            await _perimeter.GetManagedSubServiceIdsAsync(supervisorId, ct);

        if (allowedSubServiceIds.Count == 0)
            return [];

        var users = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.SubService)
                .ThenInclude(ss => ss!.Service)
                    .ThenInclude(s => s!.Floor)
            .Include(u => u.ManagedSubServices)
                .ThenInclude(us => us.SubService)
                    .ThenInclude(s => s!.Service)
            .Include(u => u.ManagedServices)
                .ThenInclude(us => us.Service)
                    .ThenInclude(s => s!.Floor)
            .Where(u => u.IsActive
                        && u.SubServiceId.HasValue
                        && allowedSubServiceIds.Contains(u.SubServiceId.Value))
            .ToListAsync(ct);

        var customByUser = await _fieldService
            .LoadCustomFieldsForUsersAsync(users.Select(u => u.Id).ToList());
        var hrByUser = await LoadHrProfilesAsync(users.Select(u => u.Id).ToList());
        var orgCtx = await LoadOrgNameContextAsync();

        return users
            .Select(u => ToDto(
                u,
                customByUser.GetValueOrDefault(u.Id),
                orgCtx,
                hrByUser.GetValueOrDefault(u.Id)))
            .ToList();
    }

    public async Task<bool> CanSupervisorAccessSubServiceAsync(
        int authUserId,
        int subServiceId,
        CancellationToken ct = default)
    {
        var supervisorId = await _context.Users
            .Where(u => u.AuthUserId == authUserId && u.IsActive)
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct);

        if (supervisorId == 0)
            return false;

        var allowed = await _perimeter
            .GetManagedSubServiceIdsAsync(supervisorId, ct);

        return allowed.Contains(subServiceId);
    }

    public async Task<bool> CanSupervisorAccessUserAsync(
        int authUserId,
        int targetUserId,
        CancellationToken ct = default)
    {
        var targetSubServiceId = await _context.Users
            .Where(u => u.Id == targetUserId && u.IsActive)
            .Select(u => u.SubServiceId)
            .FirstOrDefaultAsync(ct);

        return targetSubServiceId.HasValue
            && await CanSupervisorAccessSubServiceAsync(
                authUserId,
                targetSubServiceId.Value,
                ct);
    }
    public async Task<List<UserDto>> GetUsersBySubServiceAsync(int subServiceId)
    {
        var users = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.SubService)
            .Include(u => u.ManagedSubServices)
                .ThenInclude(us => us.SubService)
                    .ThenInclude(s => s.Service)
            .Where(u => u.SubServiceId == subServiceId)
            .ToListAsync();

        var customByUser = await _fieldService.LoadCustomFieldsForUsersAsync(users.Select(u => u.Id).ToList());
        var hrByUser = await LoadHrProfilesAsync(users.Select(u => u.Id).ToList());
        var orgCtx = await LoadOrgNameContextAsync();
        return users.Select(u => ToDto(u, customByUser.GetValueOrDefault(u.Id), orgCtx, hrByUser.GetValueOrDefault(u.Id))).ToList();
    }

    public async Task<UserDto?> GetUserByIdAsync(int id)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.SubService)
                .ThenInclude(ss => ss!.Service)
                    .ThenInclude(s => s.Floor)
            .Include(u => u.ManagedSubServices)
                .ThenInclude(us => us.SubService)
                    .ThenInclude(s => s.Service)
                        .ThenInclude(s => s.Floor)
            .Include(u => u.ManagedServices)
                .ThenInclude(us => us.Service)
                    .ThenInclude(s => s.Floor)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user is null)
            return null;

        var customByUser = await _fieldService.LoadCustomFieldsForUsersAsync([user.Id]);
        var hrProfile = await _context.UserHrProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id);
        var orgCtx = await LoadOrgNameContextAsync();
        return ToDto(user, customByUser.GetValueOrDefault(user.Id), orgCtx, hrProfile);
    }

    public async Task<UserDto?> GetUserByGuidAsync(Guid employeeGuid)
    {
        if (employeeGuid == Guid.Empty)
            return null;

        var id = await _context.Users.AsNoTracking()
            .Where(u => u.Guid == employeeGuid)
            .Select(u => u.Id)
            .FirstOrDefaultAsync();
        return id == 0 ? null : await GetUserByIdAsync(id);
    }

    public async Task SyncUserRoleToAuthAsync(int userId, CancellationToken ct = default)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            _logger.LogWarning("SyncUserRoleToAuth: utilisateur Planning {UserId} introuvable", userId);
            return;
        }

        await SyncToAuthServiceAsync(user);
    }

    public async Task SyncMissingAuthUsersAsync()
    {
        var users = await _context.Users
            .Where(u => u.AuthUserId == null && u.IsActive)
            .ToListAsync();

        _logger.LogInformation("{Count} users sans AuthUserId à synchroniser", users.Count);

        foreach (var user in users)
            await SyncToAuthServiceAsync(user);
    }
    public async Task<UserDto> CreateUserAsync(CreateUserDto dto)
    {
        if (!await IsEmailUniqueAsync(dto.Email))
            throw new InvalidOperationException($"L'adresse email « {dto.Email.Trim()} » est déjà utilisée.");

        ValidateCreateUserHrDates(dto);
        await _fieldService.ValidateCustomFieldsForCreateAsync(dto.CustomFields);

        if (dto.IdTechnicien.HasValue && !IsDirectoryWriteMaster())
            throw new InvalidOperationException("La création depuis HTEL nécessite l'annuaire central (Directory:WriteMaster). Contactez l'administrateur.");
        if (IsDirectoryWriteMaster())
            return await CreateUserDirectoryFirstAsync(dto, null, requireAuthSuccess: true);

        var password = PasswordGenerator.Generate();
        var user = new User
        {
            RoleId = dto.RoleId,
            SubServiceId = dto.SubServiceId,
            FirstName = dto.FirstName,
            LastName = dto.LastName,
            Email = dto.Email,
            HireDate = dto.HireDate,
            Level = dto.Level,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        await _context.Entry(user).Reference(u => u.Role).LoadAsync();
        await SyncToAuthServiceAsync(user, password);

        await PublishEmployeCreatedForUserAsync(user, dto.SubServiceId);
        await _context.SaveChangesAsync();

        var directoryOk = await _directoryEmployeeEnsure.TryEnsureFromPlanningAsync(user);
        if (!directoryOk && IsDirectoryEnsureRequired())
        {
            await RollbackCreatedUserAsync(user);
            throw new InvalidOperationException(
                "L'employé a été créé localement mais la synchronisation avec l'annuaire (Directory) a échoué. Réessayez ou contactez l'administrateur.");
        }

        await _fieldService.UpsertCustomFieldsAsync(user.Id, dto.CustomFields, isCreate: true);

        if (user.SubServiceId is int subId)
            await EnsureBalancedSaturdayGroupAsync(user.Id, subId);

        var created = await GetUserByIdAsync(user.Id)
            ?? throw new Exception("Erreur création utilisateur.");
        created.GeneratedPassword = password;
        return created;
    }

    public async Task<UserDto> CreateUserFromImportAsync(CreateUserFromImportDto dto)
    {
        // Import : unicité locale uniquement (évite GET check-email Directory × N).
        var email = dto.Email.Trim();
        var existsLocal = await _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower());
        if (existsLocal)
            throw new InvalidOperationException($"L'adresse email « {email} » est déjà utilisée.");

        if (!IsDirectoryWriteMaster())
            throw new InvalidOperationException(
                "L'import guidé requiert Directory__WriteMaster=true pour garantir la synchronisation plateforme.");

        return await CreateUserDirectoryFirstAsync(dto, ResolveOrGeneratePassword(dto.Password), requireAuthSuccess: true);
    }

    public async Task<IReadOnlyList<ImportChunkCreateResultDto>> CreateUsersFromImportChunkAsync(
        IReadOnlyList<ImportChunkCreateItemDto> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return Array.Empty<ImportChunkCreateResultDto>();

        if (!IsDirectoryWriteMaster())
            throw new InvalidOperationException(
                "L'import guidé requiert Directory__WriteMaster=true pour garantir la synchronisation plateforme.");

        var results = new List<ImportChunkCreateResultDto>(items.Count);
        var usersToInsert = new List<(User User, CreateUserFromImportDto Dto, string Password)>();

        // Dedup email dans le chunk uniquement. Les Users déjà projetés par
        // DirectoryEmployeeProjectionConsumer sont adoptés dans PersistImportUsersHandlingConflictsAsync
        // (ne plus échouer en « email déjà utilisée » — fausse erreur de course async).
        var seenInChunk = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Hash BCrypt en parallèle (CPU-bound) avant les écritures DB.
        var passwordByIndex = new string[items.Count];
        var hashByIndex = new string[items.Count];
        var passwordErrors = new string?[items.Count];
        Parallel.For(0, items.Count, i =>
        {
            if (!PasswordGenerator.TryResolveOrGenerate(items[i].Dto.Password, out var password, out var error))
            {
                passwordErrors[i] = error;
                return;
            }

            passwordByIndex[i] = password;
            hashByIndex[i] = BCrypt.Net.BCrypt.HashPassword(password);
        });

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var email = item.Dto.Email.Trim().ToLowerInvariant();
            if (passwordErrors[i] is { } pwdError)
            {
                results.Add(new ImportChunkCreateResultDto
                {
                    Email = email,
                    Success = false,
                    ErrorMessage = pwdError,
                });
                continue;
            }

            if (item.DirectoryEmployeeId == Guid.Empty)
            {
                results.Add(new ImportChunkCreateResultDto
                {
                    Email = email,
                    Success = false,
                    ErrorMessage = "Identifiant Directory manquant.",
                });
                continue;
            }

            if (!seenInChunk.Add(email))
            {
                results.Add(new ImportChunkCreateResultDto
                {
                    Email = email,
                    Success = false,
                    ErrorMessage = "Email en double dans le même chunk d'import.",
                });
                continue;
            }

            var password = passwordByIndex[i];
            var isActive = item.Dto.IsActiveOnImport ?? true;
            var user = new User
            {
                Guid = item.DirectoryEmployeeId,
                RoleId = item.Dto.RoleId,
                SubServiceId = item.Dto.SubServiceId,
                FirstName = item.Dto.FirstName,
                LastName = item.Dto.LastName,
                Email = item.Dto.Email.Trim(),
                HireDate = item.Dto.HireDate,
                Level = item.Dto.Level,
                PasswordHash = hashByIndex[i],
                IsActive = isActive,
                CreatedAt = DateTime.UtcNow,
            };
            usersToInsert.Add((user, item.Dto, password));
        }

        if (usersToInsert.Count == 0)
            return results;

        var insertedUsers = await PersistImportUsersHandlingConflictsAsync(usersToInsert, results, ct);
        if (insertedUsers.Count == 0)
            return results;

        var roleIds = insertedUsers.Select(u => u.User.RoleId).Distinct().ToList();
        var rolesById = await _context.Roles
            .Where(r => roleIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id, ct);
        foreach (var (user, _, _) in insertedUsers)
        {
            if (rolesById.TryGetValue(user.RoleId, out var role))
                _context.Entry(user).Reference(u => u.Role).CurrentValue = role;
        }

        var authItems = insertedUsers
            .Select(u => new AuthBatchSyncItem(u.User, u.Password))
            .ToList();
        var authResults = await SyncToAuthServiceBatchAsync(authItems, linkSubjectsInParallel: true, ct);

        var saturdayAssignments = new List<(int UserId, int SubServiceId)>();

        foreach (var (user, dto, password) in insertedUsers)
        {
            var auth = authResults.FirstOrDefault(r =>
                string.Equals(r.Email, user.Email, StringComparison.OrdinalIgnoreCase));

            if (auth is not { Success: true, AuthUserId: not null })
            {
                if (user.AuthUserId is null && user.CreatedAt >= DateTime.UtcNow.AddMinutes(-10))
                    await RollbackImportUserAsync(user);

                results.Add(new ImportChunkCreateResultDto
                {
                    Email = user.Email.ToLowerInvariant(),
                    Success = false,
                    ErrorMessage = auth?.Message
                        ?? "La synchronisation Auth a échoué. L'employé n'a pas été conservé.",
                });
                continue;
            }

            try
            {
                DetachPendingAddedUsers();

                if (dto.CustomFields is { Count: > 0 })
                    await _fieldService.UpsertCustomFieldsAsync(user.Id, dto.CustomFields, isCreate: true);

                if (NeedsLocalHrUpsertOnImport(dto))
                {
                    await UpsertLocalHrProfileAsync(
                        user.Id, dto.ChefDeProjetId, dto.SuperviseurId, dto.ReferentTechniqueId,
                        dto.HrProfile, dto.NiveauExpertiseMetier);
                }

                var needsHrDirectorySync = dto.ChefDeProjetId.HasValue
                    || dto.SuperviseurId.HasValue
                    || dto.ReferentTechniqueId.HasValue
                    || dto.NiveauExpertiseMetier is not null
                    || dto.HrProfile is { EnFormation: true };
                if (needsHrDirectorySync)
                    await _directoryEmployeeWrite.TryUpdateEmployeeAsync(user, ct);

                await PublishEmployeCreatedForUserAsync(user, dto.SubServiceId, skipRemoteSupervisorLookup: true);

                if (user.SubServiceId is int subId)
                    saturdayAssignments.Add((user.Id, subId));

                results.Add(new ImportChunkCreateResultDto
                {
                    Email = user.Email.ToLowerInvariant(),
                    Success = true,
                    PlanningUserId = user.Id,
                    EmployeeGuid = user.Guid,
                    AuthUserId = user.AuthUserId,
                    TemporaryPassword = password,
                });
            }
            catch (Exception ex)
            {
                DetachPendingHrProfile(user.Id);
                results.Add(new ImportChunkCreateResultDto
                {
                    Email = user.Email.ToLowerInvariant(),
                    Success = false,
                    ErrorMessage = FormatPersistenceError(ex),
                });
            }
        }

        if (saturdayAssignments.Count > 0)
        {
            DetachPendingAddedUsers();
            await EnsureBalancedSaturdayGroupsBatchAsync(saturdayAssignments, ct);
        }

        try
        {
            DetachPendingAddedUsers();
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(ex, "Conflit unique en fin de chunk import — résultats partiels conservés.");
        }

        return results;
    }

    /// <summary>
    /// Insert Planning en batch (1 SaveChanges). Repli un-par-un seulement si course
    /// avec DirectoryEmployeeProjectionConsumer (IX_Users_Email / Guid).
    /// </summary>
    private async Task<List<(User User, CreateUserFromImportDto Dto, string Password)>> PersistImportUsersHandlingConflictsAsync(
        List<(User User, CreateUserFromImportDto Dto, string Password)> usersToInsert,
        List<ImportChunkCreateResultDto> results,
        CancellationToken ct)
    {
        var guids = usersToInsert.Select(u => u.User.Guid).ToList();
        var emails = usersToInsert
            .Select(u => u.User.Email.Trim().ToLowerInvariant())
            .ToList();

        var existingRows = await _context.Users
            .Where(u => guids.Contains(u.Guid) || emails.Contains(u.Email.ToLower()))
            .ToListAsync(ct);

        var byGuid = existingRows.ToDictionary(u => u.Guid);
        var byEmail = existingRows
            .GroupBy(u => u.Email.Trim().ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var inserted = new List<(User User, CreateUserFromImportDto Dto, string Password)>();
        var toAdd = new List<(User User, CreateUserFromImportDto Dto, string Password)>();
        var adoptedDirty = false;

        foreach (var (user, dto, password) in usersToInsert)
        {
            var emailKey = user.Email.Trim().ToLowerInvariant();

            if (byGuid.TryGetValue(user.Guid, out var existingByGuid))
            {
                ApplyImportCreateOntoExisting(existingByGuid, user);
                adoptedDirty = true;
                inserted.Add((existingByGuid, dto, password));
                continue;
            }

            if (byEmail.TryGetValue(emailKey, out var existingByEmail))
            {
                // Course avec DirectoryEmployeeProjectionConsumer : même email déjà projeté.
                // On adopte et on aligne le Guid Directory (source de vérité WriteMaster).
                if (existingByEmail.Guid != user.Guid && byGuid.ContainsKey(user.Guid))
                {
                    results.Add(new ImportChunkCreateResultDto
                    {
                        Email = emailKey,
                        Success = false,
                        ErrorMessage =
                            $"L'adresse email « {emailKey} » est déjà utilisée (conflit IX_Users_Email).",
                    });
                    continue;
                }

                existingByEmail.Guid = user.Guid;
                ApplyImportCreateOntoExisting(existingByEmail, user);
                byGuid[user.Guid] = existingByEmail;
                adoptedDirty = true;
                inserted.Add((existingByEmail, dto, password));
                _logger.LogInformation(
                    "User {Email} adopté après projection Directory (Guid aligné).",
                    emailKey);
                continue;
            }

            toAdd.Add((user, dto, password));
        }

        if (adoptedDirty)
        {
            try
            {
                await _context.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _logger.LogWarning(ex, "Conflit unique en adoptant {Count} user(s) d'import — repli un-par-un.", inserted.Count);
                foreach (var entry in _context.ChangeTracker.Entries<User>().ToList())
                {
                    if (entry.State is EntityState.Added or EntityState.Modified)
                        entry.State = EntityState.Detached;
                }

                var adopted = inserted.ToList();
                inserted.Clear();
                inserted.AddRange(await PersistImportUsersOneByOneAsync(adopted, results, ct));
            }
        }

        if (toAdd.Count == 0)
            return inserted;

        foreach (var (user, _, _) in toAdd)
            _context.Users.Add(user);

        try
        {
            await _context.SaveChangesAsync(ct);
            inserted.AddRange(toAdd);
            return inserted;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _logger.LogWarning(
                ex,
                "Conflit unique sur insert batch import ({Count}) — repli un-par-un.",
                toAdd.Count);

            foreach (var entry in _context.ChangeTracker.Entries<User>().ToList())
            {
                if (entry.State is EntityState.Added or EntityState.Modified)
                    entry.State = EntityState.Detached;
            }

            // Les adopés déjà sauvegardés restent dans inserted ; on rejoue seulement toAdd.
            var oneByOne = await PersistImportUsersOneByOneAsync(toAdd, results, ct);
            inserted.AddRange(oneByOne);
            return inserted;
        }
    }

    private async Task<List<(User User, CreateUserFromImportDto Dto, string Password)>> PersistImportUsersOneByOneAsync(
        List<(User User, CreateUserFromImportDto Dto, string Password)> usersToInsert,
        List<ImportChunkCreateResultDto> results,
        CancellationToken ct)
    {
        var inserted = new List<(User User, CreateUserFromImportDto Dto, string Password)>();

        foreach (var (user, dto, password) in usersToInsert)
        {
            var emailKey = user.Email.Trim().ToLowerInvariant();

            var existing = await _context.Users
                .FirstOrDefaultAsync(
                    u => u.Guid == user.Guid || u.Email.ToLower() == emailKey,
                    ct);

            if (existing is not null)
            {
                // Guid égal OU même email (projection async) → adopter.
                var guidTakenByOther = existing.Guid != user.Guid
                    && await _context.Users.AnyAsync(
                        u => u.Guid == user.Guid && u.Id != existing.Id, ct);
                if (guidTakenByOther)
                {
                    results.Add(new ImportChunkCreateResultDto
                    {
                        Email = emailKey,
                        Success = false,
                        ErrorMessage =
                            $"L'adresse email « {emailKey} » est déjà utilisée (conflit IX_Users_Email).",
                    });
                    continue;
                }

                existing.Guid = user.Guid;
                ApplyImportCreateOntoExisting(existing, user);
                await _context.SaveChangesAsync(ct);
                inserted.Add((existing, dto, password));
                continue;
            }

            _context.Users.Add(user);
            try
            {
                await _context.SaveChangesAsync(ct);
                inserted.Add((user, dto, password));
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                _context.Entry(user).State = EntityState.Detached;

                existing = await _context.Users
                    .FirstOrDefaultAsync(
                        u => u.Guid == user.Guid || u.Email.ToLower() == emailKey,
                        ct);

                if (existing is not null)
                {
                    var guidTakenByOther = existing.Guid != user.Guid
                        && await _context.Users.AnyAsync(
                            u => u.Guid == user.Guid && u.Id != existing.Id, ct);
                    if (guidTakenByOther)
                    {
                        results.Add(new ImportChunkCreateResultDto
                        {
                            Email = emailKey,
                            Success = false,
                            ErrorMessage =
                                $"L'adresse email « {emailKey} » est déjà utilisée (conflit IX_Users_Email).",
                        });
                    }
                    else
                    {
                        existing.Guid = user.Guid;
                        ApplyImportCreateOntoExisting(existing, user);
                        await _context.SaveChangesAsync(ct);
                        inserted.Add((existing, dto, password));
                        _logger.LogInformation(
                            "User {Email} adopté après conflit IX_Users_Email (projection Directory).",
                            emailKey);
                    }
                }
                else
                {
                    results.Add(new ImportChunkCreateResultDto
                    {
                        Email = emailKey,
                        Success = false,
                        ErrorMessage =
                            $"L'adresse email « {emailKey} » est déjà utilisée (conflit IX_Users_Email).",
                    });
                }
            }
        }

        return inserted;
    }

    private static void ApplyImportCreateOntoExisting(User existing, User imported)
    {
        existing.FirstName = imported.FirstName;
        existing.LastName = imported.LastName;
        existing.RoleId = imported.RoleId;
        existing.SubServiceId = imported.SubServiceId;
        existing.HireDate = imported.HireDate;
        existing.Level = imported.Level;
        existing.IsActive = imported.IsActive;
        // Toujours aligner le hash sur le MDP d'import (sinon Excel ≠ Auth après projection Directory).
        if (!string.IsNullOrWhiteSpace(imported.PasswordHash))
            existing.PasswordHash = imported.PasswordHash;
    }

    private async Task EnsureBalancedSaturdayGroupsBatchAsync(
        IReadOnlyList<(int UserId, int SubServiceId)> assignments,
        CancellationToken ct)
    {
        if (assignments.Count == 0)
            return;

        var userIds = assignments.Select(a => a.UserId).ToList();
        var already = (await _context.SaturdayGroups.AsNoTracking()
                .Where(sg => userIds.Contains(sg.UserId))
                .Select(sg => sg.UserId)
                .ToListAsync(ct))
            .ToHashSet();

        var pending = assignments.Where(a => !already.Contains(a.UserId)).ToList();
        if (pending.Count == 0)
            return;

        foreach (var group in pending.GroupBy(a => a.SubServiceId))
        {
            var subServiceId = group.Key;
            var peerUserIds = await _context.Users.AsNoTracking()
                .Where(u => u.SubServiceId == subServiceId && u.IsActive)
                .Select(u => u.Id)
                .ToListAsync(ct);

            var counts = await _context.SaturdayGroups.AsNoTracking()
                .Where(sg => peerUserIds.Contains(sg.UserId))
                .GroupBy(sg => sg.GroupNumber)
                .Select(g => new { GroupNumber = g.Key, Count = g.Count() })
                .ToListAsync(ct);

            var group1Count = counts.FirstOrDefault(c => c.GroupNumber == 1)?.Count ?? 0;
            var group2Count = counts.FirstOrDefault(c => c.GroupNumber == 2)?.Count ?? 0;

            foreach (var (userId, _) in group)
            {
                var groupNumber = group1Count <= group2Count ? 1 : 2;
                if (groupNumber == 1) group1Count++;
                else group2Count++;

                _context.SaturdayGroups.Add(new SaturdayGroup
                {
                    UserId = userId,
                    GroupNumber = groupNumber,
                    IsNewEmployee = false,
                    ManagerOverride = false,
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = 0,
                });
            }
        }

        await _context.SaveChangesAsync(ct);
    }

    private static bool NeedsLocalHrUpsertOnImport(CreateUserFromImportDto dto)
    {
        if (dto.ChefDeProjetId.HasValue
            || dto.SuperviseurId.HasValue
            || dto.ReferentTechniqueId.HasValue
            || dto.NiveauExpertiseMetier is not null)
            return true;

        var p = dto.HrProfile;
        if (p is null) return false;

        return p.EnFormation
            || p.DateNaissance.HasValue
            || p.DateDebutFormation.HasValue
            || p.DateFinFormationPrevue.HasValue
            || p.DateEntree.HasValue
            || p.DateSortie.HasValue
            || !string.IsNullOrWhiteSpace(p.Cin)
            || !string.IsNullOrWhiteSpace(p.Telephone1)
            || !string.IsNullOrWhiteSpace(p.EmailPersonnel)
            || !string.IsNullOrWhiteSpace(p.Adresse)
            || !string.IsNullOrWhiteSpace(p.VilleNaissance)
            || !string.IsNullOrWhiteSpace(p.Nationalite)
            || !string.IsNullOrWhiteSpace(p.Rib)
            || !string.IsNullOrWhiteSpace(p.ImmatriculationInterne)
            || !string.IsNullOrWhiteSpace(p.ImmatriculationCnss);
    }

    private void DetachPendingHrProfile(int userId)
    {
        var tracked = _context.UserHrProfiles.Local.FirstOrDefault(p => p.UserId == userId);
        if (tracked is not null)
            _context.Entry(tracked).State = EntityState.Detached;
    }

    private async Task<UserDto> CreateUserDirectoryFirstAsync(
        CreateUserDto dto,
        string? importPassword,
        bool requireAuthSuccess = false)
    {
        if (dto is CreateUserFromImportDto importDto)
            return await CreateUserDirectoryFirstFromImportAsync(importDto, requireAuthSuccess);

        return await CreateUserDirectoryFirstCoreAsync(dto, importPassword, requireAuthSuccess);
    }

    private async Task<UserDto> CreateUserDirectoryFirstFromImportAsync(
        CreateUserFromImportDto dto,
        bool requireAuthSuccess)
    {
        var isActive = dto.IsActiveOnImport ?? true;
        var userDto = await CreateUserDirectoryFirstCoreAsync(dto, ResolveOrGeneratePassword(dto.Password), requireAuthSuccess, isActive);

        if (!isActive && userDto.Id > 0)
        {
            var updateDto = new UpdateUserDto
            {
                RoleId = dto.RoleId,
                SubServiceId = dto.SubServiceId,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                HireDate = dto.HireDate,
                Level = dto.Level,
                IsActive = false,
            };
            return await UpdateUserAsync(userDto.Id, updateDto)
                ?? throw new InvalidOperationException("Mise à jour isActive après import échouée.");
        }

        return userDto;
    }

    private static string ResolveOrGeneratePassword(string? password) =>
        PasswordGenerator.ResolveOrGenerate(password);

    public async Task<ResetPasswordResultDto?> ResetPasswordAsync(int userId, CancellationToken ct = default)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
            return null;

        var password = PasswordGenerator.Generate();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
        await _context.SaveChangesAsync(ct);

        var ok = await ResetAuthPasswordAsync(user, password, ct);
        if (!ok)
            throw new InvalidOperationException(
                "La réinitialisation Auth a échoué. Réessayez ou contactez l'administrateur.");

        _logger.LogInformation(
            "PasswordReset: PlanningUserId={UserId} Email={Email} Actor={Actor}",
            user.Id,
            user.Email,
            _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "unknown");

        return new ResetPasswordResultDto
        {
            UserId = user.Id,
            Email = user.Email,
            TemporaryPassword = password,
            FirstName = user.FirstName,
            LastName = user.LastName,
        };
    }

    public async Task<ResetPasswordsByGroupResultDto> ResetPasswordsByGroupAsync(
        ResetPasswordsByGroupRequest request,
        CancellationToken ct = default)
    {
        var scope = (request.Scope ?? string.Empty).Trim();
        var nodeId = (request.NodeId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(nodeId))
            throw new ArgumentException("Le type de groupe et l’identifiant sont requis.");

        var normalized = scope.ToLowerInvariant() switch
        {
            "pole" or "etage" => "Pole",
            "cellule" => "Cellule",
            "service" or "sousservice" or "sous-service" => "Service",
            _ => throw new ArgumentException("Groupe invalide. Utilisez Pole, Cellule ou Service."),
        };

        var hintGuids = new HashSet<Guid>();
        foreach (var raw in request.EmployeeGuids ?? [])
        {
            if (Guid.TryParse(raw, out var g))
                hintGuids.Add(g);
        }

        var query = _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.SubService)!.ThenInclude(s => s!.Service)!.ThenInclude(sv => sv.Floor)
            .Where(u => u.IsActive && (u.Role == null || u.Role.Name != "Admin"));

        query = normalized switch
        {
            "Pole" => query.Where(u =>
                hintGuids.Contains(u.Guid)
                || (u.SubService != null
                    && u.SubService.Service != null
                    && u.SubService.Service.Floor != null
                    && u.SubService.Service.Floor.PrimePoleId == nodeId)),
            "Cellule" => query.Where(u =>
                hintGuids.Contains(u.Guid)
                || (u.SubService != null
                    && u.SubService.Service != null
                    && u.SubService.Service.PrimeCelluleId == nodeId)),
            "Service" => query.Where(u =>
                hintGuids.Contains(u.Guid)
                || (u.SubService != null && u.SubService.PrimeServiceId == nodeId)),
            _ => query.Where(_ => false),
        };

        var matched = await query.ToListAsync(ct);

        const int maxBatch = 200;
        if (matched.Count > maxBatch)
            throw new InvalidOperationException(
                $"Ce groupe contient {matched.Count} employés (max {maxBatch}). Choisissez une cellule ou un service plus petit.");

        var result = new ResetPasswordsByGroupResultDto
        {
            Scope = normalized,
            NodeId = nodeId,
            TargetedCount = matched.Count,
        };

        foreach (var user in matched)
        {
            try
            {
                var reset = await ResetPasswordAsync(user.Id, ct);
                if (reset is null)
                {
                    result.FailedCount++;
                    result.Errors.Add($"{user.Email} : introuvable.");
                    continue;
                }

                result.ResetCount++;
                result.Items.Add(reset);
            }
            catch (Exception ex)
            {
                result.FailedCount++;
                result.Errors.Add($"{user.Email} : {ex.Message}");
                _logger.LogWarning(ex, "Group password reset failed for {Email}", user.Email);
            }
        }

        _logger.LogInformation(
            "PasswordResetGroup: Scope={Scope} Node={Node} Targeted={Targeted} Reset={Reset} Failed={Failed} Actor={Actor}",
            normalized,
            nodeId,
            result.TargetedCount,
            result.ResetCount,
            result.FailedCount,
            _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "unknown");

        return result;
    }

    private async Task<bool> ResetAuthPasswordAsync(User user, string newPassword, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                "api/auth/admin/reset-password",
                new
                {
                    EmployeeId = user.Guid,
                    Email = user.Email,
                    NewPassword = newPassword,
                },
                ct);

            if (response.IsSuccessStatusCode)
                return true;

            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning(
                "Reset Auth password failed for {Email}: {Status} {Body}",
                user.Email,
                response.StatusCode,
                body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reset Auth password exception for {Email}", user.Email);
            return false;
        }
    }

    private static void ValidateCreateUserHrDates(CreateUserDto dto)
    {
        var hr = dto.HrProfile;
        if (hr is null || !hr.EnFormation)
            return;

        if (hr.DateDebutFormation is null)
            throw new InvalidOperationException("La date de début de formation est obligatoire.");
        if (hr.DateFinFormationPrevue is null)
            throw new InvalidOperationException("La date de fin de formation prévue est obligatoire.");
        if (hr.DateFinFormationPrevue < hr.DateDebutFormation)
            throw new InvalidOperationException(
                "La date de fin de formation doit être postérieure ou égale à la date de début.");
        if (hr.DateEntree is not null && hr.DateDebutFormation < hr.DateEntree)
            throw new InvalidOperationException(
                "La date de début de formation doit être postérieure ou égale à la date d'entrée.");

        var hire = DateOnly.FromDateTime(dto.HireDate.Date);
        if (hr.DateFinFormationPrevue < hire)
            throw new InvalidOperationException(
                "La date de fin de formation doit être postérieure ou égale à la date d'embauche.");
    }

    private async Task<UserDto> CreateUserDirectoryFirstCoreAsync(
        CreateUserDto dto,
        string? importPassword,
        bool requireAuthSuccess = false,
        bool isActive = true)
    {
        ValidateCreateUserHrDates(dto);

        var role = await _context.Roles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == dto.RoleId);
        var roleName = role?.Name ?? KyntusRoleNames.Employee;

        string? primeServiceId = null;
        if (dto.SubServiceId.HasValue)
        {
            primeServiceId = await _context.SubServices.AsNoTracking()
                .Where(ss => ss.Id == dto.SubServiceId.Value)
                .Select(ss => ss.PrimeServiceId)
                .FirstOrDefaultAsync();
        }

        var directoryResult = await _directoryEmployeeWrite.TryCreateEmployeeAsync(
            dto.FirstName,
            dto.LastName,
            dto.Email,
            roleName,
            primeServiceId,
            dto.HireDate,
            idTechnicien: dto.IdTechnicien);

        if (!directoryResult.Success)
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(directoryResult.ErrorMessage)
                    ? "La création dans l'annuaire (Directory) a échoué. Réessayez ou contactez l'administrateur."
                    : directoryResult.ErrorMessage);

        var password = ResolveOrGeneratePassword(importPassword);
        User? user = null;
        var committed = false;

        try
        {
            user = new User
            {
                Guid = directoryResult.EmployeeId,
                IdTechnicien = dto.IdTechnicien,
                RoleId = dto.RoleId,
                SubServiceId = dto.SubServiceId,
                FirstName = dto.FirstName,
                LastName = dto.LastName,
                Email = dto.Email,
                HireDate = dto.HireDate,
                Level = dto.Level,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                IsActive = isActive,
                CreatedAt = DateTime.UtcNow
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            await _context.Entry(user).Reference(u => u.Role).LoadAsync();

            await SyncToAuthServiceAsync(user, password);

            if (requireAuthSuccess && !user.AuthUserId.HasValue)
            {
                throw new InvalidOperationException(
                    "La synchronisation Auth a échoué. L'employé n'a pas été conservé.");
            }

            await _fieldService.UpsertCustomFieldsAsync(user.Id, dto.CustomFields, isCreate: true);
            await UpsertLocalHrProfileAsync(user.Id, dto.ChefDeProjetId, dto.SuperviseurId, dto.ReferentTechniqueId, dto.HrProfile, dto.NiveauExpertiseMetier);

            // Un seul write Directory supplémentaire seulement si mentors / formation à pousser.
            var needsHrDirectorySync = dto.ChefDeProjetId.HasValue
                || dto.SuperviseurId.HasValue
                || dto.ReferentTechniqueId.HasValue
                || dto.NiveauExpertiseMetier is not null
                || dto.HrProfile is { EnFormation: true };
            if (needsHrDirectorySync)
                await _directoryEmployeeWrite.TryUpdateEmployeeAsync(user);

            await PublishEmployeCreatedForUserAsync(user, dto.SubServiceId, skipRemoteSupervisorLookup: true);
            await _context.SaveChangesAsync();

            if (user.SubServiceId is int subId)
                await EnsureBalancedSaturdayGroupAsync(user.Id, subId);

            // Import hot path : pas de GetUserById / LoadOrgNameContext (GET all employees Directory).
            var hrProfile = await _context.UserHrProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id);
            var resultDto = ToDto(user, null, EmptyOrgNameContext, hrProfile);
            resultDto.GeneratedPassword = password;
            committed = true;
            return resultDto;
        }
        catch
        {
            if (!committed)
            {
                try
                {
                    if (user is { Id: > 0 })
                        await RollbackImportCreatedUserCoreAsync(user);
                    else
                        await _directoryEmployeeWrite.TryDeleteEmployeeAsync(directoryResult.EmployeeId);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(
                        rollbackEx,
                        "Rollback create Directory-first échoué pour {Email}",
                        dto.Email);
                }
            }

            throw;
        }
    }

    private async Task SyncToAuthServiceAsync(User user, string? defaultPassword = null)
    {
        var maxRetries = 3;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Une synchronisation de rôle/affectation ne doit jamais changer
                // le mot de passe d'un compte Auth existant. Générer un mot de
                // passe uniquement lors du provisionnement initial.
                var password = !user.AuthUserId.HasValue || !string.IsNullOrWhiteSpace(defaultPassword)
                    ? ResolveOrGeneratePassword(defaultPassword)
                    : null;
                var response = await _httpClient.PostAsJsonAsync(
                    "api/auth/register-from-planning",
                    new
                    {
                        Email = user.Email,
                        DefaultPassword = password,
                        RoleName = user.Role?.Name,
                        EmployeeId = user.Guid,
                    });

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content
                        .ReadFromJsonAsync<AuthRegisterResult>();
                    if (result != null)
                    {
                        user.AuthUserId = result.Id;
                        await _context.SaveChangesAsync();
                        if (result.SubjectId != Guid.Empty)
                            await _directoryEmployeeWrite.TryLinkAuthSubjectAsync(user.Guid, result.SubjectId);
                        _logger.LogInformation("AuthUserId={Id} lié à {Email}",
                            result.Id, user.Email);
                        return;
                    }
                }

                var body = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Tentative {Attempt} → {Status} : {Body}",
                    attempt, response.StatusCode, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tentative {Attempt}/{Max} sync Auth", attempt, maxRetries);
            }

            if (attempt < maxRetries)
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
        }

        _logger.LogError("Sync Auth échouée après {Max} tentatives pour {Email}",
            maxRetries, user.Email);
    }

    private async Task<IReadOnlyList<AuthBatchSyncResult>> SyncToAuthServiceBatchAsync(
        IReadOnlyList<AuthBatchSyncItem> items,
        bool linkSubjectsInParallel = false,
        CancellationToken ct = default)
    {
        if (items.Count == 0)
            return Array.Empty<AuthBatchSyncResult>();

        var maxRetries = 3;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var payload = new
                {
                    Items = items.Select(i => new
                    {
                        Email = i.User.Email,
                        DefaultPassword = ResolveOrGeneratePassword(i.DefaultPassword),
                        RoleName = i.User.Role?.Name,
                        EmployeeId = i.User.Guid,
                    }).ToList(),
                };

                var response = await _httpClient.PostAsJsonAsync(
                    "api/auth/register-from-planning-batch",
                    payload,
                    ct);

                if (response.IsSuccessStatusCode)
                {
                    var results = await response.Content
                        .ReadFromJsonAsync<List<AuthBatchSyncResult>>(cancellationToken: ct);

                    if (results is not null)
                    {
                        var toLink = new List<User>();
                        foreach (var item in items)
                        {
                            var match = results.FirstOrDefault(r =>
                                string.Equals(r.Email, item.User.Email, StringComparison.OrdinalIgnoreCase));
                            if (match is { Success: true, AuthUserId: not null })
                            {
                                item.User.AuthUserId = match.AuthUserId;
                                toLink.Add(item.User);
                            }
                        }

                        await _context.SaveChangesAsync(ct);

                        if (toLink.Count > 0)
                        {
                            if (linkSubjectsInParallel)
                            {
                                await Parallel.ForEachAsync(
                                    toLink,
                                    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
                                    async (user, token) =>
                                    {
                                        await _directoryEmployeeWrite.TryLinkAuthSubjectAsync(
                                            user.Guid, user.Guid, token);
                                    });
                            }
                            else
                            {
                                foreach (var user in toLink)
                                    await _directoryEmployeeWrite.TryLinkAuthSubjectAsync(user.Guid, user.Guid, ct);
                            }
                        }

                        foreach (var r in results.Where(r => r.Success && r.AuthUserId.HasValue))
                            _logger.LogInformation("AuthUserId={Id} lié à {Email}", r.AuthUserId, r.Email);

                        return results;
                    }
                }

                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Tentative {Attempt} batch sync Auth → {Status} : {Body}",
                    attempt, response.StatusCode, body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tentative {Attempt}/{Max} batch sync Auth", attempt, maxRetries);
            }

            if (attempt < maxRetries)
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), ct);
        }

        _logger.LogError(
            "Batch sync Auth échouée après {Max} tentatives pour {Count} utilisateurs",
            maxRetries,
            items.Count);

        return items
            .Select(i => new AuthBatchSyncResult(i.User.Email, false, null, "Batch sync Auth échouée"))
            .ToList();
    }

    public async Task SyncAllEmployesToCongeAsync()
    {
        var users = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.SubService)
            .Where(u => u.IsActive)
            .ToListAsync();

        foreach (var user in users)
            await PublishEmployeCreatedForUserAsync(user, user.SubServiceId);
    }
    public async Task<UserDto?> UpdateUserAsync(int id, UpdateUserDto dto)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return null;

        var callerRole = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.Role)?.Value
            ?? _httpContextAccessor.HttpContext?.User?.FindFirst("role")?.Value
            ?? string.Empty;
        if (dto.NiveauExpertiseMetier.HasValue && !IsHrOrAdminRole(callerRole))
            throw new UnauthorizedAccessException("Seuls RH et Admin peuvent modifier l'expertise métier.");

        user.RoleId = dto.RoleId;
        user.SubServiceId = dto.SubServiceId;
        user.FirstName = dto.FirstName;
        user.LastName = dto.LastName;
        user.Email = dto.Email;
        user.HireDate = dto.HireDate;
        user.IsActive = dto.IsActive;
        user.Level = dto.Level;

        var existing = _context.UserSubServices.Where(us => us.UserId == id);
        _context.UserSubServices.RemoveRange(existing);

        var existingServices = _context.UserManagedServices.Where(us => us.UserId == id);
        _context.UserManagedServices.RemoveRange(existingServices);

        await _context.SaveChangesAsync();
        await _context.Entry(user).Reference(u => u.Role).LoadAsync();

        if (user.AuthUserId.HasValue)
            await SyncToAuthServiceAsync(user);

        if (!IsDirectoryWriteMaster())
        {
            await PublishEmployeUpdatedForUserAsync(user, dto.SubServiceId);
            await _context.SaveChangesAsync();
            await _directoryEmployeeEnsure.TryEnsureFromPlanningAsync(user);
        }

        await _fieldService.UpsertCustomFieldsAsync(id, dto.CustomFields, isCreate: false);
        await UpsertLocalHrProfileAsync(id, dto.ChefDeProjetId, dto.SuperviseurId, dto.ReferentTechniqueId, dto.HrProfile, dto.NiveauExpertiseMetier);
        if (IsDirectoryWriteMaster())
            await _directoryEmployeeWrite.TryUpdateEmployeeAsync(user);

        if (user.SubServiceId is int subId)
            await EnsureBalancedSaturdayGroupAsync(user.Id, subId);

        // Évite LoadOrgNameContext (GET Directory complet) sur le chemin import / update fréquent.
        var hrProfile = await _context.UserHrProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == id);
        var custom = await _fieldService.LoadCustomFieldsForUsersAsync([id]);
        return ToDto(user, custom.GetValueOrDefault(id), EmptyOrgNameContext, hrProfile);
    }

    public async Task<bool> DeleteUserAsync(int id)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return false;

        var employeeGuid = user.Guid;
        var authUserId = user.AuthUserId;

        if (authUserId.HasValue)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"api/auth/users/from-planning/{authUserId.Value}");
                if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("Suppression Auth échouée pour {Email} ({Status}): {Body}",
                        user.Email, response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Suppression Auth indisponible pour {Email}", user.Email);
            }
        }

        if (IsDirectoryWriteMaster())
            await _directoryEmployeeWrite.TryDeleteEmployeeAsync(employeeGuid);
        else
            await _directoryEmployeeEnsure.TryDeleteFromPlanningAsync(employeeGuid);

        var managedLinks = _context.UserSubServices.Where(us => us.UserId == id);
        _context.UserSubServices.RemoveRange(managedLinks);

        var managedServiceLinks = _context.UserManagedServices.Where(us => us.UserId == id);
        _context.UserManagedServices.RemoveRange(managedServiceLinks);

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ExitAfterInitialTrainingRejectionAsync(
        Guid employeeGuid,
        string reason,
        CancellationToken ct = default)
    {
        if (employeeGuid == Guid.Empty) return false;

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Guid == employeeGuid, ct);
        if (user is null) return false;

        user.IsActive = false;

        var profile = await _context.UserHrProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (profile is null)
        {
            profile = new UserHrProfile { UserId = user.Id };
            _context.UserHrProfiles.Add(profile);
        }

        profile.EnFormation = false;
        profile.DateSortie ??= DateOnly.FromDateTime(DateTime.UtcNow);

        await _context.SaveChangesAsync(ct);

        if (IsDirectoryWriteMaster())
            await _directoryEmployeeWrite.TryUpdateEmployeeAsync(user, ct);
        else
            await _directoryEmployeeEnsure.TryEnsureFromPlanningAsync(user);

        if (user.AuthUserId.HasValue)
        {
            try
            {
                var response = await _httpClient.DeleteAsync(
                    $"api/auth/users/from-planning/{user.AuthUserId.Value}",
                    ct);
                if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.NotFound)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning(
                        "Suppression Auth après rejet formation échouée pour {Email} ({Status}): {Body}",
                        user.Email,
                        response.StatusCode,
                        body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auth indisponible après rejet formation pour {Email}", user.Email);
            }
        }

        _logger.LogInformation(
            "Sortie employé {Guid} ({Email}) après rejet formation initiale. Motif: {Reason}",
            employeeGuid,
            user.Email,
            reason);
        return true;
    }

    public async Task<bool> CompleteInitialTrainingAsync(
        Guid employeeGuid,
        int niveauExpertiseMetier,
        DateOnly productionStartDate,
        CancellationToken ct = default)
    {
        if (employeeGuid == Guid.Empty) return false;

        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Guid == employeeGuid, ct);
        if (user is null) return false;

        var profile = await _context.UserHrProfiles.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        if (profile is null)
        {
            profile = new UserHrProfile { UserId = user.Id };
            _context.UserHrProfiles.Add(profile);
        }

        profile.EnFormation = false;
        profile.DateFinFormationPrevue = productionStartDate == default
            ? DateOnly.FromDateTime(DateTime.UtcNow)
            : productionStartDate;

        var expertise = niveauExpertiseMetier is >= 1 and <= 3
            ? niveauExpertiseMetier
            : 1;
        profile.NiveauExpertiseMetier = expertise;

        await _context.SaveChangesAsync(ct);

        if (IsDirectoryWriteMaster())
            await _directoryEmployeeWrite.TryUpdateEmployeeAsync(user, ct);
        else
            await _directoryEmployeeEnsure.TryEnsureFromPlanningAsync(user);

        _logger.LogInformation(
            "Passage production employé {Guid} ({Email}) — EnFormation=false, expertise={Expertise}.",
            employeeGuid,
            user.Email,
            expertise);
        return true;
    }

    public async Task<bool> IsEmailUniqueAsync(string email, int? excludeId = null)
    {
        var trimmed = email.Trim();
        var usedInPlanning = await _context.Users
            .AnyAsync(u => u.Email == trimmed && u.Id != excludeId);
        if (usedInPlanning)
            return false;

        if (!IsDirectoryWriteMaster())
            return true;

        Guid? excludeGuid = null;
        if (excludeId.HasValue)
        {
            excludeGuid = await _context.Users.AsNoTracking()
                .Where(u => u.Id == excludeId.Value)
                .Select(u => u.Guid)
                .FirstOrDefaultAsync();
            if (excludeGuid == Guid.Empty)
                excludeGuid = null;
        }

        var usedInDirectory = await _directoryEmployeeWrite.IsEmailUsedInDirectoryAsync(trimmed, excludeGuid);
        return !usedInDirectory;
    }
    public async Task<UserDto?> GetUserByAuthIdAsync(int authUserId)
    {
        if (authUserId <= 0)
            return null;

        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.SubService)
                .ThenInclude(ss => ss!.Service)
            .Include(u => u.ManagedSubServices)
                .ThenInclude(us => us.SubService)
                    .ThenInclude(s => s.Service)
            .Include(u => u.ManagedServices)
                .ThenInclude(us => us.Service)
                    .ThenInclude(s => s.Floor)
            .FirstOrDefaultAsync(u => u.AuthUserId == authUserId);

        if (user is null)
            return null;

        var customByUser = await _fieldService.LoadCustomFieldsForUsersAsync([user.Id]);
        var hrProfile = await _context.UserHrProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id);
        var orgCtx = await LoadOrgNameContextAsync();
        return ToDto(user, customByUser.GetValueOrDefault(user.Id), orgCtx, hrProfile);
    }

    public async Task<UserDto?> GetUserByEmailAsync(string email)
    {
        var needle = email.Trim().ToLowerInvariant();
        if (needle.Length == 0)
            return null;

        var user = await _context.Users
            .Include(u => u.Role)
            .Include(u => u.SubService)
                .ThenInclude(ss => ss!.Service)
            .Include(u => u.ManagedSubServices)
                .ThenInclude(us => us.SubService)
                    .ThenInclude(s => s.Service)
            .Include(u => u.ManagedServices)
                .ThenInclude(us => us.Service)
                    .ThenInclude(s => s.Floor)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == needle);

        if (user is null)
            return null;

        var customByUser = await _fieldService.LoadCustomFieldsForUsersAsync([user.Id]);
        var hrProfile = await _context.UserHrProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == user.Id);
        var orgCtx = await LoadOrgNameContextAsync();
        return ToDto(user, customByUser.GetValueOrDefault(user.Id), orgCtx, hrProfile);
    }

    public async Task<UserDto?> GetOrLinkUserForAuthAsync(int authUserId, string? email)
    {
        var user = authUserId > 0 ? await GetUserByAuthIdAsync(authUserId) : null;
        if (user is not null)
            return user;

        if (string.IsNullOrWhiteSpace(email))
            return null;

        var row = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.Trim().ToLowerInvariant());
        if (row is null)
            return null;

        if (authUserId > 0 && row.AuthUserId != authUserId)
        {
            row.AuthUserId = authUserId;
            await _context.SaveChangesAsync();
            _logger.LogInformation("AuthUserId={AuthId} lié à {Email} (planning id={PlanningId})",
                authUserId, row.Email, row.Id);
        }

        return (authUserId > 0 ? await GetUserByAuthIdAsync(authUserId) : null)
            ?? await GetUserByEmailAsync(email);
    }

    public async Task<UserDto?> GetOrEnsureUserForAuthAsync(
        int authUserId,
        string? email,
        string? authRole,
        Guid? subjectId,
        CancellationToken ct = default)
    {
        var linked = await GetOrLinkUserForAuthAsync(authUserId, email);
        if (linked is not null)
            return linked;

        if (subjectId is { } sid && sid != Guid.Empty)
        {
            var byGuidId = await _context.Users.AsNoTracking()
                .Where(u => u.Guid == sid)
                .Select(u => u.Id)
                .FirstOrDefaultAsync(ct);
            if (byGuidId > 0)
                return await GetUserByIdAsync(byGuidId);
        }

        if (authUserId <= 0 || string.IsNullOrWhiteSpace(email))
            return null;

        return await CreateStubUserFromAuthAsync(authUserId, email.Trim(), authRole, subjectId, ct);
    }

    private async Task<UserDto?> CreateStubUserFromAuthAsync(
        int authUserId,
        string email,
        string? authRole,
        Guid? subjectId,
        CancellationToken ct)
    {
        var needle = email.ToLowerInvariant();
        var existingByEmail = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == needle, ct);
        if (existingByEmail is not null)
        {
            if (existingByEmail.AuthUserId != authUserId)
            {
                existingByEmail.AuthUserId = authUserId;
                await _context.SaveChangesAsync(ct);
            }
            return await GetUserByAuthIdAsync(authUserId) ?? await GetUserByEmailAsync(email);
        }

        var roleName = NormalizeAuthRoleToPlanningRole(authRole);
        var role = await _context.Roles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Name.ToLower() == roleName.ToLower(), ct)
            ?? await _context.Roles.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Name.ToLower() == KyntusRoleNames.Pilote.ToLower(), ct);
        if (role is null)
        {
            _logger.LogError("Impossible de provisionner {Email} : aucun rôle Planning « {Role} ».", email, roleName);
            return null;
        }

        var (firstName, lastName) = SplitNameFromEmail(email);
        var guid = subjectId is { } g && g != Guid.Empty ? g : Guid.NewGuid();
        var user = new User
        {
            Guid = guid,
            AuthUserId = authUserId,
            RoleId = role.Id,
            FirstName = firstName,
            LastName = lastName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString("N")),
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            HireDate = DateTime.UtcNow.Date,
            Level = 1,
        };

        _context.Users.Add(user);
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflit création stub Planning pour {Email} — relecture.", email);
            _context.ChangeTracker.Clear();
            return await GetOrLinkUserForAuthAsync(authUserId, email);
        }

        await _context.Entry(user).Reference(u => u.Role).LoadAsync(ct);
        try
        {
            await PublishEmployeCreatedForUserAsync(user, subServiceId: null, skipRemoteSupervisorLookup: true);
            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stub Planning créé pour {Email} mais publish EmployeCreated a échoué.", email);
        }

        _ = await _directoryEmployeeEnsure.TryEnsureFromPlanningAsync(user);

        _logger.LogInformation(
            "Fiche Planning auto-provisionnée depuis Auth JWT : {Email} authId={AuthId} role={Role} planningId={Id}",
            email,
            authUserId,
            role.Name,
            user.Id);

        return await GetUserByIdAsync(user.Id);
    }

    private static string NormalizeAuthRoleToPlanningRole(string? authRole)
    {
        if (string.IsNullOrWhiteSpace(authRole))
            return "Employee";
        var r = authRole.Trim().ToLowerInvariant().Replace('_', ' ');
        return r switch
        {
            "admin" => "Admin",
            "rh" => "RH",
            "manager" => "Manager",
            "coach" or "referent technique" => KyntusRoleNames.ReferentTechnique,
            "rp" or "chef de projet" => KyntusRoleNames.ChefDeProjet,
            "audit" => "Audit",
            "superviseur" => KyntusRoleNames.Superviseur,
            "pilote" => KyntusRoleNames.Pilote,
            "employee" or "user" => KyntusRoleNames.Pilote,
            "equipe formation" or "equipeformation" => "EquipeFormation",
            _ => authRole.Trim(),
        };
    }

    private static (string FirstName, string LastName) SplitNameFromEmail(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split('.', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        static string Cap(string s) =>
            string.IsNullOrWhiteSpace(s) ? "User" : char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();
        if (parts.Length >= 2)
            return (Cap(parts[0]), Cap(parts[1]));
        return (Cap(local), "Auth");
    }

    private async Task PublishEmployeCreatedForUserAsync(
        User user,
        int? subServiceId,
        bool skipRemoteSupervisorLookup = false)
    {
        var ctx = await BuildEmployePublishContextAsync(user, subServiceId, skipRemoteSupervisorLookup);
        await _employePublisher.PublishEmployeCreatedAsync(
            employeId: user.Guid,
            nom: user.LastName,
            prenom: user.FirstName,
            email: user.Email,
            managerId: ctx.SupervisorId,
            serviceId: ctx.ServiceId,
            serviceNom: ctx.ServiceNom,
            dateEmbauche: DateTime.SpecifyKind(user.HireDate, DateTimeKind.Utc),
            estMineur: false,
            role: user.Role?.Name ?? KyntusRoleNames.Employee,
            subServiceId: ctx.SubServiceId,
            primeServiceId: ctx.PrimeServiceId,
            supervisorId: ctx.SupervisorId);
    }

    private async Task PublishEmployeUpdatedForUserAsync(User user, int? subServiceId)
    {
        var ctx = await BuildEmployePublishContextAsync(user, subServiceId, skipRemoteSupervisorLookup: false);
        await _employePublisher.PublishEmployeUpdatedAsync(
            employeId: user.Guid,
            nom: user.LastName,
            prenom: user.FirstName,
            email: user.Email,
            managerId: ctx.SupervisorId,
            serviceId: ctx.ServiceId,
            serviceNom: ctx.ServiceNom,
            role: user.Role?.Name ?? KyntusRoleNames.Employee,
            subServiceId: ctx.SubServiceId,
            primeServiceId: ctx.PrimeServiceId,
            supervisorId: ctx.SupervisorId);
    }

    private async Task<EmployePublishContext> BuildEmployePublishContextAsync(
        User user,
        int? subServiceId,
        bool skipRemoteSupervisorLookup)
    {
        if (!subServiceId.HasValue)
        {
            if (skipRemoteSupervisorLookup)
                return new EmployePublishContext(Guid.Empty, Guid.Empty, string.Empty, null, null);

            var parentOnly = await _directoryHierarchy.ResolveSupervisorIdAsync(user.Guid);
            return new EmployePublishContext(parentOnly, Guid.Empty, string.Empty, null, null);
        }

        var subService = await _context.SubServices
            .AsNoTracking()
            .FirstOrDefaultAsync(ss => ss.Id == subServiceId.Value);

        if (subService == null)
            return new EmployePublishContext(Guid.Empty, Guid.Empty, string.Empty, subServiceId, null);

        Guid supervisorId = Guid.Empty;
        if (!skipRemoteSupervisorLookup)
        {
            supervisorId = await _directoryHierarchy.ResolveSupervisorIdAsync(user.Guid);
            if (supervisorId == Guid.Empty)
            {
                var legacySupervisor = await _context.UserSubServices
                    .AsNoTracking()
                    .Include(us => us.User)
                        .ThenInclude(u => u.Role)
                    .Where(us => us.SubServiceId == subServiceId.Value
                              && us.User.Role != null
                              && (us.User.Role.Name == KyntusRoleNames.Superviseur
                                  || us.User.Role.Name == KyntusRoleNames.Manager))
                    .Select(us => us.User)
                    .FirstOrDefaultAsync();
                supervisorId = legacySupervisor?.Guid ?? Guid.Empty;
            }
        }

        return new EmployePublishContext(
            SupervisorId: supervisorId,
            ServiceId: KyntusGuidEncoding.FromIntId(subService.Id),
            ServiceNom: subService.Name,
            SubServiceId: subService.Id,
            PrimeServiceId: subService.PrimeServiceId);
    }

    private sealed record EmployePublishContext(
        Guid SupervisorId,
        Guid ServiceId,
        string ServiceNom,
        int? SubServiceId,
        string? PrimeServiceId);

    private sealed record OrgNameContext(
        Dictionary<string, string> PoleIdToDeptName,
        Dictionary<string, string> DeptIdToName,
        Dictionary<Guid, string> EmployeeOperationalDeptId);

    private static readonly OrgNameContext EmptyOrgNameContext = new(new(), new(), new());

    private async Task<OrgNameContext> LoadOrgNameContextAsync()
    {
        try
        {
            var depts = await _directoryOrg.GetOperationalDepartmentsAsync();
            var poleToDept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var deptIdToName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dept in depts)
            {
                if (!string.IsNullOrWhiteSpace(dept.Id))
                    deptIdToName[dept.Id.Trim()] = dept.Name?.Trim() ?? dept.Code?.Trim() ?? dept.Id;
                foreach (var poleId in dept.PoleIds ?? [])
                {
                    if (string.IsNullOrWhiteSpace(poleId)) continue;
                    poleToDept[poleId.Trim()] = dept.Name?.Trim() ?? dept.Code?.Trim() ?? dept.Id;
                }
            }

            var employeeDeptIds = await _directoryOrg.GetEmployeeOperationalBusinessDepartmentIdsAsync();
            return new OrgNameContext(poleToDept, deptIdToName, employeeDeptIds.ToDictionary(k => k.Key, v => v.Value));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Impossible de charger le contexte org Directory — noms vides.");
            return EmptyOrgNameContext;
        }
    }

    private static UserDto ToDto(
        User u,
        Dictionary<string, string?>? customFields,
        OrgNameContext orgCtx,
        UserHrProfile? hr = null)
    {
        var (pole, cellule, service) = ResolveOrgNames(u);
        return new UserDto
        {
            Id = u.Id,
            Guid = u.Guid,
            AuthUserId = u.AuthUserId,
            RoleId = u.RoleId,
            RoleName = u.Role?.Name ?? string.Empty,
            SubServiceId = u.SubServiceId,
            SubServiceName = u.SubService?.Name,
            OrgPoleName = pole,
            OrgCelluleName = cellule,
            OrgServiceName = service,
            OrgOperationalDepartmentName = ResolveOperationalDepartmentName(u, orgCtx),
            ManagedSubServices = u.ManagedSubServices?
                .Where(us => us.SubService != null)
                .Select(us => new SubServiceSimpleDto
                {
                    Id = us.SubService.Id,
                    Name = us.SubService.Name,
                    ServiceName = us.SubService.Service?.Name ?? string.Empty
                }).ToList() ?? new(),
            ManagedServices = u.ManagedServices?
                .Where(us => us.Service != null)
                .Select(us => new ServiceSimpleDto
                {
                    Id = us.Service.Id,
                    Name = us.Service.Name,
                    FloorName = us.Service.Floor?.Name ?? string.Empty
                }).ToList() ?? new(),
            FirstName = u.FirstName,
            LastName = u.LastName,
            Email = u.Email,
            HireDate = u.HireDate,
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt,
            Level = u.Level,
            SaturdayWorkMode = u.SaturdayWorkMode,
            IsSpecialCase = u.IsSpecialCase,
            SpecialCaseDescription = u.SpecialCaseDescription,
            IsPlateauTraining = u.IsPlateauTraining,
            NiveauExpertiseMetier = hr?.NiveauExpertiseMetier,
            ChefDeProjetId = hr?.ChefDeProjetId,
            SuperviseurId = hr?.SuperviseurId,
            ReferentTechniqueId = hr?.ReferentTechniqueId,
            IdTechnicien = u.IdTechnicien,
            HtelCode = u.HtelCode,
            HrProfile = hr is null ? null : MapHrProfileDto(hr),
            CustomFields = customFields ?? new Dictionary<string, string?>(),
            LifecycleStatus = BuildLifecycleStatus(u, hr),
        };
    }

    private static EmployeeLifecycleStatusDto BuildLifecycleStatus(User u, UserHrProfile? hr)
    {
        var enFormation = hr?.EnFormation == true;
        var authOk = u.AuthUserId is > 0;
        var active = u.IsActive;

        string phase;
        string label;
        if (!active)
        {
            phase = "inactive";
            label = "Inactif";
        }
        else if (!authOk)
        {
            phase = "awaiting_auth";
            label = "Compte Auth manquant";
        }
        else if (enFormation)
        {
            phase = "onboarding_formation";
            label = "En formation";
        }
        else
        {
            phase = "active";
            label = "Actif";
        }

        var steps = new List<EmployeeLifecycleStepDto>
        {
            new()
            {
                Id = "account",
                Label = "Compte Planning",
                State = active ? "done" : "blocked",
            },
            new()
            {
                Id = "auth",
                Label = "Compte Auth",
                State = authOk ? "done" : (active ? "current" : "pending"),
            },
            new()
            {
                Id = "formation",
                Label = "Formation initiale",
                State = enFormation ? "current" : (authOk && active ? "done" : "pending"),
            },
            new()
            {
                Id = "production",
                Label = "Passage en production",
                State = !enFormation && authOk && active ? "done" : (enFormation ? "pending" : "pending"),
            },
        };

        return new EmployeeLifecycleStatusDto
        {
            Phase = phase,
            Label = label,
            IsActive = active,
            EnFormation = enFormation,
            AuthProvisioned = authOk,
            FormationDeepLink = enFormation ? $"/users/{u.Id}/edit" : null,
            PassageProductionDeepLink = enFormation ? "/formations/passage-production" : null,
            EditDeepLink = $"/users/{u.Id}/edit",
            Steps = steps,
        };
    }

    private static UserHrProfileDto MapHrProfileDto(UserHrProfile p) => new()
    {
        DateNaissance = p.DateNaissance,
        VilleNaissance = p.VilleNaissance,
        Nationalite = p.Nationalite,
        NumeroCarteAutoentrepreneur = p.NumeroCarteAutoentrepreneur,
        Sexe = p.Sexe,
        SituationFamiliale = p.SituationFamiliale,
        NombreEnfants = p.NombreEnfants,
        Cin = p.Cin,
        Adresse = p.Adresse,
        EmailPersonnel = p.EmailPersonnel,
        Telephone1 = p.Telephone1,
        TelephoneUrgence = p.TelephoneUrgence,
        RelationUrgence = p.RelationUrgence,
        Rib = p.Rib,
        ImmatriculationInterne = p.ImmatriculationInterne,
        ImmatriculationCnss = p.ImmatriculationCnss,
        DateEntree = p.DateEntree,
        DateEmbauche = p.DateEmbauche,
        DateAnciennete = p.DateAnciennete,
        DateSortie = p.DateSortie,
        DateEvolutionPoste = p.DateEvolutionPoste,
        AncienPoste = p.AncienPoste,
        AncienService = p.AncienService,
        NiveauScolaire = p.NiveauScolaire,
        IntitulesEtudes = p.IntitulesEtudes,
        EnFormation = p.EnFormation,
        DateDebutFormation = p.DateDebutFormation,
        DateFinFormationPrevue = p.DateFinFormationPrevue,
    };

    private async Task<Dictionary<int, UserHrProfile>> LoadHrProfilesAsync(IReadOnlyList<int> userIds)
    {
        if (userIds.Count == 0) return new Dictionary<int, UserHrProfile>();
        var profiles = await _context.UserHrProfiles.AsNoTracking()
            .Where(p => userIds.Contains(p.UserId))
            .ToListAsync();
        return profiles.ToDictionary(p => p.UserId);
    }

    private async Task UpsertLocalHrProfileAsync(
        int userId,
        Guid? chefDeProjetId,
        Guid? superviseurId,
        Guid? referentTechniqueId,
        UserHrProfileDto? dto,
        int? niveauExpertiseMetier)
    {
        if (dto is null
            && !chefDeProjetId.HasValue
            && !superviseurId.HasValue
            && !referentTechniqueId.HasValue
            && !niveauExpertiseMetier.HasValue)
            return;

        var profile = _context.UserHrProfiles.Local.FirstOrDefault(p => p.UserId == userId)
            ?? await _context.UserHrProfiles.FirstOrDefaultAsync(p => p.UserId == userId);

        var inserted = false;
        if (profile is null)
        {
            profile = new UserHrProfile { UserId = userId };
            _context.UserHrProfiles.Add(profile);
            inserted = true;
        }

        ApplyHrProfileFields(
            profile,
            chefDeProjetId,
            superviseurId,
            referentTechniqueId,
            dto,
            niveauExpertiseMetier);

        // SaveChanges est global au DbContext : un User encore en Added (course Directory)
        // ferait échouer tout le chunk avec « See the inner exception » / IX_Users_Email.
        foreach (var entry in _context.ChangeTracker.Entries<User>().ToList())
        {
            if (entry.State == EntityState.Added)
                entry.State = EntityState.Detached;
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (inserted && IsUniqueConstraintViolation(ex))
        {
            // Course avec DirectoryEmployee*ProjectionConsumer : le profil a été créé en parallèle.
            _context.Entry(profile).State = EntityState.Detached;
            profile = await _context.UserHrProfiles.FirstOrDefaultAsync(p => p.UserId == userId)
                ?? throw new InvalidOperationException(
                    $"Profil RH concurrent introuvable après conflit pour UserId={userId}.");
            ApplyHrProfileFields(
                profile,
                chefDeProjetId,
                superviseurId,
                referentTechniqueId,
                dto,
                niveauExpertiseMetier);
            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "Profil RH {UserId} récupéré après conflit de clé (projection Directory concurrente).",
                userId);
        }
    }

    private static void ApplyHrProfileFields(
        UserHrProfile profile,
        Guid? chefDeProjetId,
        Guid? superviseurId,
        Guid? referentTechniqueId,
        UserHrProfileDto? dto,
        int? niveauExpertiseMetier)
    {
        if (chefDeProjetId.HasValue) profile.ChefDeProjetId = chefDeProjetId;
        if (superviseurId.HasValue) profile.SuperviseurId = superviseurId;
        if (referentTechniqueId.HasValue) profile.ReferentTechniqueId = referentTechniqueId;
        if (niveauExpertiseMetier.HasValue) profile.NiveauExpertiseMetier = niveauExpertiseMetier;

        if (dto is not null)
        {
            profile.DateNaissance = dto.DateNaissance;
            profile.VilleNaissance = dto.VilleNaissance;
            profile.Nationalite = dto.Nationalite;
            profile.NumeroCarteAutoentrepreneur = dto.NumeroCarteAutoentrepreneur;
            profile.Sexe = dto.Sexe;
            profile.SituationFamiliale = dto.SituationFamiliale;
            profile.NombreEnfants = dto.NombreEnfants;
            profile.Cin = dto.Cin;
            profile.Adresse = dto.Adresse;
            profile.EmailPersonnel = dto.EmailPersonnel;
            profile.Telephone1 = dto.Telephone1;
            profile.TelephoneUrgence = dto.TelephoneUrgence;
            profile.RelationUrgence = dto.RelationUrgence;
            profile.Rib = dto.Rib;
            profile.ImmatriculationInterne = dto.ImmatriculationInterne;
            profile.ImmatriculationCnss = dto.ImmatriculationCnss;
            profile.DateEntree = dto.DateEntree;
            profile.DateEmbauche = dto.DateEmbauche;
            profile.DateAnciennete = dto.DateAnciennete;
            profile.DateSortie = dto.DateSortie;
            profile.DateEvolutionPoste = dto.DateEvolutionPoste;
            profile.AncienPoste = dto.AncienPoste;
            profile.AncienService = dto.AncienService;
            profile.NiveauScolaire = dto.NiveauScolaire;
            profile.IntitulesEtudes = dto.IntitulesEtudes;
            profile.EnFormation = dto.EnFormation;
            profile.DateDebutFormation = dto.DateDebutFormation;
            profile.DateFinFormationPrevue = dto.DateFinFormationPrevue;
        }

        profile.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
        {
            var message = inner.Message ?? string.Empty;
            if (message.Contains("23505", StringComparison.Ordinal)
                || message.Contains("unique constraint", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void DetachPendingAddedUsers()
    {
        foreach (var entry in _context.ChangeTracker.Entries<User>().ToList())
        {
            if (entry.State == EntityState.Added)
                entry.State = EntityState.Detached;
        }
    }

    private static string FormatPersistenceError(Exception ex)
    {
        for (var inner = ex; inner is not null; inner = inner.InnerException!)
        {
            var m = inner.Message ?? string.Empty;
            if (m.Contains("IX_Users_Email", StringComparison.OrdinalIgnoreCase)
                || (m.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                    && m.Contains("Email", StringComparison.OrdinalIgnoreCase)))
            {
                return "Email déjà présent dans Planning. Relancez l'import : la ligne sera mise à jour.";
            }
        }

        var current = ex;
        while (current.InnerException is not null)
            current = current.InnerException;

        var msg = current.Message ?? ex.Message;
        if (msg.Contains("See the inner exception", StringComparison.OrdinalIgnoreCase)
            && ex.InnerException is not null)
            return FormatPersistenceError(ex.InnerException);

        return msg;
    }

    public async Task<UserDto?> UpdateContractualLevelAsync(
        int targetUserId,
        int level,
        Guid actorSubjectId,
        string actorRole,
        CancellationToken ct = default)
    {
        if (level is < 1 or > 3)
            throw new InvalidOperationException("Niveau contractuel invalide.");

        var target = await _context.Users
            .Include(u => u.SubService)
                .ThenInclude(ss => ss!.Service)
            .FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (target is null) return null;

        var role = actorRole.Trim();
        if (!ContractualLevelAccess.IsPrivileged(role))
        {
            if (!ContractualLevelAccess.IsAllowedOperationalRole(role))
                throw new UnauthorizedAccessException(ContractualLevelAccess.RoleDeniedMessage);

            var actor = await ResolveContractualLevelActorAsync(actorSubjectId, ct);
            if (actor is null)
                throw new UnauthorizedAccessException(ContractualLevelAccess.ActorNotFoundMessage);

            var rebacAllowed = false;
            var rebacUnavailable = false;
            try
            {
                rebacAllowed = await _rebac.CanActOnAsync(actor.Guid, target.Guid, ct);
            }
            catch (Exception ex)
            {
                rebacUnavailable = true;
                _logger.LogWarning(ex, "Directory CanActOn indisponible pour le niveau contractuel.");
            }

            var localOk = KyntusRoleNames.IsSuperviseur(role)
                ? await IsUserInSupervisorCelluleAsync(actor, target, ct)
                : await IsUserInReferentServiceResponsibilityAsync(actor, target, ct);

            if (!ContractualLevelAccess.AllowUpdate(rebacAllowed, rebacUnavailable, localOk))
                throw new UnauthorizedAccessException(ContractualLevelAccess.DeniedMessage);
        }

        target.Level = level;
        await _context.SaveChangesAsync(ct);
        return await GetUserByIdAsync(targetUserId);
    }

    private async Task<User?> ResolveContractualLevelActorAsync(Guid actorSubjectId, CancellationToken ct)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var jwtEmail = principal.GetEmail();
        var jwtAuthUserId = principal.GetAuthUserId();

        var query = _context.Users.AsNoTracking()
            .Include(u => u.SubService)
                .ThenInclude(ss => ss!.Service)
            .Include(u => u.ManagedSubServices)
                .ThenInclude(us => us.SubService)
                    .ThenInclude(s => s.Service);

        if (jwtAuthUserId is > 0)
        {
            var byAuth = await query.FirstOrDefaultAsync(u => u.AuthUserId == jwtAuthUserId, ct);
            if (byAuth is not null)
                return byAuth;
        }

        if (!string.IsNullOrWhiteSpace(jwtEmail))
        {
            var needle = jwtEmail.Trim().ToLowerInvariant();
            var byEmail = await query.FirstOrDefaultAsync(u => u.Email.ToLower() == needle, ct);
            if (byEmail is not null)
                return byEmail;
        }

        if (actorSubjectId != Guid.Empty)
            return await query.FirstOrDefaultAsync(u => u.Guid == actorSubjectId, ct);

        return null;
    }

    private static bool IsHrOrAdminRole(string role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> IsUserInSupervisorCelluleAsync(User supervisor, User target, CancellationToken ct)
    {
        var targetCellule = target.SubService?.Service?.PrimeCelluleId;
        if (string.IsNullOrWhiteSpace(targetCellule)) return false;

        var supervisorCellules = await _context.UserManagedServices.AsNoTracking()
            .Where(us => us.UserId == supervisor.Id)
            .Select(us => us.Service.PrimeCelluleId)
            .ToListAsync(ct);

        if (supervisor.SubService?.Service?.PrimeCelluleId is { } ownCellule)
            supervisorCellules.Add(ownCellule);

        // Ancre ManagedSubServices (legacy) : cellules via sous-services managés
        var fromSubs = await _context.UserSubServices.AsNoTracking()
            .Where(us => us.UserId == supervisor.Id)
            .Select(us => us.SubService.Service.PrimeCelluleId)
            .ToListAsync(ct);
        supervisorCellules.AddRange(fromSubs);

        return supervisorCellules.Any(c => string.Equals(c, targetCellule, StringComparison.Ordinal));
    }

    private async Task<bool> IsUserInReferentServiceResponsibilityAsync(User referent, User target, CancellationToken ct)
    {
        var targetService = target.SubService?.PrimeServiceId;
        if (string.IsNullOrWhiteSpace(targetService))
            return false;

        var managedServiceIds = await _context.UserSubServices.AsNoTracking()
            .Where(us => us.UserId == referent.Id)
            .Select(us => us.SubService.PrimeServiceId)
            .ToListAsync(ct);

        if (referent.SubService?.PrimeServiceId is { } primary)
            managedServiceIds.Add(primary);

        if (!managedServiceIds.Any(s => string.Equals(s, targetService, StringComparison.Ordinal)))
            return false;

        var targetHr = await _context.UserHrProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == target.Id, ct);
        if (targetHr?.ReferentTechniqueId is { } refId && refId != Guid.Empty)
            return refId == referent.Guid;

        return true;
    }

    private static string? ResolveOperationalDepartmentName(User u, OrgNameContext orgCtx)
    {
        if (orgCtx.EmployeeOperationalDeptId.TryGetValue(u.Guid, out var deptId)
            && orgCtx.DeptIdToName.TryGetValue(deptId, out var managerDeptName))
        {
            return managerDeptName;
        }

        var poleId = u.SubService?.Service?.Floor?.PrimePoleId
            ?? u.ManagedSubServices?.FirstOrDefault()?.SubService?.Service?.Floor?.PrimePoleId
            ?? u.ManagedServices?.FirstOrDefault()?.Service?.Floor?.PrimePoleId;
        if (!string.IsNullOrWhiteSpace(poleId)
            && orgCtx.PoleIdToDeptName.TryGetValue(poleId.Trim(), out var deptName))
        {
            return deptName;
        }

        return null;
    }

    private static (string? Pole, string? Cellule, string? Service) ResolveOrgNames(User u)
    {
        if (u.SubService?.Service != null)
        {
            return (
                u.SubService.Service.Floor?.Name,
                u.SubService.Service.Name,
                u.SubService.Name);
        }

        var managedSub = u.ManagedSubServices?.FirstOrDefault()?.SubService;
        if (managedSub?.Service != null)
        {
            return (
                managedSub.Service.Floor?.Name,
                managedSub.Service.Name,
                managedSub.Name);
        }

        var managedSvc = u.ManagedServices?.FirstOrDefault()?.Service;
        if (managedSvc != null)
        {
            return (managedSvc.Floor?.Name, managedSvc.Name, null);
        }

        return (null, null, null);
    }

    private bool IsDirectoryEnsureRequired() =>
        _configuration.GetValue("Directory:RequireEnsureOnWrite", false);

    private bool IsDirectoryWriteMaster() =>
        _configuration.GetValue("Directory:WriteMaster", true);

    public async Task RollbackImportCreatedUserAsync(int planningUserId, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == planningUserId, ct);
        if (user is null)
            return;

        await RollbackImportCreatedUserCoreAsync(user, ct);
    }

    public async Task RollbackImportUpdatedUserAsync(
        int planningUserId,
        UpdateUserDto previousState,
        Dictionary<string, string?> previousCustomFields,
        CancellationToken ct = default)
    {
        await UpdateUserAsync(planningUserId, previousState);
        await _fieldService.UpsertCustomFieldsAsync(planningUserId, previousCustomFields, isCreate: false);
    }

    public async Task RestoreSubServicePlacementAsync(int planningUserId, int subServiceId, CancellationToken ct = default)
    {
        var user = await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == planningUserId, ct);
        if (user is null)
            return;

        user.SubServiceId = subServiceId;
        await _context.SaveChangesAsync(ct);

        if (IsDirectoryWriteMaster())
            await _directoryEmployeeWrite.TryUpdateEmployeeAsync(user, ct);
        else
            await _directoryEmployeeEnsure.TryEnsureFromPlanningAsync(user);
    }

    private async Task RollbackImportUserAsync(User user) =>
        await RollbackImportCreatedUserCoreAsync(user);

    private async Task RollbackImportCreatedUserCoreAsync(User user, CancellationToken ct = default)
    {
        var employeeGuid = user.Guid;
        var authUserId = user.AuthUserId;

        var managedLinks = _context.UserSubServices.Where(us => us.UserId == user.Id);
        _context.UserSubServices.RemoveRange(managedLinks);
        var managedServiceLinks = _context.UserManagedServices.Where(us => us.UserId == user.Id);
        _context.UserManagedServices.RemoveRange(managedServiceLinks);
        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);

        await _directoryEmployeeWrite.TryDeleteEmployeeAsync(employeeGuid, ct);

        if (authUserId.HasValue)
        {
            try
            {
                await _httpClient.DeleteAsync($"api/auth/users/from-planning/{authUserId.Value}", ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rollback Auth échoué pour {Email}", user.Email);
            }
        }
    }

    private async Task RollbackCreatedUserAsync(User user)
    {
        var managedLinks = _context.UserSubServices.Where(us => us.UserId == user.Id);
        _context.UserSubServices.RemoveRange(managedLinks);
        var managedServiceLinks = _context.UserManagedServices.Where(us => us.UserId == user.Id);
        _context.UserManagedServices.RemoveRange(managedServiceLinks);
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        if (user.AuthUserId.HasValue)
        {
            try
            {
                await _httpClient.DeleteAsync($"api/auth/users/from-planning/{user.AuthUserId.Value}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rollback Auth échoué pour {Email}", user.Email);
            }
        }
    }

    public async Task<List<UserDto>> GetManagersBySubServiceAsync(int subServiceId, CancellationToken ct = default)
    {
        var users = await _context.UserSubServices
            .Include(us => us.User).ThenInclude(u => u.Role)
            .Include(us => us.User).ThenInclude(u => u.SubService)
            .Include(us => us.User)
                .ThenInclude(u => u.ManagedSubServices)
                    .ThenInclude(ms => ms.SubService)
                        .ThenInclude(s => s.Service)
            .Where(us => us.SubServiceId == subServiceId)
            .Select(us => us.User)
            .Distinct()
            .ToListAsync(ct);

        return users.Select(u => new UserDto
        {
            Id = u.Id,
            RoleId = u.RoleId,
            RoleName = u.Role?.Name ?? string.Empty,
            SubServiceId = u.SubServiceId,
            SubServiceName = u.SubService?.Name,
            ManagedSubServices = u.ManagedSubServices?.Select(ms => new SubServiceSimpleDto
            {
                Id = ms.SubService.Id,
                Name = ms.SubService.Name,
                ServiceName = ms.SubService.Service?.Name ?? string.Empty
            }).ToList() ?? [],
            FirstName = u.FirstName,
            LastName = u.LastName,
            HireDate = u.HireDate,
            Email = u.Email,
            IsActive = u.IsActive,
            CreatedAt = u.CreatedAt
        }).ToList();
    }

    /// <summary>
    /// Place l'employé dans le groupe samedi minoritaire (ex. 2:3 → 3:3).
    /// No-op s'il a déjà un SaturdayGroup.
    /// </summary>
    private async Task EnsureBalancedSaturdayGroupAsync(int userId, int subServiceId)
    {
        var already = await _context.SaturdayGroups.AnyAsync(sg => sg.UserId == userId);
        if (already) return;

        var peerUserIds = await _context.Users
            .Where(u => u.SubServiceId == subServiceId && u.IsActive && u.Id != userId)
            .Select(u => u.Id)
            .ToListAsync();

        var group1Count = await _context.SaturdayGroups
            .CountAsync(sg => peerUserIds.Contains(sg.UserId) && sg.GroupNumber == 1);
        var group2Count = await _context.SaturdayGroups
            .CountAsync(sg => peerUserIds.Contains(sg.UserId) && sg.GroupNumber == 2);

        var groupNumber = group1Count <= group2Count ? 1 : 2;
        _context.SaturdayGroups.Add(new SaturdayGroup
        {
            UserId = userId,
            GroupNumber = groupNumber,
            IsNewEmployee = false,
            ManagerOverride = false,
            AssignedAt = DateTime.UtcNow,
            AssignedBy = 0
        });
        await _context.SaveChangesAsync();
    }

    public async Task<SetNewEmployeeStatusResultDto?> SetNewEmployeeStatusAsync(
        int id,
        SetNewEmployeeDto dto,
        CancellationToken ct = default)
    {
        var user = await _context.Users.FindAsync([id], ct);
        if (user is null)
            return null;

        user.IsNewEmployee = dto.IsNewEmployee;
        await _context.SaveChangesAsync(ct);

        return new SetNewEmployeeStatusResultDto(
            user.Id,
            $"{user.FirstName} {user.LastName}",
            user.IsNewEmployee,
            user.HireDate);
    }
}
