using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Presence;

namespace Planning.API.Controllers;

/// <summary>
/// Export du planning RH publié (Excel généré par l'application). Ouvert aux rôles ayant
/// « Planning de l'équipe » ou « Valider les plannings » ; chacun ne reçoit que son périmètre.
/// </summary>
[ApiController]
[Authorize]
[Route("api/planning/export")]
public sealed class PlanningExportController(IPlanningExportService export, IPolicyEvaluator policy) : ControllerBase
{
    [HttpGet("rh")]
    public async Task<ActionResult<PlanningExportDto>> PlanningRh([FromQuery] DateOnly du, [FromQuery] DateOnly au, CancellationToken ct)
    {
        if (!await PeutAsync("planning.equipe.View", ct) && !await PeutAsync("planning.validation.View", ct))
            return Forbid();
        var role = User.GetAuthRole();
        var global = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);
        var viewer = new PresenceViewer(User.GetAuthUserId(), User.GetSubjectId(), User.GetEmail(), role, global);
        return Ok(await export.GetPlanningRhAsync(viewer, du, au, ct));
    }

    private async Task<bool> PeutAsync(string action, CancellationToken ct)
    {
        var role = User.GetAuthRole() ?? "Employee";
        var decision = await policy.EvaluateAsync(new PolicyRequest(User.GetSubjectId() ?? Guid.Empty, role, action, "platform"), ct);
        return decision.Allowed;
    }
}
