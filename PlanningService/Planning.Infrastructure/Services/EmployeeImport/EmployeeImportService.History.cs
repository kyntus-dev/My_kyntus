using Microsoft.EntityFrameworkCore;
using Planning.Application.Abstractions.EmployeeImport;
using Planning.Application.DTOs;

namespace Planning.Infrastructure.Services.EmployeeImport;

public partial class EmployeeImportService
{
    private async Task<List<EmployeeImportJobSummaryDto>> GetHistoryInternalAsync(int take = 50, CancellationToken ct = default)
    {
        return await _db.EmployeeImportJobs
            .AsNoTracking()
            .OrderByDescending(j => j.StartedAt)
            .Take(take)
            .Select(j => new EmployeeImportJobSummaryDto
            {
                Id = j.Id,
                FileName = j.FileName,
                HasSourceFile = j.FileContent != null,
                TotalLignes = j.TotalLignes,
                ProcessedLignes = j.ProcessedLignes,
                Status = j.Status,
                Crees = j.Crees,
                MisAJour = j.MisAJour,
                Ignores = j.Ignores,
                Erreurs = j.Erreurs,
                StartedByEmail = j.StartedByEmail,
                StartedAt = j.StartedAt,
                CompletedAt = j.CompletedAt
            })
            .ToListAsync(ct);
    }

    private async Task<EmployeeImportReportDto?> GetJobReportInternalAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _db.EmployeeImportJobs
            .AsNoTracking()
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job is null)
            return null;

        var report = new EmployeeImportReportDto
        {
            ImportJobId = job.Id,
            TotalLignes = job.TotalLignes,
            ProcessedLignes = job.ProcessedLignes,
            Status = string.IsNullOrWhiteSpace(job.Status)
                ? (job.CompletedAt.HasValue ? "Completed" : "Running")
                : job.Status,
            ErrorMessage = job.ErrorMessage,
            Crees = job.Crees,
            MisAJour = job.MisAJour,
            Ignores = job.Ignores,
            Erreurs = job.Erreurs,
            CompletedAt = job.CompletedAt ?? job.StartedAt,
            Lignes = job.Lines
                .OrderBy(l => l.LineNumber)
                .Select(l => new EmployeeImportRowResultDto
                {
                    LineNumber = l.LineNumber,
                    Email = l.Email,
                    Action = l.Action,
                    Message = l.Message
                })
                .ToList()
        };

        _credentialsStore.ApplyToReport(report);
        await EmployeeImportCompletude.CompleterRapportAsync(_db, report, ct);
        return report;
    }

    private async Task<EmployeeImportSourceFile?> GetJobSourceFileInternalAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _db.EmployeeImportJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new { j.FileName, j.FileContent, j.ContentType })
            .FirstOrDefaultAsync(ct);

        if (job?.FileContent is null || job.FileContent.Length == 0)
            return null;

        return new EmployeeImportSourceFile(job.FileName, job.FileContent, job.ContentType);
    }
}
