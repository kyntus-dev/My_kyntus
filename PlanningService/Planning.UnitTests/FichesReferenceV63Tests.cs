using System.Reflection;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Application.Employes;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>
/// v63 — Fichier RH de référence : lecture des colonnes, contrôles, rapprochement par le nom,
/// application sans écrasement par des cases vides, ré-import sans doublon, annulation.
/// Toutes les données sont fictives.
/// </summary>
public class FichesReferenceV63Tests
{
    private static readonly DateOnly Aujourdhui = new(2026, 11, 20);

    // ── Règles pures ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("N° Ordre", FicheReference.CleNumeroOrdre)]
    [InlineData("Agents", FicheReference.CleAgents)]
    [InlineData("Date d'anciennté", "dateAnciennete")]
    [InlineData("Date d'Embauche", "dateEmbauche")]
    [InlineData("Date de Sortie", "dateSortie")]
    [InlineData("Nomination de poste", "poste")]
    [InlineData("Type Contrat", "typeContrat")]
    [InlineData("Nationalité", "nationalite")]
    [InlineData("Ville de Naissance", "villeNaissance")]
    [InlineData("sexe", "sexe")]
    [InlineData("Situation Familiale", "situationFamiliale")]
    [InlineData("CIN", "cin")]
    [InlineData("N° d'Immatriculation", "immatriculationCnss")]
    [InlineData("Date de Naissance", "dateNaissance")]
    [InlineData("Adresse", "adresse")]
    [InlineData("TEL 1", "telephone1")]
    [InlineData("TEL 2", "telephone2")]
    [InlineData("N° BOX", FicheReference.CleBox)]
    public void Chaque_colonne_du_fichier_est_reconnue(string intitule, string cle) =>
        Assert.Equal(cle, FicheReference.ColonneDe(intitule));

    [Fact]
    public void Le_nom_est_normalise_casse_accents_espaces_et_ordre()
    {
        Assert.Equal(FicheReference.CleNom("TESTEUR  Éloïse"), FicheReference.CleNom("eloise testeur"));
        Assert.Equal(FicheReference.CleNom("TESTEUR", "Eloise"), FicheReference.CleNom(" Eloïse  TESTEUR "));
        Assert.NotEqual(FicheReference.CleNom("TESTEUR ELOISE"), FicheReference.CleNom("TESTEUR ELISE"));
        Assert.Equal(("EL TESTEUR", "Alpha Beta"), FicheReference.SeparerNom("EL TESTEUR Alpha Beta"));
        Assert.Equal(("TESTEUR", "Alpha"), FicheReference.SeparerNom("TESTEUR Alpha"));
    }

    [Fact]
    public void Les_valeurs_sont_lues_et_converties()
    {
        var v = FicheReference.Interpreter(new Dictionary<string, object?>
        {
            [FicheReference.CleNumeroOrdre] = 7d,
            [FicheReference.CleAgents] = "TESTEUR ALPHA",
            ["dateAnciennete"] = "02/03/2015",
            ["dateEmbauche"] = new DateTime(2016, 1, 4),
            ["sexe"] = "h",
            ["situationFamiliale"] = "M+2",
            ["nationalite"] = "MAROCAINE",
            ["immatriculationCnss"] = 123456789d,
            ["dateNaissance"] = 32999d, // n° de série Excel du 06/05/1990
            ["telephone2"] = "06-00000002 (SA SŒUR)",
            ["cin"] = "zz 100001",
        }, Aujourdhui);

        Assert.Equal("000007", v.NumeroOrdre);
        Assert.Equal(new DateOnly(2015, 3, 2), v.DateAnciennete);
        Assert.Equal(new DateOnly(2016, 1, 4), v.DateEmbauche);
        Assert.Equal("M", v.Sexe);
        Assert.Equal("MARIE", v.SituationFamiliale);
        Assert.Equal(2, v.NombreEnfants);
        Assert.Equal("Marocaine", v.Nationalite);
        Assert.Equal("123456789", v.ImmatriculationCnss);
        Assert.Equal(new DateOnly(1990, 5, 6), v.DateNaissance);
        Assert.Equal("06-00000002", v.TelephoneUrgence);
        Assert.Equal("Sa sœur", v.RelationUrgence);
        Assert.Equal("ZZ100001", v.Cin);
        Assert.Empty(v.Anomalies);

        var celib = FicheReference.Interpreter(new Dictionary<string, object?> { ["situationFamiliale"] = "c" }, Aujourdhui);
        Assert.Equal("CELIBATAIRE", celib.SituationFamiliale);
        Assert.Equal(0, celib.NombreEnfants);
    }

