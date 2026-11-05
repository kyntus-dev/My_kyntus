using Kyntus.Iam;
using Kyntus.Identity.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;
using Planning.Application.Abstractions.EmployeeImport;
using Planning.Application.DTOs;
using Planning.Application.Users;

namespace Planning.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController(
    IMediator mediator,
    IUserService userService,
    IEmployeeDocumentFieldQueryService documentFieldQuery,
    IPolicyEvaluator policyEvaluator,
    IAgentReaffectation reaffectation) : ControllerBase
{
    private const string MessageReaffectation =
        "Le changement de service est réservé aux rôles autorisés (« Réaffecter les agents » dans Accès par rôle).";

    /// <summary>Droit « rh.affectations » (Accès par rôle / par compte) ; l'Admin l'a toujours.</summary>
    private async Task<bool> PeutReaffecterAsync(string verbe, CancellationToken ct)
    {
        var role = User.GetAuthRole() ?? "Employee";
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) return true;
        var decision = await policyEvaluator.EvaluateAsync(
            new PolicyRequest(User.GetSubjectId() ?? Guid.Empty, role, $"rh.affectations.{verbe}", "platform"), ct);
        return decision.Allowed;
    }

    private Task<ReaffectationAuteur> AuteurReaffectationAsync(CancellationToken ct) =>
        reaffectation.ResoudreAsync(User.GetAuthUserId(), User.GetEmail(), IsHrOrAdminConnected(), ct,
            admin: string.Equals(User.GetAuthRole(), "Admin", StringComparison.OrdinalIgnoreCase));

    /// <summary>Droit « rh.activation » (v26) : désactiver / réactiver les comptes de son périmètre.</summary>
    private async Task<bool> PeutActiverAsync(string verbe, CancellationToken ct)
    {
        var role = User.GetAuthRole() ?? "Employee";
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) return true;
        var decision = await policyEvaluator.EvaluateAsync(
            new PolicyRequest(User.GetSubjectId() ?? Guid.Empty, role, $"rh.activation.{verbe}", "platform"), ct);
        return decision.Allowed;
    }

    /// <summary>Comptes des agents du périmètre, actifs et désactivés.</summary>
    [HttpGet("reaffectations/comptes")]
    public async Task<ActionResult<IReadOnlyList<CompteAgentDto>>> ComptesPerimetre(CancellationToken ct)
    {
        if (!await PeutActiverAsync("View", ct) && !await PeutActiverAsync("Act", ct)) return Forbid();
        return Ok(await reaffectation.ComptesAsync(await AuteurReaffectationAsync(ct), ct));
    }

    /// <summary>Désactive (avec date de sortie) ou réactive le compte d'un agent du périmètre.</summary>
    [HttpPost("{id:int}/activation")]
    public async Task<ActionResult<CompteAgentDto>> ActiverCompte(int id, [FromBody] ActivationCompteRequest body, CancellationToken ct)
    {
        if (!await PeutActiverAsync("Act", ct))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "L'activation des comptes est réservée aux rôles autorisés (« Activer / désactiver les comptes » dans Accès par rôle).",
            });
        try
        {
            return Ok(await reaffectation.ActiverCompteAsync(await AuteurReaffectationAsync(ct), id, body, ct));
        }
        catch (ReaffectationRefusException ex) when (ex.HorsPerimetre)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ReaffectationRefusException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Agents et services entre lesquels le demandeur peut réaffecter (son périmètre).</summary>
    [HttpGet("reaffectations/options")]
    public async Task<ActionResult<ReaffectationOptionsDto>> ReaffectationOptions(CancellationToken ct)
    {
        if (!await PeutReaffecterAsync("View", ct) && !await PeutReaffecterAsync("Act", ct)) return Forbid();
        return Ok(await reaffectation.OptionsAsync(await AuteurReaffectationAsync(ct), ct));
    }

    /// <summary>Historique des changements de service (périmètre du demandeur).</summary>
    [HttpGet("reaffectations")]
    public async Task<ActionResult<IReadOnlyList<ReaffectationDto>>> Reaffectations(
        [FromQuery] int? userId, [FromQuery] int? max, CancellationToken ct)
    {
        if (!await PeutReaffecterAsync("View", ct) && !await PeutReaffecterAsync("Act", ct)) return Forbid();
        return Ok(await reaffectation.HistoriqueAsync(await AuteurReaffectationAsync(ct), userId, max ?? 200, ct));
    }

    /// <summary>Réaffecte un agent dans un autre service (de A vers B), avec un motif.</summary>
    [HttpPost("{id:int}/reaffectation")]
    public async Task<ActionResult<ReaffectationDto>> Reaffecter(int id, [FromBody] ReaffectationRequest body, CancellationToken ct)
    {
        if (!await PeutReaffecterAsync("Act", ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = MessageReaffectation });
        try
        {
            return Ok(await reaffectation.ReaffecterAsync(await AuteurReaffectationAsync(ct), id, body, ct));
        }
        catch (ReaffectationRefusException ex) when (ex.HorsPerimetre)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ReaffectationRefusException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private async Task<ActionResult?> DenyUnlessCanManageUsersAsync()
    {
        var role = User.GetAuthRole() ?? "Employee";
        if (IsHrOrAdmin(role))
            return null;

        var subjectId = User.GetSubjectId() ?? Guid.Empty;
        var decision = await policyEvaluator.EvaluateAsync(
            new PolicyRequest(subjectId, role, "users:manage", "users", null));
        if (!decision.Allowed)
            return Forbid();
        return null;
    }

    private static bool IsHrOrAdmin(string role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);

    private static bool IsScopedManager(string role) =>
        string.Equals(role, "Superviseur", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Coach", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "RP", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Chef de projet", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "Référent technique", StringComparison.OrdinalIgnoreCase);

    private bool IsHrOrAdminConnected() =>
        IsHrOrAdmin(User.GetAuthRole() ?? string.Empty);

    private async Task<bool> CanAccessUserAsync(int targetUserId, CancellationToken ct)
    {
        if (IsHrOrAdminConnected())
            return true;

        if (!IsScopedManager(User.GetAuthRole() ?? string.Empty))
            return false;

        var authUserId = User.GetAuthUserId();
        return authUserId is > 0
            && await userService.CanSupervisorAccessUserAsync(
                authUserId.Value,
                targetUserId,
                ct);
    }

    [HttpGet]
    public async Task<ActionResult<List<UserDto>>> GetAllUsers(CancellationToken ct)
    {
        if (!IsHrOrAdminConnected())
        {
            if (!IsScopedManager(User.GetAuthRole() ?? string.Empty))
                return Forbid();

            var authUserId = User.GetAuthUserId();
            if (authUserId is not > 0)
                return Unauthorized();

            return Ok(await userService.GetUsersForSupervisorAsync(
                authUserId.Value,
                ct));
        }

        var users = await userService.GetAllUsersAsync();
        return Ok(users);
    }

    [HttpGet("by-subservice/{subServiceId}")]
    public async Task<ActionResult<List<UserDto>>> GetBySubService(
        int subServiceId,
        CancellationToken ct)
    {
        if (!IsHrOrAdminConnected())
        {
            if (!IsScopedManager(User.GetAuthRole() ?? string.Empty))
                return Forbid();

            var authUserId = User.GetAuthUserId();
            if (authUserId is not > 0)
                return Unauthorized();

            var allowed = await userService.CanSupervisorAccessSubServiceAsync(
                authUserId.Value,
                subServiceId,
                ct);

            if (!allowed)
                return Forbid();
        }

        var users = await userService.GetUsersBySubServiceAsync(subServiceId);
        return Ok(users);
    }

    [HttpGet("managers-by-subservice/{subServiceId}")]
    public async Task<ActionResult<List<UserDto>>> GetManagersBySubService(int subServiceId, CancellationToken ct)
    {
        var users = await mediator.Send(new GetManagersBySubServiceQuery(subServiceId), ct);
        return Ok(users);
    }

    [HttpPost("sync-to-conge")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<IActionResult> SyncAllToConge()
    {
        await userService.SyncAllEmployesToCongeAsync();
        return Ok(new { message = "Synchronisation envoyée via RabbitMQ." });
    }

    [HttpPut("{id}/new-employee")]
    public async Task<IActionResult> SetNewEmployeeStatus(int id, [FromBody] SetNewEmployeeDto dto, CancellationToken ct)
    {
        var result = await mediator.Send(new SetNewEmployeeStatusCommand(id, dto), ct);
        if (result is null)
            return NotFound(new { message = "Employé introuvable." });
        return Ok(result);
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> GetMe(CancellationToken ct)
    {
        var authUserId = User.GetAuthUserId();
        var email = User.GetEmail()?.Trim();
        if (authUserId is null or <= 0 && string.IsNullOrWhiteSpace(email))
            return Unauthorized(new { message = "Authentification requise." });

        var subjectId = User.GetSubjectId();
        var user = await userService.GetOrEnsureUserForAuthAsync(
            authUserId ?? 0,
            email,
            User.GetAuthRole(),
            subjectId,
            ct);

        if (user is null)
            return NotFound(new { message = "Utilisateur introuvable." });
        return Ok(user);
    }

    [HttpGet("by-auth/{authUserId:int}")]
    public async Task<ActionResult<UserDto>> GetByAuthId(int authUserId, CancellationToken ct)
    {
        var email = User.GetEmail()?.Trim();
        var user = await userService.GetOrEnsureUserForAuthAsync(
            authUserId,
            email,
            User.GetAuthRole(),
            User.GetSubjectId(),
            ct);
        if (user == null)
            return NotFound(new { message = "Utilisateur introuvable." });
        return Ok(user);
    }

    [HttpGet("by-guid/{guid:guid}/document-field-values")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<EmployeeDocumentFieldValuesDto>> GetDocumentFieldValues(
        Guid guid,
        [FromQuery] string[]? includeKeys,
        CancellationToken ct)
    {
        var result = await documentFieldQuery.GetFieldValuesAsync(guid, includeKeys, ct);
        if (result is null)
            return NotFound(new { message = "Employé introuvable." });
        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetById(int id, CancellationToken ct)
    {
        if (!await CanAccessUserAsync(id, ct))
            return Forbid();

        var user = await userService.GetUserByIdAsync(id);
        if (user == null)
            return NotFound(new { message = $"Utilisateur {id} introuvable." });
        return Ok(user);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserDto dto)
    {
        if (await DenyUnlessCanManageUsersAsync() is { } denied) return denied;
        if (!ModelState.IsValid) return BadRequest(ModelState);
        try
        {
            var user = await userService.CreateUserAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = user.Id }, user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<UserDto>> Update(
        int id,
        [FromBody] UpdateUserDto dto,
        CancellationToken ct)
    {
        if (await DenyUnlessCanManageUsersAsync() is { } denied) return denied;
        if (!await CanAccessUserAsync(id, ct)) return Forbid();
        if (!ModelState.IsValid) return BadRequest(ModelState);
        // Changer le service depuis la fiche demande le même droit que l'écran « Réaffectations ».
        var actuel = await userService.GetUserByIdAsync(id);
        if (actuel is not null && actuel.SubServiceId != dto.SubServiceId && !await PeutReaffecterAsync("Act", ct))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = MessageReaffectation });
        try
        {
            var user = await userService.UpdateUserAsync(id, dto);
            if (user == null)
                return NotFound(new { message = $"Utilisateur {id} introuvable." });
            return Ok(user);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Ancienne « suppression » : désormais une désactivation (sortie). Rien n'est supprimé,
    /// pour garder plannings, congés, primes et historique.
    /// </summary>
    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(int id, CancellationToken ct) =>
        SetActive(id, isActive: false, dateSortie: null, ct);

    /// <summary>Sortie d'un collaborateur : compte bloqué, données conservées.</summary>
    [HttpPost("{id}/deactivate")]
    public Task<IActionResult> Deactivate(int id, [FromBody] DeactivateUserRequest? body, CancellationToken ct) =>
        SetActive(id, isActive: false, body?.DateSortie, ct);

    /// <summary>Réintégration d'un collaborateur désactivé.</summary>
    [HttpPost("{id}/reactivate")]
    public Task<IActionResult> Reactivate(int id, CancellationToken ct) =>
        SetActive(id, isActive: true, dateSortie: null, ct);

    /// <summary>Annule une création incomplète (étape suivante en échec). Refusé après 30 minutes.</summary>
    [HttpPost("{id}/rollback-creation")]
    public async Task<IActionResult> RollbackCreation(int id, CancellationToken ct)
    {
        if (await DenyUnlessCanManageUsersAsync() is { } denied) return denied;
        try
        {
            return await userService.RollbackFailedCreationAsync(id, ct)
                ? NoContent()
                : NotFound(new { message = $"Utilisateur {id} introuvable." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private async Task<IActionResult> SetActive(int id, bool isActive, DateOnly? dateSortie, CancellationToken ct)
    {
        if (await DenyUnlessCanManageUsersAsync() is { } denied) return denied;
        if (!await CanAccessUserAsync(id, ct)) return Forbid();
        if (!isActive)
        {
            var target = await userService.GetUserByIdAsync(id);
            if (target is null)
                return NotFound(new { message = $"Utilisateur {id} introuvable." });
            if (target.AuthUserId is int targetAuth && targetAuth == User.GetAuthUserId())
                return BadRequest(new { message = "Vous ne pouvez pas désactiver votre propre compte." });
        }
        var found = await userService.SetUserActiveAsync(id, isActive, dateSortie, ct);
        if (!found)
            return NotFound(new { message = $"Utilisateur {id} introuvable." });
        return NoContent();
    }

    [HttpPost("reset-passwords/by-group")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<ResetPasswordsByGroupResultDto>> ResetPasswordsByGroup(
        [FromBody] ResetPasswordsByGroupRequest request,
        CancellationToken ct)
    {
        if (await DenyUnlessCanManageUsersAsync() is { } denied) return denied;
        try
        {
            var result = await userService.ResetPasswordsByGroupAsync(request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("{id}/reset-password")]
    [Authorize(Roles = "Admin,RH")]
    public async Task<ActionResult<ResetPasswordResultDto>> ResetPassword(int id, CancellationToken ct)
    {
        if (await DenyUnlessCanManageUsersAsync() is { } denied) return denied;
        try
        {
            var result = await userService.ResetPasswordAsync(id, ct);
            if (result is null)
                return NotFound(new { message = $"Utilisateur {id} introuvable." });
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("check-email/{email}")]
    public async Task<ActionResult<bool>> CheckEmail(string email, [FromQuery] int? excludeId = null)
    {
        var isUnique = await userService.IsEmailUniqueAsync(email, excludeId);
        return Ok(new { isUnique });
    }

    [HttpPatch("{id}/contractual-level")]
    public async Task<ActionResult<UserDto>> UpdateContractualLevel(
        int id,
        [FromBody] UpdateContractualLevelDto dto,
        CancellationToken ct)
    {
        if (dto.Level is < 1 or > 3)
            return BadRequest(new { message = "Niveau contractuel invalide (1-3)." });

        var role = User.GetAuthRole() ?? "Employee";
        var subjectId = User.GetSubjectId() ?? Guid.Empty;
        var hasIdentity = subjectId != Guid.Empty
            || User.GetAuthUserId() is > 0
            || !string.IsNullOrWhiteSpace(User.GetEmail());
        if (!hasIdentity && !IsHrOrAdmin(role))
            return Unauthorized();

        try
        {
            var user = await userService.UpdateContractualLevelAsync(id, dto.Level, subjectId, role, ct);
            if (user is null)
                return NotFound(new { message = $"Utilisateur {id} introuvable." });
            return Ok(user);
        }
        catch (UnauthorizedAccessException ex)
        {
            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? "Modification du niveau non autorisée."
                : ex.Message;
            return StatusCode(StatusCodes.Status403Forbidden, new { message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}

public sealed record DeactivateUserRequest(DateOnly? DateSortie);
