using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.DTOs;
using Planning.Infrastructure.Services;

namespace Planning.API.Controllers;

/// <summary>
/// v56 — API publique des employés, à consommer depuis un autre outil.
///
/// <code>
/// GET /api/public/employes?cle=LA_CLE
/// GET /api/public/employes?cle=LA_CLE&amp;cellule=Support%20technicien&amp;actif=true
/// GET /api/public/employes?cle=LA_CLE&amp;format=csv
/// </code>
///
/// La clé se donne au choix dans l'en-tête <c>X-Api-Key</c> ou dans le paramètre <c>cle</c>.
/// Elle se règle sur le serveur dans le fichier <c>.env</c> :
/// <c>PublicApi__Cles__0=une-cle-longue-et-aleatoire</c> (plusieurs clés possibles : __0, __1…).
/// Sans clé configurée, l'API reste fermée.
/// </summary>
[ApiController]
[Route("api/public/employes")]
[AllowAnonymous]
public sealed class PublicEmployesController(
    IPublicEmployesService service,
    IConfiguration configuration,
    ILogger<PublicEmployesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Lister(
        [FromQuery] string? cle,
        [FromQuery] bool? actif,
        [FromQuery] string? contrat,
        [FromQuery] string? pole,
        [FromQuery] string? cellule,
        [FromQuery(Name = "service")] string? serviceNom,
        [FromQuery] string? role,
        [FromQuery] bool? enFormation,
        [FromQuery] bool? planifiable,
        [FromQuery(Name = "q")] string? recherche,
        [FromQuery] int page = 1,
        [FromQuery] int taille = 200,
        [FromQuery] string? format = null,
        CancellationToken ct = default)
    {
        if (ClesConfigurees() is not { Count: > 0 } cles)
        {
            logger.LogWarning("API publique employés appelée sans clé configurée sur le serveur.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "API non activée : définissez PublicApi__Cles__0 dans le .env du serveur, "
                          + "puis redémarrez le service Planning.",
            });
        }

        var fournie = Request.Headers["X-Api-Key"].FirstOrDefault() ?? cle;
        if (string.IsNullOrWhiteSpace(fournie) || !cles.Contains(fournie.Trim(), StringComparer.Ordinal))
            return Unauthorized(new { message = "Clé d'API absente ou invalide." });

        var filtre = new PublicEmployesFiltre
        {
            Actif = actif ?? true,
            Contrat = contrat,
            Pole = pole,
            Cellule = cellule,
            Service = serviceNom,
            Role = role,
            EnFormation = enFormation,
            Planifiable = planifiable,
            Recherche = recherche,
            Page = page,
            Taille = taille,
        };

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = await service.ListerCsvAsync(filtre, ct);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", "employes.csv");
        }

        return Ok(await service.ListerAsync(filtre, ct));
    }

    private HashSet<string>? ClesConfigurees()
    {
        var cles = configuration.GetSection("PublicApi:Cles").Get<string[]>() ?? [];
        var uniques = cles
            .Select(c => (c ?? string.Empty).Trim())
            .Where(c => c.Length >= 16)
            .ToHashSet(StringComparer.Ordinal);
        return uniques.Count > 0 ? uniques : null;
    }
}
