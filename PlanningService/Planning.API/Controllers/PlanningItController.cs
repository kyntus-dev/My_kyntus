using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Planning.Application.PlanningIt;
using Planning.Infrastructure.Persistence;

namespace Planning.API.Controllers;

/// <summary>
/// Planning IT partagé. Droits lus dans « Accès par rôle » (écran « planning-it ») :
/// Voir = consulter, Agir = saisir ses propres horaires, Configurer = corriger toutes les lignes.
/// </summary>
[ApiController]
[Authorize]
[Route("api/planning/planning-it")]
public sealed class PlanningItController(
    IPlanningItService service,
    IPolicyEvaluator policy,
    AppDbContext db) : ControllerBase
{
    [HttpGet("semaine")]
    public async Task<IActionResult> Semaine([FromQuery] DateOnly? jour, CancellationToken ct)
    {
        var droits = await DroitsAsync(ct);
        if (!droits.Voir)
            return Forbid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return Ok(await service.GetSemaineAsync(await MeAsync(ct), droits, jour ?? today, ct));
    }

    [HttpPut("case")]
    public Task<IActionResult> Saisir([FromBody] PlanningItCaseRequest body, CancellationToken ct) =>
        Run(async droits => { await service.SaisirAsync(await MeAsync(ct), droits, body, ct); return NoContent(); }, ct);

    [HttpPut("commentaire")]
    public Task<IActionResult> Commenter([FromBody] PlanningItCommentaireRequest body, CancellationToken ct) =>
        Run(async droits => { await service.CommenterAsync(await MeAsync(ct), droits, body, ct); return NoContent(); }, ct);

    private async Task<IActionResult> Run(Func<PlanningItDroits, Task<IActionResult>> action, CancellationToken ct)
    {
        var droits = await DroitsAsync(ct);
        if (!droits.SaisirSoi && !droits.ModifierTout)
            return Forbid();
        try
        {
            return await action(droits);
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    private async Task<PlanningItDroits> DroitsAsync(CancellationToken ct)
    {
        var role = User.GetAuthRole() ?? "Employee";
        var subject = User.GetSubjectId() ?? Guid.Empty;
        async Task<bool> A(string verbe) =>
            (await policy.EvaluateAsync(new PolicyRequest(subject, role, $"planning-it.{verbe}", "platform"), ct)).Allowed;
        var modifierTout = await A("Configure");
        var saisir = await A("Act");
        var voir = modifierTout || saisir || await A("View");
        return new PlanningItDroits(voir, saisir, modifierTout);
    }

    /// <summary>Identifiant Planning du lecteur (lien Auth, puis identifiant employé, puis e-mail).</summary>
    private async Task<int?> MeAsync(CancellationToken ct)
    {
        if (User.GetAuthUserId() is int a && a > 0)
        {
            var id = await db.Users.AsNoTracking().Where(u => u.AuthUserId == a).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
            if (id is not null) return id;
        }
        if (User.GetSubjectId() is Guid g && g != Guid.Empty)
        {
            var id = await db.Users.AsNoTracking().Where(u => u.Guid == g).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
            if (id is not null) return id;
        }
        var email = User.GetEmail()?.Trim().ToLower();
        return string.IsNullOrEmpty(email)
            ? null
            : await db.Users.AsNoTracking().Where(u => u.Email.ToLower() == email).Select(u => (int?)u.Id).FirstOrDefaultAsync(ct);
    }
}
