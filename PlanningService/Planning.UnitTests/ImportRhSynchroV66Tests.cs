using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Planning.Application.Abstractions;
using Planning.Application.DTOs;
using Planning.Application.Employes;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;
using Xunit;

namespace Planning.UnitTests;

/// <summary>
/// v66 — Import du fichier RH : une fiche existante est vérifiée et mise à jour d'après le fichier
/// (et non écartée comme « déjà présente ») ; le CIN identifie la personne ; aucun doute n'est
/// appliqué d'office.
/// </summary>
public class ImportRhSynchroV66Tests
{
    // ── Rapprochement par le CIN ────────────────────────────────────────────────────────────

    [Fact]
    public void Le_CIN_identifie_une_fiche_dont_le_nom_est_ecrit_autrement()
    {
        var agents = new List<FicheReferenceRapprochement.Agent>
        {
            new(1, "EL AMRANI", "Mohamed", null, true, "AB123456"),
            new(2, "TAZI", "Sara", null, true, "CD654321"),
            new(3, "TAZI", "Sara", null, true, "EF000111"),
            new(4, "BENNANI", "Ahmed", null, true, "GH222333"),
            new(5, "IDRISSI", "Omar", null, true, "IJ444555"),
            new(6, "ALAOUI", "Karim", null, true, "KL777888"),
        };
        var lignes = new List<FicheReferenceRapprochement.Ligne>
        {
            new(2, "ELAMRANI Mohamed", null, false, null, "ab 123456"),     // nom collé, CIN en minuscules
            new(3, "TAZI Sara", null, false, null, "EF000111"),             // homonymes départagés par le CIN
            new(4, "BENNANI Ahmed", null, false, null, "ZZ999999"),         // même nom, CIN contradictoire
            new(5, "CHRAIBI Youssef", null, false, null, "IJ444555"),       // CIN d'une autre personne
            new(6, "ALAOUI Karim", null, false, null, null),                // sans CIN : nom seul (comportement v63)
        };

        var r = FicheReferenceRapprochement.Rapprocher(lignes, agents).ToDictionary(x => x.Numero);

        Assert.Equal((FicheReferenceRapprochement.Identifie, (int?)1), (r[2].Statut, r[2].UserId));
        Assert.Equal((FicheReferenceRapprochement.Identifie, (int?)3), (r[3].Statut, r[3].UserId));
        Assert.Equal((FicheReferenceRapprochement.AVerifier, (int?)4), (r[4].Statut, r[4].UserId));
        Assert.Equal((FicheReferenceRapprochement.AVerifier, (int?)5), (r[5].Statut, r[5].UserId));
        Assert.Equal((FicheReferenceRapprochement.Identifie, (int?)6), (r[6].Statut, r[6].UserId));
    }

    [Fact]
    public void Un_CIN_porte_par_deux_fiches_est_un_doublon_signale()
    {
        var agents = new List<FicheReferenceRapprochement.Agent>
        {
            new(1, "NACIRI", "Leila", null, true, "MN121212"),
            new(2, "NACIRI", "Leïla", null, false, "MN121212"),
        };
        var r = FicheReferenceRapprochement.Rapprocher(
            [new FicheReferenceRapprochement.Ligne(2, "NACIRI Leila", null, false, null, "MN121212")], agents).Single();

        Assert.Equal(FicheReferenceRapprochement.Ambigu, r.Statut);
        Assert.Null(r.UserId);
        Assert.Equal(2, r.Candidats.Count);
    }

    // ── Import de l'écran « Liaisons HTEL » ─────────────────────────────────────────────────

