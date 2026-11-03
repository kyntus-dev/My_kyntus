using Planning.Application.PlanningIt;
using Planning.Application.Pointage;
using Planning.Domain.Entities;
using Xunit;

namespace Planning.UnitTests;

/// <summary>Planning IT : droits de saisie, semaine et référence du pointage.</summary>
public class PlanningItTests
{
    private static readonly PlanningItDroits Membre = new(true, true, false);
    private static readonly PlanningItDroits Admin = new(true, false, true);
    private static readonly PlanningItDroits Lecteur = new(true, false, false);

    [Fact]
    public void Un_membre_ne_modifie_que_sa_ligne_l_Admin_corrige_tout()
    {
        var saisie = new PlanningItCaseRequest(7, new DateOnly(2026, 11, 4), PlanningItStatuts.Travail, "09:00");
        Assert.Null(PlanningItRegles.Controler(7, Membre, saisie));
        Assert.NotNull(PlanningItRegles.Controler(8, Membre, saisie));
        Assert.Null(PlanningItRegles.Controler(1, Admin, saisie));
        Assert.NotNull(PlanningItRegles.Controler(7, Lecteur, saisie));
    }

    [Fact]
    public void Saisie_controlee_et_toujours_modifiable()
    {
        var j = new DateOnly(2026, 11, 4);
        Assert.NotNull(PlanningItRegles.Controler(7, Membre, new(7, j, PlanningItStatuts.Travail, null)));
        Assert.NotNull(PlanningItRegles.Controler(7, Membre, new(7, j, PlanningItStatuts.Travail, "9h")));
        Assert.NotNull(PlanningItRegles.Controler(7, Membre, new(7, j, "Malade", null)));
        Assert.Null(PlanningItRegles.Controler(7, Membre, new(7, j, PlanningItStatuts.Repos, null)));
        Assert.Null(PlanningItRegles.Controler(7, Membre, new(7, j, null, null))); // effacer la case
        Assert.Equal(new TimeOnly(9, 15), PlanningItRegles.LireHeure("9:15"));
        Assert.Equal(new DateOnly(2026, 11, 2), PlanningItRegles.Lundi(new DateOnly(2026, 11, 7)));
    }

    [Fact]
    public void Le_planning_IT_est_la_reference_du_pointage()
    {
        var mercredi = new DateOnly(2026, 11, 4);
        var vendredi = new DateOnly(2026, 11, 6);

        var travail = RegimeResolver.PlanningDuJourIt(PlanningItStatuts.Travail, new TimeOnly(9, 0), null, mercredi, 80)!;
        Assert.Equal(new TimeOnly(9, 0), travail.Debut);
        Assert.Equal(520, travail.ProductionPrevueMinutes);
        Assert.Equal(PlanningSources.PlanningIt, travail.Source);
        Assert.Equal(460, RegimeResolver.PlanningDuJourIt(PlanningItStatuts.Travail, new TimeOnly(9, 0), null, vendredi, 80)!.ProductionPrevueMinutes);

        Assert.Null(RegimeResolver.PlanningDuJourIt(PlanningItStatuts.Repos, null, null, mercredi, 80));
        Assert.True(RegimeResolver.PlanningDuJourIt(PlanningItStatuts.Conge, null, null, mercredi, 80)!.Conge);

        // Arrivée saisie 09:00, badge 09:12 : 12 minutes de retard d'entrée.
        var r = PointageCalculator.CalculerJour(mercredi, [mercredi.ToDateTime(new TimeOnly(9, 12)), mercredi.ToDateTime(new TimeOnly(18, 30))],
            travail, mercredi.AddDays(1));
        Assert.Equal(12, r.RetardEntreeMinutes);
        Assert.Equal(PointageJourStatuts.Retard, r.Statut);
    }
}
