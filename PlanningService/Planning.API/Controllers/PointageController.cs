using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Pointage;

namespace Planning.API.Controllers;

/// <summary>
/// Pointages ZKBio Time calculés dans My-Kyntus (lecture seule côté ZKBio Time).
/// - « Mon suivi » : tout collaborateur ;
/// - équipe : superviseur / référent technique / chef de projet (son périmètre), RH et Admin (tous) ;
/// - correspondances ID pointeuse → collaborateur et état de la synchronisation : RH et Admin.
/// Route sous /api/planning/… : déjà publiée par la passerelle.
/// </summary>
[ApiController]
[Authorize]
[Route("api/planning/pointage")]
public sealed class PointageController(
    IPointageQueryService query,
    IPointageSyncService sync) : ControllerBase
{
    [HttpGet("moi")]
    public async Task<ActionResult<PointageSuiviDto>> MonSuivi([FromQuery] DateOnly? du, [FromQuery] DateOnly? au, CancellationToken ct)
    {
        var (d, a) = Periode(du, au);
        var result = await query.GetMonSuiviAsync(Viewer(), d, a, ct);
        return result is null ? NotFound(new { message = "Compte introuvable dans le Planning." }) : Ok(result);
    }

    [HttpGet("collaborateurs/{userId:int}")]
    public async Task<ActionResult<PointageSuiviDto>> Collaborateur(
        int userId, [FromQuery] DateOnly? du, [FromQuery] DateOnly? au, CancellationToken ct)
    {
        var (d, a) = Periode(du, au);
        var result = await query.GetSuiviCollaborateurAsync(Viewer(), userId, d, a, ct);
        return result is null ? Forbid() : Ok(result);
    }

    [HttpGet("equipe")]
    public async Task<ActionResult<PointageEquipeDto>> Equipe([FromQuery] DateOnly? du, [FromQuery] DateOnly? au, CancellationToken ct)
    {
        var role = User.GetAuthRole();
        if (!PointageRoles.PeutVoirEquipe(role))
            return Forbid();
        var (d, a) = Periode(du, au);
        var result = await query.GetEquipeAsync(Viewer(), d, a, ct);
        return result is null ? NotFound(new { message = "Compte introuvable dans le Planning." }) : Ok(result);
    }

    [HttpGet("correspondances")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<IReadOnlyList<PointageCorrespondanceDto>>> Correspondances([FromQuery] string? statut, CancellationToken ct) =>
        Ok(await query.GetCorrespondancesAsync(statut, ct));

    [HttpPut("correspondances/{empCode}")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<PointageCorrespondanceDto>> UpdateCorrespondance(
        string empCode, [FromBody] PointageCorrespondanceUpdateRequest body, CancellationToken ct)
    {
        try
        {
            var result = await query.UpdateCorrespondanceAsync(empCode, body, Viewer(), ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(new { message = e.Message });
        }
    }

    [HttpPost("correspondances/valider-auto")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> ValiderAuto(CancellationToken ct) =>
        Ok(new { validees = await query.ValiderCorrespondancesAutoAsync(Viewer(), ct) });

    [HttpGet("etat")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<PointageEtatDto>> Etat(CancellationToken ct) =>
        Ok(await query.GetEtatAsync(ct));

    /// <summary>Synchronisation immédiate (Admin), sans attendre le prochain tour.</summary>
    [HttpPost("synchroniser")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Synchroniser(CancellationToken ct)
    {
        try
        {
            return Ok(new { ajoutes = await sync.SynchroniserAsync(ct) });
        }
        catch (InvalidOperationException e)
        {
            return BadRequest(new { message = e.Message });
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = $"ZKBio Time injoignable : {e.Message}" });
        }
    }

    private PointageViewer Viewer() =>
        new(User.GetAuthUserId(), User.GetSubjectId(), User.GetEmail(), User.GetAuthRole());

    /// <summary>Par défaut : du 1er du mois au jour même.</summary>
    private static (DateOnly Du, DateOnly Au) Periode(DateOnly? du, DateOnly? au)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var fin = au ?? today;
        var debut = du ?? new DateOnly(fin.Year, fin.Month, 1);
        return (debut, fin);
    }
}

/// <summary>Rôles autorisés à consulter les pointages d'une équipe.</summary>
public static class PointageRoles
{
    public static bool PeutVoirEquipe(string? role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase)
        || Kyntus.Messaging.Contracts.KyntusRoleNames.IsSuperviseur(role)
        || Kyntus.Messaging.Contracts.KyntusRoleNames.IsReferentTechnique(role)
        || Kyntus.Messaging.Contracts.KyntusRoleNames.IsChefDeProjet(role)
        || Kyntus.Messaging.Contracts.KyntusRoleNames.IsSupportManager(role);
}
