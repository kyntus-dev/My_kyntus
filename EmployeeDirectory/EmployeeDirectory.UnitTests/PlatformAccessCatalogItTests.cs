using EmployeeDirectory.Application.Iam;
using Xunit;

namespace EmployeeDirectory.UnitTests;

/// <summary>v12 : rôles IT et nouveaux écrans dans « Accès par rôle », sans ouverture à tous les rôles.</summary>
public class PlatformAccessCatalogItTests
{
    [Fact]
    public void Roles_IT_presents_dans_la_matrice()
    {
        Assert.Contains("Ingénieur", PlatformAccessCatalog.Roles);
        Assert.Contains("Support IT", PlatformAccessCatalog.Roles);
        Assert.True(PlatformAccessCatalog.TryNormalizeGrant("support it", "planning-it.Act", "Self", out var role, out var action, out _, out var err));
        Assert.Null(err);
        Assert.Equal("Support IT", role);
        Assert.Equal("planning-it.Act", action);
    }

    [Fact]
    public void Planning_IT_reserve_aux_roles_IT_et_a_l_Admin()
    {
        var pilote = PlatformAccessCatalog.SeedRowsForRole("Pilote").Select(r => r.Action).ToList();
        Assert.DoesNotContain(pilote, a => a.StartsWith("planning-it."));
        Assert.Contains("pointage.self.View", pilote);
        Assert.DoesNotContain("pointage.regimes.View", pilote);

        var ingenieur = PlatformAccessCatalog.SeedRowsForRole("Ingénieur").Select(r => r.Action).ToList();
        Assert.Contains("planning-it.View", ingenieur);
        Assert.Contains("planning-it.Act", ingenieur);
        Assert.DoesNotContain("planning-it.Configure", ingenieur);
        // « Comme un collaborateur » : mêmes accès personnels qu'un Pilote.
        Assert.Contains("conges.self.View", ingenieur);
        Assert.Contains("planning.self.View", ingenieur);
        Assert.DoesNotContain("conges.validation.View", ingenieur);
    }
}
