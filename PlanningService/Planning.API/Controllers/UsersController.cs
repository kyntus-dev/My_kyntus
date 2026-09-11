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
    IPolicyEvaluator policyEvaluator) : ControllerBase
{
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

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (await DenyUnlessCanManageUsersAsync() is { } denied) return denied;
        if (!await CanAccessUserAsync(id, ct)) return Forbid();
        var result = await userService.DeleteUserAsync(id);
        if (!result)
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
