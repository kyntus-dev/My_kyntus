using Planning.Application.Pointage;
using Planning.Domain.Entities;
using Xunit;

namespace Planning.UnitTests;

/// <summary>
/// Régimes horaires (heures fixes définies par l'Admin) et indicateur de production
/// (départ − arrivée − pauses).
/// </summary>
public class RegimeHoraireTests
{
    private static readonly DateOnly Lundi = new(2026, 10, 26);
    private static readonly DateOnly Vendredi = new(2026, 10, 30);
    private static readonly DateOnly Samedi = new(2026, 10, 31);

    private static RegimeEffectif Standard() => new(
        1,
        "Standard 8h40 / vendredi 7h40",
        80,
        true,
        new Dictionary<int, (TimeOnly, int)>
        {
            [1] = (new TimeOnly(8, 0), 520),
            [2] = (new TimeOnly(8, 0), 520),
            [3] = (new TimeOnly(8, 0), 520),
            [4] = (new TimeOnly(8, 0), 520),
            [5] = (new TimeOnly(8, 0), 460),
        },
        RegimeCibles.Pole);

    [Fact]
    public void La_cible_la_plus_precise_l_emporte()
    {
        var affectations = new Dictionary<(string, int), int>
        {
            [(RegimeCibles.Pole, 6)] = 1,
            [(RegimeCibles.Cellule, 40)] = 2,
            [(RegimeCibles.Service, 300)] = 3,
            [(RegimeCibles.Utilisateur, 7)] = 4,
        };

        Assert.Equal((4, RegimeCibles.Utilisateur), RegimeResolver.Resoudre(7, 300, 40, 6, affectations));
        Assert.Equal((3, RegimeCibles.Service), RegimeResolver.Resoudre(8, 300, 40, 6, affectations));
        Assert.Equal((2, RegimeCibles.Cellule), RegimeResolver.Resoudre(8, 301, 40, 6, affectations));
        Assert.Equal((1, RegimeCibles.Pole), RegimeResolver.Resoudre(8, 301, 41, 6, affectations));
        Assert.Null(RegimeResolver.Resoudre(8, 301, 41, 5, affectations));
        Assert.Null(RegimeResolver.Resoudre(8, null, null, null, affectations));
    }

    [Fact]
    public void Planning_du_regime_8h40_puis_7h40_le_vendredi_et_samedi_non_travaille()
    {
        var lundi = RegimeResolver.PlanningDuJour(Standard(), Lundi);
        Assert.NotNull(lundi);
        // v44 : l'heure d'arrivée vient du planning publié, pas du régime.
        Assert.Null(lundi!.Debut);
        Assert.Equal(520, lundi.ProductionPrevueMinutes);
        Assert.Equal(80, lundi.PauseMinutes);
        Assert.Equal(PlanningSources.Regime, lundi.Source);

        Assert.Equal(460, RegimeResolver.PlanningDuJour(Standard(), Vendredi)!.ProductionPrevueMinutes);
        Assert.Null(RegimeResolver.PlanningDuJour(Standard(), Samedi));
    }

    [Fact]
    public void Production_depart_moins_arrivee_moins_pauses()
    {
        // Exemple du cahier des charges : 08:00 → 17:00 avec 1 h de pause = 8 h.
        var r = PointageCalculator.CalculerJour(
            Lundi,
            [At(Lundi, "08:00"), At(Lundi, "12:00"), At(Lundi, "13:00"), At(Lundi, "17:00")],
            RegimeResolver.PlanningDuJour(Standard(), Lundi),
            Lundi.AddDays(1));

        Assert.Equal(60, r.PauseMinutes);
        Assert.Equal(480, r.ProductionMinutes);
        Assert.Equal(520, r.ProductionPrevueMinutes);
        Assert.Equal(-40, r.EcartProductionMinutes);
        // v44 : sans planning publié, pas d'heure d'arrivée de référence.
        Assert.Equal(PointageJourStatuts.SansPlanning, r.Statut);
        Assert.Equal(0, r.RetardEntreeMinutes);
    }

