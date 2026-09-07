using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Application.Rbac;

namespace Prime.API.Controllers;

/// <summary>API d'administration de la matrice RBAC (Phase 1.4).</summary>
[ApiController]
[Route("api/prime/admin/rbac")]
[Authorize(Roles = "Admin")]
public sealed class RbacAdminController(IMediator mediator, IRbacAdminService? rbacAdmin) : ControllerBase
{
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<RbacPermissionDto>>> List(CancellationToken ct)
    {
        if (rbacAdmin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new ListRbacPermissionsQuery(), ct));
    }

    [HttpGet("catalog")]
    public async Task<ActionResult<RbacCatalogDto>> Catalog(CancellationToken ct)
    {
        if (rbacAdmin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        return Ok(await mediator.Send(new GetRbacCatalogQuery(), ct));
    }

    [HttpPut]
    public async Task<ActionResult<RbacPermissionDto>> Upsert([FromBody] UpsertRbacPermissionRequest body, CancellationToken ct)
    {
        if (rbacAdmin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        if (string.IsNullOrWhiteSpace(body.Role) || string.IsNullOrWhiteSpace(body.Action) || string.IsNullOrWhiteSpace(body.Scope))
            return BadRequest(new { error = "Role, Action et Scope sont obligatoires." });

        try
        {
            return Ok(await mediator.Send(new UpsertRbacPermissionCommand(body), ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (rbacAdmin is null) return StatusCode(503, new { error = "Base de données non configurée." });
        var deleted = await mediator.Send(new DeleteRbacPermissionCommand(id), ct);
        return deleted ? NoContent() : NotFound();
    }
}
