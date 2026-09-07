using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Prime.Application.Abstractions;
using Prime.Application.LegacyRead;
using Prime.Domain.Entities;

namespace Prime.API.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Roles = "Admin,Audit")]
[Obsolete("Utiliser /api/prime/admin/audit-logs et /api/prime/admin/anomalies.")]
public sealed class AuditPrimeController(IMediator mediator, IPrimeAdminReadAppService? admin) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<AuditDashboardResponse>> GetDashboard(CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetAuditDashboardQuery(), ct));
    }

    [HttpGet("operations")]
    public async Task<ActionResult<List<AuditOperation>>> GetOperations(CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetAuditOperationsQuery(), ct));
    }

    [HttpGet("trail-logs")]
    public async Task<ActionResult<List<AuditTrailLog>>> GetAuditTrailLogs(CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetAuditTrailLogsQuery(), ct));
    }

    [HttpGet("anomalies")]
    public async Task<ActionResult<List<AuditAnomaly>>> GetAnomalies(CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetAuditAnomaliesQuery(), ct));
    }
}
