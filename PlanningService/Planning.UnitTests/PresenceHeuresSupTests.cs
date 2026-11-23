using Planning.Application.Pointage;
using Planning.Application.Presence;

namespace Planning.UnitTests;

/// <summary>v61 : heures supplémentaires au-delà de la fin prévue, par blocs de 30 minutes.</summary>
public class PresenceHeuresSupTests
{
    private static readonly DateOnly Lundi = new(2026, 11, 2);
    private static readonly DateOnly Dimanche = new(2026, 11, 8);
    private static readonly DateOnly Armistice = new(2026, 11, 11);

    private static HeuresSupJour Calcul(DateOnly jour, string? code, params string[] pointages) =>
        PresenceRegles.HeuresSupplementaires(jour, PointageJourStatuts.AHeure, code, "09:00", 80, 520, pointages);

    [Fact]
    public void Sortie_apres_la_fin_prevue_compte_par_tranches_de_30_minutes()
    {
        // 09:00 + 8h40 de production + 1h20 de pause = fin prévue 19:00 ; sortie 20:10 → 1 h.
        var hs = Calcul(Lundi, "P", "09:00:00", "13:00:00", "14:20:00", "20:10:00");
        Assert.Equal("19:00", hs.FinPrevue);
        Assert.Equal(60, hs.Avant21Minutes);
        Assert.Equal(0, hs.Apres21Minutes);
        Assert.Equal(0, hs.DimancheFerieMinutes);
    }

    [Fact]
    public void Sortie_apres_21h_est_partagee_entre_avant_et_apres_21h()
    {
        var hs = Calcul(Lundi, null, "09:00:00", "22:15:00");
        Assert.Equal(120, hs.Avant21Minutes);
        Assert.Equal(60, hs.Apres21Minutes);
    }

    [Fact]
    public void Sortie_avant_la_fin_prevue_ou_depassement_de_moins_de_30_minutes_ne_compte_pas()
    {
        Assert.Equal(0, Calcul(Lundi, "P", "09:00:00", "18:30:00").Avant21Minutes);
        Assert.Equal(0, Calcul(Lundi, "P", "09:00:00", "19:29:00").Avant21Minutes);
    }

    [Fact]
    public void Dimanche_et_ferie_comptent_tout_le_temps_travaille()
    {
        var dimanche = Calcul(Dimanche, null, "09:00:00", "13:00:00", "14:00:00", "16:45:00");
        Assert.Equal(390, dimanche.DimancheFerieMinutes);
        Assert.Equal(0, dimanche.Avant21Minutes);

        var ferie = PresenceRegles.HeuresSupplementaires(
            Armistice, PointageJourStatuts.Ferie, "F", null, 0, null, ["10:00:00", "14:00:00"]);
        Assert.Equal(240, ferie.DimancheFerieMinutes);
    }

    [Fact]
    public void Absence_journee_incomplete_ou_sans_planning_ne_donnent_rien()
    {
        Assert.Equal(HeuresSupJour.Aucune, Calcul(Lundi, "ABS N", "09:00:00", "22:00:00"));
        Assert.Equal(HeuresSupJour.Aucune, Calcul(Lundi, "P", "09:00:00", "13:00:00", "22:00:00"));
        Assert.Equal(HeuresSupJour.Aucune, PresenceRegles.HeuresSupplementaires(
            Lundi, PointageJourStatuts.SansPlanning, "P", null, 80, 520, ["09:00:00", "22:00:00"]));
    }
}
