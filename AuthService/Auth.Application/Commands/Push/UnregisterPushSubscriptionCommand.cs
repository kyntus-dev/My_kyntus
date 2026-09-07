using Auth.Domain.Interfaces;
using MediatR;

namespace Auth.Application.Commands.Push;

public record UnregisterPushSubscriptionCommand(int AuthUserId, string Endpoint) : IRequest;

public sealed class UnregisterPushSubscriptionCommandHandler(IPushNotificationService push)
    : IRequestHandler<UnregisterPushSubscriptionCommand>
{
    public Task Handle(UnregisterPushSubscriptionCommand request, CancellationToken ct) =>
        push.UnregisterAsync(request.AuthUserId, request.Endpoint, ct);
}
