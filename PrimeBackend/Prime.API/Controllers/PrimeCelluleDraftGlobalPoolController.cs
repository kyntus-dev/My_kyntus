using MediatR;
using Microsoft.AspNetCore.Mvc;
using Prime.Application;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Application.Drafts;

namespace Prime.API.Controllers;

/// <summary>Fichier Excel « pool global » rattaché à un brouillon cellule (validation RH + accusé Comptabilité).</summary>
[ApiController]
[Route("api/prime/supervisor-cellule-prime-drafts")]
[Route("api/prime/supervisor-pole-prime-drafts")]
public sealed class PrimeCelluleDraftGlobalPoolController(
    IMediator mediator,
    IPrimeCelluleDraftGlobalPoolAppService? pool) : ControllerBase
{
    [HttpGet("{draftId:guid}/global-pool")]
    public async Task<IActionResult> GetState(
        Guid draftId,
        [FromQuery] string supervisorUserId,
        CancellationToken ct)
    {
        if (pool is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new GetCelluleDraftGlobalPoolStateQuery(draftId, supervisorUserId), ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpGet("{draftId:guid}/global-pool/excel")]
    public async Task<IActionResult> DownloadExcel(
        Guid draftId,
        [FromQuery] string supervisorUserId,
        [FromQuery] string? actingUserId,
        CancellationToken ct)
    {
        if (pool is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            var file = await mediator.Send(
                new DownloadCelluleDraftGlobalPoolExcelQuery(draftId, supervisorUserId, actingUserId), ct);
            return File(file.Content, file.ContentType, file.FileName);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (PrimeApiException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPut("{draftId:guid}/global-pool/excel")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public Task<IActionResult> UploadExcel(
        Guid draftId,
        [FromQuery] string supervisorUserId,
        IFormFile file,
        CancellationToken ct) =>
        Task.FromResult<IActionResult>(StatusCode(410, new
        {
            error = "L'import manuel du fichier global est désactivé. Générez la synthèse via POST …/global-pool/generate.",
        }));

    [HttpPost("{draftId:guid}/global-pool/generate")]
    public Task<IActionResult> GenerateGlobalPoolExcel(
        Guid draftId,
        [FromQuery] string supervisorUserId,
        CancellationToken ct) =>
        Task.FromResult<IActionResult>(StatusCode(410, new
        {
            error = "Génération par brouillon superviseur désactivée. Utilisez POST /api/prime/global-pool/synthesis/generate avec period, scopeType et scopeId.",
        }));

    [HttpPost("{draftId:guid}/global-pool/generate-legacy")]
    public async Task<IActionResult> GenerateGlobalPoolExcelLegacy(
        Guid draftId,
        [FromQuery] string supervisorUserId,
        CancellationToken ct)
    {
        if (pool is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(
                new GenerateCelluleDraftGlobalPoolLegacyExcelCommand(draftId, supervisorUserId), ct));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
    }

    [HttpPost("{draftId:guid}/global-pool/approve-manager")]
    public async Task<IActionResult> ApproveManager(
        Guid draftId,
        [FromQuery] string supervisorUserId,
        [FromBody] GlobalPoolActingUserRequest body,
        CancellationToken ct)
    {
        if (pool is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(
                new ApproveCelluleDraftGlobalPoolManagerCommand(draftId, supervisorUserId, body), ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (PrimeApiException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("{draftId:guid}/global-pool/approve-rh")]
    public async Task<IActionResult> ApproveRh(
        Guid draftId,
        [FromQuery] string supervisorUserId,
        [FromBody] GlobalPoolActingUserRequest body,
        CancellationToken ct)
    {
        if (pool is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(
                new ApproveCelluleDraftGlobalPoolRhCommand(draftId, supervisorUserId, body), ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (PrimeApiException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }

    [HttpPost("{draftId:guid}/global-pool/ack-compta")]
    public async Task<IActionResult> AckCompta(
        Guid draftId,
        [FromQuery] string supervisorUserId,
        [FromBody] GlobalPoolActingUserRequest body,
        CancellationToken ct)
    {
        if (pool is null) return StatusCode(503, new { error = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(
                new AckCelluleDraftGlobalPoolComptaCommand(draftId, supervisorUserId, body), ct));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = ex.Message });
        }
        catch (PrimeApiException ex)
        {
            return StatusCode(ex.StatusCode, new { error = ex.Message });
        }
    }
}
