using Auth.Application.DTOs;
using Auth.Domain.Interfaces;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Auth.Application.Commands.RefreshToken;

public record RefreshTokenCommand(string RefreshToken) : IRequest<AuthResponseDto>;

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, AuthResponseDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IJwtService _jwtService;
    private readonly ILogger<RefreshTokenCommandHandler> _logger;

    public RefreshTokenCommandHandler(
        IUserRepository userRepository,
        IJwtService jwtService,
        ILogger<RefreshTokenCommandHandler> logger)
    {
        _userRepository = userRepository;
        _jwtService = jwtService;
        _logger = logger;
    }

    public async Task<AuthResponseDto> Handle(RefreshTokenCommand request, CancellationToken ct)
    {
        var users = await _userRepository.GetAllAsync(ct);
        var user = users.FirstOrDefault(u => u.RefreshToken == request.RefreshToken);

        if (user == null || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
            throw new UnauthorizedAccessException("Refresh token invalide ou expiré");

        if (!user.IsActive || user.MustChangePassword)
        {
            _logger.LogWarning("RefreshFailed: inactive account Email={Email}", user.Email);
            throw new UnauthorizedAccessException("Refresh token invalide ou expiré");
        }

        if (user.LockoutEnd is { } lockoutEnd && lockoutEnd > DateTime.UtcNow)
        {
            _logger.LogWarning("RefreshFailed: locked account Email={Email}", user.Email);
            throw new UnauthorizedAccessException("Compte temporairement verrouillé. Réessayez plus tard.");
        }

        user.RefreshToken = _jwtService.GenerateRefreshToken();
        user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
        await _userRepository.UpdateAsync(user, ct);

        return AuthResponseMapper.ToDto(
            user,
            _jwtService.GenerateAccessToken(user),
            _jwtService.AccessTokenExpiresInSeconds);
    }
}

