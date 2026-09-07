using Auth.Application.DTOs;
using Auth.Domain.Interfaces;
using MediatR;

namespace Auth.Application.Queries.AccessAudit;

public sealed class GetAccessAuditQueryHandler(IAccessAuditWriter audit)
    : IRequestHandler<GetAccessAuditQuery, IReadOnlyList<AccessAuditEntryDto>>
{
    public async Task<IReadOnlyList<AccessAuditEntryDto>> Handle(GetAccessAuditQuery request, CancellationToken ct)
    {
        var rows = await audit.ListAsync(request.Take, ct);
        return rows.Select(e => new AccessAuditEntryDto
        {
            Id = e.Id.ToString(),
            At = e.At,
            EventType = e.EventType,
            Email = e.Email,
            UserId = e.UserId,
            IpAddress = e.IpAddress,
            UserAgent = e.UserAgent,
            Message = e.Message,
        }).ToList();
    }
}
