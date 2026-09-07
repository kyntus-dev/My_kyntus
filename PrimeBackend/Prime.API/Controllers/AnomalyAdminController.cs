using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Prime.Application.Abstractions;
using Prime.Application.Admin;
using Prime.Application.DTOs;

namespace Prime.API.Controllers;

/// <summary>API des anomalies détectées sur les fiches PRIME (Phase 1.4 + 1.5).</summary>
[ApiController]
[Route("api/prime/admin/anomalies")]
[Authorize(Roles = "Admin,RH,Audit")]
public sealed class AnomalyAdminController(IMediator mediator, IAnomalyAdminService? anomalyAdmin) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Admin,RH,Audit")]
    public async Task<ActionResult<List<AnomalyDto>>> List(
        [FromQuery] string? status,
        [FromQuery] string? type,
        [FromQuery] string? severity,
        [FromQuery] string? period,
        [FromQuery] string? serviceId,
        [FromQuery] string? celluleId,
        [FromQuery] string? poleId,
        CancellationToken ct)
    {
        if (anomalyAdmin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        var filter = new AnomalyListFilter(status, type, severity, period, serviceId, celluleId, poleId);
        return Ok(await mediator.Send(new ListAnomaliesQuery(filter), ct));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<AnomalyDto>> UpdateStatus(Guid id, [FromBody] UpdateAnomalyStatusBody body, CancellationToken ct)
    {
        if (anomalyAdmin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            var updated = await mediator.Send(new UpdateAnomalyStatusCommand(id, body), ct);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("recompute-all")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<object>> RecomputeAll(CancellationToken ct)
    {
        if (anomalyAdmin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        var n = await mediator.Send(new RecomputeAllAnomaliesCommand(), ct);
        return Ok(new { upsertedCount = n });
    }
}
