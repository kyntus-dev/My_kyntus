using Planning.Application.Employes;
using Planning.Domain.Entities;

namespace Planning.UnitTests;

/// <summary>
/// v64 — Fichier des entrepreneurs (sous-traitants) et type d'employé.
/// Toutes les données sont fictives.
/// </summary>
public class FichesReferenceV64Tests
{
    private static readonly DateOnly Aujourdhui = new(2026, 11, 21);

    [Theory]
    [InlineData("Référence", FicheReference.CleNumeroOrdre)]
    [InlineData("Nom-Prénom", FicheReference.CleAgents)]
    [InlineData("Date d'Ancienntée", "dateAnciennete")]
    [InlineData("Date d'Entrée", "dateEntree")]
    [InlineData("Date Sortie", "dateSortie")]
    [InlineData("Nationalité", "nationalite")]
    [InlineData("Sexe", "sexe")]
    [InlineData("Situation Familiale", "situationFamiliale")]
    [InlineData("Carte d'Immatricualation", "carteImmatriculation")]
    [InlineData("N°Passeport", "passeport")]
    [InlineData("N° d'inscription au registre national", "registreNational")]
    [InlineData("Date de Naissance", "dateNaissance")]
    [InlineData("Adresse", "adresse")]
    [InlineData("TEL 1", "telephone1")]
    [InlineData("TEL 2", "telephone2")]
    [InlineData("N° BOX", FicheReference.CleBox)]
    [InlineData("Type d'employé", "typeEmploye")]
    public void Chaque_colonne_du_fichier_entrepreneurs_est_reconnue(string intitule, string cle) =>
        Assert.Equal(cle, FicheReference.ColonneDe(intitule));

    [Fact]
    public void Les_colonnes_manquantes_dependent_du_profil_du_fichier()
    {
        string[] entrepreneurs =
        [
            FicheReference.CleNumeroOrdre, FicheReference.CleAgents, "dateAnciennete", "dateEntree", "dateSortie",
            "nationalite", "sexe", "situationFamiliale", "carteImmatriculation", "passeport", "registreNational",
            "dateNaissance", "adresse", "telephone1", "telephone2",
        ];
        Assert.Equal(FicheReference.ProfilSousTraitants, FicheReference.ProfilFichier(entrepreneurs));
        Assert.Empty(FicheReference.ColonnesManquantes(entrepreneurs));

        var salaries = FicheReference.Colonnes
            .Where(c => c.Profil is FicheReference.ProfilCommun or FicheReference.ProfilSalaries)
            .Select(c => c.Cle).ToList();
        Assert.Equal(FicheReference.ProfilSalaries, FicheReference.ProfilFichier(salaries));
        Assert.Empty(FicheReference.ColonnesManquantes(salaries));
    }

    [Fact]
    public void Une_ligne_entrepreneur_est_lue_entierement_et_typee_sous_traitant()
    {
        var v = FicheReference.Interpreter(new Dictionary<string, object?>
        {
            [FicheReference.CleNumeroOrdre] = "ST-001",
            [FicheReference.CleAgents] = "FICTIF ALPHA",
            ["dateAnciennete"] = new DateTime(2024, 3, 4),
            ["dateEntree"] = new DateTime(2024, 3, 4),
            ["nationalite"] = "SENEGALAISE",
            ["sexe"] = "M",
            ["situationFamiliale"] = "C",
            ["carteImmatriculation"] = "xx 000001",
            ["passeport"] = "p 0000001",
            ["registreNational"] = 100000000000001d,
            ["dateNaissance"] = new DateTime(1996, 2, 3),
            ["adresse"] = "Adresse fictive",
            ["telephone1"] = "0600000001",
        }, Aujourdhui);

        Assert.Equal(new DateOnly(2024, 3, 4), v.DateEntree);
        Assert.Equal("XX000001", v.CarteImmatriculation);
        Assert.Equal("P0000001", v.NumeroPasseport);
        Assert.Equal("100000000000001", v.RegistreNational);
        Assert.Equal(TypesEmploye.SousTraitant, v.TypeEmploye);
        Assert.Empty(v.Anomalies);

        // La comparaison avec une fiche salariée propose tous les champs, registre → carte auto-entrepreneur.
        var fiche = new UserHrProfile { TypeEmploye = TypesEmploye.Salarie };
        var modifs = FicheReference.Comparer(fiche, v);
        Assert.Contains(modifs, m => m.Cle == "registreNational" && m.Nouvelle == "100000000000001");
        Assert.Contains(modifs, m => m.Cle == "carteImmatriculation");
        Assert.Contains(modifs, m => m.Cle == "numeroPasseport");
        Assert.Contains(modifs, m => m.Cle == "dateEntree");
        Assert.Contains(modifs, m => m is { Cle: "typeEmploye", Ancienne: TypesEmploye.LibelleSalarie, Nouvelle: TypesEmploye.LibelleSousTraitant });
    }

