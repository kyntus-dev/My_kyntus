using Planning.Application.PlanningIt;
using Planning.Application.Pointage;
using Xunit;

namespace Planning.UnitTests;

/// <summary>
/// v70 : manque de pointage (seules les heures pointées, hors solde) ; Récup du planning IT qui
/// consomme le solde depuis le 24.
/// </summary>
public class PlanningV70Tests
{
    private static readonly DateOnly Mardi = new(2026, 11, 24);
    private static readonly DateOnly Lendemain = new(2026, 11, 25);
    private static DateTime T(DateOnly j, int h, int m) => j.ToDateTime(new TimeOnly(h, m));
    private static PlanningJour It930() => new(new TimeOnly(9, 30), 80, false, false, "Planning IT", 520, PlanningSources.PlanningIt);

    [Fact]
    public void Depart_avant_heure_prevue_est_un_manque_de_pointage_hors_solde()
    {
        // Cas de la capture : 08:24 → 08:39 pour une arrivée prévue à 09:30.
        var r = PointageCalculator.CalculerJour(Mardi, [T(Mardi, 8, 24), T(Mardi, 8, 39)], It930(), Lendemain);
        Assert.Equal(PointageJourStatuts.PointageManquant, r.Statut);
        Assert.Equal(15, r.ProductionPointeeMinutes);
        Assert.Null(r.ProductionMinutes);
        Assert.Null(r.EcartProductionMinutes);
        var t = PointageCalculator.Totaliser([r]);
        Assert.Equal(0, t.ProductionMinutes);
        Assert.Equal(0, t.ProductionPrevueMinutes);
        Assert.Equal([Mardi], t.JoursPointageManquant);
    }

    [Fact]
    public void Meme_situation_aujourd_hui_reste_en_cours()
    {
        var r = PointageCalculator.CalculerJour(Mardi, [T(Mardi, 8, 24), T(Mardi, 8, 39)], It930(), Mardi);
        Assert.Equal(PointageJourStatuts.EnCours, r.Statut);
        Assert.Null(r.ProductionMinutes);
    }

    [Fact]
    public void Nombre_impair_seuls_les_segments_pointes_sont_comptes()
    {
        // Arrivée 09:25, sortie pause 13:00, retour 13:40, départ oublié : 3 h 35 pointées.
        var r = PointageCalculator.CalculerJour(Mardi, [T(Mardi, 9, 25), T(Mardi, 13, 0), T(Mardi, 13, 40)], It930(), Lendemain);
        Assert.Equal(PointageJourStatuts.PointageManquant, r.Statut);
        Assert.Equal(215, r.ProductionPointeeMinutes);
        Assert.Null(r.EcartProductionMinutes);
    }

    [Fact]
    public void Journee_complete_inchangee()
    {
        var r = PointageCalculator.CalculerJour(Mardi,
            [T(Mardi, 9, 28), T(Mardi, 13, 0), T(Mardi, 14, 20), T(Mardi, 19, 30)], It930(), Lendemain);
        Assert.Equal(PointageJourStatuts.AHeure, r.Statut);
        Assert.Equal(522, r.ProductionMinutes);
        Assert.Equal(2, r.EcartProductionMinutes);
        Assert.Null(r.ProductionPointeeMinutes);
        // Départ anticipé APRÈS l'heure prévue d'arrivée : journée calculée comme avant.
        var court = PointageCalculator.CalculerJour(Mardi, [T(Mardi, 9, 28), T(Mardi, 15, 0)], It930(), Lendemain);
        Assert.Equal(PointageJourStatuts.AHeure, court.Statut);
        Assert.Equal(332, court.ProductionMinutes);
    }

    [Fact]
    public void Recup_consomme_le_solde_cumule_jamais_remis_a_zero_le_24()
    {
        PointageJourDto J(DateOnly d, string statut, int? ecart = null, int? recup = null, int? hs = null) =>
            new(d, statut, null, null, 0, null, null, [], 0, 0, 0, 0,
                EcartProductionMinutes: ecart, HeuresSupplementairesMinutes: hs, RecupMinutes: recup);
        var aujourdhui = new DateOnly(2026, 12, 2);
        var jours = new[]
        {
            J(new DateOnly(2026, 10, 20), PointageJourStatuts.AHeure, ecart: 999),           // avant le début du solde
            J(new DateOnly(2026, 11, 10), PointageJourStatuts.AHeure, ecart: 120),           // période précédente : gardé
            J(new DateOnly(2026, 11, 24), PointageJourStatuts.AHeure, ecart: 30),
            J(new DateOnly(2026, 11, 28), PointageJourStatuts.HeuresSupplementaires, ecart: 480, hs: 480),
            J(new DateOnly(2026, 11, 30), PointageJourStatuts.PointageManquant),               // hors solde
            J(new DateOnly(2026, 12, 1), PointageJourStatuts.Recup, recup: 520),
            J(new DateOnly(2027, 1, 10), PointageJourStatuts.Recup, recup: 520),               // planifiée
        };
        var s = PlanningItRegles.Solde(7, jours, aujourdhui);
        Assert.Equal(new DateOnly(2026, 10, 24), s.PeriodeDu);
        Assert.Equal(120 + 30 + 480 - 520, s.SoldePeriodeMinutes);
        Assert.Equal(520, s.RecupPlanifieesPeriodeMinutes);
    }
}
