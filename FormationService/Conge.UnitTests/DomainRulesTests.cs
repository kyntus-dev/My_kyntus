using Conge.Domain.Entities;
using Conge.Domain.Enums;
using Conge.Domain.Exceptions;
using Xunit;

namespace Conge.UnitTests;

public class PeriodeInterditeTests
{
    [Fact]
    public void Defaut_contient_septembre_et_octobre()
    {
        var cfg = PeriodeInterditeConge.CreerParDefaut();
        Assert.Equal(new[] { 9, 10 }, cfg.GetMois());
    }

    [Fact]
    public void Chevauche_mois_interdit_partiel()
    {
        var cfg = PeriodeInterditeConge.CreerParDefaut();
        Assert.True(cfg.ChevauchePeriode(new DateTime(2026, 8, 28), new DateTime(2026, 9, 2)));
        Assert.False(cfg.ChevauchePeriode(new DateTime(2026, 7, 1), new DateTime(2026, 7, 15)));
    }

    [Fact]
    public void MettreAJour_filtre_mois_invalides()
    {
        var cfg = PeriodeInterditeConge.CreerParDefaut();
        cfg.MettreAJour(new[] { 0, 3, 3, 13, 12 });
        Assert.Equal(new[] { 3, 12 }, cfg.GetMois());
    }

    [Fact]
    public void Pick_prend_le_service_avant_le_pole_et_le_general()
    {
        var deptId = Guid.NewGuid();
        var emp = EmployeSnapshot.Creer(
            Guid.NewGuid(), "A", "B", "a@test.com",
            Guid.NewGuid(), Guid.NewGuid(), "Svc",
            DateTime.UtcNow.AddYears(-1),
            poleId: "pole-1",
            celluleId: "cell-1",
            orgServiceId: "svc-1",
            businessDepartmentId: deptId);

        var general = PeriodeInterditeConge.CreerParDefaut();
        var pole = PeriodeInterditeConge.CreerPourScope(
            PeriodeInterditeScopeKinds.Pole, "pole-1", new[] { 1 });
        var service = PeriodeInterditeConge.CreerPourScope(
            PeriodeInterditeScopeKinds.Service, "svc-1", new[] { 6 });

        var picked = PeriodeInterditeConge.PickForEmploye([general, pole, service], emp, general);
        Assert.Equal(new[] { 6 }, picked.GetMois());
        Assert.Equal(PeriodeInterditeScopeKinds.Service, picked.ScopeKind);
    }

    [Fact]
    public void Pick_sans_exception_reste_sur_le_general()
    {
        var emp = EmployeSnapshot.Creer(
            Guid.NewGuid(), "A", "B", "a@test.com",
            Guid.NewGuid(), Guid.NewGuid(), "Svc",
            DateTime.UtcNow.AddYears(-1),
            poleId: "pole-x");
        var general = PeriodeInterditeConge.CreerParDefaut();
        var other = PeriodeInterditeConge.CreerPourScope(
            PeriodeInterditeScopeKinds.Pole, "autre", new[] { 2 });
        var picked = PeriodeInterditeConge.PickForEmploye([general, other], emp, general);
        Assert.True(picked.IsGeneral);
        Assert.Equal(new[] { 9, 10 }, picked.GetMois());
    }
}

public class DualValidationTests
{
    private static (EmployeSnapshot emp, SoldeConge solde) Fixtures()
    {
        var emp = EmployeSnapshot.Creer(
            Guid.NewGuid(), "Doe", "Jane", "j@test.com",
            Guid.NewGuid(), Guid.NewGuid(), "Svc A",
            DateTime.UtcNow.AddYears(-2));
        var solde = SoldeConge.Initialiser(emp.EmployeId, 18, DateTime.Today.Year);
        return (emp, solde);
    }

    [Fact]
    public void Parcours_superviseur_puis_rh()
    {
        var (emp, solde) = Fixtures();
        var debut = DateTime.UtcNow.Date.AddDays(30);
        var fin = debut.AddDays(5);
        var d = DemandeConge.CreerCongeAnnuel(emp.EmployeId, emp.ManagerId, debut, fin, solde, emp);

        Assert.Equal(StatutDemande.EnAttente, d.Statut);
        d.ValiderParSuperviseur(emp.ManagerId, "ok sup");
        Assert.Equal(StatutDemande.EnAttenteRh, d.Statut);
        Assert.NotNull(d.DateValidationSuperviseur);

        d.ValiderParRh(Guid.NewGuid(), "ok rh");
        Assert.Equal(StatutDemande.Validee, d.Statut);
        Assert.NotNull(d.DateDecision);
    }

    [Fact]
    public void Superviseur_sans_etape_rh_valide_directement()
    {
        var (emp, solde) = Fixtures();
        var debut = DateTime.UtcNow.Date.AddDays(30);
        var fin = debut.AddDays(5);
        var d = DemandeConge.CreerCongeAnnuel(emp.EmployeId, emp.ManagerId, debut, fin, solde, emp);

        d.ValiderParSuperviseur(emp.ManagerId, "ok sup", etapeRh: false);
        Assert.Equal(StatutDemande.Validee, d.Statut);
        Assert.NotNull(d.DateDecision);
        Assert.Contains(d.DomainEvents, e => e is Conge.Domain.Events.CongeValideEvent);
    }

