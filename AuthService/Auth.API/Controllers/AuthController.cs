using System.Security.Claims;
using Auth.Application.Security;
using Auth.Application.Commands.AdminResetPassword;
using Auth.Application.Commands.DeleteFromPlanning;
using Auth.Application.Commands.Login;
using Auth.Application.Commands.Logout;
using Auth.Application.Commands.RefreshToken;
using Auth.Application.Commands.RegisterFromPlanning;
using Auth.Application.Commands.RegisterFromPlanningBatch;
using Auth.Application.DTOs;
using Auth.Application.Queries.AccessAudit;
using Auth.Application.Queries.CheckEmail;
using Auth.Application.Queries.CheckUsername;
using Auth.API.Filters;
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
        catch (PasswordChangeRequiredException ex)
        {
            Response.Headers.CacheControl = "no-store";
            return StatusCode(403, new
            {
                code = "PASSWORD_CHANGE_REQUIRED",
                changeToken = ex.Challenge.ChangeToken,
                expiresIn = ex.Challenge.ExpiresIn,
                message = ex.Message,
            });
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
        });
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
    public async Task<IActionResult> RegisterFromPlanning([FromBody] RegisterFromPlanningDto dto)
    {
        try
        {
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
    public async Task<IActionResult> RegisterFromPlanningBatch([FromBody] RegisterFromPlanningBatchDto dto)
    {
        try
        {
            if (dto.Items is null || dto.Items.Count == 0)
                return BadRequest(new { message = "items requis." });

            var response = await _mediator.Send(new RegisterFromPlanningBatchCommand(dto));
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur création batch users depuis Planning");
            return StatusCode(500, new { message = "Erreur serveur" });
        }
    }

    [HttpDelete("users/from-planning/{authUserId:int}")]
    [RequireInternalService]
    public async Task<IActionResult> DeleteFromPlanning(int authUserId)
    {
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

    [HttpPost("admin/reset-password")]
    [RequireInternalService]
    public async Task<IActionResult> AdminResetPassword([FromBody] AdminResetPasswordDto dto)
    {
        try
        {
            await _mediator.Send(new AdminResetPasswordCommand(dto));
            return Ok(new { message = "Mot de passe réinitialisé." });
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

