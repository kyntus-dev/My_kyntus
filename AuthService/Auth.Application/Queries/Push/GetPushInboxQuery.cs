using Auth.Application.DTOs;
using Auth.Domain.Interfaces;
using MediatR;

namespace Auth.Application.Queries.Push;

public record GetPushInboxQuery(int AuthUserId, int Take = 30) : IRequest<IReadOnlyList<PushInboxEntryDto>>;

public sealed class GetPushInboxQueryHandler(IPushNotificationService push)
    : IRequestHandler<GetPushInboxQuery, IReadOnlyList<PushInboxEntryDto>>
{
    public async Task<IReadOnlyList<PushInboxEntryDto>> Handle(GetPushInboxQuery request, CancellationToken ct)
    {
        var rows = await push.ListInboxAsync(request.AuthUserId, request.Take, ct);
        return rows.Select(r => new PushInboxEntryDto
        {
            Id = r.Id.ToString(),
            Title = r.Title,
            Body = r.Body,
            Tag = r.Tag,
            Url = r.Url,
            IsRead = r.IsRead,
            CreatedAt = r.CreatedAt,
        }).ToList();
    }
}
