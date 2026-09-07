using Planning.Application.PilotPlanning;

namespace Planning.UnitTests;

public class PilotPlanningEmailComposerTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("pas-un-email", false)]
    [InlineData("a@b.c", true)]
    [InlineData("  pilote.perso@gmail.com  ", true)]
    public void IsValidPersonalEmail(string? email, bool expected) =>
        Assert.Equal(expected, PilotPlanningEmailComposer.IsValidPersonalEmail(email));

    [Fact]
    public void Subject_distinguishes_publish_and_update()
    {
        Assert.Equal("Planning 2026-W36 disponible", PilotPlanningEmailComposer.Subject("2026-W36", false));
        Assert.Equal("Planning 2026-W36 mis à jour", PilotPlanningEmailComposer.Subject("2026-W36", true));
    }

    [Fact]
    public void Html_matches_htel_export_for_one_pilot()
    {
        var html = PilotPlanningEmailComposer.BuildHtml(
            "Yasmine",
            "El Idrissi",
            "2026-W36",
            "Satisfaction client",
            new DateOnly(2026, 8, 31),
            [
                new PilotPlanningDayRow(
                    new DateOnly(2026, 8, 31),
                    "8h",
                    "08:00",
                    "17:00",
                    "12:00",
                    false,
                    false),
                new PilotPlanningDayRow(
                    new DateOnly(2026, 9, 1),
                    "—",
                    "",
                    "",
                    null,
                    true,
                    false),
                new PilotPlanningDayRow(
                    new DateOnly(2026, 9, 2),
                    "—",
                    "",
                    "",
                    null,
                    false,
                    true),
            ]);

        Assert.Contains("PLANNING S36", html);
        Assert.Contains("Satisfaction client", html);
        Assert.Contains("Nom", html);
        Assert.Contains("31-Aug", html);
        Assert.Contains("1-Sep", html);
        Assert.Contains("EL IDRISSI Yasmine", html);
        Assert.Contains("08:00", html);
        Assert.Contains("12:00", html);
        Assert.Contains(">CP<", html);
        Assert.Contains(">F<", html);
        Assert.DoesNotContain("17:00", html);
        Assert.DoesNotContain("<th>Fin</th>", html);
        Assert.DoesNotContain("Chraibi", html);
        Assert.DoesNotContain("href", html, StringComparison.OrdinalIgnoreCase);
        Assert.False(PilotPlanningEmailComposer.ContainsAppLink(html));
        Assert.Equal(1, CountOccurrences(html, "EL IDRISSI Yasmine"));
    }

    [Fact]
    public void ResolveExportName_prefers_htel_when_linked_and_different()
    {
        Assert.Equal(
            "AIT NEJMA Meriem",
            PilotPlanningEmailComposer.ResolveExportName("Meriem", "Ait Nejma", "AIT NEJMA Meriem", 42));
        Assert.Equal(
            "DUPONT Jean",
            PilotPlanningEmailComposer.ResolveExportName("Jean", "Dupont", "DUPONT Jean", 7));
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        var n = 0;
        var i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            n++;
            i += needle.Length;
        }
        return n;
    }
}
