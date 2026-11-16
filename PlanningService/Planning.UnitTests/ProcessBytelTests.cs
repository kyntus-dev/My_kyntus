using Planning.Application.Presence;
using Planning.Infrastructure.Services.Presence;
using Xunit;

namespace Planning.UnitTests;

/// <summary>v48 — Type de formation et bibliothèque Process Bytel.</summary>
public class ProcessBytelTests
{
    [Theory]
    [InlineData("contractuel", "contractuel")]
    [InlineData("Contractuel", "contractuel")]
    [InlineData("non contractuel", "non-contractuel")]
    [InlineData("NON_CONTRACTUEL", "non-contractuel")]
    [InlineData("hors-contrat", "non-contractuel")]
    [InlineData("", null)]
    [InlineData("autre", null)]
    public void Type_de_formation_normalise(string saisi, string? attendu) =>
        Assert.Equal(attendu, TypesFormation.Normaliser(saisi));

    [Fact]
    public void Libelles_des_types_de_formation()
    {
        Assert.Equal("Contractuel", TypesFormation.Libelle("contractuel"));
        Assert.Equal("Non contractuel", TypesFormation.Libelle("non-contractuel"));
        Assert.Equal(string.Empty, TypesFormation.Libelle(null));
        Assert.True(TypesFormation.EstValide("contractuel"));
        Assert.False(TypesFormation.EstValide("stage"));
    }

    [Fact]
    public void Dossier_par_defaut_et_listes_separees()
    {
        Assert.Equal("ITs", ProcessDocuments.NormaliserDossier(null));
        Assert.Equal("ITs", ProcessDocuments.NormaliserDossier("   "));
        Assert.Equal("Qualité", ProcessDocuments.NormaliserDossier("  Qualité "));

        Assert.Equal("Bytel Kyntus|Orange", ProcessDocuments.Joindre(new[] { "Bytel Kyntus", " Orange ", "Orange", "" }));
        Assert.Equal(new[] { "p1", "p2" }, ProcessDocuments.Decouper(" p1 | p2 | "));
        Assert.Empty(ProcessDocuments.Decouper(null));
    }

    [Fact]
    public void Document_pour_tous_visible_meme_sans_pole()
    {
        Assert.True(ProcessDocumentService.EstVisible(true, string.Empty, null));
        Assert.True(ProcessDocumentService.EstVisible(true, string.Empty, "p1"));
    }

    [Fact]
    public void Document_reserve_a_une_activite_visible_par_ses_poles_seulement()
    {
        Assert.True(ProcessDocumentService.EstVisible(false, "p1|p2", "p2"));
        Assert.True(ProcessDocumentService.EstVisible(false, "p1|p2", "P2"));
        Assert.False(ProcessDocumentService.EstVisible(false, "p1|p2", "p9"));
        // Agent sans pôle (en formation, support) : seulement les documents « pour tous ».
        Assert.False(ProcessDocumentService.EstVisible(false, "p1|p2", null));
    }
}