    [Fact]
    public void Les_valeurs_incoherentes_sont_refusees_et_signalees()
    {
        // Date de naissance et immatriculation inversées.
        var inverses = FicheReference.Interpreter(new Dictionary<string, object?>
        {
            ["dateNaissance"] = 987654321d,
            ["immatriculationCnss"] = new DateTime(1992, 1, 1),
        }, Aujourdhui);
        Assert.Null(inverses.DateNaissance);
        Assert.Null(inverses.ImmatriculationCnss);
        Assert.Contains(inverses.Anomalies, a => a.Contains("inversés"));

        // Naissance après l'embauche, sortie avant l'embauche, sexe inconnu.
        var incoherente = FicheReference.Interpreter(new Dictionary<string, object?>
        {
            ["dateEmbauche"] = new DateTime(2020, 1, 1),
            ["dateNaissance"] = new DateTime(2022, 6, 1),
            ["dateSortie"] = new DateTime(2019, 1, 1),
            ["sexe"] = "X",
        }, Aujourdhui);
        Assert.Null(incoherente.DateNaissance);
        Assert.Null(incoherente.DateSortie);
        Assert.Null(incoherente.Sexe);
        Assert.Equal(3, incoherente.Anomalies.Count);
    }

    [Fact]
    public void Une_case_vide_ne_vide_jamais_la_fiche_et_l_ancien_poste_est_garde()
    {
        var fiche = new UserHrProfile { Adresse = "Adresse fiable", Cin = "ZZ1", Poste = "Agent" };
        var v = new FicheReference.Valeurs { Adresse = null, Cin = "zz1", Poste = "Pilote" };

        var modifs = FicheReference.Comparer(fiche, v);

        Assert.DoesNotContain(modifs, m => m.Cle == "adresse");
        Assert.DoesNotContain(modifs, m => m.Cle == "cin"); // même valeur, à la casse près
        Assert.Contains(modifs, m => m is { Cle: "poste", Ancienne: "Agent", Nouvelle: "Pilote" });
        Assert.Contains(modifs, m => m is { Cle: "ancienPoste", Nouvelle: "Agent" });
    }

    [Fact]
    public void La_completude_liste_les_champs_obligatoires_vides()
    {
        Assert.Equal(FicheReference.ChampsObligatoires.Count, FicheReference.ChampsManquants((UserHrProfile?)null).Count);
        var fiche = new UserHrProfile { Cin = "ZZ1", Telephone1 = "0600000000" };
        var manquants = FicheReference.ChampsManquants(fiche);
        Assert.DoesNotContain("CIN", manquants);
        Assert.Contains("Adresse", manquants);
    }

