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
        ILogger<RegisterFromPlanningCommandHandler> logger)
    {
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
        // v38 : jamais d'e-mail de connexion enregistré avec des espaces autour.
        dto.Email = (dto.Email ?? string.Empty).Trim();
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

            // Import / ré-provision : si un MDP est fourni, l'aligner (évite MDP inconnu après projection Directory).
            // v38 : jamais un mot de passe généré par une simple synchronisation (ImposerMotDePasse = false) :
            // il écrasait le mot de passe que la personne venait de recevoir.
            if (dto.ImposerMotDePasse != false
                && !string.IsNullOrWhiteSpace(dto.DefaultPassword)
                && PasswordPolicy.TryValidate(dto.DefaultPassword, out _))
            {
                existing.PasswordHash = _passwordHasher.HashPassword(dto.DefaultPassword!.Trim());
                dirty = true;
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

        // v38 : pas de compte à cet e-mail, mais un compte existe pour ce collaborateur (même GUID) avec
        // un ancien e-mail : on corrige son e-mail au lieu de créer un second compte. Sinon la personne
        // se connectait sur un compte au mot de passe inconnu (« e-mail ou mot de passe incorrect »).
        if (dto.EmployeeId != Guid.Empty
            && await _userRepository.GetBySubjectIdAsync(dto.EmployeeId, ct) is { } memeEmploye
            && !string.IsNullOrWhiteSpace(dto.Email) && dto.Email.Trim().Length <= 100)
        {
            var nouvelEmail = dto.Email.Trim();
            _logger.LogInformation(
                "Email realigned instead of duplicate: AuthUserId={UserId} {Old} -> {New}",
                memeEmploye.Id, memeEmploye.Email, nouvelEmail);
            if (string.Equals(memeEmploye.Username, memeEmploye.Email, StringComparison.OrdinalIgnoreCase) && nouvelEmail.Length <= 50)
                memeEmploye.Username = nouvelEmail;
            memeEmploye.Email = nouvelEmail;
            memeEmploye.RoleId = role.Id;
            if (dto.ImposerMotDePasse != false
                && !string.IsNullOrWhiteSpace(dto.DefaultPassword)
                && PasswordPolicy.TryValidate(dto.DefaultPassword, out _))
                memeEmploye.PasswordHash = _passwordHasher.HashPassword(dto.DefaultPassword!.Trim());
            memeEmploye.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateAsync(memeEmploye, ct);
            return new RegisterFromPlanningResponseDto
            {
                Id = memeEmploye.Id,
                Email = memeEmploye.Email,
                SubjectId = memeEmploye.SubjectId,
            };
        }

        if (!PasswordPolicy.TryValidate(dto.DefaultPassword, out var passwordError))
            throw new ArgumentException(passwordError);

        var user = new User
        {
            Username = dto.Email,
            Email = dto.Email,
            SubjectId = dto.EmployeeId != Guid.Empty
                ? dto.EmployeeId
                : _subjectIdResolver.ResolveForEmail(dto.Email),
            PasswordHash = _passwordHasher.HashPassword(dto.DefaultPassword!.Trim()),
            RoleId = role.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            RefreshToken = null,
            RefreshTokenExpiryTime = null,
            AccessFailedCount = 0,
            LockoutEnd = null,
        };

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
