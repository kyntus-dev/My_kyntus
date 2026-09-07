using Formation.Application.DTOs;
using Formation.Infrastructure.Services;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Formation.API.Controllers;

[ApiController]
[Route("api/formations/initial-paths")]
public sealed class InitialTrainingPathsController(TrainingWorkflowService training) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<InitialTrainingPathDto>> Create([FromBody] CreateInitialTrainingPathRequest body, CancellationToken ct) =>
        Ok(await training.CreateInitialPathAsync(body, ct));

    [HttpGet("formateur")]
    public Task<IReadOnlyList<InitialTrainingPathDto>> FormateurQueue(CancellationToken ct) =>
        training.ListInitialForFormateurAsync(ct);

    [HttpGet("rh-pending")]
    public Task<IReadOnlyList<InitialTrainingPathDto>> RhPending(CancellationToken ct) =>
        training.ListInitialPendingRhAsync(ct);

    [HttpGet("overview")]
    public Task<IReadOnlyList<InitialTrainingPathDto>> Overview(CancellationToken ct) =>
        training.ListInitialOverviewAsync(ct);

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<InitialTrainingPathDto>>> Me(CancellationToken ct)
    {
        var employeeId = User.GetSubjectId() ?? Guid.Empty;
        var email = User.GetEmail();
        if (employeeId == Guid.Empty && string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Identifiant utilisateur manquant dans le jeton." });
        return Ok(await training.ListInitialByEmployeeAsync(employeeId, ct, email));
    }

    [HttpGet("by-employee/{employeeId:guid}")]
    public Task<IReadOnlyList<InitialTrainingPathDto>> ByEmployee(Guid employeeId, CancellationToken ct) =>
        training.ListInitialByEmployeeAsync(employeeId, ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<InitialTrainingPathDto>> GetById(Guid id, CancellationToken ct)
    {
        var path = await training.GetInitialPathAsync(id, ct);
        return path is null ? NotFound() : Ok(path);
    }

    [HttpPost("{id:guid}/quiz-result")]
    [Authorize(Policy = "CanRecordInitialQuiz")]
    public async Task<ActionResult<InitialTrainingPathDto>> QuizResult(
        Guid id,
        [FromBody] RecordInitialQuizRequest body,
        CancellationToken ct)
    {
        var updated = await training.RecordQuizAsync(id, body, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("{id:guid}/quiz-results")]
    [Authorize(Policy = "CanRecordInitialQuiz")]
    public async Task<ActionResult<InitialTrainingPathDto>> AddQuizResult(
        Guid id,
        [FromBody] AddInitialQuizResultRequest body,
        CancellationToken ct)
    {
        try
        {
            var updated = await training.AddQuizResultAsync(id, body, ct);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id:guid}/quiz-results/{resultId:guid}")]
    [Authorize(Policy = "CanRecordInitialQuiz")]
    public async Task<ActionResult<InitialTrainingPathDto>> DeleteQuizResult(
        Guid id,
        Guid resultId,
        CancellationToken ct)
    {
        var updated = await training.DeleteQuizResultAsync(id, resultId, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("{id:guid}/formateur-validate")]
    [Authorize]
    public async Task<ActionResult<InitialTrainingPathDto>> FormateurValidate(
        Guid id,
        [FromBody] FormateurValidateOrgRequest? body,
        CancellationToken ct)
    {
        try
        {
            var updated = await training.FormateurValidateAsync(id, User.GetSubjectId(), body, ct);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/formateur-reject")]
    [Authorize]
    public async Task<ActionResult<InitialTrainingPathDto>> FormateurReject(
        Guid id,
        [FromBody] RejectInitialTrainingRequest body,
        CancellationToken ct)
    {
        PreferJwtSubjectAsRejectedBy(body);
        var updated = await training.FormateurRejectAsync(id, body, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpPost("{id:guid}/extend")]
    public async Task<ActionResult<InitialTrainingPathDto>> Extend(
        Guid id,
        [FromBody] ExtendInitialTrainingRequest body,
        CancellationToken ct)
    {
        try
        {
            var updated = await training.ExtendInitialAsync(id, body, ct);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/rh-validate")]
    [Authorize]
    public async Task<ActionResult<InitialTrainingPathDto>> RhValidate(Guid id, CancellationToken ct)
    {
        try
        {
            var updated = await training.RhValidateAsync(id, User.GetSubjectId(), ct);
            return updated is null ? NotFound() : Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id:guid}/rh-reject")]
    [Authorize]
    public async Task<ActionResult<InitialTrainingPathDto>> RhReject(
        Guid id,
        [FromBody] RejectInitialTrainingRequest body,
        CancellationToken ct)
    {
        PreferJwtSubjectAsRejectedBy(body);
        var updated = await training.RhRejectAsync(id, body, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    private void PreferJwtSubjectAsRejectedBy(RejectInitialTrainingRequest body)
    {
        var subjectId = User.GetSubjectId();
        if (subjectId is Guid g && g != Guid.Empty)
            body.RejectedBy = g.ToString();
    }

    [HttpGet("{id:guid}/checklist")]
    public async Task<ActionResult<IReadOnlyList<FormationDocumentChecklistItemDto>>> Checklist(
        Guid id,
        CancellationToken ct,
        [FromServices] FormationDocumentChecklistService checklist)
    {
        var rows = await checklist.GetChecklistForPathAsync(id, ct);
        return rows is null ? NotFound() : Ok(rows);
    }

    [HttpPatch("{id:guid}/checklist/{itemId:guid}")]
    public async Task<ActionResult<FormationDocumentChecklistItemDto>> UpdateChecklistItem(
        Guid id,
        Guid itemId,
        [FromBody] UpdateChecklistItemRequest body,
        CancellationToken ct,
        [FromServices] FormationDocumentChecklistService checklist)
    {
        var updated = await checklist.UpdateChecklistItemAsync(id, itemId, body, ct);
        return updated is null ? NotFound() : Ok(updated);
    }

    [HttpGet("by-employee/{employeeId:guid}/checklist")]
    public async Task<ActionResult<IReadOnlyList<FormationDocumentChecklistItemDto>>> ChecklistByEmployee(
        Guid employeeId,
        CancellationToken ct,
        [FromServices] FormationDocumentChecklistService checklist)
    {
        var rows = await checklist.GetChecklistForEmployeeAsync(employeeId, ct);
        return Ok(rows ?? Array.Empty<FormationDocumentChecklistItemDto>());
    }
}