    // ── Rapprochement ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Rapprochement_prudent_homonymes_noms_proches_reembauches()
    {
        var agents = new List<FicheReferenceRapprochement.Agent>
        {
            new(1, "TESTEUR", "Alpha", null, true),
            new(3, "HOMONYME", "Gamma", "000103", true),
            new(4, "HOMONYME", "Gamma", null, true),
            new(5, "HOMONYME", "Delta", null, true),
            new(6, "HOMONYME", "Delta", null, true),
            new(7, "REEMBAUCHE", "Zeta", null, true),
            new(9, "TESTEUR", "Theta", null, true),
            new(10, "DOUBLE", "Iota", null, true),
        };
        var lignes = new List<FicheReferenceRapprochement.Ligne>
        {
            new(2, "alpha  testeur", "000101", false, null),
            new(4, "HOMONYME GAMMA", "103", false, null),
            new(5, "HOMONYME DELTA", null, false, null),
            new(6, "NOUVEAU EPSILON", null, false, null),
            new(7, "BOX DIRECTION", null, false, null),
            new(8, "REEMBAUCHE ZETA", "000106", true, new DateOnly(2015, 1, 1)),
            new(9, "REEMBAUCHE ZETA", "000107", false, new DateOnly(2020, 1, 1)),
            new(11, "TESTEUR TETA", null, false, null),
            new(12, "INCONNU SORTI", null, true, null),
            new(13, "DOUBLE IOTA", null, false, null),
            new(14, "IOTA DOUBLE", null, false, null),
        };

        var r = FicheReferenceRapprochement.Rapprocher(lignes, agents).ToDictionary(x => x.Numero);

        Assert.Equal((FicheReferenceRapprochement.Identifie, (int?)1), (r[2].Statut, r[2].UserId));
        Assert.Equal((FicheReferenceRapprochement.Identifie, (int?)3), (r[4].Statut, r[4].UserId));
        Assert.Equal(FicheReferenceRapprochement.Ambigu, r[5].Statut);
        Assert.Null(r[5].UserId);
        Assert.Equal(2, r[5].Candidats.Count);
        Assert.Equal(FicheReferenceRapprochement.Nouveau, r[6].Statut);
        Assert.Equal(FicheReferenceRapprochement.Ignore, r[7].Statut);
        Assert.Equal(FicheReferenceRapprochement.Historique, r[8].Statut);
        Assert.Equal((FicheReferenceRapprochement.Identifie, (int?)7), (r[9].Statut, r[9].UserId));
        Assert.Equal((FicheReferenceRapprochement.AVerifier, (int?)9), (r[11].Statut, r[11].UserId));
        Assert.Equal(FicheReferenceRapprochement.SortiInconnu, r[12].Statut);
        // Deux lignes actives au même nom sans N° d'ordre : aucune n'est appliquée d'office.
        Assert.Equal(FicheReferenceRapprochement.Ambigu, r[13].Statut);
        Assert.Equal(FicheReferenceRapprochement.Ambigu, r[14].Statut);
    }

    // ── Service : analyse, application, ré-import, annulation ────────────────────────────────

