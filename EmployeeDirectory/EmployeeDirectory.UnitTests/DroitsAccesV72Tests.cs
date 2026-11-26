using EmployeeDirectory.Application.Iam;
using Kyntus.Iam;

namespace EmployeeDirectory.UnitTests;

/// <summary>
/// v72 — « Si tu as le droit, c'est bon ; si tu ne l'as pas, rien n'est proposé. »
/// Une action que la page propose avec son propre droit ne doit plus être refusée par le serveur parce
/// que la route n'était rattachée qu'à une autre rubrique ; et une action dont le droit est retiré reste
/// refusée.
/// </summary>
public class DroitsAccesV72Tests
{
    /// <summary>Même décision que AdminRestrictionMiddleware : refus seulement si toutes les rubriques sont retirées.</summary>
    private static bool Accepte(string method, string path, params string[] retraits)
    {
        var feature = AdminFeatureRouteMap.Resolve(method, path);
        if (feature is null) return true;
        var snapshot = new AdminRestrictionSnapshot(false, retraits);
        return feature.Screens.Any(s => !snapshot.IsDenied(s, feature.Verb));
    }

    [Fact]
    public void Generer_le_planning_est_refuse_quand_Modifier_est_retire_et_accepte_sinon()
    {
        Assert.False(Accepte("POST", "/api/planning/generate", "planning.generate.Act"));
        Assert.True(Accepte("POST", "/api/planning/generate"));
        // La page reste consultable avec Consulter seul.
        Assert.True(Accepte("GET", "/api/planning/config", "planning.generate.Act"));
    }

    [Theory]
    [InlineData("GET", "/api/planning/change-requests/my")]
    [InlineData("POST", "/api/planning/change-requests")]
    [InlineData("POST", "/api/planning/change-requests/5/cancel")]
    [InlineData("POST", "/api/planning/change-requests/5/partner-accept")]
    [InlineData("POST", "/api/planning/change-requests/5/partner-reject")]
    [InlineData("GET", "/api/planning/exceptional-requests/my")]
    [InlineData("POST", "/api/planning/exceptional-requests")]
    [InlineData("POST", "/api/planning/exceptional-requests/5/cancel")]
    public void Mes_demandes_restent_ouvertes_avec_Mes_plannings_sans_le_droit_de_traiter(string method, string path)
    {
        Assert.True(Accepte(method, path, "planning.change-requests.View", "planning.exceptional-requests.View"));
    }

    [Theory]
    [InlineData("POST", "/api/planning/change-requests/5/approve")]
    [InlineData("POST", "/api/planning/change-requests/5/reject")]
    public void Traiter_un_changement_reste_reserve_au_droit_Traiter_les_changements(string method, string path)
    {
        Assert.False(Accepte(method, path, "planning.change-requests.View"));
        Assert.False(Accepte(method, path, "planning.change-requests.Validate"));
        Assert.True(Accepte(method, path));
    }

    [Theory]
    [InlineData("POST", "/api/planning/exceptional-requests/5/supervisor-approve")]
    [InlineData("POST", "/api/planning/exceptional-requests/5/rh-approve")]
    public void Valider_une_demande_exceptionnelle_suit_son_droit(string method, string path)
    {
        Assert.False(Accepte(method, path, "planning.exceptional-requests.Act"));
        Assert.True(Accepte(method, path));
    }

    [Fact]
    public void Consulter_un_planning_suit_chacune_des_pages_qui_l_affichent()
    {
        Assert.True(Accepte("GET", "/api/planning/12/consult", "planning.equipe.View", "planning.validation.View", "planning.publish.View"));
        Assert.False(Accepte("GET", "/api/planning/12/consult",
            "planning.self.View", "planning.equipe.View", "planning.validation.View", "planning.publish.View"));
    }

    [Theory]
    [InlineData("PUT", "/api/formations/sessions/3/assignments/4/attendance")]
    [InlineData("POST", "/api/formations/sessions/3/report")]
    public void Le_formateur_saisit_la_presence_et_le_rapport_depuis_Mes_formations(string method, string path)
    {
        Assert.True(Accepte(method, path, "formation.planification.Act", "formation.admin.Act"));
    }

    [Theory]
    [InlineData("POST", "/api/directory/employees/3/clear-org-placement")]
    [InlineData("PUT", "/api/directory/org/assignments/pilote/4")]
    [InlineData("POST", "/api/admin/org-reconciliation/sync-from-directory")]
    public void Organisation_accepte_ses_actions_avec_le_droit_Organisation(string method, string path)
    {
        Assert.True(Accepte(method, path, "admin.users.Act"));
        Assert.False(Accepte(method, path, "admin.users.Act", "admin.organisation.Act", "admin.sync.Act"));
    }

    [Fact]
    public void Anomalies_lisibles_depuis_Administration_et_Audit()
    {
        Assert.True(Accepte("GET", "/api/prime/admin/anomalies", "prime.workflow.View", "prime.rbac.View", "prime.catalog.View"));
        Assert.True(Accepte("GET", "/api/parrainage/anomalies", "parrainage.module.View"));
    }

    [Fact]
    public void Les_rubriques_ajoutees_existent_dans_le_catalogue()
    {
        var ids = PlatformAccessCatalog.Screens.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Empty(AdminFeatureRouteMap.ReferencedScreens.Where(s => !ids.Contains(s)));
    }

    /// <summary>
    /// v77 — « Voir les périmètres des autres encadrants » : rubrique à part, accordée par défaut à
    /// l'Admin et aux RH seulement. Elle doit figurer dans les rubriques ajoutées, sinon ses lignes ne
    /// seraient jamais créées sur une base déjà en service (plus rien n'y est réensemencé en bloc).
    /// </summary>
    [Fact]
    public void Le_perimetre_global_est_reserve_a_l_admin_et_aux_rh()
    {
        var ecran = PlatformAccessCatalog.Screens.Single(s => s.Id == "perimetre.global");
        Assert.Contains("Admin", ecran.View);
        Assert.Contains("RH", ecran.View);
        Assert.DoesNotContain("Superviseur", ecran.View);
        Assert.DoesNotContain("Référent technique", ecran.View);

        Assert.Contains("perimetre.global", PlatformAccessCatalog.RubriquesAjoutees);

        // Les lignes semées pour un superviseur ne contiennent pas cette rubrique.
        Assert.DoesNotContain(
            PlatformAccessCatalog.SeedRowsForRole("Superviseur"),
            r => r.Action.StartsWith("perimetre.global.", StringComparison.Ordinal));
        Assert.Contains(
            PlatformAccessCatalog.SeedRowsForRole("RH"),
            r => r.Action == "perimetre.global.View");
    }
}
