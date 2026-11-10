using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;

namespace Planning.API.Controllers;

/// <summary>
/// Notifications par e-mail : préférence de l'utilisateur connecté et état de la file d'envoi
/// (Admin / RH). Route sous /api/planning/… : déjà publiée par la passerelle.
/// </summary>
[ApiController]
[Authorize]
[Route("api/planning/notification-email")]
public sealed class NotificationEmailController(INotificationEmailSettingsService settings) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<MyNotificationEmailPreferenceDto>> GetMine(CancellationToken ct)
    {
        var dto = await settings.GetMineAsync(Viewer(), ct);
        return dto is null ? NotFound(new { message = "Compte Planning introuvable." }) : Ok(dto);
    }

    [HttpPut("me")]
    public async Task<ActionResult<MyNotificationEmailPreferenceDto>> SetMine(
        [FromBody] UpdateNotificationEmailPreferenceRequest body,
        CancellationToken ct)
    {
        var dto = await settings.SetMineAsync(Viewer(), body.EmailEnabled, ct);
        return dto is null ? NotFound(new { message = "Compte Planning introuvable." }) : Ok(dto);
    }

    [HttpGet("status")]
    public async Task<ActionResult<NotificationEmailStatusDto>> Status(CancellationToken ct)
    {
        if (!IsAdminOrRh()) return Forbid();
        return Ok(await settings.GetStatusAsync(ct));
    }

    [HttpPost("retry-failed")]
    public async Task<IActionResult> RetryFailed(CancellationToken ct)
    {
        if (!IsAdminOrRh()) return Forbid();
        return Ok(new { requeued = await settings.RetryFailedAsync(ct) });
    }

    private NotificationEmailViewer Viewer() =>
        new(User.GetAuthUserId(), User.GetSubjectId(), User.GetEmail());

    private bool IsAdminOrRh()
    {
        var role = User.GetAuthRole();
        return string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
               || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);
    }
}
