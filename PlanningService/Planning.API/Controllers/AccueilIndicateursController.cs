using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;

namespace Planning.API.Controllers;

/// <summary>
/// v87 — Historique personnel des indicateurs de l'accueil (tendances ▲/▼ et courbes).
/// Chaque personne connectée enregistre et relit uniquement SES valeurs (celles que son accueil a
/// affichées, calculées avec ses droits). Aucune autre donnée n'est lue ni exposée ; aucun droit nouveau.
/// </summary>
[ApiController]
[Route("api/planning/accueil/indicateurs")]
[Authorize]
public sealed class AccueilIndicateursController(IAccueilIndicateursService service) : ControllerBase
{
    public sealed record EnregistrementIndicateurs(Dictionary<string, decimal>? Valeurs);

    private static DateOnly Aujourdhui()
    {
        try
        {
            var paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, paris));
        }
        catch (TimeZoneNotFoundException)
        {
            return DateOnly.FromDateTime(DateTime.Now);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Lire(CancellationToken ct, [FromQuery] int jours = 35)
    {
        if (User.GetSubjectId() is not Guid sujet || sujet == Guid.Empty) return Unauthorized();
        var data = await service.LireAsync(sujet, Aujourdhui(), jours, ct);
        return Ok(data.Select(d => new { jour = d.Jour.ToString("yyyy-MM-dd"), valeurs = d.Valeurs }));
    }

    [HttpPost]
    public async Task<IActionResult> Enregistrer([FromBody] EnregistrementIndicateurs corps, CancellationToken ct)
    {
        if (User.GetSubjectId() is not Guid sujet || sujet == Guid.Empty) return Unauthorized();
        if (corps.Valeurs is null || corps.Valeurs.Count == 0) return Ok(new { retenues = 0 });
        var n = await service.EnregistrerAsync(sujet, Aujourdhui(), corps.Valeurs, ct);
        return Ok(new { retenues = n });
    }
}
