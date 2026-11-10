using System.Text.Json;
using Planning.Application.Pointage;
using Planning.Infrastructure.Services.Pointage;
using Xunit;

namespace Planning.UnitTests;

/// <summary>
/// Pointages ZKBio Time : calcul d'une journée (mêmes règles que HTEL) et lecture des réponses de l'API.
/// Les pointeuses n'enregistrent pas l'état : arrivée / pauses / départ sont déduits de l'ordre.
/// </summary>
public class PointageTests
{
    private static readonly DateOnly Jour = new(2026, 10, 5);
    private static readonly DateOnly Lendemain = new(2026, 10, 6);

    private static DateTime T(string hhmmss) => Jour.ToDateTime(TimeOnly.Parse(hhmmss));
    private static PlanningJour Planning(string debut, int pause = 60) => new(TimeOnly.Parse(debut), pause, false, false, "Matin");

    [Fact]
    public void Retard_entree_compte_en_entier_au_dela_de_la_tolerance()
    {
        var r = PointageCalculator.CalculerJour(
            Jour, [T("09:36:00"), T("13:00:00"), T("14:00:00"), T("18:30:00")], Planning("09:30"), Lendemain);

        Assert.Equal(PointageJourStatuts.Retard, r.Statut);
        Assert.Equal(6, r.RetardEntreeMinutes);
        Assert.Equal(0, r.RetardPauseMinutes);
        Assert.Equal(60, r.PauseMinutes);
        Assert.Equal(new TimeOnly(9, 36), r.Arrivee);
        Assert.Equal(new TimeOnly(18, 30), r.Depart);
    }

    [Fact]
    public void Arrivee_dans_les_cinq_minutes_n_est_pas_un_retard()
    {
        Assert.Equal(0, PointageCalculator.CalculerJour(Jour, [T("10:03:00"), T("18:00:00")], Planning("10:00"), Lendemain).RetardEntreeMinutes);
        Assert.Equal(0, PointageCalculator.CalculerJour(Jour, [T("10:05:59"), T("18:00:00")], Planning("10:00"), Lendemain).RetardEntreeMinutes);
        Assert.Equal(6, PointageCalculator.CalculerJour(Jour, [T("10:06:00"), T("18:00:00")], Planning("10:00"), Lendemain).RetardEntreeMinutes);
        Assert.Equal(PointageJourStatuts.AHeure,
            PointageCalculator.CalculerJour(Jour, [T("09:57:00"), T("18:00:00")], Planning("10:00"), Lendemain).Statut);
    }

    [Theory]
    [InlineData(85, 0)]  // 1 h 25 pour 1 h 20 : dans la tolérance
    [InlineData(86, 6)]  // 1 h 26 : tout le dépassement compte
    [InlineData(80, 0)]
    public void Pause_tolerance_de_5_minutes_puis_depassement_entier(int pauseMinutes, int retardAttendu)
    {
        Assert.Equal(retardAttendu, PointageCalculator.RetardAvecTolerance(pauseMinutes - 80, 5));
    }

    [Fact]
    public void Pointeuse_armoire_technique_ignoree()
    {
        var motifs = PointageTerminaux.Motifs("armoire, SN-ZONE-9");
        Assert.True(PointageTerminaux.EstExclu("Armoire Technique", "CQZ1", motifs));
        Assert.True(PointageTerminaux.EstExclu("Porte IT", "sn-zone-9", motifs));
        Assert.False(PointageTerminaux.EstExclu("Entrée principale", "CQZ2", motifs));
        Assert.False(PointageTerminaux.EstExclu("Armoire", null, PointageTerminaux.Motifs("")));
    }

    [Fact]
    public void Depassement_de_pause_cumule_sur_la_journee()
    {
        // 30 min + 39 min de pause pour 60 autorisées : 9 minutes de retard pause.
        var r = PointageCalculator.CalculerJour(
            Jour,
            [T("09:57:00"), T("12:00:00"), T("12:30:00"), T("15:00:00"), T("15:39:00"), T("19:00:00")],
            Planning("10:00"),
            Lendemain);

        Assert.Equal(69, r.PauseMinutes);
        Assert.Equal(0, r.RetardEntreeMinutes);
        Assert.Equal(9, r.RetardPauseMinutes);
        Assert.Equal(9, r.RetardTotalMinutes);
    }

    [Fact]
    public void Nombre_impair_de_pointages_pointage_manquant_ou_journee_en_cours()
    {
        var passe = PointageCalculator.CalculerJour(Jour, [T("10:30:00"), T("13:00:00"), T("14:00:00")], Planning("10:30"), Lendemain);
        Assert.Equal(PointageJourStatuts.PointageManquant, passe.Statut);
        Assert.Equal(0, passe.RetardTotalMinutes);

        var enCours = PointageCalculator.CalculerJour(Jour, [T("10:40:00")], Planning("10:30"), Jour);
        Assert.Equal(PointageJourStatuts.EnCours, enCours.Statut);
    }

    [Fact]
    public void Double_badgeage_fusionne()
    {
        var r = PointageCalculator.CalculerJour(
            Jour, [T("10:01:00"), T("10:01:40"), T("18:00:00")], Planning("10:00"), Lendemain);

        Assert.Equal(2, r.Pointages.Count);
        Assert.Equal(PointageJourStatuts.AHeure, r.Statut);
    }

