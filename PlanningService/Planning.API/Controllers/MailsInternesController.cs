using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Application.Users;

namespace Planning.API.Controllers;

/// <summary>
/// v47 — Mails internes : collaborateurs créés sans adresse professionnelle, puis attribution en
/// masse à partir d'un fichier (rapprochement nom / prénom, contrôlé avant application).
/// </summary>
[ApiController]
[Route("api/users/mails-internes")]
[Authorize(Roles = "Admin,RH")]
public class MailsInternesController(IUserService userService) : ControllerBase
{
    private const int MaxLignes = 2000;

    /// <summary>Collaborateurs sans mail interne.</summary>
    [HttpGet("sans-mail")]
    public async Task<ActionResult<List<AgentSansMailDto>>> SansMail(CancellationToken ct) =>
        Ok(await userService.ListerAgentsSansMailAsync(ct));

    /// <summary>
    /// Aperçu : rapproche chaque ligne du fichier d'un collaborateur sans mail interne.
    /// Rien n'est enregistré.
    /// </summary>
    [HttpPost("apercu")]
    public async Task<ActionResult<ApercuMailsResultDto>> Apercu(
        [FromBody] ApercuMailsRequest request,
        CancellationToken ct)
    {
        var lignes = request?.Lignes ?? new List<LigneMailImportDto>();
        if (lignes.Count == 0)
            return BadRequest(new { message = "Le fichier ne contient aucune ligne exploitable." });
        if (lignes.Count > MaxLignes)
            return BadRequest(new { message = $"Fichier trop volumineux ({lignes.Count} lignes, maximum {MaxLignes})." });

        var agents = await userService.ListerAgentsSansMailAsync(ct);
        var utilises = await userService.ListerMailsUtilisesAsync(ct);

        var resultats = RapprochementMails.Rapprocher(
            lignes.Select(l => new RapprochementMails.Ligne(l.Numero, l.Nom, l.Prenom, l.Email)).ToList(),
            agents.Select(a => new RapprochementMails.Agent(a.UserId, a.Nom, a.Prenom)).ToList(),
            utilises);

        var dto = resultats
            .Select(r => new LigneMailApercuDto(
                r.Numero,
                r.Nom,
                r.Prenom,
                r.Email,
                r.Statut,
                r.UserId,
                r.Message,
                r.Candidats
                    .Select(c => new CandidatMailDto(c.UserId, c.Nom, c.Prenom, Math.Round(c.Score, 3)))
                    .ToList()))
            .ToList();

        return Ok(new ApercuMailsResultDto(
            dto,
            dto.Count,
            dto.Count(l => l.Statut == RapprochementMails.StatutExact),
            dto.Count(l => l.Statut == RapprochementMails.StatutProbable),
            dto.Count(l => l.Statut == RapprochementMails.StatutAmbigu),
            dto.Count(l => l.Statut == RapprochementMails.StatutIntrouvable),
            dto.Count(l => l.Statut is RapprochementMails.StatutDoublon
                or RapprochementMails.StatutEmailUtilise
                or RapprochementMails.StatutInvalide
                or RapprochementMails.StatutDejaAttribue),
            agents.Count));
    }

    /// <summary>Applique les attributions validées à l'écran, une par une (aucun lot partiel caché).</summary>
    [HttpPost("appliquer")]
    public async Task<ActionResult<AppliquerMailsResultDto>> Appliquer(
        [FromBody] AppliquerMailsRequest request,
        CancellationToken ct)
    {
        var attributions = request?.Attributions ?? new List<AttributionMailDto>();
        if (attributions.Count == 0)
            return BadRequest(new { message = "Aucune attribution sélectionnée." });
        if (attributions.Count > MaxLignes)
            return BadRequest(new { message = $"Trop d'attributions ({attributions.Count}, maximum {MaxLignes})." });

        var doublonsAgent = attributions.GroupBy(a => a.UserId).Any(g => g.Count() > 1);
        var doublonsMail = attributions
            .GroupBy(a => MailInterne.Cle(a.Email)).Any(g => g.Count() > 1);
        if (doublonsAgent || doublonsMail)
            return BadRequest(new { message = "Un collaborateur ou une adresse apparaît deux fois dans la sélection." });

        var resultats = new List<AttributionMailResultDto>(attributions.Count);
        foreach (var a in attributions)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                resultats.Add(await userService.AttribuerMailInterneAsync(
                    a.UserId, a.Email, request!.CreerComptes, ct));
            }
            catch (Exception ex)
            {
                resultats.Add(new AttributionMailResultDto(
                    a.UserId, "", "", a.Email, false, ex.Message, null));
            }
        }

        return Ok(new AppliquerMailsResultDto(
            resultats,
            resultats.Count(r => r.Ok),
            resultats.Count(r => !r.Ok)));
    }
}
