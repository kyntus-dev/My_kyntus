using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.DTOs;
using Planning.Application.Abstractions.EmployeeImport;

namespace Planning.API.Controllers;

[ApiController]
[Route("api/users/import/v2")]
[Authorize]
public class EmployeeImportController(IEmployeeImportService importService, IEmployeeImportConfigService configService) : ControllerBase
{
    private static bool IsHrOrAdmin(string role) =>
        string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "RH", StringComparison.OrdinalIgnoreCase);

    private ActionResult? DenyUnlessHrOrAdmin()
    {
        var role = GetClaimValue("role", ClaimTypes.Role) ?? string.Empty;
        return IsHrOrAdmin(role) ? null : Forbid();
    }

    [HttpGet("config")]
    public async Task<ActionResult<List<EmployeeImportFieldConfigDto>>> GetConfig(CancellationToken ct)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;
        return Ok(await configService.GetConfigAsync(ct));
    }

    [HttpPut("config")]
    public async Task<ActionResult<List<EmployeeImportFieldConfigDto>>> UpdateConfig(
        [FromBody] UpdateEmployeeImportConfigRequest request,
        CancellationToken ct)
    {
        var role = GetClaimValue("role", ClaimTypes.Role) ?? string.Empty;
        if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
            return Forbid();

        try
        {
            return Ok(await configService.UpdateConfigAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("template")]
    public async Task<IActionResult> DownloadTemplate(CancellationToken ct)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        var bytes = await importService.BuildTemplateAsync(ct);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "template_employes.xlsx");
    }

    [HttpPost("analyze")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<ActionResult<EmployeeImportAnalyzeResponse>> Analyze(IFormFile file, CancellationToken ct)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        if (file is null || file.Length == 0)
            return BadRequest("Fichier manquant.");

        try
        {
            return Ok(await importService.AnalyzeAsync(file, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("revalidate-org")]
    public async Task<ActionResult<EmployeeImportRevalidateOrgResponse>> RevalidateOrg(
        [FromBody] EmployeeImportRevalidateOrgRequest request,
        CancellationToken ct)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        try
        {
            return Ok(await importService.RevalidateOrgAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("preview")]
    public async Task<ActionResult<EmployeeImportPreviewResponse>> Preview(
        [FromBody] EmployeeImportPreviewRequest request,
        CancellationToken ct)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        try
        {
            return Ok(await importService.PreviewAsync(request, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("execute")]
    public async Task<ActionResult<EmployeeImportReportDto>> Execute(
        [FromBody] EmployeeImportExecuteRequest request,
        CancellationToken ct)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        try
        {
            var email = GetClaimValue("email", ClaimTypes.Email);
            var report = await importService.ExecuteAsync(request, email, ct);
            return Ok(report);
        }
        catch (InvalidOperationException ex)
        {
            var message = ex.InnerException?.Message is { Length: > 0 } inner && !ex.Message.Contains(inner, StringComparison.Ordinal)
                ? $"{ex.Message}\n\n{inner}"
                : ex.Message;
            return BadRequest(message);
        }
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<EmployeeImportJobSummaryDto>>> History(
        [FromQuery] int take = 50,
        CancellationToken ct = default)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        return Ok(await importService.GetHistoryAsync(Math.Clamp(take, 1, 200), ct));
    }

    [HttpGet("history/{jobId:guid}")]
    public async Task<ActionResult<EmployeeImportReportDto>> GetJob(Guid jobId, CancellationToken ct)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        var report = await importService.GetJobReportAsync(jobId, ct);
        return report is null ? NotFound() : Ok(report);
    }

    [HttpGet("history/{jobId:guid}/file")]
    public async Task<IActionResult> DownloadSourceFile(Guid jobId, CancellationToken ct)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        var file = await importService.GetJobSourceFileAsync(jobId, ct);
        if (file is null)
            return NotFound("Fichier source indisponible pour cet import.");

        var contentType = string.IsNullOrWhiteSpace(file.ContentType)
            ? "application/octet-stream"
            : file.ContentType;

        return File(file.Content, contentType, file.FileName);
    }

    [HttpPost("history/{jobId:guid}/rollback")]
    public async Task<ActionResult<EmployeeImportRollbackResultDto>> RollbackJob(Guid jobId, CancellationToken ct)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        try
        {
            return Ok(await importService.RollbackJobAsync(jobId, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("history/rollback-by-time")]
    public async Task<ActionResult<EmployeeImportRollbackResultDto>> RollbackByTime(
        [FromBody] EmployeeImportRollbackByTimeRequest request,
        CancellationToken ct)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        if (request is null || request.StartedAt == default)
            return BadRequest(new { message = "startedAt requis." });

        try
        {
            return Ok(await importService.RollbackJobByStartedAtAsync(
                request.StartedAt,
                request.FileNameContains,
                ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("history/{jobId:guid}/restore-placements")]
    public async Task<ActionResult<EmployeeImportPlacementRestoreResultDto>> RestorePlacements(
        Guid jobId,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        try
        {
            return Ok(await importService.RestorePlacementsAfterImportAsync(jobId, dryRun, ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("history/{jobId:guid}/errors.csv")]
    public async Task<IActionResult> ExportErrors(Guid jobId, CancellationToken ct)
    {
        var denied = DenyUnlessHrOrAdmin();
        if (denied is not null) return denied;

        var report = await importService.GetJobReportAsync(jobId, ct);
        if (report is null)
            return NotFound();

        var sb = new StringBuilder();
        sb.AppendLine("Ligne;Email;Action;Message");
        foreach (var line in report.Lignes.Where(l => l.Action is "error" or "ignore"))
        {
            sb.AppendLine($"{line.LineNumber};{EscapeCsv(line.Email)};{EscapeCsv(line.Action)};{EscapeCsv(line.Message)}");
        }

        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        return File(bytes, "text/csv", $"import_erreurs_{jobId:N}.csv");
    }

    private string? GetClaimValue(string jwtClaim, string fallbackType) =>
        User.FindFirst(jwtClaim)?.Value ?? User.FindFirst(fallbackType)?.Value;

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
