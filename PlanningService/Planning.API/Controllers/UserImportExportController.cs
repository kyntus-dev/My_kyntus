using MediatR;
using Microsoft.AspNetCore.Mvc;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Application.Users;

namespace Planning.API.Controllers;

[ApiController]
[Route("api/Users")]
public class UserImportExportController(IMediator mediator) : ControllerBase
{
    [HttpGet("template")]
    public async Task<IActionResult> DownloadTemplate(CancellationToken ct)
    {
        var bytes = await mediator.Send(new DownloadUserImportTemplateQuery(), ct);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "template_employes.xlsx");
    }

    [HttpPost("import")]
    public async Task<ActionResult<ImportResultDto>> ImportUsers(IFormFile file, [FromServices] IJournalActionsService journal, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
            return BadRequest("Fichier manquant.");

        await using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);
        stream.Position = 0;

        var result = await mediator.Send(new ImportUsersFromExcelCommand(stream), ct);
        // v64 : journal d'actions.
        await journal.EnregistrerAsync(new JournalEvenement(
            "Imports et exports", "Import", $"Import Excel d'employés « {Path.GetFileName(file.FileName)} »", "UserImport"), ct: ct);
        return Ok(result);
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportUsers([FromServices] IJournalActionsService journal, CancellationToken ct)
    {
        var bytes = await mediator.Send(new ExportUsersToExcelQuery(), ct);
        await journal.EnregistrerAsync(new JournalEvenement(
            "Imports et exports", "Export", "Export Excel des employés", "UserExport"), ct: ct);
        var fileName = $"employes_{DateTime.Now:yyyyMMdd_HHmm}.xlsx";
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
