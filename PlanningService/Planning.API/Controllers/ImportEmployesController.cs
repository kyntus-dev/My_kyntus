using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;

namespace Planning.API.Controllers;

/// <summary>
/// v49 — Import du fichier RH depuis l'écran « Liaisons HTEL » : les employés sont créés en lot,
/// sans mail interne (v47). Réservé aux RH, chefs de projet (RP compris) et à l'Admin.
/// <para>
/// v66 — Chaque import vérifie aussi les fiches existantes : une ligne dont la fiche existe déjà
/// (CIN, puis nom) met cette fiche en conformité avec le fichier au lieu d'être écartée ; seules les
/// lignes sans fiche sont créées, et les cas douteux sont signalés sans rien modifier.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[Route("api/users/import-employes")]
public sealed class ImportEmployesController(IUserService userService, IFichesReferenceService fiches) : ControllerBase
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
            // 1. Fiches existantes : vérification et mise à jour d'après le fichier.
            var auteur = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                         ?? User.FindFirst("email")?.Value
                         ?? User.Identity?.Name
                         ?? "inconnu";
            var synchro = await fiches.SynchroniserImportEmployesAsync(lignes, auteur, ct);

            // 2. Lignes sans fiche : création (avec le contrôle de doublon existant).
            var aCreer = lignes.Where(l => synchro.ACreer.Contains(l.Numero)).ToList();
            var creation = aCreer.Count == 0
                ? new ImportEmployesResultDto(new List<ImportEmployeResultatDto>(), 0, 0)
                : await userService.ImporterEmployesAsync(request with { Lignes = aCreer }, ct);

            var resultats = synchro.Resultats.Concat(creation.Resultats).OrderBy(r => r.Numero).ToList();
            int Nb(string statut) => resultats.Count(r => r.Statut == statut);
            return Ok(new ImportEmployesResultDto(
                resultats,
                creation.Crees,
                Nb(ImportEmployeStatuts.Erreur),
                Nb(ImportEmployeStatuts.MisAJour),
                Nb(ImportEmployeStatuts.AJour),
                Nb(ImportEmployeStatuts.AVerifier),
                resultats.Any(r => r.Statut == ImportEmployeStatuts.MisAJour) ? synchro.ImportId : null));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
