using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Prime.Application;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Application.Org;
using Prime.Domain.Entities;

namespace Prime.API.Controllers;

[ApiController]
[Route("api/prime/org")]
public sealed class PrimeOrgAssignmentsController(
    IMediator mediator,
    IPrimeOrgAssignmentsAppService? org) : ControllerBase
{
    [HttpPost("employees/ensure-from-planning")]
    public async Task<ActionResult<object>> EnsureEmployeeFromPlanning(
        [FromBody] EnsureEmployeeFromPlanningRequest body,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            var result = await mediator.Send(new EnsureEmployeeFromPlanningCommand(body), ct);
            return Ok(new { employeeId = result.EmployeeId });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("employees/dedupe-by-email")]
    public async Task<ActionResult<object>> DedupeEmployeesByEmail(CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        var result = await mediator.Send(new DedupeEmployeesByEmailCommand(), ct);
        return Ok(new { merged = result.Merged });
    }

    [HttpGet("etages")]
    public async Task<ActionResult<List<PoleNode>>> GetEtages(CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetOrgEtagesQuery(), ct));
    }

    [HttpGet("services")]
    public async Task<ActionResult<List<CelluleNode>>> GetServices(CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetOrgServicesQuery(), ct));
    }

    [HttpGet("supervisor-scope")]
    public async Task<ActionResult<List<SupervisorOrgScopePoleDto>>> GetSupervisorScope(
        [FromQuery] string supervisorUserId,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new GetOrgSupervisorScopeQuery(supervisorUserId), ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("sous-services")]
    public async Task<ActionResult<List<CelluleNode>>> GetSousServices(CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetOrgSousServicesQuery(), ct));
    }

    [HttpGet("assignments/manager-etage")]
    public async Task<ActionResult<List<ChefProjetPoleAssignment>>> GetChefProjetPoleAssignments(
        [FromQuery] string? userId,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetChefProjetPoleAssignmentsQuery(userId), ct));
    }

    [HttpGet("assignments/supervisor-service")]
    public async Task<ActionResult<List<SupervisorCelluleAssignment>>> GetSupervisorCelluleAssignments(
        [FromQuery] string? userId,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetSupervisorCelluleAssignmentsQuery(userId), ct));
    }

    [HttpGet("assignments/coach-sous-service")]
    public async Task<ActionResult<List<ReferentTechniqueServiceAssignment>>> GetReferentTechniqueServiceAssignments(
        [FromQuery] string? userId,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetReferentTechniqueServiceAssignmentsQuery(userId), ct));
    }

    [HttpGet("assignments/coach-pilot")]
    public async Task<ActionResult<List<ReferentTechniquePilotLink>>> GetReferentTechniquePilotLinks(
        [FromQuery] string? coachUserId,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetReferentTechniquePilotLinksQuery(coachUserId), ct));
    }

    [HttpPost("assignments/manager-etage")]
    public async Task<ActionResult<ChefProjetPoleAssignment>> AssignManagerEtage(
        [FromBody] AssignChefProjetPoleRequest req,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new AssignManagerEtageCommand(req), ct));
        }
        catch (InvalidOperationException e) { return Conflict(new { error = e.Message }); }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
    }

    [HttpPost("assignments/supervisor-service")]
    public async Task<ActionResult<SupervisorCelluleAssignment>> AssignSupervisorService(
        [FromBody] AssignSupervisorCelluleRequest req,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new AssignSupervisorServiceCommand(req), ct));
        }
        catch (InvalidOperationException e) { return Conflict(new { error = e.Message }); }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
    }

    [HttpPost("assignments/coach-sous-service")]
    public async Task<ActionResult<ReferentTechniqueServiceAssignment>> AssignCoachSousService(
        [FromBody] AssignReferentTechniqueServiceRequest req,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new AssignCoachSousServiceCommand(req), ct));
        }
        catch (InvalidOperationException e) { return Conflict(new { error = e.Message }); }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
    }

    [HttpPost("assignments/coach-pilot")]
    public async Task<ActionResult<ReferentTechniquePilotLink>> AssignCoachPilot(
        [FromBody] AssignReferentTechniquePilotRequest req,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new AssignCoachPilotCommand(req), ct));
        }
        catch (InvalidOperationException e) { return Conflict(new { error = e.Message }); }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
    }

    [HttpDelete("assignments/manager-etage/{assignmentId}")]
    public async Task<IActionResult> RemoveChefProjetPoleAssignment(string assignmentId, CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            await mediator.Send(new RemoveChefProjetPoleAssignmentCommand(assignmentId), ct);
            return NoContent();
        }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
    }

    [HttpDelete("assignments/supervisor-service/{assignmentId}")]
    public async Task<IActionResult> RemoveSupervisorCelluleAssignment(string assignmentId, CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            await mediator.Send(new RemoveSupervisorCelluleAssignmentCommand(assignmentId), ct);
            return NoContent();
        }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
    }

    [HttpDelete("assignments/coach-sous-service/{assignmentId}")]
    public async Task<IActionResult> RemoveReferentTechniqueServiceAssignment(string assignmentId, CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            await mediator.Send(new RemoveReferentTechniqueServiceAssignmentCommand(assignmentId), ct);
            return NoContent();
        }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
    }

    [HttpDelete("assignments/coach-pilot/{linkId}")]
    public async Task<IActionResult> RemoveReferentTechniquePilotLink(string linkId, CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            await mediator.Send(new RemoveReferentTechniquePilotLinkCommand(linkId), ct);
            return NoContent();
        }
        catch (KeyNotFoundException e) { return NotFound(new { error = e.Message }); }
    }

    [HttpPost("structure/departments")]
    public async Task<ActionResult<Department>> CreateDepartment([FromBody] CreateOrgPoleBody body, CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new CreateOrgDepartmentCommand(body), ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (PrimeApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); }
    }

    [HttpPost("structure/departments/{departmentId}/poles")]
    public async Task<ActionResult<Pole>> CreatePoleForDepartment(
        string departmentId,
        [FromBody] CreateOrgNodeNameBody body,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new CreateOrgPoleForDepartmentCommand(departmentId, body), ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (PrimeApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); }
    }

    [HttpPost("structure/poles/{celluleId}/cellules")]
    public async Task<ActionResult<Cellule>> CreateCelluleForPole(
        string celluleId,
        [FromBody] CreateOrgNodeNameBody body,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new CreateOrgCelluleForPoleCommand(celluleId, body), ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (PrimeApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); }
    }

    [HttpPost("structure/departments/{poleId}/manager")]
    public async Task<IActionResult> SetManagerForDepartment(
        string poleId,
        [FromBody] SetOrgResponsibleBody body,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            await mediator.Send(new SetManagerForDepartmentCommand(poleId, body), ct);
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpDelete("structure/departments/{poleId}/manager")]
    public async Task<IActionResult> ClearManagerForDepartment(string poleId, CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        await mediator.Send(new ClearManagerForDepartmentCommand(poleId), ct);
        return NoContent();
    }

    [HttpPost("structure/poles/{celluleId}/supervisor")]
    public async Task<IActionResult> SetSupervisorForPole(
        string celluleId,
        [FromBody] SetOrgResponsibleBody body,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            await mediator.Send(new SetSupervisorForPoleCommand(celluleId, body), ct);
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpDelete("structure/poles/{celluleId}/supervisor")]
    public async Task<IActionResult> ClearSupervisorForPole(string celluleId, CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        await mediator.Send(new ClearSupervisorForPoleCommand(celluleId), ct);
        return NoContent();
    }

    [HttpPost("structure/cellules/{serviceId}/coach")]
    public async Task<IActionResult> SetCoachForCellule(
        string serviceId,
        [FromBody] SetOrgResponsibleBody body,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            await mediator.Send(new SetCoachForCelluleCommand(serviceId, body), ct);
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpDelete("structure/cellules/{serviceId}/coach")]
    public async Task<IActionResult> ClearCoachForCellule(string serviceId, CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        await mediator.Send(new ClearCoachForCelluleCommand(serviceId), ct);
        return NoContent();
    }

    [HttpPost("structure/cellules/{serviceId}/pilots")]
    public async Task<IActionResult> AddPilotToCellule(
        string serviceId,
        [FromBody] AddPilotToServiceBody body,
        CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            await mediator.Send(new AddPilotToCelluleCommand(serviceId, body), ct);
            return NoContent();
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [HttpDelete("structure/cellules/{serviceId}/pilots/{employeeId}")]
    public async Task<IActionResult> RemovePilotFromCellule(string serviceId, string employeeId, CancellationToken ct)
    {
        if (org is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            await mediator.Send(new RemovePilotFromCelluleCommand(serviceId, employeeId), ct);
            return NoContent();
        }
        catch (KeyNotFoundException ex) { return NotFound(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}
