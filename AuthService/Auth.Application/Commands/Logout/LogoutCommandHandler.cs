using Auth.Domain.Interfaces;
using MediatR;

namespace Auth.Application.Commands.Logout;

public record LogoutCommand(string RefreshToken) : IRequest<bool>;

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, bool>
{
    private readonly IUserRepository _userRepository;
    private readonly IAccessAuditWriter _accessAudit;

    public LogoutCommandHandler(IUserRepository userRepository, IAccessAuditWriter accessAudit)
    {
        _userRepository = userRepository;
        _accessAudit = accessAudit;
    }

    public async Task<bool> Handle(LogoutCommand request, CancellationToken ct)
    {
        var users = await _userRepository.GetAllAsync(ct);
        var user = users.FirstOrDefault(u => u.RefreshToken == request.RefreshToken);
        if (user == null)
            return false;

        user.RefreshToken = null;
        user.RefreshTokenExpiryTime = null;
        await _userRepository.UpdateAsync(user, ct);
        await _accessAudit.WriteAsync("Logout", user.Email, user.Id, null, ct);
        return true;
    }
}
