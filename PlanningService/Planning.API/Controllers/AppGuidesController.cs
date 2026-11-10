using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;

namespace Planning.API.Controllers;

/// <summary>
/// Espace Aide › Guide de l'application. Lecture et « consulté » pour tout utilisateur connecté ;
/// gestion des guides réservée aux rôles Admin et RH.
/// Route sous /api/planning/… : déjà publiée par la passerelle (aucune modification de la passerelle).
/// </summary>
[ApiController]
[Authorize]
[Route("api/planning/app-guides")]
public sealed class AppGuidesController(IAppGuideService guides) : ControllerBase
{
    [HttpGet("mine")]
    public async Task<ActionResult<MyAppGuidesDto>> Mine(CancellationToken ct) =>
        Ok(await guides.GetMineAsync(User.GetAuthRole(), CurrentUserKey(), ct));

    [HttpPost("{id:int}/viewed")]
    public async Task<IActionResult> MarkViewed(int id, CancellationToken ct) =>
        await guides.MarkViewedAsync(id, User.GetAuthRole(), CurrentUserKey(), ct) ? NoContent() : NotFound();

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AppGuideAdminDto>>> List(CancellationToken ct)
    {
        if (!IsAdminOrRh()) return Forbid();
        return Ok(await guides.ListAllAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<AppGuideAdminDto>> Create([FromBody] SaveAppGuideRequest body, CancellationToken ct)
    {
        if (!IsAdminOrRh()) return Forbid();
        try
        {
            return Ok(await guides.CreateAsync(body, CurrentUserKey(), ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AppGuideAdminDto>> Update(int id, [FromBody] SaveAppGuideRequest body, CancellationToken ct)
    {
        if (!IsAdminOrRh()) return Forbid();
        try
        {
            var updated = await guides.UpdateAsync(id, body, ct);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private bool IsAdminOrRh()
    {
        var role = User.GetAuthRole();
        return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
               || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);
    }

    private string CurrentUserKey()
    {
        if (User.GetSubjectId() is Guid subject && subject != Guid.Empty)
            return subject.ToString();
        if (User.GetAuthUserId() is int authId)
            return $"auth:{authId}";
        return (User.GetEmail() ?? string.Empty).Trim().ToLowerInvariant();
    }
}
