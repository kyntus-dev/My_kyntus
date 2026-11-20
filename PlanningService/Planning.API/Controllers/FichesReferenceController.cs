using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;

namespace Planning.API.Controllers;

/// <summary>
/// v63 — Fiches employés : mise à jour depuis le fichier RH de référence et contrôle de complétude.
/// Analyse (rien n'est écrit) → application des lignes validées → historique et annulation.
/// </summary>
[ApiController]
[Route("api/users/fiches-reference")]
[Authorize(Roles = "Admin,RH")]
public class FichesReferenceController(IFichesReferenceService service) : ControllerBase
{
    [HttpPost("analyser")]
    [RequestSizeLimit(16 * 1024 * 1024)]
    public async Task<ActionResult<AnalyseFichesReferenceDto>> Analyser(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fichier manquant." });
        try
        {
            await using var flux = file.OpenReadStream();
            return Ok(await service.AnalyserAsync(flux, file.FileName, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("appliquer")]
    public async Task<ActionResult<ApplicationFichesReferenceDto>> Appliquer(
        [FromBody] AppliquerFichesReferenceRequest demande, CancellationToken ct)
    {
        try
        {
            return Ok(await service.AppliquerAsync(demande, Auteur(), ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("historique")]
    public async Task<ActionResult<IReadOnlyList<ImportFichesReferenceDto>>> Historique(CancellationToken ct) =>
        Ok(await service.HistoriqueAsync(ct));

    [HttpGet("historique/{importId:guid}")]
    public async Task<ActionResult<IReadOnlyList<LigneAppliqueeFicheDto>>> Detail(Guid importId, CancellationToken ct) =>
        Ok(await service.DetailImportAsync(importId, ct));

    [HttpPost("historique/{importId:guid}/annuler")]
    public async Task<ActionResult<AnnulationFichesReferenceDto>> Annuler(Guid importId, CancellationToken ct)
    {
        try
        {
            return Ok(await service.AnnulerAsync(importId, Auteur(), ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Fiches d'employés actifs auxquelles il manque des informations obligatoires.</summary>
    [HttpGet("incompletes")]
    public async Task<ActionResult<IReadOnlyList<FicheIncompleteDto>>> Incompletes(CancellationToken ct) =>
        Ok(await service.FichesIncompletesAsync(ct));

    /// <summary>Même liste, en CSV (séparateur « ; », UTF-8 avec BOM pour Excel).</summary>
    [HttpGet("incompletes/export")]
    public async Task<IActionResult> ExportIncompletes(CancellationToken ct)
    {
        var fiches = await service.FichesIncompletesAsync(ct);
        var sb = new StringBuilder("Nom;Prénom;N° d'ordre;Service;Rôle;Nombre de champs manquants;Champs manquants\n");
        foreach (var f in fiches)
            sb.AppendJoin(';', Csv(f.Nom), Csv(f.Prenom), Csv(f.Matricule), Csv(f.Service), Csv(f.Role),
                    f.ChampsManquants.Count.ToString(), Csv(string.Join(", ", f.ChampsManquants)))
              .Append('\n');
        var octets = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(octets, "text/csv; charset=utf-8", $"fiches_incompletes_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private string Auteur() =>
        User.FindFirst("email")?.Value
        ?? User.FindFirst(ClaimTypes.Email)?.Value
        ?? User.FindFirst("name")?.Value
        ?? User.Identity?.Name
        ?? User.FindFirst("sub")?.Value
        ?? "inconnu";

    private static string Csv(string? valeur)
    {
        if (string.IsNullOrEmpty(valeur)) return string.Empty;
        // Neutralise les formules Excel (=, +, -, @) et échappe les séparateurs.
        if (valeur[0] is '=' or '+' or '-' or '@') valeur = "'" + valeur;
        return valeur.Contains(';') || valeur.Contains('"') || valeur.Contains('\n')
            ? $"\"{valeur.Replace("\"", "\"\"")}\""
            : valeur;
    }
}