    [Fact]
    public async Task Import_complet_puis_reimport_sans_doublon_puis_annulation()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Roles.AddRange(new Role { Id = 1, Name = "Employee" }, new Role { Id = 2, Name = "Pilote" });
        void Agent(int id, string nom, string prenom, UserHrProfile? fiche = null)
        {
            db.Users.Add(new User { Id = id, RoleId = 1, LastName = nom, FirstName = prenom, Email = $"u{id}@test.local", HireDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            if (fiche is not null) { fiche.UserId = id; db.UserHrProfiles.Add(fiche); }
        }
        Agent(1, "TESTEUR", "Alpha", new UserHrProfile { Cin = "ZZ100001", Adresse = "Ancienne adresse", Telephone1 = "0699999999", Poste = "Agent" });
        Agent(2, "TESTEUR", "Beta", new UserHrProfile { Adresse = "Adresse fiable", Telephone1 = "0688888888" });
        Agent(3, "HOMONYME", "Gamma", new UserHrProfile { ImmatriculationInterne = "000103" });
        Agent(4, "HOMONYME", "Gamma");
        Agent(5, "HOMONYME", "Delta");
        Agent(6, "HOMONYME", "Delta");
        Agent(7, "REEMBAUCHE", "Zeta");
        Agent(8, "INVERSE", "Eta");
        Agent(9, "TESTEUR", "Theta");
        await db.SaveChangesAsync();

        var annuaire = new Annuaire();
        var service = new FichesReferenceService(db, StubUsers(db), annuaire.Proxy(),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<FichesReferenceService>.Instance);

        // 1. Analyse : rien n'est écrit.
        var analyse = await service.AnalyserAsync(Classeur(), "bdd_test.xlsx");
        Assert.Equal(0, await db.FichesReferenceModifications.CountAsync());
        Assert.Equal(17, analyse.ColonnesReconnues.Count);
        Assert.Empty(analyse.ColonnesManquantes.Where(c => c != "N° BOX"));
        Assert.Contains("LISTE BOX EN", analyse.FeuillesBox);
        Assert.Equal(10, analyse.Compteurs.LignesLues);
        var l = analyse.Lignes.ToDictionary(x => x.Numero);
        Assert.Equal((FicheReferenceRapprochement.Identifie, (int?)1), (l[2].Statut, l[2].UserId));
        Assert.Equal((FicheReferenceRapprochement.Identifie, (int?)2), (l[3].Statut, l[3].UserId));
        Assert.DoesNotContain(l[3].Modifications, m => m.Cle == "adresse");
        Assert.DoesNotContain("Adresse", l[3].ChampsManquants); // la fiche garde son adresse
        Assert.Equal((FicheReferenceRapprochement.Identifie, (int?)3), (l[4].Statut, l[4].UserId));
        Assert.Equal(FicheReferenceRapprochement.Ambigu, l[5].Statut);
        Assert.Equal(("nouveau", "creer"), (l[6].Statut, l[6].ChoixParDefaut));
        Assert.Equal(FicheReferenceRapprochement.Ignore, l[7].Statut);
        Assert.Equal(FicheReferenceRapprochement.Historique, l[8].Statut);
        Assert.Equal((FicheReferenceRapprochement.Identifie, (int?)7), (l[9].Statut, l[9].UserId));
        Assert.NotEmpty(l[10].Anomalies);
        Assert.DoesNotContain(l[10].Modifications, m => m.Cle is "dateNaissance" or "immatriculationCnss");
        Assert.Equal((FicheReferenceRapprochement.AVerifier, "ignorer"), (l[11].Statut, l[11].ChoixParDefaut));

        // 2. Application : homonyme choisi à la main, nouveau créé, nom approchant laissé de côté.
        var application = await service.AppliquerAsync(new AppliquerFichesReferenceRequest
        {
            Jeton = analyse.Jeton,
            Choix = new Dictionary<int, string> { [5] = "user:5", [6] = "creer", [11] = "ignorer" },
        }, "rh@test.local");
        Assert.Equal(0, application.Compteurs.Erreurs);
        Assert.Equal(1, application.Compteurs.Crees);

        var alpha = await db.UserHrProfiles.AsNoTracking().SingleAsync(p => p.UserId == 1);
        Assert.Equal("1 rue fictive", alpha.Adresse);
        Assert.Equal("Pilote télécom", alpha.Poste);
        Assert.Equal("Agent", alpha.AncienPoste);
        Assert.Equal("CDI", alpha.TypeContrat);
        Assert.Equal(("F", "MARIE", (int?)2), (alpha.Sexe, alpha.SituationFamiliale, alpha.NombreEnfants));
        Assert.Equal(("0600000002", "Sa sœur"), (alpha.TelephoneUrgence, alpha.RelationUrgence));
        Assert.Equal(new DateOnly(1990, 5, 6), alpha.DateNaissance);
        Assert.Equal(new DateOnly(2015, 3, 2), alpha.DateEntree);
        Assert.Equal("B1", alpha.NumeroBox);
        Assert.Equal(new DateTime(2016, 1, 4), (await db.Users.AsNoTracking().SingleAsync(u => u.Id == 1)).HireDate.Date);

        var beta = await db.UserHrProfiles.AsNoTracking().SingleAsync(p => p.UserId == 2);
        Assert.Equal("Adresse fiable", beta.Adresse);       // case vide du fichier : rien d'écrasé
        Assert.Equal("0688888888", beta.Telephone1);
        Assert.Equal("B1", beta.NumeroBox);                 // N° BOX reporté sur les lignes suivantes

        Assert.Null((await db.UserHrProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == 9))?.Cin);
        Assert.Equal(10, await db.Users.CountAsync());      // un seul employé créé
        Assert.True(annuaire.Appels > 0);
        Assert.True(await db.FichesReferenceModifications.AnyAsync(m => m.Champ == "__creation__"));

        // L'analyse ne s'applique qu'une fois.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.AppliquerAsync(
            new AppliquerFichesReferenceRequest { Jeton = analyse.Jeton }, "rh@test.local"));

        // 3. Ré-import du même fichier : tout est à jour, aucun doublon.
        var seconde = await service.AnalyserAsync(Classeur(), "bdd_test.xlsx");
        Assert.Equal(0, seconde.Compteurs.AMettreAJour);
        Assert.Equal(0, seconde.Compteurs.Nouveaux);
        var reapplication = await service.AppliquerAsync(new AppliquerFichesReferenceRequest { Jeton = seconde.Jeton }, "rh@test.local");
        Assert.Equal(0, reapplication.Compteurs.ChampsModifies);
        Assert.Equal(10, await db.Users.CountAsync());

        // 4. Annulation : valeurs d'avant remises, valeurs modifiées depuis conservées, fiche créée désactivée.
        var modifieeDepuis = await db.UserHrProfiles.SingleAsync(p => p.UserId == 1);
        modifieeDepuis.Telephone1 = "0611111111";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var annulation = await service.AnnulerAsync(application.ImportId, "rh@test.local");
        Assert.True(annulation.ValeursRestaurees > 0);
        Assert.True(annulation.ValeursConservees >= 1);
        Assert.Equal(1, annulation.FichesDesactivees);
        var apres = await db.UserHrProfiles.AsNoTracking().SingleAsync(p => p.UserId == 1);
        Assert.Equal("Ancienne adresse", apres.Adresse);
        Assert.Equal("Agent", apres.Poste);
        Assert.Equal("0611111111", apres.Telephone1);
        var historique = await service.HistoriqueAsync();
        Assert.True(historique.Single(h => h.ImportId == application.ImportId).Annule);
    }

    /// <summary>Classeur fictif au format du fichier RH (17 colonnes + 1 colonne vide sans intitulé, feuille BOX).</summary>
    private static MemoryStream Classeur()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("BDS");
        string[] entetes =
        [
            "N° Ordre", "Agents", "Date d'anciennté", "Date d'Embauche", "Date de Sortie", "Nomination de poste", "Type Contrat",
            "Nationalité", "Ville de Naissance", "sexe", "Situation Familiale", "CIN", "N° d'Immatriculation", "Date de Naissance",
            "Adresse", "TEL 1", "TEL 2",
        ];
        for (var i = 0; i < entetes.Length; i++) ws.Cell(1, i + 1).Value = entetes[i];

        void Ligne(int r, params object?[] valeurs)
        {
            for (var i = 0; i < valeurs.Length; i++)
            {
                var cellule = ws.Cell(r, i + 1);
                switch (valeurs[i])
                {
                    case null: break;
                    case DateTime d: cellule.Value = d; break;
                    case double n: cellule.Value = n; break;
                    case string s: cellule.Value = s; break;
                }
            }
        }
        var D = (int y, int m, int j) => new DateTime(y, m, j);
        Ligne(2, "000101", "TESTEUR ALPHA", D(2015, 3, 2), D(2016, 1, 4), null, "Pilote télécom", "CDI", "MAROCAINE", "Ville A", "F", "M+2",
            "ZZ100001", 123456789d, D(1990, 5, 6), "1 rue fictive", "0600000001", "0600000002 (SA SŒUR)");
        Ligne(3, "000102", "beta  testeur", D(2017, 2, 1), D(2017, 2, 1), null, "Pilote", "CDD", "MAROCAINE", null, "F", "C");
        Ligne(4, "000103", "HOMONYME GAMMA", D(2018, 1, 1), D(2018, 1, 1), null, "Pilote", "CDI");
        Ligne(5, "000104", "HOMONYME DELTA", D(2018, 1, 1), D(2018, 1, 1), null, "Pilote", "CDI");
        Ligne(6, "000105", "NOUVEAU EPSILON", D(2026, 10, 1), D(2026, 10, 1), null, "Pilote", "CI-Anapec", "MAROCAINE", "Ville B", "H", "C",
            "ZZ100005", 223456789d, D(2000, 1, 1), "5 rue fictive", "0600000005");
        Ligne(7, null, "BOX DIRECTION");
        Ligne(8, "000106", "REEMBAUCHE ZETA", D(2012, 1, 1), D(2012, 1, 1), D(2014, 1, 1), "Pilote", "CDD");
        Ligne(9, "000107", "REEMBAUCHE ZETA", D(2020, 1, 1), D(2020, 1, 1), null, "Pilote", "CDI");
        Ligne(10, "000108", "INVERSE ETA", D(2019, 1, 1), D(2019, 1, 1), null, "Pilote", "CDI", null, null, "H", "C", "ZZ100008",
            D(1992, 1, 1), 987654321d);
        Ligne(11, "000109", "TESTEUR TETA", D(2019, 1, 1), D(2019, 1, 1), null, "Pilote", "CDI", null, null, null, null, "ZZ100009");

        var box = wb.AddWorksheet("LISTE BOX EN");
        box.Cell(1, 1).Value = "N° BOX";
        box.Cell(1, 2).Value = "N° Ordre";
        box.Cell(1, 3).Value = "Agents";
        box.Cell(2, 1).Value = "B1";
        box.Cell(2, 2).Value = "000101";
        box.Cell(2, 3).Value = "TESTEUR ALPHA";
        box.Cell(3, 2).Value = "000102";
        box.Cell(3, 3).Value = "BETA TESTEUR";

        var flux = new MemoryStream();
        wb.SaveAs(flux);
        flux.Position = 0;
        return flux;
    }

    // ── Doublures ────────────────────────────────────────────────────────────────────────────

    public class Doublure : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Traiter { get; set; } = (m, _) => throw new NotSupportedException(m.Name);
        protected override object? Invoke(MethodInfo? methode, object?[]? args) => Traiter(methode!, args);
    }

    /// <summary>API employés : création (sans mail) et (dés)activation, sur la même base.</summary>
    private static IUserService StubUsers(AppDbContext db)
    {
        var proxy = DispatchProxy.Create<IUserService, Doublure>();
        ((Doublure)(object)proxy).Traiter = (m, args) =>
        {
            switch (m.Name)
            {
                case nameof(IUserService.CreateUserAsync):
                {
                    var dto = (CreateUserDto)args![0]!;
                    var user = new User { RoleId = dto.RoleId, FirstName = dto.FirstName, LastName = dto.LastName, Email = "", HireDate = dto.HireDate };
                    db.Users.Add(user);
                    db.SaveChanges();
                    return Task.FromResult(new UserDto { Id = user.Id });
                }
                case nameof(IUserService.SetUserActiveAsync):
                {
                    var user = db.Users.Single(u => u.Id == (int)args![0]!);
                    user.IsActive = (bool)args[1]!;
                    db.SaveChanges();
                    return Task.FromResult(true);
                }
                default:
                    throw new NotSupportedException(m.Name);
            }
        };
        return proxy;
    }

    private sealed class Annuaire
    {
        public int Appels;

        public IDirectoryEmployeeWriteClient Proxy()
        {
            var proxy = DispatchProxy.Create<IDirectoryEmployeeWriteClient, Doublure>();
            ((Doublure)(object)proxy).Traiter = (m, _) =>
            {
                if (m.Name != nameof(IDirectoryEmployeeWriteClient.TryUpdateEmployeeAsync))
                    throw new NotSupportedException(m.Name);
                Interlocked.Increment(ref Appels);
                return Task.FromResult(true);
            };
            return proxy;
        }
    }
}