    [Fact]
    public void Le_type_explicite_prime_et_une_valeur_inconnue_est_signalee()
    {
        var explicite = FicheReference.Interpreter(new Dictionary<string, object?>
        {
            ["typeEmploye"] = "Salarié", ["registreNational"] = "100000000000002",
        }, Aujourdhui);
        Assert.Equal(TypesEmploye.Salarie, explicite.TypeEmploye);

        var inconnu = FicheReference.Interpreter(new Dictionary<string, object?> { ["typeEmploye"] = "Stagiaire ?" }, Aujourdhui);
        Assert.Null(inconnu.TypeEmploye);
        Assert.Contains(inconnu.Anomalies, a => a.Contains("Type d'employé"));

        var salarie = FicheReference.Interpreter(new Dictionary<string, object?> { ["immatriculationCnss"] = 123456789d }, Aujourdhui);
        Assert.Equal(TypesEmploye.Salarie, salarie.TypeEmploye);
    }

    [Fact]
    public void La_completude_d_un_sous_traitant_accepte_toute_piece_d_identite()
    {
        var fiche = new UserHrProfile
        {
            TypeEmploye = TypesEmploye.SousTraitant, ImmatriculationInterne = "ST-001",
            DateAnciennete = new DateOnly(2024, 3, 4), DateEntree = new DateOnly(2024, 3, 4),
            Nationalite = "Autre", Sexe = "M", SituationFamiliale = "CELIBATAIRE",
            NumeroCarteAutoentrepreneur = "100000000000001", DateNaissance = new DateOnly(1996, 2, 3),
            Adresse = "Adresse fictive", Telephone1 = "0600000001",
        };
        Assert.Single(FicheReference.ChampsManquants(fiche)); // seule la pièce d'identité manque
        fiche.NumeroPasseport = "P0000001";
        Assert.Empty(FicheReference.ChampsManquants(fiche));

        fiche.NumeroCarteAutoentrepreneur = null;
        Assert.Single(FicheReference.ChampsManquants(fiche)); // le registre national est exigé
    }

    [Theory]
    [InlineData("Salarié", TypesEmploye.Salarie)]
    [InlineData("sous-traitant", TypesEmploye.SousTraitant)]
    [InlineData("Auto entrepreneur", TypesEmploye.SousTraitant)]
    [InlineData("SOUS_TRAITANT", TypesEmploye.SousTraitant)]
    [InlineData("", null)]
    [InlineData("inconnu", null)]
    public void Le_type_d_employe_est_normalise(string valeur, string? attendu) =>
        Assert.Equal(attendu, TypesEmploye.Normaliser(valeur));

    [Fact]
    public void Sans_type_la_carte_auto_entrepreneur_designe_un_sous_traitant()
    {
        Assert.Equal(TypesEmploye.SousTraitant, TypesEmploye.Code(new UserHrProfile { NumeroCarteAutoentrepreneur = "1" }));
        Assert.Equal(TypesEmploye.Salarie, TypesEmploye.Code(new UserHrProfile()));
        Assert.Equal(TypesEmploye.LibelleSalarie, TypesEmploye.Libelle(new UserHrProfile { TypeEmploye = "SALARIE", NumeroCarteAutoentrepreneur = "1" }));
    }
}
