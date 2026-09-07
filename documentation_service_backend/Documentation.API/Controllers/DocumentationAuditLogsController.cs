using Documentation.Application;
using Documentation.Application.Abstractions;
using Documentation.Application.Api;
using Documentation.Application.Audit;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Documentation.API.Controllers;

/// <summary>Journal d'audit du tenant courant.</summary>
[ApiController]
[Route("api/documentation/data")]
public sealed class DocumentationAuditLogsController(
    IMediator mediator,
    IAuditLogQueryAppService? auditLogs) : ControllerBase
{
    [HttpGet("audit-logs")]
    public async Task<ActionResult<PagedResponse<AuditLogResponse>>> GetAuditLogs(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? action = null,
        [FromQuery] string? role = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null,
        CancellationToken ct = default)
    {
        if (auditLogs is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new ListAuditLogsQuery(new AuditLogListQuery
            {
                Page = page,
                PageSize = pageSize,
                Action = action,
                Role = role,
                SortBy = sortBy,
                SortOrder = sortOrder,
            }), ct));
        }
        catch (DocumentationApiException ex)
        {
            return StatusCode(ex.StatusCode, new { message = ex.Message });
        }
    }
}
