using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;
using Planning.Application.Presence;
using Planning.Infrastructure.Services.Presence;

namespace Planning.API.Controllers;

/// <summary>
/// v58 — Validation du tableau de paie, <b>réservée aux RH</b> (et à l'Admin, qui administre la
/// plateforme).
///
/// <para>
/// Consulter l'état est ouvert à ceux qui ont le droit de voir le tableau ; valider et rouvrir ne
/// le sont pas, quel que soit le reste de leurs droits. Une période validée est verrouillée :
/// présences et montants n'y sont plus modifiables (contrôlé dans les services, pas seulement à
/// l'écran). Le nom du validateur et la date restent affichés à tous.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/planning/presences/paie")]
public sealed class PaieValidationController(
    IPaieValidationService validation,
    IUserService users,
    IPolicyEvaluator policy) : ControllerBase
{
    /// <summary>État de la période : validée ou non, par qui, quand.</summary>
    [HttpGet("validation")]
    public async Task<ActionResult<PaieValidationDto>> Etat(
        [FromQuery] DateOnly? du,
        [FromQuery] DateOnly? au,
        CancellationToken ct)
    {
        if (!await PeutVoirAsync(ct)) return Forbid();
        var (d, a) = PresencesController.PeriodeParDefaut(du, au);
        return Ok(await validation.EtatAsync(d, a, ct));
    }

    /// <summary>Valide la période : RH et Admin uniquement. La période devient non modifiable.</summary>
    [HttpPost("validation")]
    public async Task<ActionResult<PaieValidationDto>> Valider(
        [FromQuery] DateOnly? du,
        [FromQuery] DateOnly? au,
        [FromBody] ValiderPaieRequest? body,
        CancellationToken ct)
    {
        if (!EstRh()) return Refus();
        var (d, a) = PresencesController.PeriodeParDefaut(du, au);
        try
        {
            var (id, nom) = await QuiSuisJeAsync();
            return Ok(await validation.ValiderAsync(d, a, id, nom, body?.Commentaire, ct));
        }
        catch (PresenceRefusException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Rouvre la période pour correction : RH et Admin uniquement, action tracée.</summary>
    [HttpDelete("validation")]
    public async Task<ActionResult<PaieValidationDto>> Rouvrir(
        [FromQuery] DateOnly? du,
        [FromQuery] DateOnly? au,
        CancellationToken ct)
    {
        if (!EstRh()) return Refus();
        var (d, a) = PresencesController.PeriodeParDefaut(du, au);
        try
        {
            var (id, nom) = await QuiSuisJeAsync();
            return Ok(await validation.RouvrirAsync(d, a, id, nom, ct));
        }
        catch (PresenceRefusException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // ── Outils ──────────────────────────────────────────────────────────────────

    private bool EstRh()
    {
        var role = (User.GetAuthRole() ?? string.Empty).Trim();
        return role.Equals("RH", StringComparison.OrdinalIgnoreCase)
               || role.Equals("Admin", StringComparison.OrdinalIgnoreCase);
    }

    private ObjectResult Refus() =>
        StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = "La validation du tableau de paie est réservée au service RH.",
        });

    private async Task<bool> PeutVoirAsync(CancellationToken ct)
    {
        var role = User.GetAuthRole() ?? "Employee";
        foreach (var action in new[] { "presences.export.View", "presences.evp.View", "presences.View" })
        {
            var decision = await policy.EvaluateAsync(
                new PolicyRequest(User.GetSubjectId() ?? Guid.Empty, role, action, "platform"), ct);
            if (decision.Allowed) return true;
        }
        return false;
    }

    private async Task<(int Id, string Nom)> QuiSuisJeAsync()
    {
        var authUserId = User.GetAuthUserId();
        if (authUserId is > 0)
        {
            var user = await users.GetUserByAuthIdAsync(authUserId.Value);
            if (user is not null)
                return (user.Id, $"{user.LastName} {user.FirstName}".Trim());
        }
        return (0, User.GetEmail() ?? "RH");
    }
}

public sealed record ValiderPaieRequest(string? Commentaire);
