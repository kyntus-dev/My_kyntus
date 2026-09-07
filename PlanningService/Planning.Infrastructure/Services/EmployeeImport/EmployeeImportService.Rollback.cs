using Microsoft.EntityFrameworkCore;
using Planning.Application.DTOs;
using Planning.Infrastructure.Services;

namespace Planning.Infrastructure.Services.EmployeeImport;

public partial class EmployeeImportService
{
    public async Task<EmployeeImportRollbackResultDto> RollbackJobByStartedAtAsync(
        DateTime startedAt,
        string? fileNameContains,
        CancellationToken ct = default)
    {
        var from = startedAt.AddHours(-4);
        var to = startedAt.AddHours(4);
        var jobs = await _db.EmployeeImportJobs
            .AsNoTracking()
            .Where(j => j.StartedAt >= from && j.StartedAt <= to)
            .Select(j => new { j.Id, j.FileName, j.StartedAt, j.Status })
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(fileNameContains))
        {
            var needle = fileNameContains.Trim();
            jobs = jobs
                .Where(j => j.FileName.Contains(needle, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var match = jobs
            .OrderBy(j => Math.Abs((j.StartedAt - startedAt).Ticks))
            .FirstOrDefault()
            ?? throw new InvalidOperationException(
                "Aucun import trouvé autour de cette date. Ouvrez l'historique et utilisez l'identifiant du job.");

        return await RollbackJobAsync(match.Id, ct);
    }

    public async Task<EmployeeImportRollbackResultDto> RollbackJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await _db.EmployeeImportJobs
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new InvalidOperationException("Import introuvable.");

        if (string.Equals(job.Status, "RolledBack", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cet import a déjà été annulé.");

        if (!string.Equals(job.Status, "Completed", StringComparison.OrdinalIgnoreCase)
            && job.CompletedAt is null)
            throw new InvalidOperationException("Seuls les imports terminés peuvent être annulés.");

        var createEmails = job.Lines
            .Where(l => string.Equals(l.Action, "create", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(l.Email))
            .Select(l => l.Email!.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();
        var touchedEmails = job.Lines
            .Where(l =>
                (string.Equals(l.Action, "create", StringComparison.OrdinalIgnoreCase)
                 || string.Equals(l.Action, "update", StringComparison.OrdinalIgnoreCase))
                && !string.IsNullOrWhiteSpace(l.Email))
            .Select(l => l.Email!.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var users = await _db.Users
            .Where(u => touchedEmails.Contains(u.Email.ToLower()))
            .ToListAsync(ct);

        var guids = users.Select(u => u.Guid).Where(g => g != Guid.Empty).Distinct().ToList();
        var windowStart = job.StartedAt.AddMinutes(-2);
        var windowEnd = (job.CompletedAt ?? job.StartedAt).AddMinutes(20);

        var dirResult = guids.Count == 0
            ? new DirectoryUndoImportWindowResult()
            : await directoryOrg.UndoImportAssignmentWindowAsync(guids, windowStart, windowEnd, ct);

        var auth = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        await orgMirror.SyncFromDirectoryOverviewAsync(
            string.IsNullOrWhiteSpace(auth) ? null : auth, ct);

        var createdUsers = users
            .Where(u => createEmails.Contains(u.Email.Trim().ToLowerInvariant()))
            .ToList();
        var createdIds = createdUsers.Select(u => u.Id).ToList();
        if (createdIds.Count > 0)
            await RemoveCreatedUserPlanningLinksAsync(createdIds, ct);

        var deletedEmails = new List<string>();
        foreach (var created in createdUsers)
        {
            await userService.RollbackImportCreatedUserAsync(created.Id, ct);
            deletedEmails.Add(created.Email);
        }

        job.Status = "RolledBack";
        job.ErrorMessage =
            $"Annulé le {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC — créations supprimées, " +
            "affectations de la fenêtre d'import restaurées (rotations postérieures conservées).";
        await _db.SaveChangesAsync(ct);

        return new EmployeeImportRollbackResultDto
        {
            JobId = job.Id,
            FileName = job.FileName,
            StartedAt = job.StartedAt,
            CreatedDeleted = deletedEmails.Count,
            DirectoryAssignmentsDeleted = dirResult.DeletedAssignments,
            DirectoryAssignmentsRestored = dirResult.RestoredAssignments,
            DeletedEmails = deletedEmails,
            Message =
                "Import annulé : les employés créés par cet import sont retirés, " +
                "les affectations de cet import sont défaites. " +
                "Les cellules, employés et rotations ajoutés après cet import sont conservés.",
        };
    }

    private async Task RemoveCreatedUserPlanningLinksAsync(List<int> createdIds, CancellationToken ct)
    {
        _db.SaturdayGroups.RemoveRange(
            await _db.SaturdayGroups.Where(sg => createdIds.Contains(sg.UserId)).ToListAsync(ct));
        _db.SaturdayHistories.RemoveRange(
            await _db.SaturdayHistories.Where(h => createdIds.Contains(h.UserId)).ToListAsync(ct));
        _db.ShiftAssignments.RemoveRange(
            await _db.ShiftAssignments.Where(a => createdIds.Contains(a.UserId)).ToListAsync(ct));
        _db.WeeklyEmployeeShiftModes.RemoveRange(
            await _db.WeeklyEmployeeShiftModes.Where(m => createdIds.Contains(m.UserId)).ToListAsync(ct));
        _db.Declarations.RemoveRange(
            await _db.Declarations.Where(d => createdIds.Contains(d.UserId)).ToListAsync(ct));
        _db.PlanningConsultations.RemoveRange(
            await _db.PlanningConsultations.Where(c => createdIds.Contains(c.UserId)).ToListAsync(ct));
        _db.PlanningComments.RemoveRange(
            await _db.PlanningComments.Where(c => createdIds.Contains(c.UserId)).ToListAsync(ct));
        _db.PlanningReinforcementVolunteers.RemoveRange(
            await _db.PlanningReinforcementVolunteers.Where(v => createdIds.Contains(v.UserId)).ToListAsync(ct));
        await _db.SaveChangesAsync(ct);
    }
}
