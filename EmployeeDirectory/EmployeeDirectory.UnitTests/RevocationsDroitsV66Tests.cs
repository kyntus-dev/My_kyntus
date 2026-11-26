using EmployeeDirectory.Infrastructure.Services;
using Kyntus.Iam;

namespace EmployeeDirectory.UnitTests;

/// <summary>
/// v66 — Droits retirés dans « Accès par rôle » / « Accès par compte » : appliqués par le serveur pour
/// tous les rôles, hiérarchie compte &gt; rôle, et un écran retiré emporte tous ses verbes.
/// </summary>
public class RevocationsDroitsV66Tests
{
    [Fact]
    public void Seule_une_action_decochee_sur_tous_ses_perimetres_est_retiree()
    {
        var revoquees = AdminRestrictionRules.RevocationsExplicites(
            [
                ("pointage.regimes.View", false),                 // décochée
                ("planning.shift-config.View", false),            // Global retiré…
                ("planning.shift-config.View", true),             // …mais Cellule accordé : reste ouvert
                ("conges.self.View", true),
                ("*", false),
            ],
            []);

        Assert.Equal(new[] { "pointage.regimes.View" }, revoquees);
    }

    [Fact]
    public void Le_compte_l_emporte_sur_le_role()
    {
        var revoquees = AdminRestrictionRules.RevocationsExplicites(
            [("pointage.regimes.View", false), ("presences.Act", false)],
            [
                ("pointage.regimes.Act", true),   // accordé au compte : lève le retrait de l'écran
                ("conges.validation.View", false), // retiré au compte seulement
            ]);

        Assert.Equal(new[] { "conges.validation.View", "presences.Act" }, revoquees);
    }

    [Fact]
    public void Un_ecran_retire_emporte_tous_ses_verbes_sans_toucher_aux_ecrans_voisins()
    {
        var cles = AdminRestrictionRules.RetirerRevoquees(
            ["presences.View:Cellule", "presences.Act:Cellule", "presences.export.View:Global", "*:Global"],
            ["presences.View"]);

        Assert.Equal(new[] { "presences.export.View:Global", "*:Global" }, cles);
    }

    [Fact]
    public void Exception_de_compte_Consulter_retire_retire_tout_l_ecran()
    {
        var cles = DirectoryIamReadService.AppliquerExceptions(
            ["pointage.regimes.View:Global", "pointage.regimes.Configure:Global", "pointage.self.View:Self"],
            [("pointage.regimes.View", false)]);

        Assert.Equal(new[] { "pointage.self.View:Self" }, cles);
    }

    [Fact]
    public void Supprimer_et_Exporter_suivent_Modifier_et_Consulter_pour_un_role()
    {
        Assert.Equal("Act", AdminRestrictionRules.VerbePourRole("Delete"));
        Assert.Equal("View", AdminRestrictionRules.VerbePourRole("Export"));
        Assert.Equal("Validate", AdminRestrictionRules.VerbePourRole("Validate"));

        var snapshot = new AdminRestrictionSnapshot(false, ["pointage.regimes.View"]);
        Assert.True(snapshot.Restricted);
        Assert.True(snapshot.IsDenied("pointage.regimes", AdminRestrictionRules.VerbePourRole("Delete")));
        Assert.False(snapshot.IsDenied("pointage.self", "View"));
    }

    [Theory]
    [InlineData("GET", "/api/users/fiches-reference/historique", "admin.users", "View")]
    [InlineData("POST", "/api/users/fiches-reference/appliquer", "admin.users", "Act")]
    [InlineData("POST", "/api/users/mails-internes/import", "admin.users", "Act")]
    [InlineData("GET", "/api/users/mails-internes", null, null)]
    public void Routes_ajoutees_v66(string method, string path, string? screen, string? verb)
    {
        var feature = AdminFeatureRouteMap.Resolve(method, path);
        if (screen is null)
        {
            Assert.Null(feature);
            return;
        }
        Assert.NotNull(feature);
        Assert.Contains(screen, feature!.Screens);
        Assert.Equal(verb, feature.Verb);
    }
}
