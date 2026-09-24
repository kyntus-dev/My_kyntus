using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Auth.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Auth.API.Controllers;

[ApiController]
[Route("api/Auth")]
public sealed class FirstLoginController(IFirstLoginService service) : ControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting("auth-login")]
    [HttpPost("complete-first-login")]
    public async Task<IActionResult> Complete(CompleteFirstLoginRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            await service.CompleteAsync(request.ChangeToken, request.NewPassword, request.ConfirmPassword, ct);
            return Ok(new { message = "Mot de passe enregistré. Connectez-vous avec votre mot de passe personnel." });
        }
        catch (UnauthorizedAccessException ex) { return Unauthorized(new { message = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Compte modifié. Reconnectez-vous." }); }
    }

    [Authorize(Roles = "Admin,RH")]
    [HttpPost("first-login/enroll")]
    public async Task<IActionResult> Enroll(EnrollFirstLoginRequest request, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var actor = User.FindFirstValue("sub") ?? "unknown";
            return Ok(await service.EnrollAsync(request.UserIds, request.DryRun, request.RenewExpired, actor, ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { message = "Comptes modifiés. Relancez la prévisualisation." }); }
    }

    // OnTokenValidated already checks current DB state. Called by the other backends.
    [Authorize]
    [HttpGet("session/validate")]
    public IActionResult ValidateSession()
    {
        Response.Headers.CacheControl = "no-store";
        return NoContent();
    }
}

public sealed class CompleteFirstLoginRequest
{
    [Required, StringLength(64, MinimumLength = 64)] public string ChangeToken { get; set; } = "";
    [Required, StringLength(64, MinimumLength = 12)] public string NewPassword { get; set; } = "";
    [Required, StringLength(64, MinimumLength = 12)] public string ConfirmPassword { get; set; } = "";
}

public sealed class EnrollFirstLoginRequest
{
    [Required, MinLength(1), MaxLength(500)] public int[] UserIds { get; set; } = [];
    public bool DryRun { get; set; } = true;
    public bool RenewExpired { get; set; }
}