    [Fact]
    public void Pause_de_1h20_comme_l_Excel_le_depassement_compte_en_retard_pause()
    {
        // 08:00 → 18:05, pauses 10 + 60 + 19 = 89 min pour 80 autorisées : 9 min de retard pause.
        var r = PointageCalculator.CalculerJour(
            Lundi,
            [
                At(Lundi, "08:00"), At(Lundi, "10:00"), At(Lundi, "10:10"), At(Lundi, "12:30"),
                At(Lundi, "13:30"), At(Lundi, "15:30"), At(Lundi, "15:49"), At(Lundi, "18:05"),
            ],
            RegimeResolver.PlanningDuJour(Standard(), Lundi),
            Lundi.AddDays(1));

        Assert.Equal(89, r.PauseMinutes);
        Assert.Equal(9, r.RetardPauseMinutes);
        Assert.Equal(516, r.ProductionMinutes);
    }

    [Fact]
    public void Production_non_calculee_si_pointage_manquant_et_totaux()
    {
        var plan = RegimeResolver.PlanningDuJour(Standard(), Lundi);
        var manquant = PointageCalculator.CalculerJour(Lundi, [At(Lundi, "08:00"), At(Lundi, "12:00"), At(Lundi, "13:00")], plan, Lundi.AddDays(1));
        Assert.Null(manquant.ProductionMinutes);

        var complet = PointageCalculator.CalculerJour(Lundi.AddDays(1),
            [At(Lundi.AddDays(1), "08:00"), At(Lundi.AddDays(1), "18:00")], RegimeResolver.PlanningDuJour(Standard(), Lundi.AddDays(1)), Lundi.AddDays(2));
        var sansPointage = PointageCalculator.CalculerJour(Lundi.AddDays(2), [], RegimeResolver.PlanningDuJour(Standard(), Lundi.AddDays(2)), Lundi.AddDays(3));

        var t = PointageCalculator.Totaliser([manquant, complet, sansPointage]);
        Assert.Equal(600, t.ProductionMinutes);
        // La production prévue ne compte que les jours dont la production est calculée.
        Assert.Equal(520, t.ProductionPrevueMinutes);
        Assert.Equal(1, t.JoursProductionCalculee);
    }

    [Fact]
    public void Production_de_l_Excel_quand_aucun_regime()
    {
        Assert.Equal(520, RegimeResolver.ProductionExcelMinutes(Lundi));
        Assert.Equal(460, RegimeResolver.ProductionExcelMinutes(Vendredi));
        // v65 : le samedi ne vaut plus 07h40 d'office. Il n'est une journée de production que s'il
        // figure au planning de l'agent, et la production vient alors de ce planning. Hors planning,
        // rien n'est attendu : les heures pointées sont des heures supplémentaires.
        Assert.Equal(0, RegimeResolver.ProductionExcelMinutes(Samedi));
        Assert.Equal(80, RegimeResolver.PauseExcelMinutes);
    }

    [Fact]
    public void Controle_d_un_regime_saisi()
    {
        var ok = new RegimeHoraireRequest("Pilotes", 80, true, [new RegimeJourDto(1, "08:00", 520), new RegimeJourDto(5, "08:00", 460)]);
        Assert.Null(RegimeResolver.Valider(ok));
        Assert.NotNull(RegimeResolver.Valider(ok with { Nom = " " }));
        Assert.NotNull(RegimeResolver.Valider(ok with { Jours = [] }));
        Assert.NotNull(RegimeResolver.Valider(ok with { Jours = [new RegimeJourDto(1, "08:00", 520), new RegimeJourDto(1, "09:00", 520)] }));
        Assert.NotNull(RegimeResolver.Valider(ok with { Jours = [new RegimeJourDto(8, "08:00", 520)] }));
        Assert.NotNull(RegimeResolver.Valider(ok with { Jours = [new RegimeJourDto(1, "abc", 520)] }));
        Assert.NotNull(RegimeResolver.Valider(ok with { Jours = [new RegimeJourDto(1, "08:00", 0)] }));
        Assert.NotNull(RegimeResolver.Valider(ok with { PauseAutoriseeMinutes = 0 }));
    }

    private static DateTime At(DateOnly d, string hhmm) => d.ToDateTime(TimeOnly.Parse(hhmm));

