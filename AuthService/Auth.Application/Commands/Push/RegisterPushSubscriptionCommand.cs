using Auth.Application.DTOs;
using Auth.Domain.Interfaces;
using MediatR;

namespace Auth.Application.Commands.Push;

public record RegisterPushSubscriptionCommand(int AuthUserId, PushSubscriptionRequestDto Dto) : IRequest;

public sealed class RegisterPushSubscriptionCommandHandler(IPushNotificationService push)
    : IRequestHandler<RegisterPushSubscriptionCommand>
{
    public Task Handle(RegisterPushSubscriptionCommand request, CancellationToken ct) =>
        push.RegisterAsync(
            request.AuthUserId,
            request.Dto.Endpoint,
            request.Dto.P256dh,
            request.Dto.Auth,
            request.Dto.UserAgent,
            ct);
}
