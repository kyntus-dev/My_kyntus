using Auth.Application.DTOs;
using Auth.Domain.Entities;
using Auth.Domain.Interfaces;
using MediatR;

namespace Auth.Application.Commands.Register;

public record RegisterCommand(RegisterDto Dto) : IRequest<AuthResponseDto>;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, AuthResponseDto>
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IJwtService _jwtService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISubjectIdResolver _subjectIdResolver;

    public RegisterCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IJwtService jwtService,
        IPasswordHasher passwordHasher,
        ISubjectIdResolver subjectIdResolver)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _jwtService = jwtService;
        _passwordHasher = passwordHasher;
        _subjectIdResolver = subjectIdResolver;
    }

    public async Task<AuthResponseDto> Handle(RegisterCommand request, CancellationToken ct)
    {
        var registerDto = request.Dto;

        if (await _userRepository.ExistsAsync(registerDto.Email, ct))
            throw new InvalidOperationException("Cet email est déjà utilisé");

        if (await _userRepository.UsernameExistsAsync(registerDto.Username, ct))
            throw new InvalidOperationException("Ce nom d'utilisateur est déjà pris");

        var role = await _roleRepository.GetByNameAsync("Employee", ct)
            ?? throw new InvalidOperationException("Le rôle par défaut 'User' n'existe pas");

        var user = new User
        {
            Username = registerDto.Username,
            Email = registerDto.Email,
            SubjectId = _subjectIdResolver.ResolveForEmail(registerDto.Email),
            PasswordHash = _passwordHasher.HashPassword(registerDto.Password),
            PasswordChangedAt = DateTime.UtcNow,
            RoleId = role.Id,
            RefreshToken = _jwtService.GenerateRefreshToken(),
            RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7),
        };

        await _userRepository.CreateAsync(user, ct);
        user.Role = role;

        return AuthResponseMapper.ToDto(
            user,
            _jwtService.GenerateAccessToken(user),
            _jwtService.AccessTokenExpiresInSeconds);
    }
}

