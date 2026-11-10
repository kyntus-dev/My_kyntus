using Auth.Application.DTOs;
using Auth.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Commands.Login;

public record LoginCommand(LoginDto Dto) : IRequest<AuthResponseDto>;

public class LoginCommandHandler : IRequestHandler<LoginCommand, AuthResponseDto>
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private readonly IUserRepository _userRepository;
    private readonly IJwtService _jwtService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ILogger<LoginCommandHandler> _logger;
    private readonly IAccessAuditWriter _accessAudit;

    public LoginCommandHandler(
        IUserRepository userRepository,
        IJwtService jwtService,
        IPasswordHasher passwordHasher,
        ILogger<LoginCommandHandler> logger,
        IAccessAuditWriter accessAudit)
    {
        _userRepository = userRepository;
        _jwtService = jwtService;
        _passwordHasher = passwordHasher;
        _logger = logger;
        _accessAudit = accessAudit;
    }

    public async Task<AuthResponseDto> Handle(LoginCommand request, CancellationToken ct)
    {
        var loginDto = request.Dto;
        // v38 : espaces autour de l'e-mail (copier-coller, clavier de téléphone) ignorés.
        var email = (loginDto.Email ?? string.Empty).Trim();
        var user = await _userRepository.GetByEmailAsync(email, ct);
        if (user is null)
        {
            await _accessAudit.WriteAsync("LoginFailed", email, null, "unknown-email", ct);
            throw new UnauthorizedAccessException("Email ou mot de passe incorrect");
        }

        if (!user.IsActive)
        {
            _logger.LogWarning("LoginFailed: inactive account Email={Email}", user.Email);
            await _accessAudit.WriteAsync("LoginFailed", user.Email, user.Id, "inactive", ct);
            // v38 : avec le bon mot de passe, la personne apprend que son compte est désactivé (au lieu
            // d'un « mot de passe incorrect » trompeur) ; sans, le message reste le même pour tous.
            if (MotDePasseValide(user.PasswordHash, loginDto.Password))
                throw new UnauthorizedAccessException("Votre compte est désactivé. Contactez votre responsable.");
            throw new UnauthorizedAccessException("Email ou mot de passe incorrect");
        }

        if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > DateTime.UtcNow)
        {
            _logger.LogWarning("LoginFailed: locked account Email={Email} until {LockoutEnd}", user.Email, lockoutEnd);
            await _accessAudit.WriteAsync("Lockout", user.Email, user.Id, "still-locked", ct);
            throw new UnauthorizedAccessException("Compte temporairement verrouillé. Réessayez plus tard.");
        }

        if (!MotDePasseValide(user.PasswordHash, loginDto.Password))
        {
            user.AccessFailedCount++;
            if (user.AccessFailedCount >= MaxFailedAttempts)
            {
                user.LockoutEnd = DateTime.UtcNow.Add(LockoutDuration);
                user.AccessFailedCount = 0;
                _logger.LogWarning("AccountLocked: Email={Email} until {LockoutEnd}", user.Email, user.LockoutEnd);
                await _accessAudit.WriteAsync("Lockout", user.Email, user.Id, "threshold", ct);
            }
            else
            {
                _logger.LogWarning(
                    "LoginFailed: bad password Email={Email} attempts={Attempts}",
                    user.Email,
                    user.AccessFailedCount);
                await _accessAudit.WriteAsync("LoginFailed", user.Email, user.Id, "bad-password", ct);
            }

            await _userRepository.UpdateAsync(user, ct);
            throw new UnauthorizedAccessException("Email ou mot de passe incorrect");
        }

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.RefreshToken = _jwtService.GenerateRefreshToken();
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await _userRepository.UpdateAsync(user, ct);
        await _accessAudit.WriteAsync("LoginSuccess", user.Email, user.Id, null, ct);

        return AuthResponseMapper.ToDto(
            user,
            _jwtService.GenerateAccessToken(user),
            _jwtService.AccessTokenExpiresInSeconds);
    }

    /// <summary>
    /// v38 : les mots de passe sont enregistrés sans espaces autour (réinitialisation, import). Un espace
    /// ajouté au début ou à la fin (copier-coller, clavier de téléphone) ne fait donc plus échouer la connexion.
    /// </summary>
    private bool MotDePasseValide(string hash, string? saisi)
    {
        if (string.IsNullOrEmpty(saisi)) return false;
        if (_passwordHasher.VerifyPassword(hash, saisi)) return true;
        var nettoye = saisi.Trim();
        return nettoye.Length > 0 && nettoye != saisi && _passwordHasher.VerifyPassword(hash, nettoye);
    }
}
