using Planning.Application.Org;

namespace Planning.UnitTests;

public class ImportPlacementRestorePlannerTests
{
    [Fact]
    public void Infers_dominant_cell_from_plannings_before_import()
    {
        var importAt = new DateTime(2026, 8, 27, 14, 31, 5, DateTimeKind.Utc);
        var appearances = new[]
        {
            new PlacementAppearance(10, importAt.AddDays(-14)),
            new PlacementAppearance(10, importAt.AddDays(-7)),
            new PlacementAppearance(10, importAt.AddDays(-1)),
            new PlacementAppearance(99, importAt.AddHours(2)),
        };

        Assert.Equal(10, ImportPlacementRestorePlanner.InferPreviousSubServiceId(appearances, importAt));
    }

    [Fact]
    public void Skips_restore_when_later_rotation_exists()
    {
        Assert.False(ImportPlacementRestorePlanner.ShouldRestore(99, 10, hasLaterOrgAssignment: true));
    }

    [Fact]
    public void Restores_when_current_cell_differs_and_no_later_rotation()
    {
        Assert.True(ImportPlacementRestorePlanner.ShouldRestore(99, 10, hasLaterOrgAssignment: false));
        Assert.False(ImportPlacementRestorePlanner.ShouldRestore(10, 10, hasLaterOrgAssignment: false));
    }
}
