using Formation.Application.DTOs;
using Formation.Domain.Enums;
using Formation.Infrastructure.Services;
using Kyntus.Identity.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Formation.API.Controllers;

[ApiController]
[Route("api/formations/catalog")]
[Authorize]
public sealed class TrainingCatalogController(
    LearningCatalogService catalog,
    TrainingWorkflowService training,
    IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TrainingCatalogItemDto>>> List(
        [FromQuery] string? category,
        [FromQuery] bool includeArchived = false,
        CancellationToken ct = default) =>
        Ok(await catalog.ListAsync(category, includeArchived, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TrainingCatalogItemDto>> Get(Guid id, CancellationToken ct)
    {
        var item = await catalog.GetAsync(id, ct);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpPost]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingCatalogItemDto>> Create(
        [FromBody] UpsertTrainingCatalogItemRequest body,
        CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(body.CreatedByUserId))
                body.CreatedByUserId = User.GetSubjectId()?.ToString() ?? "";
            return Ok(await catalog.CreateAsync(body, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingCatalogItemDto>> Update(
        Guid id,
        [FromBody] UpsertTrainingCatalogItemRequest body,
        CancellationToken ct)
    {
        try { return Ok(await catalog.UpdateAsync(id, body, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{id:guid}/publish")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingCatalogItemDto>> Publish(Guid id, CancellationToken ct)
    {
        try { return Ok(await catalog.PublishAsync(id, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingCatalogItemDto>> Archive(Guid id, CancellationToken ct)
    {
        try { return Ok(await catalog.ArchiveAsync(id, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            await catalog.DeleteAsync(id, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("{id:guid}/audience")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingCatalogAudienceDto>> UpsertAudience(
        Guid id,
        [FromBody] UpsertTrainingCatalogAudienceRequest body,
        CancellationToken ct)
    {
        try { return Ok(await catalog.UpsertAudienceAsync(id, body, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("{id:guid}/structure")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<ReplaceCatalogStructureResponse>> ReplaceStructure(
        Guid id,
        [FromBody] ReplaceCatalogStructureRequest body,
        CancellationToken ct)
    {
        try { return Ok(await catalog.ReplaceStructureAsync(id, body, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{id:guid}/modules")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingModuleDto>> CreateModule(
        Guid id,
        [FromBody] UpsertTrainingModuleRequest body,
        CancellationToken ct)
    {
        try { return Ok(await catalog.UpsertModuleAsync(id, null, body, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("{id:guid}/modules/{moduleId:guid}")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingModuleDto>> UpdateModule(
        Guid id,
        Guid moduleId,
        [FromBody] UpsertTrainingModuleRequest body,
        CancellationToken ct)
    {
        try { return Ok(await catalog.UpsertModuleAsync(id, moduleId, body, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("{id:guid}/modules/{moduleId:guid}")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<IActionResult> DeleteModule(Guid id, Guid moduleId, CancellationToken ct)
    {
        try
        {
            await catalog.DeleteModuleAsync(id, moduleId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("modules/{moduleId:guid}/lessons")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingLessonDto>> CreateLesson(
        Guid moduleId,
        [FromBody] UpsertTrainingLessonRequest body,
        CancellationToken ct)
    {
        try { return Ok(await catalog.UpsertLessonAsync(moduleId, null, body, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("modules/{moduleId:guid}/lessons/{lessonId:guid}")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingLessonDto>> UpdateLesson(
        Guid moduleId,
        Guid lessonId,
        [FromBody] UpsertTrainingLessonRequest body,
        CancellationToken ct)
    {
        try { return Ok(await catalog.UpsertLessonAsync(moduleId, lessonId, body, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("modules/{moduleId:guid}/lessons/{lessonId:guid}")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<IActionResult> DeleteLesson(Guid moduleId, Guid lessonId, CancellationToken ct)
    {
        try
        {
            await catalog.DeleteLessonAsync(moduleId, lessonId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("lessons/{lessonId:guid}/resources")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingResourceDto>> CreateResource(
        Guid lessonId,
        [FromBody] UpsertTrainingResourceRequest body,
        CancellationToken ct)
    {
        try { return Ok(await catalog.UpsertResourceAsync(lessonId, null, body, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("lessons/{lessonId:guid}/resources/{resourceId:guid}")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingResourceDto>> UpdateResource(
        Guid lessonId,
        Guid resourceId,
        [FromBody] UpsertTrainingResourceRequest body,
        CancellationToken ct)
    {
        try { return Ok(await catalog.UpsertResourceAsync(lessonId, resourceId, body, ct)); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("lessons/{lessonId:guid}/resources/upload")]
    [Authorize(Policy = "CanPlanContinue")]
    [RequestSizeLimit(500_000_000)]
    public async Task<ActionResult<TrainingResourceDto>> UploadResource(
        Guid lessonId,
        IFormFile file,
        [FromForm] string? title,
        [FromForm] TrainingResourceType? type,
        [FromForm] int? sortOrder,
        CancellationToken ct)
    {
        try
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { error = "Fichier manquant." });
            var inferred = type ?? InferType(file.FileName, file.ContentType);
            await using var stream = file.OpenReadStream();
            return Ok(await catalog.UploadResourceFileAsync(
                lessonId,
                file.FileName,
                file.ContentType ?? "application/octet-stream",
                stream,
                configuration["Formation:Learning:RootPath"],
                inferred,
                title,
                ct,
                sortOrder));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("lessons/{lessonId:guid}/resources/{resourceId:guid}")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<IActionResult> DeleteResource(Guid lessonId, Guid resourceId, CancellationToken ct)
    {
        try
        {
            await catalog.DeleteResourceAsync(lessonId, resourceId, ct);
            return NoContent();
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("resources/{resourceId:guid}/access")]
    public async Task<ActionResult<ResourceAccessTokenDto>> IssueResourceAccess(
        Guid resourceId,
        CancellationToken ct)
    {
        var info = await catalog.GetResourceFileInfoAsync(resourceId, ct);
        if (info is null) return NotFound();
        var key = ResolveSigningKey();
        var (token, expires) = LearningResourceAccessToken.Issue(resourceId, key);
        var url = $"/api/formations/catalog/resources/file/{resourceId}?t={Uri.EscapeDataString(token)}";
        return Ok(new ResourceAccessTokenDto(url, expires));
    }

    [HttpGet("resources/file/{resourceId:guid}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadResource(
        Guid resourceId,
        [FromQuery] string? t,
        CancellationToken ct)
    {
        var key = ResolveSigningKey();
        var tokenOk = LearningResourceAccessToken.TryValidate(resourceId, t, key);
        if (!tokenOk && !(User.Identity?.IsAuthenticated ?? false))
            return Unauthorized();

        var info = await catalog.GetResourceFileInfoAsync(resourceId, ct);
        if (info is null) return NotFound();
        var (resource, fullPath, length, lastWrite) = info.Value;

        var etag = $"\"{resourceId:N}-{length}-{lastWrite.Ticks}\"";
        Response.Headers.ETag = etag;
        Response.Headers.LastModified = lastWrite.ToString("R");
        Response.Headers.CacheControl = "private, max-age=3600";

        if (Request.Headers.TryGetValue("If-None-Match", out var inm) && inm == etag)
            return StatusCode(StatusCodes.Status304NotModified);

        var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        return File(
            stream,
            resource.ContentType ?? "application/octet-stream",
            fileDownloadName: resource.FileName ?? "resource",
            enableRangeProcessing: true);
    }

    private string ResolveSigningKey()
    {
        var key = configuration["JwtSettings:Secret"] ?? configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException(
                "JwtSettings:Secret manquant — signature média e-learning refusée (pas de clé de secours).");
        return key;
    }

    [HttpGet("me/self-service")]
    public async Task<ActionResult<IReadOnlyList<MySelfServiceCatalogItemDto>>> MySelfService(CancellationToken ct)
    {
        var employeeId = User.GetSubjectId() ?? Guid.Empty;
        var email = User.GetEmail();
        if (employeeId == Guid.Empty && string.IsNullOrWhiteSpace(email))
            return BadRequest(new { error = "Identifiant utilisateur manquant dans le jeton." });
        return Ok(await catalog.ListMySelfServiceCatalogAsync(employeeId, ct, email));
    }

    [HttpGet("{catalogItemId:guid}/player")]
    public async Task<ActionResult<CatalogPlayerDto>> PlayerByCatalog(
        Guid catalogItemId,
        CancellationToken ct)
    {
        try
        {
            var employeeId = User.GetSubjectId() ?? Guid.Empty;
            return Ok(await catalog.GetPlayerByCatalogAsync(catalogItemId, employeeId, ct, User.GetEmail()));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{catalogItemId:guid}/lessons/{lessonId:guid}/complete")]
    public async Task<ActionResult<TrainingLessonDto>> CompleteLessonByCatalog(
        Guid catalogItemId,
        Guid lessonId,
        [FromBody] CompleteLessonRequest body,
        CancellationToken ct)
    {
        try
        {
            body.EmployeeId = User.GetSubjectId() ?? Guid.Empty;
            return Ok(await catalog.CompleteLessonByCatalogAsync(catalogItemId, lessonId, body, ct, User.GetEmail()));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("{catalogItemId:guid}/quiz/for-employee")]
    public async Task<ActionResult<TrainingQuizForEmployeeDto>> QuizForCatalogEmployee(
        Guid catalogItemId,
        CancellationToken ct)
    {
        try
        {
            var employeeId = User.GetSubjectId() ?? Guid.Empty;
            return Ok(await training.GetPublishedQuizForCatalogEmployeeAsync(
                catalogItemId, employeeId, ct, User.GetEmail()));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("{catalogItemId:guid}/quiz/attempts")]
    public async Task<ActionResult<TrainingQuizAttemptDto>> SubmitCatalogAttempt(
        Guid catalogItemId,
        [FromBody] SubmitTrainingQuizAttemptRequest body,
        CancellationToken ct)
    {
        try
        {
            body.EmployeeId = User.GetSubjectId() ?? Guid.Empty;
            return Ok(await training.SubmitCatalogQuizAttemptAsync(
                catalogItemId, body, ct, User.GetEmail()));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("{catalogItemId:guid}/quiz/my-attempts")]
    public async Task<ActionResult<IReadOnlyList<TrainingQuizAttemptDto>>> ListMyCatalogAttempts(
        Guid catalogItemId,
        CancellationToken ct)
    {
        try
        {
            var employeeId = User.GetSubjectId() ?? Guid.Empty;
            return Ok(await training.ListMyCatalogQuizAttemptsAsync(
                catalogItemId, employeeId, ct, User.GetEmail()));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("sessions/{sessionId:guid}/player")]
    public async Task<ActionResult<CatalogPlayerDto>> Player(
        Guid sessionId,
        CancellationToken ct)
    {
        try
        {
            var employeeId = User.GetSubjectId() ?? Guid.Empty;
            return Ok(await catalog.GetPlayerAsync(sessionId, employeeId, ct, User.GetEmail()));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("sessions/{sessionId:guid}/lessons/{lessonId:guid}/complete")]
    public async Task<ActionResult<TrainingLessonDto>> CompleteLesson(
        Guid sessionId,
        Guid lessonId,
        [FromBody] CompleteLessonRequest body,
        CancellationToken ct)
    {
        try
        {
            body.EmployeeId = User.GetSubjectId() ?? Guid.Empty;
            return Ok(await catalog.CompleteLessonAsync(sessionId, lessonId, body, ct, User.GetEmail()));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("~/api/formations/sessions/{sessionId:guid}/catalog-link")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<TrainingSessionDto>> LinkSession(
        Guid sessionId,
        [FromBody] LinkSessionCatalogRequest body,
        CancellationToken ct)
    {
        try
        {
            if (body.ActorUserId == Guid.Empty)
                body.ActorUserId = User.GetSubjectId() ?? Guid.Empty;
            return Ok(await catalog.LinkSessionCatalogAsync(sessionId, body, ct));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("stats")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<LearningQuizStatsDto>> Stats(
        [FromQuery] Guid? catalogItemId,
        CancellationToken ct) =>
        Ok(await catalog.GetLearningStatsAsync(catalogItemId, ct));

    [HttpGet("results/export")]
    [Authorize(Policy = "CanPlanContinue")]
    public async Task<ActionResult<IReadOnlyList<LearningQuizResultExportRowDto>>> Export(
        [FromQuery] Guid? sessionId,
        [FromQuery] Guid? catalogItemId,
        CancellationToken ct) =>
        Ok(await catalog.ExportResultsAsync(sessionId, catalogItemId, ct));

    private static TrainingResourceType InferType(string fileName, string? contentType)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is ".mp4" or ".webm" or ".mov" or ".avi" or ".mkv")
            return TrainingResourceType.Video;
        if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".svg"
            || (contentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false))
            return TrainingResourceType.Image;
        if (ext == ".pdf" || (contentType?.Contains("pdf", StringComparison.OrdinalIgnoreCase) ?? false))
            return TrainingResourceType.Pdf;
        return TrainingResourceType.Pdf;
    }
}
