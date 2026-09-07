using EmployeeDirectory.Application.Org;

namespace EmployeeDirectory.UnitTests;

public class ImportAssignmentWindowUndoPlannerTests
{
    [Fact]
    public void Removes_import_assignment_and_reopens_previous_when_no_later_rotation()
    {
        var employee = Guid.NewGuid();
        var previous = Guid.NewGuid();
        var imported = Guid.NewGuid();
        var start = new DateTime(2026, 8, 27, 14, 31, 5, DateTimeKind.Utc);
        var end = start.AddMinutes(8);

        var rows = new[]
        {
            new AssignmentWindowRow(previous, employee, start.AddMonths(-2), start),
            new AssignmentWindowRow(imported, employee, start, null),
        };

        var plan = ImportAssignmentWindowUndoPlanner.Plan(rows, start, end);

        Assert.Contains(plan, m => m.AssignmentId == imported && m.Kind == AssignmentWindowMutationKind.Delete);
        Assert.Contains(plan, m =>
            m.AssignmentId == previous
            && m.Kind == AssignmentWindowMutationKind.SetEffectiveTo
            && m.EffectiveTo is null);
    }

    [Fact]
    public void Keeps_later_rotation_and_skips_import_assignment()
    {
        var employee = Guid.NewGuid();
        var previous = Guid.NewGuid();
        var imported = Guid.NewGuid();
        var later = Guid.NewGuid();
        var start = new DateTime(2026, 8, 27, 14, 31, 5, DateTimeKind.Utc);
        var end = start.AddMinutes(8);
        var laterFrom = start.AddDays(1);

        var rows = new[]
        {
            new AssignmentWindowRow(previous, employee, start.AddMonths(-2), start),
            new AssignmentWindowRow(imported, employee, start, laterFrom),
            new AssignmentWindowRow(later, employee, laterFrom, null),
        };

        var plan = ImportAssignmentWindowUndoPlanner.Plan(rows, start, end);

        Assert.Contains(plan, m => m.AssignmentId == imported && m.Kind == AssignmentWindowMutationKind.Delete);
        Assert.Contains(plan, m =>
            m.AssignmentId == previous
            && m.Kind == AssignmentWindowMutationKind.SetEffectiveTo
            && m.EffectiveTo == laterFrom);
        Assert.DoesNotContain(plan, m => m.AssignmentId == later);
    }

    [Fact]
    public void Ignores_employees_without_changes_in_window()
    {
        var employee = Guid.NewGuid();
        var start = new DateTime(2026, 8, 27, 14, 31, 5, DateTimeKind.Utc);
        var rows = new[]
        {
            new AssignmentWindowRow(Guid.NewGuid(), employee, start.AddDays(-10), null),
        };

        var plan = ImportAssignmentWindowUndoPlanner.Plan(rows, start, start.AddMinutes(5));
        Assert.Empty(plan);
    }
}
