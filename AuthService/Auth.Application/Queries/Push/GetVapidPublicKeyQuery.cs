using Auth.Application.DTOs;
using Auth.Domain.Interfaces;
using MediatR;

namespace Auth.Application.Queries.Push;

public record GetVapidPublicKeyQuery : IRequest<VapidPublicKeyDto>;

public sealed class GetVapidPublicKeyQueryHandler(IPushNotificationService push)
    : IRequestHandler<GetVapidPublicKeyQuery, VapidPublicKeyDto>
{
    public Task<VapidPublicKeyDto> Handle(GetVapidPublicKeyQuery request, CancellationToken ct) =>
        Task.FromResult(new VapidPublicKeyDto { PublicKey = push.GetVapidPublicKey() ?? string.Empty });
}
