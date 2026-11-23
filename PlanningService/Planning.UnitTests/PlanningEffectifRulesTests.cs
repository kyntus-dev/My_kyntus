using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>v61 — Synchronisation de l'effectif des plannings (agents sortis / arrivés).</summary>
public class PlanningEffectifRulesTests
{
    private static readonly DateOnly Lundi = new(2026, 11, 23);

    [Fact]
    public void New_agent_appears_from_his_entry_date_only()
    {
        var agent = new PlanningEffectifRules.Agent(7, true, 3, Lundi.AddDays(2), null);
        Assert.False(PlanningEffectifRules.DoitFigurer(agent, 3, Lundi));
        Assert.True(PlanningEffectifRules.DoitFigurer(agent, 3, Lundi.AddDays(2)));
        Assert.False(PlanningEffectifRules.DoitFigurer(agent, 4, Lundi.AddDays(2)));
    }

    [Fact]
    public void Departed_agent_is_hidden_after_exit_date_but_history_stays()
    {
        var sortie = Lundi.AddDays(1);
        Assert.False(PlanningEffectifRules.MasqueeApresSortie(false, sortie, Lundi, Lundi));
        Assert.False(PlanningEffectifRules.MasqueeApresSortie(false, sortie, sortie, Lundi));
        Assert.True(PlanningEffectifRules.MasqueeApresSortie(false, sortie, sortie.AddDays(1), Lundi));
        // Désactivé sans date : masqué à partir d'aujourd'hui, le passé reste visible.
        Assert.False(PlanningEffectifRules.MasqueeApresSortie(false, null, Lundi.AddDays(-1), Lundi));
        Assert.True(PlanningEffectifRules.MasqueeApresSortie(false, null, Lundi, Lundi));
        // Actif sans date de sortie : jamais masqué.
        Assert.False(PlanningEffectifRules.MasqueeApresSortie(true, null, Lundi.AddDays(3), Lundi));
    }

    [Fact]
    public void Added_agent_takes_the_shift_that_lacks_the_most_people()
    {
        var choix = PlanningEffectifRules.ChoisirCreneau(
        [
            new PlanningEffectifRules.Creneau(1, 10, 10, 0),
            new PlanningEffectifRules.Creneau(2, 8, 5, 1),
            new PlanningEffectifRules.Creneau(3, 6, 4, 2),
        ]);
        Assert.Equal(2, choix);
        Assert.Null(PlanningEffectifRules.ChoisirCreneau([]));
    }

    [Fact]
    public void Added_agent_takes_the_least_busy_break()
    {
        var douze = new TimeOnly(12, 0);
        var treize = new TimeOnly(13, 0);
        var pause = PlanningEffectifRules.ChoisirPause([douze, treize], new Dictionary<TimeOnly, int> { [douze] = 5, [treize] = 2 });
        Assert.Equal(treize, pause);
        Assert.Null(PlanningEffectifRules.ChoisirPause([], new Dictionary<TimeOnly, int>()));
    }
}
