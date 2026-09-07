using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Prime.Application.Abstractions;
using Prime.Application.Admin;
using Prime.Application.DTOs;

namespace Prime.API.Controllers;

/// <summary>API de consultation du journal d'audit (Phase 1.4).</summary>
[ApiController]
[Route("api/prime/admin/audit-logs")]
[Authorize]
public sealed class AuditLogAdminController(IMediator mediator, IAuditLogAdminService? auditAdmin) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Admin,Audit")]
    public async Task<ActionResult<List<AuditLogDto>>> List(
        [FromQuery] string? userId,
        [FromQuery] string? role,
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? take,
        CancellationToken ct)
    {
        if (auditAdmin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        var filter = new AuditLogListFilter(userId, role, action, entityType, entityId, from, to, take);
        return Ok(await mediator.Send(new ListAuditLogsQuery(filter), ct));
    }

    [HttpPost("nav")]
    [Authorize]
    public async Task<IActionResult> RecordNavigation([FromBody] RecordAuditNavigationRequest body, CancellationToken ct)
    {
        if (auditAdmin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            await mediator.Send(new RecordAuditNavigationCommand(body), ct);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
