using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Parrainage.Application.Audit;
using Parrainage.Application.DTOs;

namespace Parrainage.API.Controllers;

[ApiController]
[Route("api/parrainage/audit")]
public sealed class AuditController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Admin,Audit")]
    public async Task<ActionResult<List<AuditLogDto>>> List([FromQuery] int? take, CancellationToken ct) =>
        Ok(await mediator.Send(new ListParrainageAuditLogsQuery(take), ct));

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AuditLogDto>> Create([FromBody] CreateAuditRequest body, CancellationToken ct)
    {
        try
        {
            return Ok(await mediator.Send(new CreateParrainageAuditLogCommand(body), ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
