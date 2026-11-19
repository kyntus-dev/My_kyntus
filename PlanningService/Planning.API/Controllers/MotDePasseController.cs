using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Infrastructure.Services;

namespace Planning.API.Controllers;

/// <summary>
/// v56 — « Mot de passe oublié » depuis l'écran de connexion, pour tout le monde.
/// La réponse est toujours la même, que l'adresse existe ou non : le nouveau mot de passe part
/// par e-mail à l'adresse professionnelle de la fiche, jamais à l'écran.
/// </summary>
[ApiController]
[Route("api/mot-de-passe")]
[AllowAnonymous]
public sealed class MotDePasseController(
    IMotDePasseOublieService service,
    ILogger<MotDePasseController> logger) : ControllerBase
{
    private const string Reponse =
        "Si cette adresse correspond à un compte actif, un nouveau mot de passe vient d'y être envoyé. "
        + "Pensez à regarder vos indésirables.";

    [HttpPost("oublie")]
    public async Task<IActionResult> Oublie([FromBody] MotDePasseOublieRequest? body, CancellationToken ct)
    {
        try
        {
            await service.DemanderAsync(
                body?.Email,
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                ct);
            return Ok(new { message = Reponse });
        }
        catch (InvalidOperationException ex)
        {
            // Configuration absente ou envoi impossible : le dire, sinon la personne attend un
            // e-mail qui n'arrivera jamais.
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Mot de passe oublié : erreur inattendue.");
            return StatusCode(500, new { message = "Erreur serveur : réessayez plus tard." });
        }
    }
}

public sealed record MotDePasseOublieRequest(string? Email);