    [Fact]
    public void File_rh_se_cloture_quand_etape_rh_inactive()
    {
        var (emp, solde) = Fixtures();
        var debut = DateTime.UtcNow.Date.AddDays(30);
        var fin = debut.AddDays(5);
        var d = DemandeConge.CreerCongeAnnuel(emp.EmployeId, emp.ManagerId, debut, fin, solde, emp);
        d.ValiderParSuperviseur(emp.ManagerId, "ok sup");
        Assert.Equal(StatutDemande.EnAttenteRh, d.Statut);

        d.FinaliserSansEtapeRh(emp.ManagerId, emp.NomComplet, "Superviseur");
        Assert.Equal(StatutDemande.Validee, d.Statut);
        Assert.Null(d.RhDecideurId);
    }

    [Fact]
    public void Refus_possible_depuis_en_attente_rh()
    {
        var (emp, solde) = Fixtures();
        var debut = DateTime.UtcNow.Date.AddDays(40);
        var fin = debut.AddDays(2);
        var d = DemandeConge.CreerCongeAnnuel(
            emp.EmployeId, emp.ManagerId, debut, fin, solde, emp,
            statutInitial: StatutDemande.EnAttenteRh);

        d.Refuser(Guid.NewGuid(), "Refus RH pour effectif");
        Assert.Equal(StatutDemande.Refusee, d.Statut);
    }

    [Fact]
    public void Annuler_autorise_en_attente_rh()
    {
        var (emp, solde) = Fixtures();
        var debut = DateTime.UtcNow.Date.AddDays(50);
        var fin = debut.AddDays(2);
        var d = DemandeConge.CreerCongeAnnuel(
            emp.EmployeId, emp.ManagerId, debut, fin, solde, emp,
            statutInitial: StatutDemande.EnAttenteRh);
        d.Annuler();
        Assert.Equal(StatutDemande.Annulee, d.Statut);
    }

    [Fact]
    public void Demande_superviseur_peut_partir_direct_en_attente_rh()
    {
        var (emp, solde) = Fixtures();
        var debut = DateTime.UtcNow.Date.AddDays(20);
        var fin = debut.AddDays(3);
        var d = DemandeConge.CreerCongeAnnuel(
            emp.EmployeId, emp.ManagerId, debut, fin, solde, emp,
            statutInitial: StatutDemande.EnAttenteRh);
        Assert.Equal(StatutDemande.EnAttenteRh, d.Statut);
    }
}

public class EmployeSnapshotUtcTests
{
    [Fact]
    public void Creer_normalise_date_embauche_unspecified_en_utc()
    {
        var unspecified = new DateTime(2021, 6, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var snap = EmployeSnapshot.Creer(
            Guid.NewGuid(), "Doe", "Jane", "j@test.com",
            Guid.Empty, Guid.Empty, "", unspecified);

        Assert.Equal(DateTimeKind.Utc, snap.DateEmbauche.Kind);
        Assert.Equal(new DateTime(2021, 6, 1, 0, 0, 0, DateTimeKind.Utc), snap.DateEmbauche);
    }

    [Fact]
    public void MettreAJour_normalise_date_embauche_unspecified_en_utc()
    {
        var snap = EmployeSnapshot.Creer(
            Guid.NewGuid(), "Doe", "Jane", "j@test.com",
            Guid.Empty, Guid.Empty, "", DateTime.UtcNow);
        var unspecified = new DateTime(2019, 1, 10, 0, 0, 0, DateTimeKind.Unspecified);

        snap.MettreAJour("Doe", "Jane", "j@test.com", Guid.Empty, Guid.Empty, "", dateEmbauche: unspecified);

        Assert.Equal(DateTimeKind.Utc, snap.DateEmbauche.Kind);
        Assert.Equal(new DateTime(2019, 1, 10, 0, 0, 0, DateTimeKind.Utc), snap.DateEmbauche);
    }
}

public class EligibiliteCongeAnnuelTests
{
    private static (EmployeSnapshot emp, SoldeConge solde) EmployeEmbauche(DateTime dateEmbauche)
    {
        var emp = EmployeSnapshot.Creer(
            Guid.NewGuid(), "Doe", "Jane", "j@test.com",
            Guid.NewGuid(), Guid.NewGuid(), "Svc A",
            dateEmbauche);
        var solde = SoldeConge.Initialiser(emp.EmployeId, 18, DateTime.Today.Year);
        return (emp, solde);
    }

    [Fact]
    public void Demande_autorisee_si_debut_apres_6_mois_meme_si_anciennete_actuelle_insuffisante()
    {
        var hire = DateTime.UtcNow.Date.AddMonths(-2);
        var (emp, solde) = EmployeEmbauche(hire);
        Assert.False(emp.EstEligibleCongeAnnuel());

        var debut = emp.DateEligibiliteCongeAnnuel().AddDays(14);
        var fin = debut.AddDays(4);

        var d = DemandeConge.CreerCongeAnnuel(emp.EmployeId, emp.ManagerId, debut, fin, solde, emp);
        Assert.Equal(StatutDemande.EnAttente, d.Statut);
        Assert.Equal(debut, d.DateDebut);
    }

    [Fact]
    public void Demande_refusee_si_debut_avant_6_mois()
    {
        var hire = DateTime.UtcNow.Date.AddMonths(-1);
        var (emp, solde) = EmployeEmbauche(hire);
        var debut = DateTime.UtcNow.Date.AddDays(21);
        var fin = debut.AddDays(3);

        var ex = Assert.Throws<EligibiliteException>(() =>
            DemandeConge.CreerCongeAnnuel(emp.EmployeId, emp.ManagerId, debut, fin, solde, emp));
        Assert.Contains("6 mois", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("demande peut être déposée", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
