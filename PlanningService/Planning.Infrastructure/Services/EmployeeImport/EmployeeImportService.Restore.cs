using Microsoft.EntityFrameworkCore;
using Planning.Application.DTOs;
using Planning.Application.Org;

namespace Planning.Infrastructure.Services.EmployeeImport;

public partial class EmployeeImportService
{
    public async Task<EmployeeImportPlacementRestoreResultDto> RestorePlacementsAfterImportAsync(
        Guid jobId,
        bool dryRun,
        CancellationToken ct = default)
    {
        var job = await _db.EmployeeImportJobs
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new InvalidOperationException("Import introuvable.");

        var updateEmails = job.Lines
            .Where(l => string.Equals(l.Action, "update", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(l.Email))
            .Select(l => l.Email!.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        var users = await _db.Users
            .Include(u => u.SubService)
                .ThenInclude(ss => ss!.Service)
                    .ThenInclude(s => s.Floor)
            .Where(u => updateEmails.Contains(u.Email.ToLower()))
            .ToListAsync(ct);

        var userIds = users.Select(u => u.Id).ToList();
        var appearances = await _db.ShiftAssignments.AsNoTracking()
            .Where(a => userIds.Contains(a.UserId))
            .Select(a => new
            {
                a.UserId,
                a.WeeklyPlanning.SubServiceId,
                a.WeeklyPlanning.CreatedAt,
            })
            .ToListAsync(ct);

        var byUser = appearances
            .GroupBy(a => a.UserId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<PlacementAppearance>)g
                    .Select(x => new PlacementAppearance(x.SubServiceId, x.CreatedAt))
                    .ToList());

        var guids = users.Select(u => u.Guid).Where(g => g != Guid.Empty).Distinct().ToList();
        var later = guids.Count == 0
            ? new HashSet<Guid>()
            : (await directoryOrg.GetEmployeeIdsWithAssignmentsAfterAsync(
                guids, job.CompletedAt ?? job.StartedAt, ct)).ToHashSet();

        var labels = await _db.SubServices.AsNoTracking()
            .Select(ss => new
            {
                ss.Id,
                Label = (ss.Service.Floor != null ? ss.Service.Floor.Name + " / " : "")
                    + ss.Service.Name + " / " + ss.Name,
            })
            .ToDictionaryAsync(x => x.Id, x => x.Label, ct);

        string LabelOf(int? id) =>
            id is int sid && labels.TryGetValue(sid, out var label) ? label : "—";

        var result = new EmployeeImportPlacementRestoreResultDto
        {
            JobId = job.Id,
            DryRun = dryRun,
        };

        foreach (var user in users)
        {
            byUser.TryGetValue(user.Id, out var userAppearances);
            var previous = ImportPlacementRestorePlanner.InferPreviousSubServiceId(
                userAppearances ?? [], job.StartedAt);
            var hasLater = later.Contains(user.Guid);
            var restore = ImportPlacementRestorePlanner.ShouldRestore(user.SubServiceId, previous, hasLater);

            var item = new EmployeeImportPlacementRestoreItemDto
            {
                Email = user.Email,
                CurrentLabel = LabelOf(user.SubServiceId),
                PreviousLabel = LabelOf(previous),
                Action = restore
                    ? (dryRun ? "would_restore" : "restored")
                    : hasLater
                        ? "skipped_rotation"
                        : previous is null
                            ? "skipped_no_history"
                            : "skipped_already",
            };
            result.Items.Add(item);

            switch (item.Action)
            {
                case "restored":
                case "would_restore":
                    result.Restored++;
                    break;
                case "skipped_rotation":
                    result.SkippedRotation++;
                    break;
                case "skipped_already":
                    result.SkippedAlready++;
                    break;
                default:
                    result.SkippedNoHistory++;
                    break;
            }

            if (!dryRun && restore && previous.HasValue)
                await userService.RestoreSubServicePlacementAsync(user.Id, previous.Value, ct);
        }

        result.Message = dryRun
            ? $"{result.Restored} employé(s) seraient remis dans leur cellule d'avant l'import. Rotations postérieures : {result.SkippedRotation}."
            : $"{result.Restored} employé(s) remis dans leur cellule d'avant l'import. Rotations postérieures conservées : {result.SkippedRotation}.";

        return result;
    }
}
