using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>v27 : postes hors production (jamais planifiés en production, sans service).</summary>
public class PostesProductionTests
{
    [Theory]
    [InlineData("Pilote", false)]
    [InlineData("Employee", false)]
    [InlineData("Référent technique", false)]
    [InlineData("EquipeFormation", true)]
    [InlineData("Equipe_Formation", true)]
    [InlineData("Formateur", true)]
    [InlineData("RH", true)]
    [InlineData("Ingénieur", true)]
    [InlineData("Qualiticien", true)]
    public void Postes_hors_production(string role, bool horsProduction) =>
        Assert.Equal(horsProduction, PostesProduction.EstHorsProduction(role));

    [Theory]
    [InlineData("EquipeFormation", true)]
    [InlineData("Formateur", true)]
    [InlineData("Ingénieur", false)]
    [InlineData("Qualiticien", false)]
    [InlineData("Pilote", false)]
    public void Postes_qui_quittent_leur_service(string role, bool quitte) =>
        Assert.Equal(quitte, PostesProduction.QuitteLeService(role));
}
