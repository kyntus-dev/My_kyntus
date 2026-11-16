using Planning.Application.PilotPlanning;
using Planning.Application.Users;
using Xunit;

namespace Planning.UnitTests;

/// <summary>v47 — Mail interne facultatif et import des adresses (rapprochement nom / prénom).</summary>
public class MailInterneTests
{
    private static RapprochementMails.Agent Agent(int id, string nom, string prenom) =>
        new(id, nom, prenom);

    private static RapprochementMails.Ligne Ligne(int n, string nom, string prenom, string email) =>
        new(n, nom, prenom, email);

    private static IReadOnlyList<RapprochementMails.Resultat> Rapprocher(
        IReadOnlyList<RapprochementMails.Ligne> lignes,
        IReadOnlyList<RapprochementMails.Agent> agents,
        Dictionary<string, string>? utilises = null) =>
        RapprochementMails.Rapprocher(lignes, agents, utilises ?? new Dictionary<string, string>());

    [Fact]
    public void Adresse_provisoire_generee_est_unique_et_reconnue()
    {
        var a = MailInterne.Generer("Hanane", "ARBAOUI");
        var b = MailInterne.Generer("Hanane", "ARBAOUI");

        Assert.NotEqual(a, b);
        Assert.EndsWith("@" + MailInterne.DomaineProvisoire, a);
        Assert.StartsWith("arbaoui.hanane.", a);
        Assert.True(MailInterne.EstProvisoire(a));
        Assert.True(MailInterne.EstProvisoire(""));
        Assert.True(MailInterne.EstProvisoire(null));
        Assert.False(MailInterne.EstProvisoire("hanane.arbaoui@kyntus.com"));
        Assert.Null(MailInterne.Afficher(a));
        Assert.Equal("hanane.arbaoui@kyntus.com", MailInterne.Afficher(" hanane.arbaoui@kyntus.com "));
    }

    [Fact]
    public void Adresse_provisoire_ne_recoit_jamais_d_email()
    {
        var provisoire = MailInterne.Generer("Siham", "BOUCHEBTI");

        Assert.False(PilotPlanningEmailComposer.IsValidPersonalEmail(provisoire));
        Assert.True(PilotPlanningEmailComposer.IsValidPersonalEmail("siham.bouchebti@kyntus.com"));
    }

    [Theory]
    [InlineData("omar.elmahi@kyntus.com", true)]
    [InlineData("omar.elmahi@kyntus.ma", true)]
    [InlineData("omar.elmahi", false)]
    [InlineData("omar@com", false)]
    [InlineData("a b@kyntus.com", false)]
    [InlineData("omar@@kyntus.com", false)]
    public void Format_adresse_controle(string email, bool attendu) =>
        Assert.Equal(attendu, MailInterne.FormatValide(email));

    [Fact]
    public void Nom_identique_donne_une_correspondance_exacte()
    {
        var agents = new[] { Agent(1, "ARBAOUI", "Hanane"), Agent(2, "AZIZ", "Nasreddine") };
        var lignes = new[] { Ligne(2, "ARBAOUI", "Hanane", "hanane.arbaoui@kyntus.com") };

        var r = Rapprocher(lignes, agents).Single();

        Assert.Equal(RapprochementMails.StatutExact, r.Statut);
        Assert.Equal(1, r.UserId);
    }

    [Fact]
    public void Accents_casse_et_colonnes_inversees_restent_exacts()
    {
        var agents = new[] { Agent(7, "BELHADI", "Mohammed Amine") };

        var accents = Rapprocher(
            new[] { Ligne(2, "bélhadi", "mohammed amine", "m.belhadi@kyntus.com") }, agents).Single();
        var inverse = Rapprocher(
            new[] { Ligne(2, "Mohammed Amine", "BELHADI", "m.belhadi@kyntus.com") }, agents).Single();

        Assert.Equal(RapprochementMails.StatutExact, accents.Statut);
        Assert.Equal(7, accents.UserId);
        Assert.Equal(RapprochementMails.StatutExact, inverse.Statut);
        Assert.Equal(7, inverse.UserId);
    }

    [Fact]
    public void Faute_de_frappe_demande_une_confirmation()
    {
        var agents = new[] { Agent(3, "BOUCHEBTI", "Siham"), Agent(4, "AOUNDOU", "Jupsie") };
        var lignes = new[] { Ligne(2, "BOUCHEBTY", "Siham", "siham.bouchebti@kyntus.com") };

        var r = Rapprocher(lignes, agents).Single();

        Assert.Equal(RapprochementMails.StatutProbable, r.Statut);
        Assert.Equal(3, r.UserId);
        Assert.NotEmpty(r.Candidats);
    }

