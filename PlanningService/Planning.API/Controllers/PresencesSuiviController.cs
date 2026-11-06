using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Presence;

namespace Planning.API.Controllers;

/// <summary>
/// Présences et paie, lot B : avertissements, éléments variables de paie (EVP) et dossier administratif.
/// Droits dans « Accès par rôle » : écrans presences.avertissements, presences.evp, presences.dossier
/// (Voir / Agir). Le service vérifie en plus que l'agent est dans le périmètre du demandeur.
/// </summary>
[ApiController]
[Authorize]
[Route("api/planning/presences")]
public sealed class PresencesSuiviController(IPresenceSuiviService suivi, IPolicyEvaluator policy) : ControllerBase
{
    /// <summary>Listes proposées dans les formulaires.</summary>
    [HttpGet("catalogues")]
    public ActionResult<object> Catalogues() => Ok(new
    {
        avertissements = SuiviCatalogues.TypesAvertissement,
        manquements = Referentiel.Manquements,
        evp = SuiviCatalogues.TypesEvp,
        dossier = SuiviCatalogues.CategoriesDossier,
        tailleMaxDocument = SuiviCatalogues.TailleMaxDocument,
    });

    /// <summary>Agents du périmètre du demandeur (listes de choix).</summary>
    [HttpGet("agents")]
    public Task<IActionResult> Agents(CancellationToken ct) =>
        Executer("presences.avertissements.View", async () => Ok(await suivi.ListerAgentsAsync(Viewer(), ct)), ct,
            "presences.evp.View", "presences.dossier.View", "presences.export.View");

    // ── Avertissements ──────────────────────────────────────────────────────────

    [HttpGet("avertissements")]
    public Task<IActionResult> ListerAvertissements([FromQuery] DateOnly? du, [FromQuery] DateOnly? au, CancellationToken ct) =>
        Executer("presences.avertissements.View", async () =>
        {
            var (d, a) = PresencesController.PeriodeParDefaut(du, au);
            return Ok(await suivi.ListerAvertissementsAsync(Viewer(), d, a, ct));
        }, ct);

    [HttpPost("avertissements")]
    public Task<IActionResult> AjouterAvertissement([FromBody] AvertissementRequest body, CancellationToken ct) =>
        Executer("presences.avertissements.Act", async () => Ok(await suivi.AjouterAvertissementAsync(Viewer(), body, ct)), ct);

    [HttpDelete("avertissements/{id:long}")]
    public Task<IActionResult> SupprimerAvertissement(long id, CancellationToken ct) =>
        Executer("presences.avertissements.Act", async () =>
        {
            await suivi.SupprimerAvertissementAsync(Viewer(), id, ct);
            return NoContent();
        }, ct);

    // ── Éléments variables de paie ──────────────────────────────────────────────

    [HttpGet("evp")]
    public Task<IActionResult> ListerEvp([FromQuery] DateOnly? du, [FromQuery] DateOnly? au, CancellationToken ct) =>
        Executer("presences.evp.View", async () =>
        {
            var (d, a) = PresencesController.PeriodeParDefaut(du, au);
            return Ok(await suivi.ListerEvpAsync(Viewer(), d, a, ct));
        }, ct, "presences.export.View");

    [HttpPost("evp")]
    public Task<IActionResult> AjouterEvp([FromBody] EvpRequest body, CancellationToken ct) =>
        Executer("presences.evp.Act", async () => Ok(await suivi.AjouterEvpAsync(Viewer(), body, ct)), ct);

    [HttpPut("evp/{id:long}")]
    public Task<IActionResult> ModifierEvp(long id, [FromBody] EvpRequest body, CancellationToken ct) =>
        Executer("presences.evp.Act", async () => Ok(await suivi.ModifierEvpAsync(Viewer(), id, body, ct)), ct);

    [HttpDelete("evp/{id:long}")]
    public Task<IActionResult> SupprimerEvp(long id, CancellationToken ct) =>
        Executer("presences.evp.Act", async () =>
        {
            await suivi.SupprimerEvpAsync(Viewer(), id, ct);
            return NoContent();
        }, ct);

    // ── Dossier administratif ───────────────────────────────────────────────────

    [HttpGet("dossiers/{userId:int}")]
    public Task<IActionResult> Dossier(int userId, CancellationToken ct) =>
        Executer("presences.dossier.View", async () => Ok(await suivi.GetDossierAsync(Viewer(), userId, ct)), ct);

    /// <summary>Ajout d'un document (multipart : fichier, categorie, commentaire). 10 Mo au plus.</summary>
    [HttpPost("dossiers/{userId:int}/documents")]
    [RequestSizeLimit(SuiviCatalogues.TailleMaxDocument + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = SuiviCatalogues.TailleMaxDocument + 1024 * 1024)]
    public Task<IActionResult> AjouterDocument(
        int userId, IFormFile? fichier, [FromForm] string? categorie, [FromForm] string? commentaire, CancellationToken ct) =>
        Executer("presences.dossier.Act", async () =>
        {
            if (fichier is null || fichier.Length == 0) return BadRequest(new { message = "Aucun fichier reçu." });
            if (fichier.Length > SuiviCatalogues.TailleMaxDocument)
                return BadRequest(new { message = "Fichier trop volumineux : 10 Mo au plus." });
            using var ms = new MemoryStream();
            await fichier.CopyToAsync(ms, ct);
            return Ok(await suivi.AjouterDocumentAsync(
                Viewer(), userId, categorie ?? string.Empty, fichier.FileName, fichier.ContentType, ms.ToArray(), commentaire, ct));
        }, ct);

    [HttpGet("documents/{id:long}")]
    public Task<IActionResult> TelechargerDocument(long id, CancellationToken ct) =>
        Executer("presences.dossier.View", async () =>
        {
            var doc = await suivi.GetDocumentAsync(Viewer(), id, ct);
            return File(doc.Contenu, doc.ContentType, doc.NomFichier);
        }, ct);

    [HttpDelete("documents/{id:long}")]
    public Task<IActionResult> SupprimerDocument(long id, CancellationToken ct) =>
        Executer("presences.dossier.Act", async () =>
        {
            await suivi.SupprimerDocumentAsync(Viewer(), id, ct);
            return NoContent();
        }, ct);

    // ── Outils ──────────────────────────────────────────────────────────────────

    /// <summary>Exécute <paramref name="corps"/> si l'un des droits est accordé ; erreurs métier → 400 / 403.</summary>
    private async Task<IActionResult> Executer(string action, Func<Task<IActionResult>> corps, CancellationToken ct, params string[] alternatives)
    {
        var autorise = await PeutAsync(action, ct);
        foreach (var alt in alternatives)
        {
            if (autorise) break;
            autorise = await PeutAsync(alt, ct);
        }
        if (!autorise) return Forbid();
        try
        {
            return await corps();
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
