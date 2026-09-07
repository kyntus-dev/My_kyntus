using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Application.LegacyRead;
using Prime.Domain.Entities;

namespace Prime.API.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
[Obsolete("Utiliser /api/prime/admin/* comme source canonique.")]
public sealed class AdminPrimeController(IMediator mediator, IPrimeAdminReadAppService? admin) : ControllerBase
{
    [HttpGet("dashboard")]
    public async Task<ActionResult<AdminDashboardResponse>> GetDashboard(CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetAdminDashboardQuery(), ct));
    }

    [HttpGet("calculation-config")]
    public async Task<ActionResult<AdminCalculationConfig>> GetCalculationConfig(CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetAdminCalculationConfigQuery(), ct));
    }

    [HttpPut("calculation-config")]
    public async Task<ActionResult<AdminCalculationConfig>> SaveCalculationConfig(
        [FromBody] AdminCalculationConfig payload, CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new SaveAdminCalculationConfigCommand(payload), ct));
    }

    [HttpGet("rbac-matrix")]
    public async Task<ActionResult<List<AdminRbacRow>>> GetRbacMatrix(CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetAdminRbacMatrixQuery(), ct));
    }

    [HttpPut("rbac-matrix/toggle")]
    public async Task<ActionResult<List<AdminRbacRow>>> ToggleRbacPermission(
        [FromBody] ToggleRbacPermissionRequest req, CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new ToggleAdminRbacPermissionCommand(req), ct));
    }

    [HttpGet("workflow-config")]
    public async Task<ActionResult<AdminWorkflowConfig>> GetWorkflowConfig(CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetAdminWorkflowConfigQuery(), ct));
    }

    [HttpPut("workflow-config")]
    public async Task<ActionResult<AdminWorkflowConfig>> SaveWorkflowConfig(
        [FromBody] AdminWorkflowConfig payload, CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new SaveAdminWorkflowConfigCommand(payload), ct));
    }

    [HttpGet("audit-logs")]
    public async Task<ActionResult<List<AdminAuditLog>>> GetAuditLogs(CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetAdminAuditLogsQuery(), ct));
    }

    [HttpGet("anomalies")]
    public async Task<ActionResult<List<AdminAnomaly>>> GetAnomalies(CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetAdminAnomaliesQuery(), ct));
    }

    [HttpPut("anomalies/{id}/status")]
    public async Task<ActionResult<List<AdminAnomaly>>> UpdateAnomalyStatus(
        string id, [FromBody] UpdateAnomalyStatusRequest req, CancellationToken ct)
    {
        if (admin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new UpdateAdminAnomalyStatusCommand(id, req), ct));
    }
}
