using Planning.Application.Presence;

namespace Planning.UnitTests;

/// <summary>
/// v37 : le référentiel du serveur est contrôlé ligne à ligne contre le PDF de la direction
/// (« Référentiel unifié – discipline, prime variable, qualité et process »).
/// Même tableau que manquements.util.spec.ts côté interface.
/// </summary>
public class ReferentielPdfTests
{
    private const string Pdf =
        "R01 50 100 150 1|R02 100 150 200 2|R03 200 250 300 3|P01 50 100 200 1|P02 50 100 200 1|P03 100 200 300 2|P04 50 100 200 1|" +
        "C01 100 150 200 2|C02 50 100 200 1|C03 50 100 200 3|C04 50 100 200 2|C05 50 100 200 1|C06 50 100 200 1|C07 30 60 100 1|" +
        "M01 100 250 500 2|M02 500 500 500 3|M03 100 200 300 2|G01 100 200 500 2|G02 - - - 1 à 3|G03 - - - 3|G04 100 250 500 3|" +
        "G05 500 500 500 3|Q01 50 100 200 1|Q02 50 100 200 1|Q03 50 150 250 1 à 2|Q04 250 400 500 3|Q05 100 250 500 1 à 3|" +
        "Q06 50 100 200 1|Q07 150 300 500 2 à 3|Q08 50 100 200 1|Q09 250 400 500 3|Q10 100 200 300 2|Q11 50 100 200 1|" +
        "Q12 50 100 200 1|Q13 50 100 200 1";

    private static decimal? Montant(string v) => v == "-" ? null : decimal.Parse(v);

    [Fact]
    public void Referentiel_identique_au_pdf()
    {
        var lignes = Pdf.Split('|');
        Assert.Equal(35, lignes.Length);
        Assert.Equal(lignes.Select(l => l.Split(' ')[0]), Referentiel.Manquements.Select(m => m.Code));
        foreach (var l in lignes)
        {
            var p = l.Split(' ');
            var m = Referentiel.Trouver(p[0]);
            Assert.NotNull(m);
            Assert.Equal(Montant(p[1]), m!.Mois1);
            Assert.Equal(Montant(p[2]), m.Mois2);
            Assert.Equal(Montant(p[3]), m.Mois3);
            Assert.Equal(string.Join(' ', p.Skip(4)), m.Gravite);
        }
    }
}
