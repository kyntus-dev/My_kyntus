using Auth.Application.DTOs;
using Auth.Application.Security;
using Auth.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Commands.AdminResetPassword;

public record AdminResetPasswordCommand(AdminResetPasswordDto Dto) : IRequest<bool>;

public class AdminResetPasswordCommandHandler : IRequestHandler<AdminResetPasswordCommand, bool>
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<AdminResetPasswordCommandHandler> _logger;

    public AdminResetPasswordCommandHandler(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        ILogger<AdminResetPasswordCommandHandler> logger)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<bool> Handle(AdminResetPasswordCommand request, CancellationToken ct)
    {
        var dto = request.Dto;
        if (!PasswordPolicy.TryValidate(dto.NewPassword, out var error))
            throw new ArgumentException(error);

        var user = await ResolveUserAsync(dto, ct)
            ?? throw new KeyNotFoundException("Utilisateur introuvable.");

        user.CredentialVersion++;
        user.FirstLoginTokenHash = null;
        user.FirstLoginTokenExpiresAt = null;
        if (user.MustChangePassword && FirstLoginRoles.IsEligible(user.Role.Name))
            user.TemporaryPasswordExpiresAt = DateTime.UtcNow.AddHours(24);
        else
        {
            user.MustChangePassword = false;
            user.TemporaryPasswordExpiresAt = null;
            user.PasswordChangedAt = DateTime.UtcNow;
        }
        user.PasswordHash = _passwordHasher.HashPassword(dto.NewPassword.Trim());
        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, ct);

        _logger.LogInformation(
            "PasswordReset: AuthUserId={UserId} Email={Email} SubjectId={SubjectId}",
            user.Id,
            user.Email,
            user.SubjectId);

        return true;
    }

    private async Task<Domain.Entities.User?> ResolveUserAsync(AdminResetPasswordDto dto, CancellationToken ct)
    {
        if (dto.EmployeeId is { } employeeId && employeeId != Guid.Empty)
        {
            var bySubject = await _userRepository.GetBySubjectIdAsync(employeeId, ct);
            if (bySubject is not null)
                return bySubject;
        }

        if (!string.IsNullOrWhiteSpace(dto.Email))
            return await _userRepository.GetByEmailAsync(dto.Email.Trim(), ct);

        return null;
    }
}

