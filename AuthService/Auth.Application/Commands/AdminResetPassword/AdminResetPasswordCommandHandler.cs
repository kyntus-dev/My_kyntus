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

        var (user, trouvePar) = await ResolveUserAsync(dto, ct);
        if (user is null)
            throw new KeyNotFoundException(
                "Compte de connexion introuvable (ni par identifiant Auth, ni par GUID employé, ni par e-mail).");

        await AlignerIdentifiantsAsync(user, dto, trouvePar, ct);

        user.PasswordHash = _passwordHasher.HashPassword(dto.NewPassword.Trim());
        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user, ct);

        _logger.LogInformation(
            "PasswordReset: AuthUserId={UserId} Email={Email} SubjectId={SubjectId} TrouvePar={TrouvePar}",
            user.Id,
            user.Email,
            user.SubjectId,
            trouvePar);

        return true;
    }

    /// <summary>
    /// Ordre de recherche : GUID employé (SubjectId), identifiant Auth connu du Planning, puis e-mail.
    /// Le compte trouvé par identifiant Auth n'est retenu que si son GUID ou son e-mail correspond.
    /// </summary>
    private async Task<(Domain.Entities.User? User, string TrouvePar)> ResolveUserAsync(AdminResetPasswordDto dto, CancellationToken ct)
    {
        if (dto.EmployeeId is { } employeeId && employeeId != Guid.Empty)
        {
            var bySubject = await _userRepository.GetBySubjectIdAsync(employeeId, ct);
            if (bySubject is not null)
                return (bySubject, "guid");
        }

        if (dto.AuthUserId is > 0)
        {
            var byId = await _userRepository.GetByIdAsync(dto.AuthUserId.Value, ct);
            // Retenu seulement si c'est bien la même personne (même GUID ou même e-mail) :
            // un identifiant périmé ne doit jamais réinitialiser le compte de quelqu'un d'autre.
            var memePersonne = byId is not null
                && ((dto.EmployeeId is { } guid && guid != Guid.Empty && byId.SubjectId == guid)
                    || (!string.IsNullOrWhiteSpace(dto.Email)
                        && string.Equals(byId.Email, dto.Email.Trim(), StringComparison.OrdinalIgnoreCase)));
            if (memePersonne)
                return (byId, "identifiant");
            if (byId is not null)
                _logger.LogWarning(
                    "PasswordReset: AuthUserId={Id} ignoré : e-mail {Email} et SubjectId {SubjectId} ne correspondent pas.",
                    byId.Id, byId.Email, byId.SubjectId);
        }

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            var byEmail = await _userRepository.GetByEmailAsync(dto.Email.Trim(), ct);
            if (byEmail is not null)
                return (byEmail, "email");
        }

        return (null, "aucun");
    }

    /// <summary>
    /// Compte retrouvé autrement que par son GUID : on aligne le GUID employé et l'e-mail de connexion
    /// sur le Planning, seulement s'ils ne sont pas déjà portés par un autre compte.
    /// </summary>
    private async Task AlignerIdentifiantsAsync(Domain.Entities.User user, AdminResetPasswordDto dto, string trouvePar, CancellationToken ct)
    {
        if (trouvePar == "guid")
            return;

        if (dto.EmployeeId is { } guid && guid != Guid.Empty && user.SubjectId != guid
            && await _userRepository.GetBySubjectIdAsync(guid, ct) is null)
        {
            _logger.LogInformation(
                "PasswordReset: SubjectId réaligné AuthUserId={Id} {Old} -> {New}", user.Id, user.SubjectId, guid);
            user.SubjectId = guid;
        }

        var email = dto.Email?.Trim();
        if (!string.IsNullOrWhiteSpace(email) && email.Length <= 100
            && !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
        {
            var autre = await _userRepository.GetByEmailAsync(email, ct);
            if (autre is null || autre.Id == user.Id)
            {
                _logger.LogInformation(
                    "PasswordReset: e-mail réaligné AuthUserId={Id} {Old} -> {New}", user.Id, user.Email, email);
                if (string.Equals(user.Username, user.Email, StringComparison.OrdinalIgnoreCase) && email.Length <= 50)
                    user.Username = email;
                user.Email = email;
            }
        }
    }
}
