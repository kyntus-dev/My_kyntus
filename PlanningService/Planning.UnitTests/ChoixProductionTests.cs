using Planning.Application.Pointage;

namespace Planning.UnitTests;

public class ChoixProductionTests
{
    [Theory]
    [InlineData("0840", 2026, 11, 2, 520)]  // lundi : 08h40
    [InlineData("0840", 2026, 11, 6, 460)]  // vendredi : 07h40
    [InlineData("0740", 2026, 11, 2, 460)]  // 07h40 tous les jours
    [InlineData("0740", 2026, 11, 6, 460)]
    public void Production_du_choix(string choix, int y, int m, int d, int minutes) =>
        Assert.Equal(minutes, ChoixProductionSortie.ProductionMinutes(choix, new DateOnly(y, m, d)));

    [Fact]
    public void Choix_valides()
    {
        Assert.True(ChoixProductionSortie.EstValide(null));
        Assert.True(ChoixProductionSortie.EstValide("0740"));
        Assert.False(ChoixProductionSortie.EstValide("0900"));
        Assert.Null(ChoixProductionSortie.ProductionMinutes(null, new DateOnly(2026, 11, 2)));
    }
}
