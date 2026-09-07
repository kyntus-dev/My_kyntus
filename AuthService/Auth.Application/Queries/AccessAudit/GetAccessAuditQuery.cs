using Auth.Application.DTOs;
using MediatR;

namespace Auth.Application.Queries.AccessAudit;

public record GetAccessAuditQuery(int Take = 200) : IRequest<IReadOnlyList<AccessAuditEntryDto>>;
