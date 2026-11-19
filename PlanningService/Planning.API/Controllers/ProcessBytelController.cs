using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Presence;

namespace Planning.API.Controllers;

/// <summary>
/// v48 — Bibliothèque « Process Bytel » : l'encadrement dépose les procédures, les agents les
/// consultent. Droits dans « Accès par rôle » : écran <c>process-bytel</c> — <b>Voir</b> pour
/// consulter, <b>Agir</b> pour déposer et supprimer.
/// </summary>
[ApiController]
[Authorize]
[Route("api/planning/process-bytel")]
public sealed class ProcessBytelController(IProcessDocumentService documents, IPolicyEvaluator policy) : ControllerBase
{
    /// <summary>Documents visibles par le demandeur, et droit de dépôt.</summary>
    [HttpGet]
    public async Task<IActionResult> Lister(CancellationToken ct)
    {
        if (!await PeutAsync("process-bytel.View", ct) && !await PeutAsync("process-bytel.Act", ct))
            return Forbid();
        return await Executer(async () =>
            Ok(await documents.ListerAsync(Viewer(), await PeutDeposerAsync(ct), ct)));
    }

    /// <summary>Dépôt d'un document (multipart). 25 Mo au plus.</summary>
    [HttpPost]
    [RequestSizeLimit(ProcessDocuments.TailleMax + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProcessDocuments.TailleMax + 1024 * 1024)]
    public async Task<IActionResult> Deposer(
        IFormFile? fichier,
        [FromForm] string? dossier,
        [FromForm] string? titre,
        [FromForm] string? commentaire,
        [FromForm] bool pourTous,
        [FromForm] string? activites,
        [FromForm] string? poles,
        CancellationToken ct)
    {
        if (!await PeutDeposerAsync(ct)) return Forbid();
        if (fichier is null || fichier.Length == 0)
            return BadRequest(new { message = "Aucun fichier reçu." });
        if (fichier.Length > ProcessDocuments.TailleMax)
            return BadRequest(new { message = "Fichier trop volumineux : 25 Mo au plus." });

        using var ms = new MemoryStream();
        await fichier.CopyToAsync(ms, ct);

        var visibilite = new ProcessVisibiliteRequest(
            pourTous,
            ProcessDocuments.Decouper(activites).ToList(),
            ProcessDocuments.Decouper(poles).ToList());

        return await Executer(async () => Ok(await documents.AjouterAsync(
            Viewer(), dossier, titre, fichier.FileName, fichier.ContentType, ms.ToArray(), commentaire, visibilite, ct)));
    }

    [HttpGet("{id:long}/fichier")]
    public async Task<IActionResult> Telecharger(long id, CancellationToken ct)
    {
        var peutDeposer = await PeutDeposerAsync(ct);
        if (!peutDeposer && !await PeutAsync("process-bytel.View", ct)) return Forbid();
        return await Executer(async () =>
        {
            var doc = await documents.TelechargerAsync(Viewer(), id, peutDeposer, ct);
            return File(doc.Contenu, doc.ContentType, doc.NomFichier);
        });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Supprimer(long id, CancellationToken ct)
    {
        if (!await PeutDeposerAsync(ct)) return Forbid();
        return await Executer(async () =>
        {
            await documents.SupprimerAsync(Viewer(), id, ct);
            return NoContent();
        });
    }

    // ── Outils ──────────────────────────────────────────────────────────────────

    private async Task<IActionResult> Executer(Func<Task<IActionResult>> corps)
    {
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

    private Task<bool> PeutDeposerAsync(CancellationToken ct) => PeutAsync("process-bytel.Act", ct);

    private async Task<bool> PeutAsync(string action, CancellationToken ct)
    {
        var role = User.GetAuthRole() ?? "Employee";
        var decision = await policy.EvaluateAsync(
            new PolicyRequest(User.GetSubjectId() ?? Guid.Empty, role, action, "platform"), ct);
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
