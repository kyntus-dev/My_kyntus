using System.Text.Json;
using Documentation.Application;
using Documentation.Application.Abstractions;
using Documentation.Application.Api;
using Documentation.Application.DocumentTemplates;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Documentation.API.Controllers;

/// <summary>Modèles de documents — CRUD, versions, upload et génération.</summary>
[ApiController]
[Route("api/documentation/data")]
public sealed class DocumentationDocumentTemplatesController(
    IMediator mediator,
    IDocumentTemplateAppService? templates) : ControllerBase
{
    [HttpGet("document-templates")]
    public async Task<IActionResult> GetDocumentTemplates(CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new GetDocumentTemplatesQuery(), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpGet("document-templates/{id:guid}")]
    public async Task<IActionResult> GetDocumentTemplate(Guid id, CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new GetDocumentTemplateQuery(id), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpGet("document-templates/{id:guid}/template-file-url")]
    public async Task<IActionResult> GetTemplateSourceFileUrl(Guid id, CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new GetTemplateSourceFileUrlQuery(id), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpGet("document-templates/{id:guid}/template-file")]
    public async Task<IActionResult> GetTemplateSourceFile(Guid id, CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            var file = await mediator.Send(new GetTemplateSourceFileQuery(id), ct);
            ApplyResponseHeaders(file.ResponseHeaders);
            return File(file.Content, file.ContentType);
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpGet("document-templates/{id:guid}/template-preview")]
    public async Task<IActionResult> GetTemplatePreview(Guid id, CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            var file = await mediator.Send(new GetTemplatePreviewQuery(id), ct);
            ApplyResponseHeaders(file.ResponseHeaders);
            return File(file.Content, file.ContentType);
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPost("document-templates")]
    public async Task<IActionResult> CreateDocumentTemplate(
        [FromBody] CreateDocumentTemplateRequest body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new CreateDocumentTemplateCommand(body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPut("document-templates/{id:guid}")]
    public async Task<IActionResult> UpdateDocumentTemplate(
        Guid id,
        [FromBody] UpdateDocumentTemplateRequest body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new UpdateDocumentTemplateCommand(id, body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPatch("document-templates/{id:guid}/status")]
    public async Task<IActionResult> UpdateDocumentTemplateStatus(
        Guid id,
        [FromBody] UpdateTemplateStatusRequest body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new UpdateDocumentTemplateStatusCommand(id, body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpDelete("document-templates/{id:guid}")]
    public async Task<IActionResult> DeleteDocumentTemplate(Guid id, CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            await mediator.Send(new DeleteDocumentTemplateCommand(id), ct);
            return NoContent();
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPost("document-templates/upload")]
    [RequestSizeLimit(52_428_800)]
    public async Task<IActionResult> UploadTemplate(CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return BadRequest(new { message = "Fichier « file » requis (multipart/form-data)." });

            var code = form["code"].ToString();
            var name = form["name"].ToString();
            var description = string.IsNullOrWhiteSpace(form["description"].ToString()) ? null : form["description"].ToString();
            Guid? documentTypeId = null;
            if (Guid.TryParse(form["documentTypeId"].ToString(), out var dt))
                documentTypeId = dt;
            var asStatic = string.Equals(form["kind"].ToString(), "static", StringComparison.OrdinalIgnoreCase);
            var requiresPilotUpload = string.Equals(form["requiresPilotUpload"].ToString(), "true", StringComparison.OrdinalIgnoreCase)
                || form["requiresPilotUpload"].ToString() == "1"
                || string.Equals(form["requiresPilotUpload"].ToString(), "on", StringComparison.OrdinalIgnoreCase);

            try
            {
                return Ok(await mediator.Send(new UploadTemplateFromFileCommand(
                    file, code, name, description, documentTypeId, asStatic, requiresPilotUpload), ct));
            }
            catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
        }

        UploadTemplateRequest body;
        try
        {
            using var sr = new StreamReader(Request.Body);
            var json = await sr.ReadToEndAsync(ct);
            body = JsonSerializer.Deserialize<UploadTemplateRequest>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? throw new JsonException("empty");
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Corps JSON invalide (attendu UploadTemplateRequest) ou utilisez multipart/form-data avec le champ file." });
        }

        try
        {
            return Ok(await mediator.Send(new UploadTemplateFromJsonCommand(body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPost("document-templates/internal-engine/analyze")]
    public async Task<IActionResult> AnalyzeInternalEngineTemplate(
        [FromBody] InternalEngineTemplateRequest body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new AnalyzeInternalEngineTemplateQuery(body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPost("document-templates/internal-engine")]
    public async Task<IActionResult> CreateInternalEngineTemplate(
        [FromBody] InternalEngineTemplateRequest body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new CreateInternalEngineTemplateCommand(body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPost("document-templates/generate")]
    public async Task<IActionResult> GenerateTemplateFromAi(
        [FromBody] AiGenerateTemplateRequest body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new GenerateTemplateFromAiCommand(body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPost("document-templates/rule-generate")]
    public async Task<IActionResult> GenerateRuleBasedTemplate(
        [FromBody] RuleGenerateTemplateRequest body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new GenerateRuleBasedTemplateCommand(body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPost("document-templates/{id:guid}/versions")]
    public async Task<IActionResult> CreateTemplateVersion(
        Guid id,
        [FromBody] CreateTemplateVersionRequest body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new CreateTemplateVersionCommand(id, body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPut("document-templates/{id:guid}/current-version/variables")]
    public async Task<IActionResult> PutCurrentVersionVariables(
        Guid id,
        [FromBody] IReadOnlyList<TemplateVariableInput>? body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new PutCurrentVersionVariablesCommand(id, body ?? Array.Empty<TemplateVariableInput>()), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpGet("document-templates/{id:guid}/versions")]
    public async Task<IActionResult> GetTemplateVersions(Guid id, CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new GetTemplateVersionsQuery(id), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPost("document-templates/{id:guid}/test-run")]
    public async Task<IActionResult> TestRunTemplate(
        Guid id,
        [FromBody] TemplateTestRunRequest body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new TestRunTemplateCommand(id, body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPost("document-templates/{id:guid}/generate")]
    public async Task<IActionResult> GenerateFromTemplate(
        Guid id,
        [FromBody] DocumentTemplateGenerateRequest? body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new GenerateFromTemplateCommand(id, body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    [HttpPost("document-templates/{id:guid}/resolve-employee-values")]
    public async Task<IActionResult> ResolveEmployeeValues(
        Guid id,
        [FromBody] ResolveTemplateEmployeeValuesRequest body,
        CancellationToken ct)
    {
        if (templates is null) return StatusCode(503, new { message = "Base de données non configurée." });
        try
        {
            return Ok(await mediator.Send(new ResolveTemplateEmployeeValuesCommand(id, body), ct));
        }
        catch (DocumentationApiException ex) { return MapExceptionResult(ex); }
    }

    private void ApplyResponseHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null) return;
        foreach (var (key, value) in headers)
            Response.Headers.Append(key, value);
    }

    private IActionResult MapExceptionResult(DocumentationApiException ex)
    {
        var body = ex.Payload ?? new { message = ex.Message };
        return ex.StatusCode switch
        {
            StatusCodes.Status401Unauthorized => Unauthorized(),
            StatusCodes.Status403Forbidden => Forbid(),
            StatusCodes.Status404NotFound => NotFound(body),
            StatusCodes.Status409Conflict => Conflict(body),
            StatusCodes.Status422UnprocessableEntity => UnprocessableEntity(body),
            _ => StatusCode(ex.StatusCode, body),
        };
    }
}
