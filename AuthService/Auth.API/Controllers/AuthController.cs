using System.Security.Claims;
using Auth.Application.Commands.AdminResetPassword;
using Auth.Application.Commands.ChangeMyPassword;
using Auth.Application.Commands.DeleteFromPlanning;
using Auth.Application.Commands.Login;
using Auth.Application.Commands.Logout;
using Auth.Application.Commands.RefreshToken;
using Auth.Application.Commands.RegisterFromPlanning;
using Auth.Application.Commands.RegisterFromPlanningBatch;
using Auth.Application.Commands.SetActiveFromPlanning;
using Auth.Application.DTOs;
using Auth.Application.Queries.AccessAudit;
using Auth.Application.Queries.CheckEmail;
using Auth.Application.Queries.CheckUsername;
using Auth.API.Filters;
using Auth.Domain.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Auth.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<AuthController> _logger;

    /// <summary>v60 — Claim signée du compte Super Admin.</summary>
    private const string SuperAdminClaim = "kyntus_super_admin";

    /// <summary>
    /// v60 — Posé par Planning (appel interne authentifié par clé) quand l'utilisateur à l'origine de
    /// l'action est le Super Admin. Sans lui, le compte Super Admin ne peut être ni modifié (rôle,
    /// mot de passe), ni désactivé, ni supprimé : un administrateur ne peut pas s'en emparer.
    /// </summary>
    public const string ActingSuperAdminHeader = "X-Kyntus-Acting-Super-Admin";

    private bool ActionParSuperAdmin =>
        string.Equals(Request.Headers[ActingSuperAdminHeader].ToString(), "true", StringComparison.OrdinalIgnoreCase);

    private ObjectResult CompteSuperAdminProtege() =>
        StatusCode(StatusCodes.Status403Forbidden, new
        {
            code = "SUPER_ADMIN_PROTECTED",
            message = "Compte Super Admin : seul le Super Admin peut le modifier, le désactiver ou le supprimer.",
        });

    private static async Task<bool> CibleSuperAdminAsync(
        IUserRepository users, string? email, Guid? employeeId, int? authUserId, CancellationToken ct)
    {
        if (authUserId is > 0 && (await users.GetByIdAsync(authUserId.Value, ct))?.EstSuperAdminEffectif == true)
            return true;
        if (!string.IsNullOrWhiteSpace(email) && (await users.GetByEmailAsync(email.Trim(), ct))?.EstSuperAdminEffectif == true)
            return true;
        return employeeId is { } g && g != Guid.Empty
               && (await users.GetBySubjectIdAsync(g, ct))?.EstSuperAdminEffectif == true;
    }

    public AuthController(IMediator mediator, ILogger<AuthController> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public IActionResult Register()
    {
        return StatusCode(StatusCodes.Status403Forbidden, new
        {
            message = "L'inscription libre est désactivée. Les comptes sont créés par la RH.",
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> Login([FromBody] LoginDto loginDto)
    {
        try
        {
            var response = await _mediator.Send(new LoginCommand(loginDto));
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Tentative de connexion échouée pour {Email}", loginDto.Email);
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur serveur lors de la connexion");
            return StatusCode(500, new { message = "Une erreur est survenue lors de la connexion" });
        }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenDto refreshTokenDto)
    {
        try
        {
            var response = await _mediator.Send(new RefreshTokenCommand(refreshTokenDto.RefreshToken));
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Tentative de rafraîchissement avec un token invalide");
            return Unauthorized(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur serveur lors du rafraîchissement du token");
            return StatusCode(500, new { message = "Une erreur est survenue lors du rafraîchissement" });
        }
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var sub = User.FindFirstValue("sub")
                  ?? User.FindAll(ClaimTypes.NameIdentifier)
                      .Select(c => c.Value)
                      .FirstOrDefault(v => v.Contains('-', StringComparison.Ordinal));
        var email = User.FindFirstValue(ClaimTypes.Email)
                    ?? User.FindFirstValue("email")
                    ?? "";
        var role = User.FindFirstValue(ClaimTypes.Role)
                   ?? User.FindFirstValue("role")
                   ?? "Employee";
        var authUserId = User.FindAll(ClaimTypes.NameIdentifier)
                             .Select(c => c.Value)
                             .FirstOrDefault(v => int.TryParse(v, out _))
                         ?? User.FindFirstValue("nameid");
        if (!Guid.TryParse(sub, out var subjectId) || subjectId == Guid.Empty)
            return Unauthorized(new { message = "Claim sub manquant." });

        return Ok(new AuthMeDto
        {
            SubjectId = subjectId,
            AuthUserId = int.TryParse(authUserId, out var id) ? id : 0,
            Email = email,
            Role = role,
            TenantId = "atlas-tech-demo",
            IsSuperAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
                           && string.Equals(User.FindFirstValue(SuperAdminClaim), "true", StringComparison.OrdinalIgnoreCase),
        });
    }

    /// <summary>
    /// Rôle actuel en base (et non celui du jeton). L'application le consulte régulièrement :
    /// quand un Admin change le rôle d'un compte, la session se met à jour sans reconnexion.
    /// </summary>
    [HttpGet("me/role")]
    [Authorize]
    public async Task<IActionResult> RoleActuel([FromServices] IUserRepository users, CancellationToken ct)
    {
        var sub = User.FindFirstValue("sub")
                  ?? User.FindAll(ClaimTypes.NameIdentifier)
                      .Select(c => c.Value)
                      .FirstOrDefault(v => v.Contains('-', StringComparison.Ordinal));
        var user = Guid.TryParse(sub, out var subjectId) && subjectId != Guid.Empty
            ? await users.GetBySubjectIdAsync(subjectId, ct)
            : null;
        if (user is null)
        {
            var authUserId = User.FindAll(ClaimTypes.NameIdentifier)
                                 .Select(c => c.Value)
                                 .FirstOrDefault(v => int.TryParse(v, out _))
                             ?? User.FindFirstValue("nameid");
            if (int.TryParse(authUserId, out var id))
                user = await users.GetByIdAsync(id, ct);
        }
        if (user is null)
            return NotFound(new { message = "Compte introuvable." });

        return Ok(new { role = user.Role?.Name ?? "User", isActive = user.IsActive, isSuperAdmin = user.EstSuperAdminEffectif });
    }

    [HttpGet("audit/access")]
    [Authorize(Roles = "Admin,Audit")]
    public async Task<IActionResult> AccessAudit([FromQuery] int take = 200, CancellationToken ct = default)
    {
        var rows = await _mediator.Send(new GetAccessAuditQuery(take), ct);
        return Ok(rows);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenDto refreshTokenDto)
    {
        try
        {
            var result = await _mediator.Send(new LogoutCommand(refreshTokenDto.RefreshToken));
            if (result)
                return Ok(new { message = "Déconnexion réussie" });
            return BadRequest(new { message = "Échec de la déconnexion" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur serveur lors de la déconnexion");
            return StatusCode(500, new { message = "Une erreur est survenue lors de la déconnexion" });
        }
    }

    [HttpGet("check-email")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckEmail([FromQuery] string email)
    {
        try
        {
            var exists = await _mediator.Send(new CheckEmailQuery(email));
            return Ok(new { exists });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la vérification de l'email");
            return StatusCode(500, new { message = "Erreur lors de la vérification" });
        }
    }

    [HttpGet("check-username")]
    [AllowAnonymous]
    public async Task<IActionResult> CheckUsername([FromQuery] string username)
    {
        try
        {
            var exists = await _mediator.Send(new CheckUsernameQuery(username));
            return Ok(new { exists });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur lors de la vérification du username");
            return StatusCode(500, new { message = "Erreur lors de la vérification" });
        }
    }

    [HttpPost("register-from-planning")]
    [RequireInternalService]
    public async Task<IActionResult> RegisterFromPlanning(
        [FromBody] RegisterFromPlanningDto dto,
        [FromServices] IUserRepository users,
        CancellationToken ct)
    {
        try
        {
            // v60 — Compte Super Admin : ni rôle ni mot de passe modifiés par un autre que lui.
            if (!ActionParSuperAdmin && await CibleSuperAdminAsync(users, dto.Email, dto.EmployeeId, null, ct))
            {
                var protege = await users.GetByEmailAsync(dto.Email.Trim(), ct)
                              ?? (dto.EmployeeId != Guid.Empty ? await users.GetBySubjectIdAsync(dto.EmployeeId, ct) : null);
                _logger.LogWarning("Compte Super Admin protégé : synchronisation Planning ignorée pour {Email}.", dto.Email);
                if (protege is not null)
                    return Ok(new RegisterFromPlanningResponseDto { Id = protege.Id, Email = protege.Email, SubjectId = protege.SubjectId });
                return CompteSuperAdminProtege();
            }
            var response = await _mediator.Send(new RegisterFromPlanningCommand(dto));
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Mot de passe invalide (RoleName={RoleName})", dto.RoleName);
            return BadRequest(new { message = ex.Message });
        }
        catch (RegisterFromPlanningRoleNotFoundException)
        {
            _logger.LogWarning("⚠️ Rôle introuvable (RoleName={RoleName}, RoleId={RoleId})", dto.RoleName, dto.RoleId);
            return BadRequest(new { message = "Rôle introuvable" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Erreur création user depuis Planning");
            return StatusCode(500, new { message = "Erreur serveur" });
        }
    }

    [HttpPost("register-from-planning-batch")]
    [RequireInternalService]
    public async Task<IActionResult> RegisterFromPlanningBatch(
        [FromBody] RegisterFromPlanningBatchDto dto,
        [FromServices] IUserRepository users,
        CancellationToken ct)
    {
        try
        {
            if (dto.Items is null || dto.Items.Count == 0)
                return BadRequest(new { message = "items requis." });

            // v60 — Le compte Super Admin n'est jamais modifié par un import ou une synchronisation.
            var proteges = new List<RegisterFromPlanningBatchItemResultDto>();
            if (!ActionParSuperAdmin)
            {
                foreach (var item in dto.Items.ToList())
                {
                    if (!await CibleSuperAdminAsync(users, item.Email, item.EmployeeId, null, ct))
                        continue;
                    dto.Items.Remove(item);
                    proteges.Add(new RegisterFromPlanningBatchItemResultDto
                    {
                        Email = item.Email,
                        Success = false,
                        Message = "Compte Super Admin protégé : non modifié.",
                    });
                }
                if (dto.Items.Count == 0)
                    return Ok(proteges);
            }

            var response = await _mediator.Send(new RegisterFromPlanningBatchCommand(dto));
            return Ok(proteges.Count == 0 ? response : response.Concat(proteges).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur création batch users depuis Planning");
            return StatusCode(500, new { message = "Erreur serveur" });
        }
    }

    /// <summary>Active / désactive un compte (sortie d'un collaborateur) sans le supprimer.</summary>
    [HttpPut("users/from-planning/{authUserId:int}/active")]
    [RequireInternalService]
    public async Task<IActionResult> SetActiveFromPlanning(
        int authUserId,
        [FromBody] SetActiveFromPlanningRequest body,
        [FromServices] IUserRepository users,
        CancellationToken ct)
    {
        if (!body.IsActive && !ActionParSuperAdmin && await CibleSuperAdminAsync(users, null, null, authUserId, ct))
            return CompteSuperAdminProtege();
        var found = await _mediator.Send(new SetActiveFromPlanningCommand(authUserId, body.IsActive));
        if (!found)
            return NotFound(new { message = $"Utilisateur Auth {authUserId} introuvable." });
        _logger.LogInformation("Compte Auth {Id} {Etat} depuis Planning.", authUserId, body.IsActive ? "réactivé" : "désactivé");
        return NoContent();
    }

    /// <summary>v60 — Le compte est-il le Super Admin ? (Planning protège ce compte dans ses écrans.)</summary>
    [HttpGet("users/from-planning/{authUserId:int}/super-admin")]
    [RequireInternalService]
    public async Task<IActionResult> EstSuperAdmin(int authUserId, [FromServices] IUserRepository users, CancellationToken ct) =>
        Ok(new { superAdmin = (await users.GetByIdAsync(authUserId, ct))?.EstSuperAdminEffectif == true });

    [HttpDelete("users/from-planning/{authUserId:int}")]
    [RequireInternalService]
    public async Task<IActionResult> DeleteFromPlanning(
        int authUserId,
        [FromServices] IUserRepository users,
        CancellationToken ct)
    {
        if (!ActionParSuperAdmin && await CibleSuperAdminAsync(users, null, null, authUserId, ct))
            return CompteSuperAdminProtege();
        try
        {
            var deleted = await _mediator.Send(new DeleteFromPlanningUserCommand(authUserId));
            if (!deleted)
                return NotFound(new { message = $"Utilisateur Auth {authUserId} introuvable." });

            _logger.LogInformation("Utilisateur Auth {Id} supprimé depuis Planning.", authUserId);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur suppression user Auth depuis Planning");
            return StatusCode(500, new { message = "Erreur serveur" });
        }
    }

    /// <summary>
    /// v56 — Chacun change son propre mot de passe depuis l'application (l'ancien est exigé).
    /// Les sessions ouvertes ailleurs sont coupées : il faut se reconnecter avec le nouveau.
    /// </summary>
    [HttpPost("me/password")]
    [Authorize]
    [EnableRateLimiting("password")]
    public async Task<IActionResult> ChangerMonMotDePasse([FromBody] ChangeMyPasswordDto dto)
    {
        var sub = User.FindFirstValue("sub")
                  ?? User.FindAll(ClaimTypes.NameIdentifier)
                      .Select(c => c.Value)
                      .FirstOrDefault(v => v.Contains('-', StringComparison.Ordinal));
        var email = User.FindFirstValue(ClaimTypes.Email) ?? User.FindFirstValue("email");
        Guid.TryParse(sub, out var subjectId);

        try
        {
            var r = await _mediator.Send(new ChangeMyPasswordCommand(
                subjectId, email, dto?.AncienMotDePasse ?? "", dto?.NouveauMotDePasse ?? ""));
            return Ok(new { message = "Mot de passe modifié. Reconnectez-vous avec le nouveau.", email = r.Email });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur changement de mot de passe");
            return StatusCode(500, new { message = "Erreur serveur" });
        }
    }

    [HttpPost("admin/reset-password")]
    [RequireInternalService]
    public async Task<IActionResult> AdminResetPassword(
        [FromBody] AdminResetPasswordDto dto,
        [FromServices] IUserRepository users,
        CancellationToken ct)
    {
        if (!ActionParSuperAdmin && await CibleSuperAdminAsync(users, dto.Email, dto.EmployeeId, dto.AuthUserId, ct))
            return CompteSuperAdminProtege();
        try
        {
            var r = await _mediator.Send(new AdminResetPasswordCommand(dto));
            return Ok(new { message = "Mot de passe réinitialisé.", id = r.Id, email = r.Email, subjectId = r.SubjectId, corrections = r.Corrections });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur réinitialisation mot de passe admin");
            return StatusCode(500, new { message = "Erreur serveur" });
        }
    }
}

public sealed record SetActiveFromPlanningRequest(bool IsActive);
