using System.Security.Claims;
using Auth.API.Filters;
using Auth.Application.Commands.Push;
using Auth.Application.DTOs;
using Auth.Application.Queries.Push;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auth.API.Controllers;

[ApiController]
[Route("api/Auth/push")]
public sealed class PushController(IMediator mediator) : ControllerBase
{
    [HttpGet("vapid-public-key")]
    [Authorize]
    public async Task<IActionResult> VapidPublicKey(CancellationToken ct)
    {
        var dto = await mediator.Send(new GetVapidPublicKeyQuery(), ct);
        if (string.IsNullOrWhiteSpace(dto.PublicKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Notifications bureau non configurées." });
        return Ok(dto);
    }

    [HttpPost("subscriptions")]
    [Authorize]
    public async Task<IActionResult> Register([FromBody] PushSubscriptionRequestDto dto, CancellationToken ct)
    {
        if (!TryGetAuthUserId(out var authUserId))
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(dto.Endpoint) || string.IsNullOrWhiteSpace(dto.P256dh) || string.IsNullOrWhiteSpace(dto.Auth))
            return BadRequest(new { message = "Abonnement push incomplet." });

        await mediator.Send(new RegisterPushSubscriptionCommand(authUserId, dto), ct);
        return NoContent();
    }

    [HttpDelete("subscriptions")]
    [Authorize]
    public async Task<IActionResult> Unregister([FromBody] UnregisterPushSubscriptionDto dto, CancellationToken ct)
    {
        if (!TryGetAuthUserId(out var authUserId))
            return Unauthorized();
        if (string.IsNullOrWhiteSpace(dto.Endpoint))
            return BadRequest(new { message = "Endpoint requis." });

        await mediator.Send(new UnregisterPushSubscriptionCommand(authUserId, dto.Endpoint), ct);
        return NoContent();
    }

    [HttpGet("inbox")]
    [Authorize]
    public async Task<IActionResult> Inbox([FromQuery] int take = 30, CancellationToken ct = default)
    {
        if (!TryGetAuthUserId(out var authUserId))
            return Unauthorized();
        var rows = await mediator.Send(new GetPushInboxQuery(authUserId, take), ct);
        return Ok(rows);
    }

    [HttpPost("inbox/{id:guid}/read")]
    [Authorize]
    public async Task<IActionResult> MarkInboxRead(Guid id, CancellationToken ct)
    {
        if (!TryGetAuthUserId(out var authUserId))
            return Unauthorized();
        await mediator.Send(new MarkPushInboxReadCommand(authUserId, id), ct);
        return NoContent();
    }

    [HttpPost("notify")]
    [AllowAnonymous]
    [RequireInternalService]
    public async Task<IActionResult> Notify([FromBody] DesktopPushNotifyDto dto, CancellationToken ct)
    {
        await mediator.Send(new SendDesktopPushCommand(dto), ct);
        return Accepted();
    }

    private bool TryGetAuthUserId(out int authUserId)
    {
        foreach (var claim in User.FindAll(ClaimTypes.NameIdentifier))
        {
            if (int.TryParse(claim.Value, out authUserId) && authUserId > 0)
                return true;
        }

        var raw = User.FindFirstValue("nameid");
        if (int.TryParse(raw, out authUserId) && authUserId > 0)
            return true;

        authUserId = 0;
        return false;
    }
}