    [Fact]
    public void Journees_sans_planning_sans_pointage_conge_et_ferie()
    {
        Assert.Equal(PointageJourStatuts.SansPlanning,
            PointageCalculator.CalculerJour(Jour, [T("08:32:00"), T("17:00:00")], null, Lendemain).Statut);
        Assert.Equal(PointageJourStatuts.SansPointage,
            PointageCalculator.CalculerJour(Jour, [], Planning("10:00"), Lendemain).Statut);
        Assert.Equal(PointageJourStatuts.AVenir,
            PointageCalculator.CalculerJour(Lendemain, [], Planning("10:00"), Jour).Statut);
        Assert.Equal(PointageJourStatuts.Repos,
            PointageCalculator.CalculerJour(Jour, [], null, Lendemain).Statut);
        Assert.Equal(PointageJourStatuts.Conge,
            PointageCalculator.CalculerJour(Jour, [], new PlanningJour(null, 60, true, false, null), Lendemain).Statut);
        Assert.Equal(PointageJourStatuts.Ferie,
            PointageCalculator.CalculerJour(Jour, [], new PlanningJour(null, 60, false, true, null), Lendemain).Statut);
    }

    [Fact]
    public void Totaux_de_la_periode()
    {
        var jours = new[]
        {
            PointageCalculator.CalculerJour(Jour, [T("09:36:00"), T("18:00:00")], Planning("09:30"), Lendemain),
            PointageCalculator.CalculerJour(Jour.AddDays(1), [Jour.AddDays(1).ToDateTime(new TimeOnly(10, 0)), Jour.AddDays(1).ToDateTime(new TimeOnly(18, 0))], Planning("10:00"), Jour.AddDays(5)),
            PointageCalculator.CalculerJour(Jour.AddDays(2), [Jour.AddDays(2).ToDateTime(new TimeOnly(10, 0))], Planning("10:00"), Jour.AddDays(5)),
        };

        var t = PointageCalculator.Totaliser(jours);

        Assert.Equal(6, t.RetardTotalMinutes);
        Assert.Equal(1, t.JoursEnRetard);
        Assert.Equal(1, t.JoursAHeure);
        Assert.Equal(2, t.JoursPointes);
        Assert.Equal(new[] { Jour.AddDays(2) }, t.JoursPointageManquant);
    }

    [Fact]
    public void Rapprochement_des_noms_sans_accents_ni_ordre()
    {
        Assert.Equal(PointageNameMatcher.Cle("Omar", "EL MAHI"), PointageNameMatcher.Cle("El Mahi", "Omar"));
        Assert.Equal(PointageNameMatcher.Cle("Hajar", "FERDI"), PointageNameMatcher.Cle("FERDI", "HAJAR"));
        Assert.Equal(PointageNameMatcher.Cle("Hélène", "Aït-Nejma"), PointageNameMatcher.Cle("HELENE", "AIT NEJMA"));
        Assert.NotEqual(PointageNameMatcher.Cle("Imane", "CHERRAQ"), PointageNameMatcher.Cle("Imane", "MIMOUNI"));
    }

    [Fact]
    public void Lecture_d_un_pointage_ZKBio_Time()
    {
        const string json = """
            {"id": 98213, "emp": 412, "emp_code": "210", "first_name": "Fabrice", "last_name": "NGOIMON",
             "department": "Orange", "position": null, "punch_time": "2026-11-02 10:56:24", "punch_state": "255",
             "punch_state_display": "Undefined", "verify_type": 15, "verify_type_display": "Face",
             "work_code": "0", "gps_location": null, "area_alias": "Kyntus", "terminal_sn": "EUF7240900241",
             "temperature": 0.0, "is_mask": "No", "terminal_alias": "Etage 2", "upload_time": "2026-11-02 10:56:27"}
            """;
        using var doc = JsonDocument.Parse(json);

        var tx = BioTimeClient.ParseTransaction(doc.RootElement);

        Assert.NotNull(tx);
        Assert.Equal(98213, tx!.Id);
        Assert.Equal("210", tx.EmpCode);
        Assert.Equal(new DateTime(2026, 11, 2, 10, 56, 24), tx.PunchTime);
        Assert.Equal("Etage 2", tx.TerminalAlias);
        Assert.Equal(15, tx.VerifyType);
        Assert.Equal("Orange", tx.Department);
        Assert.Equal(new DateTime(2026, 11, 2, 10, 56, 27), tx.UploadTime);
    }

    [Fact]
    public void Reprise_d_historique_lue_jour_par_jour()
    {
        var debut = new DateTime(2026, 9, 1, 6, 0, 0);
        var tranches = PointageSyncService.Tranches(debut, debut.AddHours(50), TimeSpan.FromHours(24)).ToList();

        Assert.Equal(3, tranches.Count);
        Assert.Equal(debut, tranches[0].Debut);
        Assert.Equal(tranches[0].Fin, tranches[1].Debut);
        Assert.Equal(debut.AddHours(50), tranches[2].Fin);
    }

    [Fact]
    public void Pointage_illisible_ignore()
    {
        using var doc = JsonDocument.Parse("""{"id": "x", "emp_code": "", "punch_time": "hier"}""");
        Assert.Null(BioTimeClient.ParseTransaction(doc.RootElement));
        Assert.Equal(new DateTime(2026, 11, 2, 8, 0, 0), BioTimeClient.ParseDate("2026-11-02T08:00:00.000000+01:00"));
    }
}
