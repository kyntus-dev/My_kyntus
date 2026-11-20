using Microsoft.EntityFrameworkCore;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>v63 : affectation de référence (réaffectations) et arrêts maladie dans tous les plannings.</summary>
public class AffectationEtAbsencesV63Tests
{
    [Fact]
    public void Une_synchro_ne_remet_jamais_l_agent_dans_un_service_quitte()
    {
        // Réaffecté de 10 vers 20 (plus récent en premier).
        var historique = new List<(int? Ancien, int? Nouveau)> { (10, 20) };
        Assert.True(AffectationDeReference.RetourInterdit(20, 10, historique));
        // Vers un troisième service (mouvement légitime d'Organisation RH) : accepté.
        Assert.False(AffectationDeReference.RetourInterdit(20, 30, historique));
        // Sans réaffectation, ou affectation actuelle qui n'en vient pas : rien à protéger.
        Assert.False(AffectationDeReference.RetourInterdit(20, 10, []));
        Assert.False(AffectationDeReference.RetourInterdit(30, 10, historique));
        // Retour volontaire par une nouvelle réaffectation (20 → 10) : 10 devient la référence.
        var retour = new List<(int? Ancien, int? Nouveau)> { (20, 10), (10, 20) };
        Assert.True(AffectationDeReference.RetourInterdit(10, 20, retour));
        Assert.False(AffectationDeReference.RetourInterdit(10, null, retour));
    }

    [Fact]
    public async Task Synchro_annuaire_refusee_apres_plusieurs_reaffectations()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.ReaffectationsAgents.AddRange(
            new ReaffectationAgent { UserId = 5, AncienSubServiceId = 10, NouveauSubServiceId = 20, NouveauLibelle = "B", CreatedAt = new DateTime(2026, 10, 1) },
            new ReaffectationAgent { UserId = 5, AncienSubServiceId = 20, NouveauSubServiceId = 30, NouveauLibelle = "C", CreatedAt = new DateTime(2026, 11, 1) });
        await db.SaveChangesAsync();

        Assert.True(await AffectationDeReference.RetourInterditAsync(db, 5, 30, 10));
        Assert.True(await AffectationDeReference.RetourInterditAsync(db, 5, 30, 20));
        Assert.False(await AffectationDeReference.RetourInterditAsync(db, 5, 30, 40));
        Assert.False(await AffectationDeReference.RetourInterditAsync(db, 6, 30, 10));
    }

    [Fact]
    public void Agent_reaffecte_quitte_les_jours_a_venir_sauf_renfort()
    {
        var agent = new PlanningEffectifRules.Agent(5, true, 20, null, null);
        Assert.True(PlanningEffectifRules.ReaffecteAilleurs(agent, 10, renfortOuExceptionnel: false));
        Assert.False(PlanningEffectifRules.ReaffecteAilleurs(agent, 10, renfortOuExceptionnel: true));
        Assert.False(PlanningEffectifRules.ReaffecteAilleurs(agent, 20, renfortOuExceptionnel: false));
    }

    [Fact]
    public async Task Arret_maladie_applique_a_toutes_les_semaines_et_conserve_apres_regeneration()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var lundi = new DateOnly(2026, 11, 9);
        db.WeeklyPlannings.AddRange(
            new WeeklyPlanning { Id = 1, SubServiceId = 10, WeekCode = "S46", WeekStartDate = lundi, Status = PlanningStatus.Published },
            new WeeklyPlanning { Id = 2, SubServiceId = 10, WeekCode = "S47", WeekStartDate = lundi.AddDays(7), Status = PlanningStatus.Draft });
        db.ShiftAssignments.AddRange(
            new ShiftAssignment { WeeklyPlanningId = 1, UserId = 7, AssignedDate = lundi.AddDays(3), DayOfWeek = DayOfWeekEnum.Thursday, ShiftId = 1 },
            new ShiftAssignment { WeeklyPlanningId = 2, UserId = 7, AssignedDate = lundi.AddDays(8), DayOfWeek = DayOfWeekEnum.Tuesday, ShiftId = 1 },
            new ShiftAssignment { WeeklyPlanningId = 2, UserId = 8, AssignedDate = lundi.AddDays(8), DayOfWeek = DayOfWeekEnum.Tuesday, ShiftId = 1 });
        db.PresenceJours.AddRange(
            new PresenceJour { UserId = 7, Jour = lundi.AddDays(3), Code = "AM" },
            new PresenceJour { UserId = 7, Jour = lundi.AddDays(8), Code = "AM" },
            new PresenceJour { UserId = 8, Jour = lundi.AddDays(8), Code = "ABS", AbsencePartielle = true });
        await db.SaveChangesAsync();

        Assert.Equal(2, await PresenceAbsencesPlanning.MarquerToutesAbsencesAsync(db, lundi.AddDays(-90)));
        Assert.Equal(0, await PresenceAbsencesPlanning.MarquerToutesAbsencesAsync(db, lundi.AddDays(-90)));
        var lignes = await db.ShiftAssignments.AsNoTracking().ToListAsync();
        Assert.All(lignes.Where(a => a.UserId == 7), a => Assert.True(a.IsOnLeave));
        Assert.False(lignes.Single(a => a.UserId == 8).IsOnLeave);

        // Génération / régénération : le motif devient un congé virtuel, donc aucun shift.
        var virtuels = await PresenceAbsencesPlanning.CongesVirtuelsAsync(db, [7, 8], lundi, lundi.AddDays(13));
        Assert.Equal(2, virtuels.Count);
        Assert.All(virtuels, c => Assert.Equal(AbsenceType.ArretMaladie, c.AbsenceType));
    }
}
