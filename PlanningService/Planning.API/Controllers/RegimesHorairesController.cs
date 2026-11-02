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
