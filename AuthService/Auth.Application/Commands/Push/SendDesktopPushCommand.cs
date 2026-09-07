using Auth.Application.DTOs;
using Auth.Domain.Interfaces;
using MediatR;

namespace Auth.Application.Commands.Push;

public record SendDesktopPushCommand(DesktopPushNotifyDto Dto) : IRequest;

public sealed class SendDesktopPushCommandHandler(IPushNotificationService push)
    : IRequestHandler<SendDesktopPushCommand>
{
    public Task Handle(SendDesktopPushCommand request, CancellationToken ct)
    {
        var dto = request.Dto;
        var authIds = new List<int>(dto.AuthUserIds ?? []);
        if (dto.AuthUserId is > 0)
            authIds.Add(dto.AuthUserId.Value);

        var subjectIds = new List<Guid>(dto.SubjectIds ?? []);
        if (dto.SubjectId is { } sid && sid != Guid.Empty)
            subjectIds.Add(sid);

        return push.NotifyAsync(
            new DesktopPushNotifyRequest(authIds, subjectIds, dto.Title, dto.Body, dto.Tag, dto.Url),
            ct);
    }
}
