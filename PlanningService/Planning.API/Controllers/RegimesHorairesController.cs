using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planning.Application.Pointage;
using Planning.Infrastructure.Persistence;

namespace Planning.API.Controllers;

/// <summary>
/// Régimes horaires (heures fixes par jour de semaine, pause autorisée, accès à « Heure de sortie »)
/// et leurs affectations aux pôles, cellules, services ou collaborateurs.
/// Consultation : Admin et RH ; modification : Admin.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,RH")]
[Route("api/planning/regimes-horaires")]
public sealed class RegimesHorairesController(IRegimeHoraireService service, AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RegimeHoraireDto>>> GetAll(CancellationToken ct) =>
        Ok(await service.GetAllAsync(ct));

    [HttpGet("cibles/{cible}")]
    public Task<IActionResult> Cibles(string cible, CancellationToken ct) =>
        Run(async () => Ok(await service.GetCiblesAsync(cible, ct)));

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> Create([FromBody] RegimeHoraireRequest body, CancellationToken ct) =>
        Run(async () => Ok(await service.CreateAsync(body, await MeAsync(ct), ct)));

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> Update(int id, [FromBody] RegimeHoraireRequest body, CancellationToken ct) =>
        Run(async () => await service.UpdateAsync(id, body, await MeAsync(ct), ct) is { } r ? Ok(r) : NotFound());

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) =>
        Run(async () => await service.DeleteAsync(id, ct) ? NoContent() : NotFound());

    [HttpPost("{id:int}/affectations")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> Affecter(int id, [FromBody] RegimeAffectationRequest body, CancellationToken ct) =>
        Run(async () => await service.AffecterAsync(id, body, await MeAsync(ct), ct) is { } r ? Ok(r) : NotFound());

    [HttpDelete("affectations/{affectationId:int}")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> Retirer(int affectationId, CancellationToken ct) =>
        Run(async () => await service.RetirerAffectationAsync(affectationId, ct) ? NoContent() : NotFound());

    /// <summary>
    /// Comptes et accès « Heure de sortie ». v67 : réservé au Super Admin — lui seul voit la page et
    /// accorde ou retire ce droit, compte par compte, avec l'horaire 07h40 / 08h40.
    /// </summary>
    [HttpGet("acces-heure-sortie")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<HeureSortieAccesDto>>> GetAcces(CancellationToken ct)
    {
        if (ReserveSuperAdmin() is { } refus) return refus;
        return Ok(await service.GetAccesHeureSortieAsync(ct));
    }

    [HttpPut("acces-heure-sortie")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetAcces([FromBody] HeureSortieAccesRequest body, CancellationToken ct)
    {
        if (ReserveSuperAdmin() is { } refus) return refus;
        if (body is null) return BadRequest(new { message = "Requête vide." });
        try
        {
            return Ok(new { modifies = await service.SetAccesHeureSortieAsync(body, await MeAsync(ct), ct) });
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    /// <summary>v43 : horaire de base d'un compte (08h40 avec une journée à 07h40 par semaine, ou 07h40).</summary>
    [HttpPut("acces-heure-sortie/{userId:int}/base")]
    [Authorize(Roles = "Admin")]
    public Task<IActionResult> SetBase(int userId, [FromBody] HeureSortieBaseRequest body, CancellationToken ct)
    {
        if (ReserveSuperAdmin() is { } refus) return Task.FromResult<IActionResult>(refus);
        return Run(async () => await service.SetBaseHeureSortieAsync(userId, body.Base, ct) ? NoContent() : NotFound());
    }

    /// <summary>v67 — « Heure de sortie » : seul le Super Admin gère ce droit.</summary>
    private ObjectResult? ReserveSuperAdmin()
    {
        if (KyntusAdminClaims.IsSuperAdmin(User)) return null;
        const string message = "Réservé au Super Admin : seul le Super Admin accorde le droit « Heure de sortie ».";
        return StatusCode(StatusCodes.Status403Forbidden, new { code = "SUPER_ADMIN_ONLY", error = message, message });
    }

    private async Task<IActionResult> Run(Func<Task<IActionResult>> action)
    {
        try
        {
            return await action();
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    /// <summary>Identifiant Planning de l'Admin connecté (traçabilité des modifications).</summary>
    private async Task<int?> MeAsync(CancellationToken ct)
    {
        var authId = User.GetAuthUserId();
        if (authId is int a && a > 0)
            return await db.Users.AsNoTracking().Where(u => u.AuthUserId == a).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
        return null;
    }
}
