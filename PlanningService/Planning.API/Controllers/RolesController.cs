using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Application.Queries.Roles;

namespace Planning.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RolesController(IMediator mediator, IRoleService roles) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAllRoles(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAllRolesQuery(), ct);
        return Ok(result);
    }

    /// <summary>Gestion des rôles (Admin) : nombre de comptes et rôles système protégés.</summary>
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> GetAdmin(CancellationToken ct) =>
        Ok(await roles.GetRolesAdminAsync(ct));

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> Creer([FromBody] RoleSaisieRequest body, CancellationToken ct) =>
        Executer(async () => Ok(await roles.CreerAsync(body, ct)));

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> Modifier(int id, [FromBody] RoleSaisieRequest body, CancellationToken ct) =>
        Executer(async () => Ok(await roles.ModifierAsync(id, body, ct)));

    /// <summary>Supprime le rôle après avoir basculé ses comptes vers <paramref name="remplacementId"/>.</summary>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> Supprimer(int id, [FromQuery] int? remplacementId, CancellationToken ct) =>
        Executer(async () => Ok(await roles.SupprimerAsync(id, remplacementId, ct)));

    private async Task<IActionResult> Executer(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (RoleRegleException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}
