using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.DTOs;
using Planning.Application.Abstractions.EmployeeImport;
using System.Security.Claims;

namespace Planning.API.Controllers;

[ApiController]
[Route("api/users/fields")]
[Authorize]
public class EmployeeFieldsController(
    IEmployeeFieldService fieldService,
    IEmployeeDocumentFieldQueryService documentFieldQuery) : ControllerBase
{
    private static bool IsHrOrAdmin(string role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);

    [HttpGet]
    public async Task<ActionResult<List<EmployeeImportFieldConfigDto>>> GetFields(
        [FromQuery] bool enabledOnly = false,
        CancellationToken ct = default)
    {
        if (!IsHrOrAdmin(GetRole()))
            return Forbid();

        return Ok(await fieldService.GetAllAsync(enabledOnly, ct));
    }

    [HttpPost]
    public async Task<ActionResult<EmployeeImportFieldConfigDto>> CreateField(
        [FromBody] CreateEmployeeFieldRequest request,
        CancellationToken ct = default)
    {
        if (!IsHrOrAdmin(GetRole()))
            return Forbid();

        try
        {
            return Ok(await fieldService.CreateCustomFieldAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{fieldKey}")]
    public async Task<ActionResult<EmployeeImportFieldConfigDto>> UpdateField(
        string fieldKey,
        [FromBody] UpdateEmployeeFieldRequest request,
        CancellationToken ct = default)
    {
        if (!IsHrOrAdmin(GetRole()))
            return Forbid();

        try
        {
            var updated = await fieldService.UpdateFieldAsync(fieldKey, request, ct);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("match-variables")]
    public async Task<ActionResult<List<EmployeeVariableMatchDto>>> MatchVariables(
        [FromBody] EmployeeVariableMatchRequest request,
        CancellationToken ct = default)
    {
        if (!IsHrOrAdmin(GetRole()))
            return Forbid();

        var sources = request.Sources ?? [];
        return Ok(await documentFieldQuery.MatchVariablesAsync(sources, ct));
    }

    [HttpDelete("{fieldKey}")]
    public async Task<IActionResult> DeleteField(string fieldKey, CancellationToken ct = default)
    {
        if (!IsHrOrAdmin(GetRole()))
            return Forbid();

        var deleted = await fieldService.DeleteCustomFieldAsync(fieldKey, ct);
        return deleted ? NoContent() : NotFound();
    }

    private string GetRole() =>
        User.FindFirst("role")?.Value ?? User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;
}
