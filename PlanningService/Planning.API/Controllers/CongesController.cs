using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;
using Planning.Application.Conges;
using Planning.Application.DTOs;

namespace Planning.API.Controllers;

/// <summary>
/// Absences opérationnelles Planning (/api/Conges).
/// Le workflow soldes/quotas passe par /api/conges (service Congé).
/// v64 : consultation, ajout et modification réglés dans « Accès par rôle » (par rôle et par compte) :
/// rubriques « Absences du planning — consulter / ajouter / modifier ». Admin et RH les ont par défaut ;
/// hors portée « Toute l'entreprise », un compte ne gère que les absences de ses cellules.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CongesController(IMediator mediator, IPolicyEvaluator policy, IPlanningCongeService conges) : ControllerBase
{
    public const string CapConsulter = "conges.planning.View";
    public const string CapAjouter = "conges.planning-ajout.Act";
    public const string CapModifier = "conges.planning-modification.Act";

    private const string MessageRefus = "Droit insuffisant (Accès par rôle › Absences du planning).";
    private const string MessageHorsPerimetre = "Cet employé n'est pas dans votre périmètre.";

    [HttpGet("subservice/{subServiceId}")]
    public async Task<IActionResult> GetBySubService(
        int subServiceId,
        [FromQuery] string? weekStart = null,
        CancellationToken ct = default)
    {
        // Lecture conservée pour le planning (vue équipe) ; le détail d'une absence suit la rubrique.
        var result = await mediator.Send(new GetCongesBySubServiceQuery(subServiceId, weekStart), ct);
        return Ok(result);
    }

    [HttpGet("new-employees/{subServiceId}")]
    public async Task<IActionResult> GetNewEmployees(int subServiceId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetNewEmployeesBySubServiceQuery(subServiceId), ct);
        return Ok(result);
    }

    /// <summary>v64 : droits de l'utilisateur courant sur les absences du planning (boutons de l'écran).</summary>
    [HttpGet("droits")]
    public async Task<IActionResult> Droits(CancellationToken ct) => Ok(new
    {
        consulter = await PeutAsync(CapConsulter, ct),
        ajouter = await PeutAsync(CapAjouter, ct),
        modifier = await PeutAsync(CapModifier, ct),
    });

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        if (await RefusAsync(CapConsulter, congeId: id, userId: null, ct) is { } refus) return refus;
        var conge = await mediator.Send(new GetPlanningCongeByIdQuery(id), ct);
        return conge is null ? NotFound() : Ok(conge);
    }

    [HttpGet("user/{userId:int}")]
    public async Task<IActionResult> GetByUser(int userId, CancellationToken ct)
    {
        if (await RefusAsync(CapConsulter, congeId: null, userId: userId, ct) is { } refus) return refus;
        return Ok(await mediator.Send(new GetPlanningCongesByUserQuery(userId), ct));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateCongeDto dto, CancellationToken ct)
    {
        if (await RefusAsync(CapAjouter, congeId: null, userId: dto.UserId, ct) is { } refus) return refus;
        try
        {
            var result = await mediator.Send(new CreatePlanningCongeCommand(dto), ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] CreateCongeDto dto, CancellationToken ct)
    {
        // Périmètre vérifié sur l'employé actuel de l'absence ET sur l'employé visé (pas de déplacement
        // d'une absence vers un agent hors périmètre).
        if (await RefusAsync(CapModifier, congeId: id, userId: null, ct) is { } refus) return refus;
        if (await RefusAsync(CapModifier, congeId: null, userId: dto.UserId, ct) is { } refusCible) return refusCible;
        try
        {
            var result = await mediator.Send(new UpdatePlanningCongeCommand(id, dto), ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (await RefusAsync(CapModifier, congeId: id, userId: null, ct) is { } refus) return refus;
        if (!await mediator.Send(new DeletePlanningCongeCommand(id), ct))
            return NotFound();
        return NoContent();
    }

    [HttpPost("saturday-slot")]
    [Authorize(Roles = "Admin,RH,Superviseur,Manager")]
    public async Task<IActionResult> SetSaturdaySlot([FromBody] SetSaturdaySlotDto dto, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new SetSaturdaySlotCommand(dto), ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost("absence-days/bulk")]
    public async Task<IActionResult> GetBulkAbsenceDays(
        [FromBody] BulkAbsenceDaysRequestDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var result = await mediator.Send(new GetBulkAbsenceDaysCommand(dto), ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── Droits ──────────────────────────────────────────────────────────────────────────────

    private static bool EstAdminOuRh(string? role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);

    private async Task<bool> PeutAsync(string action, CancellationToken ct, string? ressource = null)
    {
        var role = User.GetAuthRole() ?? "Employee";
        var decision = await policy.EvaluateAsync(
            new PolicyRequest(User.GetSubjectId() ?? Guid.Empty, role, action, "platform", ressource), ct);
        return decision.Allowed;
    }

    /// <summary>
    /// Refus (403) si le compte n'a pas la rubrique, ou si l'employé visé est hors de ses cellules
    /// (sauf Admin, RH ou portée « Toute l'entreprise »). Null : autorisé.
    /// </summary>
    private async Task<IActionResult?> RefusAsync(string action, int? congeId, int? userId, CancellationToken ct)
    {
        if (!await PeutAsync(action, ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = MessageRefus });
        if (EstAdminOuRh(User.GetAuthRole()) || await PeutAsync(action, ct, "toute-l-entreprise"))
            return null;
        var cellule = await conges.SousServiceDeAsync(congeId, userId, ct);
        if (cellule is null)
            return congeId is not null ? NotFound() : StatusCode(StatusCodes.Status403Forbidden, new { message = MessageHorsPerimetre });
        var gerables = await conges.SousServicesGerablesAsync(User.GetSubjectId() ?? Guid.Empty, ct);
        return gerables.Contains(cellule.Value)
            ? null
            : StatusCode(StatusCodes.Status403Forbidden, new { message = MessageHorsPerimetre });
    }
}
