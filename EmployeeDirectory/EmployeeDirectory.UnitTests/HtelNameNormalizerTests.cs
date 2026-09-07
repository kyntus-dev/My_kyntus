using EmployeeDirectory.Infrastructure.Services;

namespace EmployeeDirectory.UnitTests;

public class HtelNameNormalizerTests
{
    [Theory]
    [InlineData("AARAB Fatima Zahra", "Fatima Zahra", "AARAB", true)]
    [InlineData("AARAB Fatima Zahra", "AARAB", "Fatima Zahra", true)]
    [InlineData("Dupont Jean", "Jean", "Dupont", true)]
    [InlineData("Dupont Jean", "Paul", "Dupont", false)]
    public void Employee_keys_match_technicien(string technicien, string first, string last, bool expectMatch)
    {
        var techKey = HtelNameNormalizer.TechnicienKey(technicien);
        var keys = HtelNameNormalizer.EmployeeNameKeys(first, last).ToHashSet();
        Assert.Equal(expectMatch, keys.Contains(techKey));
    }

    [Fact]
    public void Normalize_strips_accents_case_punctuation_and_spaces()
    {
        Assert.Equal("garcon jean", HtelNameNormalizer.Normalize("  Garçon-Jean  "));
        Assert.Equal("dupont jean", HtelNameNormalizer.Normalize("Dupont   Jean"));
    }

    [Theory]
    [InlineData("Jean", "Dupont", "Dupont Jean")] // exact
    [InlineData("Jean", "Dupont", "Jean Dupont")] // ordre inversé
    [InlineData("Jean", "Dupont", "  Dupont   Jean  ")] // espaces
    [InlineData("Jean", "Dupont", "Dupontt Jean")] // +1 caractère
    [InlineData("Jean", "Dupont", "Dupond Jean")] // typo d/t
    [InlineData("Jean", "Dupont", "Dupont Jeaan")] // typo prénom
    [InlineData("Fatima Zahra", "Aarab", "AARAB Fatima Zahra")] // accents / casse
    [InlineData("Jean-Pierre", "Dupont", "Dupont Jean Pierre")] // tiret → espaces
    public void Fuzzy_match_detects_near_duplicates(string first, string last, string technicien)
    {
        var score = HtelNameNormalizer.ScoreEmployeeToTechnicien(first, last, technicien);
        Assert.True(
            score >= HtelNameNormalizer.AutoLinkThreshold,
            $"Expected auto-link score for '{first} {last}' vs '{technicien}', got {score:F3}");
        Assert.True(HtelNameNormalizer.IsNameMatch(first, last, technicien));
    }

    [Fact]
    public void Extra_character_still_auto_links()
    {
        // Cas signalé : même nom avec un caractère de plus
        var score = HtelNameNormalizer.ScoreEmployeeToTechnicien("Marie", "Martin", "Martin Mariee");
        Assert.True(score >= HtelNameNormalizer.AutoLinkThreshold, $"score={score:F3}");
    }

    [Fact]
    public void Distant_names_do_not_match()
    {
        var score = HtelNameNormalizer.ScoreEmployeeToTechnicien("Jean", "Dupont", "Bernard Paul");
        Assert.True(score < HtelNameNormalizer.AlertThreshold, $"score={score:F3}");
    }
}
