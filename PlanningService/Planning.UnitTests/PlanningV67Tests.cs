using Planning.Application.PlanningIt;
using Planning.Application.Pointage;
using Planning.Application.Presence;
using Planning.Application.Users;
using Planning.Domain.Entities;
using Xunit;

namespace Planning.UnitTests;

/// <summary>v67 : Récup (planning IT), solde d'heures supplémentaires, suppression définitive.</summary>
public class PlanningV67Tests
{
    private static readonly PlanningItDroits Membre = new(true, true, false);
    private static readonly DateOnly Mercredi = new(2026, 11, 4);
    private static readonly DateOnly Samedi = new(2026, 11, 7);

    private static DateTime T(DateOnly j, int h, int m) => j.ToDateTime(new TimeOnly(h, m));

    [Fact]
    public void Recup_est_un_etat_valide_du_planning_IT()
    {
        Assert.True(PlanningItStatuts.EstValide(PlanningItStatuts.Recup));
        Assert.Null(PlanningItRegles.Controler(7, Membre, new(7, Mercredi, PlanningItStatuts.Recup, null)));
    }

    [Fact]
    public void Recup_sans_production_attendue_deduit_la_production_du_jour()
    {
        var recup = RegimeResolver.PlanningDuJourIt(PlanningItStatuts.Recup, null, null, Mercredi, 80)!;
        Assert.False(recup.Planifie);
        Assert.Equal(0, recup.ProductionPrevueMinutes);
        Assert.Equal(520, recup.RecupMinutes); // 08h40 du lundi au jeudi (règle de l'Excel)
        Assert.Equal(460, RegimeResolver.PlanningDuJourIt(PlanningItStatuts.Recup, null, null, Samedi, 80)!.RecupMinutes);
        // Les autres états ne sont pas des récupérations.
        Assert.Null(RegimeResolver.PlanningDuJourIt(PlanningItStatuts.Travail, new TimeOnly(9, 0), null, Mercredi, 80)!.RecupMinutes);
    }

    [Fact]
    public void Journee_de_recup_non_pointee_jamais_absente_et_deduite()
    {
        var recup = RegimeResolver.PlanningDuJourIt(PlanningItStatuts.Recup, null, null, Mercredi, 80);
        var passe = PointageCalculator.CalculerJour(Mercredi, [], recup, Mercredi.AddDays(3));
        Assert.Equal(PointageJourStatuts.Recup, passe.Statut);
        Assert.Equal(520, passe.RecupMinutes);
        Assert.Null(passe.HeuresSupplementairesMinutes);
        // Récup déjà planifiée (jour à venir) : comptée aussi.
        var aVenir = PointageCalculator.CalculerJour(Mercredi, [], recup, Mercredi.AddDays(-2));
        Assert.Equal(PointageJourStatuts.Recup, aVenir.Statut);

        var totaux = PointageCalculator.Totaliser([passe]);
        Assert.Equal(520, totaux.RecupMinutes);
        Assert.Equal(1, totaux.JoursRecup);
        Assert.Empty(totaux.JoursSansPointage);
        Assert.Equal(0, totaux.ProductionPrevueMinutes);
    }

    [Fact]
    public void Recup_pointee_quand_meme_les_heures_partent_en_heures_sup()
    {
        var recup = RegimeResolver.PlanningDuJourIt(PlanningItStatuts.Recup, null, null, Mercredi, 80);
        var r = PointageCalculator.CalculerJour(Mercredi, [T(Mercredi, 9, 0), T(Mercredi, 12, 0)], recup, Mercredi.AddDays(1));
        Assert.Equal(PointageJourStatuts.HeuresSupplementaires, r.Statut);
        Assert.Equal(180, r.HeuresSupplementairesMinutes);
        Assert.Equal(520, r.RecupMinutes);
    }

    [Fact]
    public void Recup_un_jour_de_conge_ou_ferie_ne_deduit_rien()
    {
        var recup = RegimeResolver.PlanningDuJourIt(PlanningItStatuts.Recup, null, null, Mercredi, 80)! with { Conge = true };
        var r = PointageCalculator.CalculerJour(Mercredi, [], recup, Mercredi.AddDays(1));
        Assert.Equal(PointageJourStatuts.Conge, r.Statut);
        Assert.Null(r.RecupMinutes);
    }

    [Fact]
    public void Presence_recup_code_REC()
    {
        var jour = new PointageJourDto(Mercredi, PointageJourStatuts.Recup, null, "Récup", 0, null, null, [], 0, 0, 0, 0,
            RecupMinutes: 520);
        Assert.Equal("REC", PresenceRegles.CodeAuto(jour, null, false));
        Assert.True(PresenceCodes.EstValide(PresenceCodes.Recup));
    }

    [Fact]
    public void Solde_acquises_moins_recuperees()
    {
        PointageJourDto J(DateOnly d, string statut, int? hs = null, int? recup = null) =>
            new(d, statut, null, null, 0, null, null, [], 0, 0, 0, 0, HeuresSupplementairesMinutes: hs, RecupMinutes: recup);
        var jours = new[]
        {
            J(Samedi, PointageJourStatuts.HeuresSupplementaires, hs: 300),
            J(Samedi.AddDays(7), PointageJourStatuts.HeuresSupplementaires, hs: 400),
            J(Mercredi.AddDays(7), PointageJourStatuts.Recup, recup: 520),
            J(Mercredi.AddDays(28), PointageJourStatuts.Recup, recup: 460),
        };
        var s = PlanningItRegles.Solde(7, jours, Mercredi.AddDays(10));
        Assert.Equal(700, s.AcquisesMinutes);
        Assert.Equal(980, s.RecupereesMinutes);
        Assert.Equal(-280, s.RestantesMinutes);
        Assert.Equal(2, s.JoursRecup);
        Assert.Equal(460, s.RecupPlanifieesMinutes);
    }

    [Fact]
    public void Suppression_definitive_confirmation_et_libelles()
    {
        Assert.True(SuppressionDefinitiveRegles.ConfirmationValide(" Jean.Dupont@Kyntus.com ", "jean.dupont@kyntus.com"));
        Assert.False(SuppressionDefinitiveRegles.ConfirmationValide("jean.dupont@kyntus.co", "jean.dupont@kyntus.com"));
        Assert.False(SuppressionDefinitiveRegles.ConfirmationValide(null, "a@b.c"));
        Assert.True(SuppressionDefinitiveRegles.ConfirmationValide("supprimer", null));
        Assert.Equal("affectations de planning", SuppressionDefinitiveRegles.Libelle("ShiftAssignments"));
        Assert.Equal("affectations de planning", SuppressionDefinitiveRegles.Libelle("\"ShiftAssignments\""));
        Assert.Equal("données « Pointages »", SuppressionDefinitiveRegles.Libelle("Pointages"));
    }
}
