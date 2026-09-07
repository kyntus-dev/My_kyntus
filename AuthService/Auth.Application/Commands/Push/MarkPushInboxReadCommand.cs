using Auth.Domain.Interfaces;
using MediatR;

namespace Auth.Application.Commands.Push;

public record MarkPushInboxReadCommand(int AuthUserId, Guid Id) : IRequest;

public sealed class MarkPushInboxReadCommandHandler(IPushNotificationService push)
    : IRequestHandler<MarkPushInboxReadCommand>
{
    public Task Handle(MarkPushInboxReadCommand request, CancellationToken ct) =>
        push.MarkInboxReadAsync(request.AuthUserId, request.Id, ct);
}