    [Fact]
    public void Homonymes_ne_sont_jamais_attribues_automatiquement()
    {
        var agents = new[] { Agent(10, "BENSAID", "Hnia"), Agent(11, "Hnia", "BENSAID") };
        var lignes = new[] { Ligne(2, "BENSAID", "Hnia", "hnia.bensaid@kyntus.com") };

        var r = Rapprocher(lignes, agents).Single();

        Assert.Equal(RapprochementMails.StatutAmbigu, r.Statut);
        Assert.Null(r.UserId);
        Assert.Equal(2, r.Candidats.Count);
    }

    [Fact]
    public void Nom_absent_de_la_liste_est_introuvable()
    {
        var agents = new[] { Agent(1, "ARBAOUI", "Hanane") };
        var lignes = new[] { Ligne(2, "ZAHIRI", "Youssef", "youssef.zahiri@kyntus.com") };

        var r = Rapprocher(lignes, agents).Single();

        Assert.Equal(RapprochementMails.StatutIntrouvable, r.Statut);
        Assert.Null(r.UserId);
    }

    [Fact]
    public void Adresse_deja_utilisee_est_rejetee()
    {
        var agents = new[] { Agent(1, "ARBAOUI", "Hanane") };
        var lignes = new[] { Ligne(2, "ARBAOUI", "Hanane", "Omar.ElMahi@kyntus.com") };
        var utilises = new Dictionary<string, string> { ["omar.elmahi@kyntus.com"] = "ELMAHI Omar" };

        var r = Rapprocher(lignes, agents, utilises).Single();

        Assert.Equal(RapprochementMails.StatutEmailUtilise, r.Statut);
        Assert.Null(r.UserId);
        Assert.Contains("ELMAHI Omar", r.Message ?? string.Empty);
    }

    [Fact]
    public void Adresse_en_double_dans_le_fichier_est_rejetee()
    {
        var agents = new[] { Agent(1, "ARBAOUI", "Hanane"), Agent(2, "AZIZ", "Nasreddine") };
        var lignes = new[]
        {
            Ligne(2, "ARBAOUI", "Hanane", "contact@kyntus.com"),
            Ligne(3, "AZIZ", "Nasreddine", "CONTACT@kyntus.com"),
        };

        var r = Rapprocher(lignes, agents);

        Assert.All(r, x => Assert.Equal(RapprochementMails.StatutDoublon, x.Statut));
        Assert.All(r, x => Assert.Null(x.UserId));
    }

    [Fact]
    public void Deux_lignes_pour_le_meme_collaborateur_sont_rejetees()
    {
        var agents = new[] { Agent(1, "ARBAOUI", "Hanane") };
        var lignes = new[]
        {
            Ligne(2, "ARBAOUI", "Hanane", "hanane.arbaoui@kyntus.com"),
            Ligne(3, "ARBAOUI", "Hanane", "h.arbaoui@kyntus.com"),
        };

        var r = Rapprocher(lignes, agents);

        Assert.All(r, x => Assert.Equal(RapprochementMails.StatutDoublon, x.Statut));
        Assert.All(r, x => Assert.Null(x.UserId));
    }

    [Fact]
    public void Ligne_invalide_est_signalee_sans_bloquer_les_autres()
    {
        var agents = new[] { Agent(1, "ARBAOUI", "Hanane"), Agent(2, "AZIZ", "Nasreddine") };
        var lignes = new[]
        {
            Ligne(2, "ARBAOUI", "Hanane", "hanane.arbaoui"),
            Ligne(3, "", "", "orphelin@kyntus.com"),
            Ligne(4, "AZIZ", "Nasreddine", "nasreddine.aziz@kyntus.com"),
            Ligne(5, "AZIZ", "Nasreddine", "n.aziz@" + MailInterne.DomaineProvisoire),
        };

        var r = Rapprocher(lignes, agents);

        Assert.Equal(RapprochementMails.StatutInvalide, r[0].Statut);
        Assert.Equal(RapprochementMails.StatutInvalide, r[1].Statut);
        Assert.Equal(RapprochementMails.StatutExact, r[2].Statut);
        Assert.Equal(2, r[2].UserId);
        Assert.Equal(RapprochementMails.StatutInvalide, r[3].Statut);
    }
}
