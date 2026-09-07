namespace EmployeeDirectory.Application.Org;

public readonly record struct AssignmentWindowRow(
    Guid Id,
    Guid EmployeeId,
    DateTime EffectiveFrom,
    DateTime? EffectiveTo);

public enum AssignmentWindowMutationKind
{
    Delete,
    SetEffectiveTo,
}

public readonly record struct AssignmentWindowMutation(
    Guid AssignmentId,
    AssignmentWindowMutationKind Kind,
    DateTime? EffectiveTo);

/// <summary>
/// Annule uniquement les affectations créées / fermées dans la fenêtre d’un import.
/// Les rotations postérieures (EffectiveFrom après la fenêtre) sont conservées.
/// </summary>
public static class ImportAssignmentWindowUndoPlanner
{
    public static IReadOnlyList<AssignmentWindowMutation> Plan(
        IReadOnlyList<AssignmentWindowRow> rows,
        DateTime windowStart,
        DateTime windowEnd)
    {
        if (windowEnd < windowStart)
            (windowStart, windowEnd) = (windowEnd, windowStart);

        var mutations = new List<AssignmentWindowMutation>();
        foreach (var group in rows.GroupBy(r => r.EmployeeId))
        {
            var list = group.ToList();
            var createdIds = list
                .Where(r => r.EffectiveFrom >= windowStart && r.EffectiveFrom <= windowEnd)
                .Select(r => r.Id)
                .ToHashSet();
            var laterStart = list
                .Where(r => r.EffectiveFrom > windowEnd)
                .Select(r => r.EffectiveFrom)
                .OrderBy(d => d)
                .Cast<DateTime?>()
                .FirstOrDefault();

            foreach (var id in createdIds)
                mutations.Add(new AssignmentWindowMutation(id, AssignmentWindowMutationKind.Delete, null));

            foreach (var closed in list.Where(r =>
                         r.EffectiveTo is { } to && to >= windowStart && to <= windowEnd && !createdIds.Contains(r.Id)))
            {
                mutations.Add(new AssignmentWindowMutation(
                    closed.Id,
                    AssignmentWindowMutationKind.SetEffectiveTo,
                    laterStart));
            }
        }

        return mutations;
    }
}

public record UndoImportAssignmentWindowRequest(
    IReadOnlyList<Guid> EmployeeIds,
    DateTime WindowStart,
    DateTime WindowEnd);

public record UndoImportAssignmentWindowResult(
    int DeletedAssignments,
    int RestoredAssignments,
    int EmployeesTouched);

public record EmployeeIdsCreatedAfterRequest(
    IReadOnlyList<Guid> EmployeeIds,
    DateTime After);
