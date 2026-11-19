using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces;

namespace Auth.Application;

internal static class AuthResponseMapper
{
    public static AuthResponseDto ToDto(User user, string accessToken, int expiresInSeconds) => new()
    {
        AccessToken = accessToken,
        RefreshToken = user.RefreshToken ?? string.Empty,
        ExpiresIn = expiresInSeconds > 0 ? expiresInSeconds : 3600,
        TokenType = "Bearer",
        User = new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            Role = user.Role?.Name ?? "Employee",
            IsSuperAdmin = user.EstSuperAdminEffectif,
        },
    };
}
