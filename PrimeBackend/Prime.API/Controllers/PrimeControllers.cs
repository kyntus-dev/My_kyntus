using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Application.Org;
using Prime.Domain.Entities;

namespace Prime.API.Controllers;

[ApiController]
[Route("api/prime")]
public class PrimeController(IMediator mediator, IPrimeCoreQueryAppService? core) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken ct)
    {
        var h = await mediator.Send(new GetPrimeHealthQuery(), ct);
        return h.Status switch
        {
            "ok" => Ok(new { status = h.Status, mode = h.Mode, database = h.Database }),
            "db-unreachable" => StatusCode(503, new { status = h.Status }),
            _ => StatusCode(503, new { status = h.Status, error = h.Error }),
        };
    }

    [HttpGet("departments")]
    public async Task<ActionResult<List<Department>>> GetPoles(CancellationToken ct)
    {
        if (core is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetLegacyDepartmentsQuery(), ct));
    }

    [HttpGet("org/operational-departments")]
    public async Task<ActionResult<OperationalOrgTreeDto>> GetOperationalDepartments(CancellationToken ct)
    {
        if (core is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetOperationalDepartmentsQuery(), ct));
    }

    [HttpGet("employees")]
    public async Task<ActionResult<List<Employee>>> GetEmployees(CancellationToken ct)
    {
        if (core is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetLegacyEmployeesQuery(), ct));
    }

    [HttpGet("types")]
    public async Task<ActionResult<List<PrimeType>>> GetPrimeTypes(CancellationToken ct)
    {
        if (core is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetPrimeTypesQuery(), ct));
    }

    [HttpGet("rules")]
    public async Task<ActionResult<List<PrimeRule>>> GetPrimeRules(CancellationToken ct)
    {
        if (core is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetPrimeRulesQuery(), ct));
    }

    [HttpGet("results")]
    public async Task<ActionResult<List<PrimeResult>>> GetPrimeResults(CancellationToken ct)
    {
        if (core is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetPrimeResultsQuery(), ct));
    }

    [HttpGet("my-results")]
    public async Task<ActionResult<List<PrimeResult>>> GetMyPrimeResults([FromQuery] string employeeId, CancellationToken ct)
    {
        if (core is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetMyPrimeResultsQuery(employeeId), ct));
    }

    [HttpPut("results/{id}/status")]
    public ActionResult<PrimeResult> UpdatePrimeResultStatus(string id, [FromBody] UpdatePrimeResultStatusRequest req)
        => StatusCode(StatusCodes.Status410Gone,
            new { error = "Utilisez l'API /api/prime/validation pour approuver ou rejeter une fiche." });

    [HttpGet("dashboard-stats")]
    public async Task<ActionResult<object>> GetDashboardStats(CancellationToken ct)
    {
        if (core is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetPrimeDashboardStatsQuery(), ct));
    }
}