    [Fact]
    public async Task Les_fiches_existantes_sont_verifiees_et_mises_a_jour_et_seules_les_nouvelles_sont_a_creer()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Roles.Add(new Role { Id = 1, Name = "Pilote" });
        void Fiche(int id, string nom, string prenom, UserHrProfile? profil = null)
        {
            db.Users.Add(new User { Id = id, RoleId = 1, LastName = nom, FirstName = prenom, Email = $"u{id}@test.local", HireDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
            if (profil is not null) { profil.UserId = id; db.UserHrProfiles.Add(profil); }
        }
        Fiche(1, "TESTEUR", "Alpha", new UserHrProfile { Cin = "AA100001", Adresse = "Ancienne adresse", Telephone1 = "0611111111" });
        Fiche(2, "TESTEUR", "Beta", new UserHrProfile { Cin = "AA100002", Adresse = "1 rue juste", Telephone1 = "0622222222", SituationFamiliale = "MARIE" });
        Fiche(3, "HOMONYME", "Gamma");
        Fiche(4, "HOMONYME", "Gamma");
        await db.SaveChangesAsync();

        var annuaire = 0;
        var service = new FichesReferenceService(db, Stub<IUserService>(), AnnuaireStub(() => annuaire++),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<FichesReferenceService>.Instance);

        var lignes = new List<LigneImportEmployeDto>
        {
            // Fiche existante : adresse différente → remplacée ; e-mail et naissance vides → complétés.
            new(2, "TESTEUR", "Alpha", "AA100001", new DateOnly(1991, 4, 2), "MARIE", 1, "2 avenue nouvelle", "alpha@gmail.com", "0611111111"),
            // Même fiche que la ligne 6 (par le CIN ici, par le nom là) : doublon dans le fichier.
            new(3, "testeur", "BETA", "AA100002", null, null, null, "1 rue juste", null, "0622222222"),
            // Homonymes sans CIN : à vérifier, rien n'est modifié.
            new(4, "HOMONYME", "Gamma", null, null, null, null, "Adresse X", null, null),
            // Nouvel employé.
            new(5, "NOUVEAU", "Delta", "AA100005", null, null, null, null, null, null),
            // Valeur refusée (e-mail invalide) : signalée, non appliquée.
            new(6, "TESTEUR", "Beta", null, null, null, null, null, "pas-un-email", null),
        };

        var synchro = await service.SynchroniserImportEmployesAsync(lignes, "rh@test.local");

        Assert.Equal(new[] { 5 }, synchro.ACreer);
        var r = synchro.Resultats.ToDictionary(x => x.Numero);
        Assert.Equal(ImportEmployeStatuts.MisAJour, r[2].Statut);
        Assert.Contains(r[2].Modifications!, m => m.Cle == "adresse" && m.Ancienne == "Ancienne adresse" && m.Nouvelle == "2 avenue nouvelle");
        Assert.Contains(r[2].Modifications!, m => m.Cle == "emailPersonnel" && m.Nouvelle == "alpha@gmail.com");
        // Lignes 3 et 6 visent la même fiche : aucune des deux n'est appliquée d'office.
        Assert.Equal(ImportEmployeStatuts.AVerifier, r[3].Statut);
        Assert.Equal(ImportEmployeStatuts.AVerifier, r[6].Statut);
        Assert.Equal(ImportEmployeStatuts.AVerifier, r[4].Statut);
        Assert.Null(r[4].UserId);
        Assert.Equal(2, r[4].Candidats!.Count);

        var alpha = await db.UserHrProfiles.AsNoTracking().SingleAsync(p => p.UserId == 1);
        Assert.Equal("2 avenue nouvelle", alpha.Adresse);
        Assert.Equal("alpha@gmail.com", alpha.EmailPersonnel);
        Assert.Equal(new DateOnly(1991, 4, 2), alpha.DateNaissance);
        Assert.Equal(1, alpha.NombreEnfants);
        Assert.Null(await db.UserHrProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == 3));
        Assert.True(annuaire > 0);
        // Tracé et annulable comme un fichier RH de référence.
        Assert.True(await db.FichesReferenceModifications.AnyAsync(m => m.ImportId == synchro.ImportId && m.UserId == 1));
        Assert.Equal(4, await db.Users.CountAsync()); // rien n'est créé ici
    }

    [Fact]
    public async Task Une_fiche_deja_conforme_est_signalee_a_jour()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Roles.Add(new Role { Id = 1, Name = "Pilote" });
        db.Users.Add(new User { Id = 1, RoleId = 1, LastName = "TESTEUR", FirstName = "Beta", Email = "b@test.local", HireDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        db.UserHrProfiles.Add(new UserHrProfile { UserId = 1, Cin = "AA100002", Adresse = "1 rue juste" });
        await db.SaveChangesAsync();
        var service = new FichesReferenceService(db, Stub<IUserService>(), AnnuaireStub(() => { }),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<FichesReferenceService>.Instance);

        var synchro = await service.SynchroniserImportEmployesAsync(
            [new LigneImportEmployeDto(2, "Testeur", "Béta", "aa100002", null, null, null, "1 RUE JUSTE", null, null)], "rh@test.local");

        var r = Assert.Single(synchro.Resultats);
        Assert.Equal(ImportEmployeStatuts.AJour, r.Statut);
        Assert.True(r.Ok);
        Assert.Empty(r.Modifications!);
        Assert.Empty(synchro.ACreer);
    }

    // ── Doublures ───────────────────────────────────────────────────────────────────────────

    public class Doublure : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Traiter { get; set; } = (m, _) => throw new NotSupportedException(m.Name);
        protected override object? Invoke(MethodInfo? methode, object?[]? args) => Traiter(methode!, args);
    }

    private static T Stub<T>() where T : class => DispatchProxy.Create<T, Doublure>();

    private static IDirectoryEmployeeWriteClient AnnuaireStub(Action appel)
    {
        var proxy = DispatchProxy.Create<IDirectoryEmployeeWriteClient, Doublure>();
        ((Doublure)(object)proxy).Traiter = (m, _) =>
        {
            if (m.Name != nameof(IDirectoryEmployeeWriteClient.TryUpdateEmployeeAsync))
                throw new NotSupportedException(m.Name);
            appel();
            return Task.FromResult(true);
        };
        return proxy;
    }
}
