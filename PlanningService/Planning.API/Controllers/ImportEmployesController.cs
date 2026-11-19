using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;

namespace Planning.API.Controllers;

/// <summary>
/// v49 — Import du fichier RH depuis l'écran « Liaisons HTEL » : les employés sont créés en lot,
/// sans mail interne (v47). Réservé aux RH, chefs de projet (RP compris) et à l'Admin.
/// </summary>
[ApiController]
[Authorize]
[Route("api/users/import-employes")]
public sealed class ImportEmployesController(IUserService userService) : ControllerBase
{
    private const int MaxLignes = 500;

    private static readonly string[] RolesAutorises =
        ["Admin", "RH", "RP", "Chef de projet"];

    [HttpPost]
    public async Task<IActionResult> Importer([FromBody] ImportEmployesRequest request, CancellationToken ct)
    {
        var role = User.GetAuthRole() ?? string.Empty;
        if (!RolesAutorises.Contains(role, StringComparer.OrdinalIgnoreCase))
            return Forbid();

        var lignes = request?.Lignes ?? new List<LigneImportEmployeDto>();
        if (lignes.Count == 0)
            return BadRequest(new { message = "Aucune ligne à importer." });
        if (lignes.Count > MaxLignes)
            return BadRequest(new { message = $"Fichier trop volumineux ({lignes.Count} lignes, maximum {MaxLignes})." });
        if (request!.RoleId <= 0)
            return BadRequest(new { message = "Choisissez le rôle des employés importés." });

        try
        {
            return Ok(await userService.ImporterEmployesAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
