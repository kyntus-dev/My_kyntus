using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Presence;

namespace Planning.API.Controllers;

/// <summary>
/// Tableau des présences (superviseur, référent technique, chef de projet, RH, Admin).
/// Droits dans « Accès par rôle » : écran « presences » (Voir / Agir). Chaque responsable ne voit
/// et ne modifie que les agents de son périmètre ; RH et Admin voient toute l'entreprise.
/// Route sous /api/planning/… : déjà publiée par la passerelle.
/// </summary>
[ApiController]
[Authorize]
[Route("api/planning/presences")]
public sealed class PresencesController(IPresenceService presences, IPolicyEvaluator policy) : ControllerBase
{
    private const string CapVoir = "presences.View";
    private const string CapAgir = "presences.Act";

    /// <summary>Tableau du <paramref name="du"/> au <paramref name="au"/> (31 jours au plus).</summary>
    [HttpGet]
    public async Task<ActionResult<PresenceGrilleDto>> Grille([FromQuery] DateOnly? du, [FromQuery] DateOnly? au, CancellationToken ct)
    {
        if (!await PeutAsync(CapVoir, ct)) return Forbid();
        var (d, a) = PeriodeParDefaut(du, au);
        var result = await presences.GetGrilleAsync(Viewer(), d, a, await PeutAsync(CapAgir, ct), ct);
        return result is null ? NotFound(new { message = "Compte introuvable dans le Planning." }) : Ok(result);
    }

    /// <summary>
    /// Saisie sur une journée (ou une période) : motif d'absence, retard autorisé, commentaire.
    /// Renvoie la ligne de l'agent recalculée sur la période affichée (<paramref name="du"/> → <paramref name="au"/>).
    /// </summary>
    [HttpPut("jour")]
    public async Task<ActionResult<PresenceAgentDto>> Saisir(
        [FromBody] PresenceSaisieRequest body, [FromQuery] DateOnly? du, [FromQuery] DateOnly? au, CancellationToken ct)
    {
        if (!await PeutAsync(CapAgir, ct)) return Forbid();
        var (d, a) = PeriodeParDefaut(du, au);
        try
        {
            return Ok(await presences.SaisirAsync(Viewer(), body, d, a, ct));
        }
        catch (PresenceRefusException ex) when (ex.HorsPerimetre)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (PresenceRefusException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Période de paie par défaut : du 24 du mois précédent au 23 du mois.</summary>
    internal static (DateOnly Du, DateOnly Au) PeriodeParDefaut(DateOnly? du, DateOnly? au)
    {
        if (du is DateOnly d && au is DateOnly a) return (d, a);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fin = today.Day > 23 ? new DateOnly(today.Year, today.Month, 23).AddMonths(1) : new DateOnly(today.Year, today.Month, 23);
        return (fin.AddMonths(-1).AddDays(1), fin);
    }

    private async Task<bool> PeutAsync(string action, CancellationToken ct)
    {
        var role = User.GetAuthRole() ?? "Employee";
        var decision = await policy.EvaluateAsync(new PolicyRequest(User.GetSubjectId() ?? Guid.Empty, role, action, "platform"), ct);
        return decision.Allowed;
    }

    private PresenceViewer Viewer()
    {
        var role = User.GetAuthRole();
        var global = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);
        return new PresenceViewer(User.GetAuthUserId(), User.GetSubjectId(), User.GetEmail(), role, global);
    }
}
