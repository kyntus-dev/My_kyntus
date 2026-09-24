using Auth.Application.DTOs;
using Auth.Application.Security;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Commands.RegisterFromPlanning;

public record RegisterFromPlanningCommand(RegisterFromPlanningDto Dto) : IRequest<RegisterFromPlanningResponseDto>;

public class RegisterFromPlanningCommandHandler
    : IRequestHandler<RegisterFromPlanningCommand, RegisterFromPlanningResponseDto>
{
    private readonly IFirstLoginService _firstLogin;
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISubjectIdResolver _subjectIdResolver;
    private readonly ILogger<RegisterFromPlanningCommandHandler> _logger;

    public RegisterFromPlanningCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        ISubjectIdResolver subjectIdResolver,
        ILogger<RegisterFromPlanningCommandHandler> logger,
        IFirstLoginService firstLogin)
    {
        _firstLogin = firstLogin;
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _subjectIdResolver = subjectIdResolver;
        _logger = logger;
    }

    public async Task<RegisterFromPlanningResponseDto> Handle(
        RegisterFromPlanningCommand request,
        CancellationToken ct)
    {
        var dto = request.Dto;
        var role = await ResolveAuthRoleAsync(dto, ct)
            ?? throw new RegisterFromPlanningRoleNotFoundException();

        var existing = await _userRepository.GetByEmailAsync(dto.Email, ct);
        if (existing != null)
        {
            var dirty = false;
            if (existing.RoleId != role.Id)
            {
                existing.RoleId = role.Id;
                dirty = true;
            }

            // Aligner le SubjectId JWT sur le Guid Planning (affectations / notifs Formation).
            if (dto.EmployeeId != Guid.Empty && existing.SubjectId != dto.EmployeeId)
            {
                _logger.LogInformation(
                    "SubjectId realigned: AuthUserId={UserId} Email={Email} OldSubjectId={Old} NewSubjectId={New}",
                    existing.Id,
                    existing.Email,
                    existing.SubjectId,
                    dto.EmployeeId);
                existing.SubjectId = dto.EmployeeId;
                dirty = true;
            }

            // Synchronisation et réimport ne réinitialisent jamais le mot de passe.
            // La réinitialisation explicite passe par admin/reset-password ou first-login/enroll.
            if (dirty)
            {
                existing.CredentialVersion++;
                existing.FirstLoginTokenHash = null;
                existing.FirstLoginTokenExpiresAt = null;
                existing.RefreshToken = null;
                existing.RefreshTokenExpiryTime = null;
            }

            if (dirty)
                await _userRepository.UpdateAsync(existing, ct);

            return new RegisterFromPlanningResponseDto
            {
                Id = existing.Id,
                Email = existing.Email,
                SubjectId = existing.SubjectId,
            };
        }

        var user = new User
        {
            Username = dto.Email,
            Email = dto.Email,
            SubjectId = dto.EmployeeId != Guid.Empty
                ? dto.EmployeeId
                : _subjectIdResolver.ResolveForEmail(dto.Email),
            Role = role,
            RoleId = role.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            RefreshToken = null,
            RefreshTokenExpiryTime = null,
            AccessFailedCount = 0,
            LockoutEnd = null,
        };

        if (!_firstLogin.TryInitializeNewUser(user))
        {
            if (!PasswordPolicy.TryValidate(dto.DefaultPassword, out var passwordError))
                throw new ArgumentException(passwordError);
            user.PasswordHash = _passwordHasher.HashPassword(dto.DefaultPassword.Trim());
        }
        await _userRepository.CreateAsync(user, ct);

        _logger.LogInformation(
            "UserProvisioned: AuthUserId={UserId} Email={Email} SubjectId={SubjectId}",
            user.Id,
            user.Email,
            user.SubjectId);

        return new RegisterFromPlanningResponseDto
        {
            Id = user.Id,
            Email = user.Email,
            SubjectId = user.SubjectId,
        };
    }

    async Task<Role?> ResolveAuthRoleAsync(RegisterFromPlanningDto dto, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(dto.RoleName))
        {
            var mappedName = PlanningRoleToAuthRoleMapper.MapToAuthRoleName(dto.RoleName);
            if (!string.IsNullOrWhiteSpace(mappedName))
            {
                var byMappedName = await _roleRepository.GetByNameAsync(mappedName, ct);
                if (byMappedName is not null)
                    return byMappedName;
            }

            var byPlanningName = await _roleRepository.GetByNameAsync(dto.RoleName.Trim(), ct);
            if (byPlanningName is not null)
                return byPlanningName;
        }

        return null;
    }
}

public class RegisterFromPlanningRoleNotFoundException : Exception;

