using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Services;

namespace EmployeeDirectory.UnitTests;

/// <summary>
/// v66 — Rapport « Liaisons HTEL » : un technicien n'est « non défini sur MyKyntus » que si aucune
/// fiche de l'application ne lui correspond — ni libre, ni inactive, ni déjà liée ailleurs.
/// </summary>
public class HtelLiaisonsReportTests
{
    private static HtelTechnicienDto Tech(int id, string nom, int actif = 1) => new(id, nom, actif, "SUPPORT TECHNICIEN BYTEL");

    private static Employee Fiche(string nom, string prenom, bool actif = true, int? idTechnicien = null) => new()
    {
        Id = Guid.NewGuid(),
        LastName = nom,
        FirstName = prenom,
        Email = $"{prenom}.{nom}@kyntus.ma".ToLowerInvariant(),
        IsActive = actif,
        IdTechnicien = idTechnicien,
    };

    [Fact]
    public void Fiche_inactive_n_est_pas_un_technicien_non_defini()
    {
        var report = HtelFusionService.BuildReport(
            [Tech(10, "BENNANI Ahmed")],
            [Fiche("Bennani", "Ahmed", actif: false)]);

        Assert.Empty(report.OrphansHtel);
        var aRapprocher = Assert.Single(report.Ambiguous);
        var candidat = Assert.Single(aRapprocher.Candidates);
        Assert.False(candidat.IsActive);
    }

    [Fact]
    public void Fiche_deja_liee_a_un_autre_identifiant_est_proposee_et_non_ignoree()
    {
        // Doublon côté HTEL : la même personne a deux identifiants.
        var report = HtelFusionService.BuildReport(
            [Tech(10, "ALAOUI Fatima Zahra"), Tech(11, "ALAOUI Fatima Zahra")],
            [Fiche("Alaoui", "Fatima Zahra", idTechnicien: 10)]);

        Assert.Empty(report.OrphansHtel);
        var aRapprocher = Assert.Single(report.Ambiguous);
        Assert.Equal(11, aRapprocher.IdTechnicien);
        Assert.Equal(10, Assert.Single(aRapprocher.Candidates).IdTechnicienActuel);
    }

    [Fact]
    public void Particule_collee_et_ordre_inverse_sont_rapproches()
    {
        var report = HtelFusionService.BuildReport(
            [Tech(20, "ELAMRANI Mohamed Amine")],
            [Fiche("El Amrani", "Mohamed Amine")]);

        Assert.Empty(report.OrphansHtel);
        Assert.Single(report.Ambiguous);
    }

    [Fact]
    public void Technicien_sans_aucune_fiche_reste_non_defini()
    {
        var report = HtelFusionService.BuildReport(
            [Tech(30, "TAZI Karim")],
            [Fiche("Bennani", "Ahmed")]);

        Assert.Equal(30, Assert.Single(report.OrphansHtel).IdTechnicien);
        Assert.Empty(report.Ambiguous);
    }

    [Fact]
    public void Liaison_vers_un_identifiant_absent_ou_inactif_est_une_divergence()
    {
        var report = HtelFusionService.BuildReport(
            [Tech(40, "IDRISSI Sara", actif: 0)],
            [Fiche("Idrissi", "Sara", idTechnicien: 40), Fiche("Tahiri", "Omar", idTechnicien: 999)]);

        Assert.Equal(2, report.Linked.Count);
        Assert.NotNull(report.Divergences);
        Assert.Equal(2, report.Divergences!.Count);
        Assert.Contains(report.Divergences, d => d.IdTechnicien == 999 && d.HtelIntrouvable);
        Assert.Contains(report.Divergences, d => d.IdTechnicien == 40 && d.HtelActif == 0);
    }
}
