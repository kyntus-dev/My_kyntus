using Planning.Application.Pointage;

namespace Planning.UnitTests;

public class ChoixProductionTests
{
    [Theory]
    [InlineData("0840", 2026, 11, 2, 520)]  // 08h40
    [InlineData("0840", 2026, 11, 6, 520)]  // v40 : 08h40 aussi le vendredi (choix libre de la personne)
    [InlineData("0740", 2026, 11, 2, 460)]  // 07h40
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

    [Fact]
    public void Choix_valable_le_jour_meme_seulement_v40()
    {
        var lundi = new DateOnly(2026, 11, 9);
        Assert.Equal("0840", ChoixProductionSortie.ChoixDuJour("0840", lundi, lundi));
        Assert.Null(ChoixProductionSortie.ChoixDuJour("0840", lundi, lundi.AddDays(1)));
        Assert.Null(ChoixProductionSortie.ChoixDuJour("0840", null, lundi));
    }
}