    [Fact]
    public void Jour_du_regime_sans_pointage_reste_une_absence_v44()
    {
        var r = PointageCalculator.CalculerJour(Lundi, [], RegimeResolver.PlanningDuJour(Standard(), Lundi), Lundi.AddDays(1));
        Assert.Equal(PointageJourStatuts.SansPointage, r.Statut);
        var samedi = PointageCalculator.CalculerJour(Samedi, [], RegimeResolver.PlanningDuJour(Standard(), Samedi), Samedi.AddDays(1));
        Assert.Equal(PointageJourStatuts.Repos, samedi.Statut);
    }

    /// <summary>v65 — Le samedi suit le planning de l'agent, et non un planning commun.</summary>
    [Fact]
    public void Samedi_du_regime_de_l_agent_est_un_jour_travaille_v65()
    {
        var regimeAvecSamedi = Standard() with
        {
            Jours = new Dictionary<int, (TimeOnly, int)>(Standard().Jours) { [6] = (new TimeOnly(8, 0), 460) },
        };
        Assert.True(RegimeResolver.JourTravailleDuRegime(regimeAvecSamedi, Samedi));
        Assert.False(RegimeResolver.JourTravailleDuRegime(Standard(), Samedi));
        Assert.True(RegimeResolver.JourTravailleDuRegime(Standard(), Lundi));
        Assert.False(RegimeResolver.JourTravailleDuRegime(null, Lundi));

        // Samedi au planning de l'agent : journée de production, comparée à sa production prévue.
        var planifie = RegimeResolver.PlanningDuJour(regimeAvecSamedi, Samedi);
        Assert.NotNull(planifie);
        var travaille = PointageCalculator.CalculerJour(
            Samedi, [At(Samedi, "08:00"), At(Samedi, "16:50")], planifie, Samedi.AddDays(1));
        Assert.Equal(460, travaille.ProductionPrevueMinutes ?? -1);
        Assert.True(travaille.Planifie);
        // Pas de double comptage : rien en heures supplémentaires pour un samedi déjà prévu.
        Assert.Null(travaille.HeuresSupplementairesMinutes);
    }

    /// <summary>
    /// v65 — Samedi hors planning effectivement travaillé : heures supplémentaires, et jamais
    /// d'heures manquantes (cas signalé : 12:43 → 13:50 comparé à 08h40 « horaire de base »).
    /// </summary>
    [Fact]
    public void Samedi_hors_planning_travaille_donne_des_heures_supplementaires_v65()
    {
        // Jour de repos de l'agent : production prévue nulle (Planifie = false).
        var horsPlanning = new PlanningJour(null, 80, false, false, null, 0, null, Planifie: false);
        var r = PointageCalculator.CalculerJour(
            Samedi, [At(Samedi, "12:43"), At(Samedi, "13:50")], horsPlanning, Samedi.AddDays(2));

        Assert.Equal(PointageJourStatuts.HeuresSupplementaires, r.Statut);
        Assert.False(r.Planifie);
        Assert.Equal(67, r.ProductionMinutes ?? -1);
        Assert.Equal(0, r.ProductionPrevueMinutes ?? -1);
        // L'écart est positif : 1 h 07 en plus, et non 7 h 33 manquantes.
        Assert.Equal(67, r.EcartProductionMinutes ?? -1);
        Assert.Equal(67, r.HeuresSupplementairesMinutes ?? -1);
        Assert.Equal(0, r.RetardTotalMinutes);

        // Sans pointage, le même jour reste un repos : jamais une absence.
        var sansPointage = PointageCalculator.CalculerJour(Samedi, [], horsPlanning, Samedi.AddDays(2));
        Assert.Equal(PointageJourStatuts.Repos, sansPointage.Statut);
        Assert.Null(sansPointage.HeuresSupplementairesMinutes);

        // Totaux : les heures supplémentaires sont comptées à part de la production prévue.
        var totaux = PointageCalculator.Totaliser([r, sansPointage]);
        Assert.Equal(67, totaux.HeuresSupplementairesMinutes);
        Assert.Equal(1, totaux.JoursHeuresSupplementaires);
        Assert.Equal(0, totaux.ProductionPrevueMinutes);
    }
}
