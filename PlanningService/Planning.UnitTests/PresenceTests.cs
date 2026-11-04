using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Planning.Application.Pointage;
using Planning.Application.Presence;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;
using Planning.Infrastructure.Services.Presence;

namespace Planning.UnitTests;

/// <summary>Tableau des présences (lot A) : codes calculés, retards autorisés, périmètre.</summary>
public class PresenceTests
{
    private static readonly DateOnly Lundi = new(2026, 11, 2);

    private static PointageJourDto Jour(string statut, string? planning = "09:00", int retardEntree = 0, int retardPause = 0, params string[] pointages) =>
        new(Lundi, statut, planning, null, 80, pointages.FirstOrDefault(), pointages.LastOrDefault(), pointages,
            0, retardEntree, retardPause, retardEntree + retardPause);

    [Fact]
    public void Code_calcule_depuis_le_pointage()
    {
        Assert.Equal("P", PresenceRegles.CodeAuto(Jour(PointageJourStatuts.AHeure, pointages: ["09:00:00", "18:00:00"]), null, false));
        Assert.Equal("ABS N", PresenceRegles.CodeAuto(Jour(PointageJourStatuts.SansPointage), null, false));
        Assert.Null(PresenceRegles.CodeAuto(Jour(PointageJourStatuts.Repos, planning: null), null, false));
        Assert.Equal("AM", PresenceRegles.CodeAuto(Jour(PointageJourStatuts.Conge), "AM", false));
        Assert.Equal("F", PresenceRegles.CodeAuto(Jour(PointageJourStatuts.Ferie), null, false));
        Assert.Equal("FOR", PresenceRegles.CodeAuto(Jour(PointageJourStatuts.SansPointage), null, enFormation: true));
    }

    [Fact]
    public void Retard_autorise_ne_compte_pas()
    {
        Assert.Equal(16, PresenceRegles.RetardRetenu("P", 10, 6, false, false));
        Assert.Equal(6, PresenceRegles.RetardRetenu("P", 10, 6, entreeAutorisee: true, pauseAutorisee: false));
        Assert.Equal(0, PresenceRegles.RetardRetenu("P", 10, 6, true, true));
        Assert.Equal(16, PresenceRegles.RetardAutorise("P", 10, 6, true, true));
        Assert.Equal(0, PresenceRegles.RetardRetenu("ABS N", 10, 6, false, false));
    }

    [Fact]
    public void Codes_des_conges_alignes_sur_la_liste_paie()
    {
        Assert.Equal("ABS N", PresenceCodes.DepuisConge(AbsenceType.AbsenceNonAutorisee));
        Assert.Equal("CSS", PresenceCodes.DepuisConge(AbsenceType.CongesSansSolde));
        Assert.True(PresenceCodes.EstValide("ENF"));
        Assert.False(PresenceCodes.EstValide("XYZ"));
    }

    [Fact]
    public async Task Saisie_hors_perimetre_refusee_dans_le_perimetre_appliquee()
    {
        await using var db = await SeedAsync();
        var svc = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()));
        var sup = new PresenceViewer(500, null, null, "Superviseur", Global: false);

        var hors = await Assert.ThrowsAsync<PresenceRefusException>(() =>
            svc.SaisirAsync(sup, new PresenceSaisieRequest(3, Lundi, null, "AM", false, false, null, null), Lundi, Lundi));
        Assert.True(hors.HorsPerimetre);

        var ligne = await svc.SaisirAsync(sup,
            new PresenceSaisieRequest(2, Lundi, null, "AM", false, false, null, "Justificatif reçu"), Lundi, Lundi);
        var jour = Assert.Single(ligne.Jours);
        Assert.Equal("AM", jour.Code);
        Assert.False(jour.Auto);
        Assert.Equal("ABS N", jour.CodeAuto);
        Assert.Equal("Justificatif reçu", jour.Commentaire);
        Assert.Equal("sup", jour.ModifiePar);

        // Vider la journée : retour au calcul du pointage.
        ligne = await svc.SaisirAsync(sup, new PresenceSaisieRequest(2, Lundi, null, null, false, false, null, null), Lundi, Lundi);
        Assert.Equal("ABS N", ligne.Jours.Single().Code);
        Assert.Empty(db.PresenceJours);
    }

    [Fact]
    public async Task Grille_du_superviseur_limitee_a_son_perimetre()
    {
        await using var db = await SeedAsync();
        var svc = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()));

        var grille = await svc.GetGrilleAsync(new PresenceViewer(500, null, null, "Superviseur", false), Lundi, Lundi, true);

        var agent = Assert.Single(grille!.Agents);
        Assert.Equal(2, agent.UserId);
        Assert.Equal("Sous-traitant", agent.Type);
        Assert.Equal("Cellule A", agent.Cellule);
        Assert.Equal(1, agent.Totaux.AbsencesNonQualifiees);

        var rh = await svc.GetGrilleAsync(new PresenceViewer(null, null, null, "RH", true), Lundi, Lundi, true);
        Assert.Equal(2, rh!.Agents.Count);
    }

    [Fact]
    public async Task Avertissements_et_evp_limites_au_perimetre()
    {
        await using var db = await SeedAsync();
        var presences = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()));
        var suivi = new PresenceSuiviService(db, presences);
        var sup = new PresenceViewer(500, null, null, "Superviseur", Global: false);

        var hors = await Assert.ThrowsAsync<PresenceRefusException>(() =>
            suivi.AjouterAvertissementAsync(sup, new AvertissementRequest(3, Lundi, "Avertissement écrit", "Retards répétés")));
        Assert.True(hors.HorsPerimetre);
        await Assert.ThrowsAsync<PresenceRefusException>(() =>
            suivi.AjouterAvertissementAsync(sup, new AvertissementRequest(2, Lundi, "Inconnu", "x")));

        var av = await suivi.AjouterAvertissementAsync(sup, new AvertissementRequest(2, Lundi, "avertissement écrit", "Retards répétés"));
        Assert.Equal("Avertissement écrit", av.Type);
        Assert.Equal("Alpha Agent", av.Agent);
        Assert.Equal("sup", av.Auteur);

        var evp = await suivi.AjouterEvpAsync(sup, new EvpRequest(2, Lundi, "Retenue", -150.456m, "Matériel"));
        Assert.Equal(-150.46m, evp.Montant);
        evp = await suivi.ModifierEvpAsync(sup, evp.Id, new EvpRequest(2, Lundi, "Prime", 300m, "Objectif atteint"));
        Assert.Equal("Prime", evp.Type);

        // Une ligne hors périmètre (saisie par RH) reste invisible au superviseur.
        var rh = new PresenceViewer(null, null, null, "RH", Global: true);
        await suivi.AjouterEvpAsync(rh, new EvpRequest(3, Lundi, "Prime", 100m, "RH"));
        Assert.Single(await suivi.ListerEvpAsync(sup, Lundi, Lundi));
        Assert.Equal(2, (await suivi.ListerEvpAsync(rh, Lundi, Lundi)).Count);

        await suivi.SupprimerAvertissementAsync(sup, av.Id);
        Assert.Empty(db.Avertissements);
    }

    [Fact]
    public async Task Dossier_masque_le_rib_et_gere_les_documents()
    {
        await using var db = await SeedAsync();
        db.UserHrProfiles.Single(p => p.UserId == 2).Rib = "011 780 0000123456789012 34";
        await db.SaveChangesAsync();
        var presences = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()));
        var suivi = new PresenceSuiviService(db, presences);
        var rh = new PresenceViewer(null, null, null, "RH", Global: true);

        var doc = await suivi.AjouterDocumentAsync(rh, 2, "contrat", "../../etc/contrat.pdf", "application/pdf", [1, 2, 3], " signé ");
        Assert.Equal("Contrat", doc.Categorie);
        Assert.Equal("contrat.pdf", doc.NomFichier);
        Assert.Equal("signé", doc.Commentaire);
        await Assert.ThrowsAsync<PresenceRefusException>(() =>
            suivi.AjouterDocumentAsync(rh, 2, "Contrat", "vide.pdf", "application/pdf", [], null));

        var dossier = await suivi.GetDossierAsync(rh, 2);
        Assert.Equal("Sous-traitant", dossier.Fiche.Type);
        Assert.EndsWith("1234", dossier.Fiche.Rib);
        Assert.DoesNotContain("0000123456", dossier.Fiche.Rib);
        Assert.Single(dossier.Documents);
        Assert.Equal(new byte[] { 1, 2, 3 }, (await suivi.GetDocumentAsync(rh, doc.Id)).Contenu);

        var sup = new PresenceViewer(500, null, null, "Superviseur", Global: false);
        var refus = await Assert.ThrowsAsync<PresenceRefusException>(() => suivi.GetDossierAsync(sup, 3));
        Assert.True(refus.HorsPerimetre);
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Roles.Add(new Role { Id = 1, Name = "Pilote" });
        db.Floors.Add(new Floor { Id = 1, Name = "Pôle 1", PrimePoleId = "pole-1" });
        db.Services.AddRange(
            new Service { Id = 1, FloorId = 1, Name = "Cellule A", Code = "A", PrimeCelluleId = "cel-a" },
            new Service { Id = 2, FloorId = 1, Name = "Cellule B", Code = "B", PrimeCelluleId = "cel-b" });
        db.SubServices.AddRange(
            new SubService { Id = 10, ServiceId = 1, Name = "Service A", Code = "SA" },
            new SubService { Id = 20, ServiceId = 2, Name = "Service B", Code = "SB" });
        db.Users.AddRange(
            new User { Id = 1, RoleId = 1, FirstName = "Sup", LastName = "Erviseur", Email = "sup@kyntus.com", AuthUserId = 500, IsActive = true },
            new User { Id = 2, RoleId = 1, FirstName = "Agent", LastName = "Alpha", Email = "a@kyntus.com", SubServiceId = 10, IsActive = true },
            new User { Id = 3, RoleId = 1, FirstName = "Agent", LastName = "Beta", Email = "b@kyntus.com", SubServiceId = 20, IsActive = true });
        db.UserHrProfiles.Add(new UserHrProfile { UserId = 2, NumeroCarteAutoentrepreneur = "AE-1" });
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class FakePerimetre(int[] subServices) : IPlanningPerimeterResolver
    {
        public Task<HashSet<int>> GetManagedSubServiceIdsAsync(User manager, CancellationToken ct = default) => Task.FromResult(subServices.ToHashSet());
        public Task<HashSet<int>> GetManagedSubServiceIdsAsync(int userId, CancellationToken ct = default) => Task.FromResult(subServices.ToHashSet());
    }

    private sealed class Monitor(PointageOptions value) : IOptionsMonitor<PointageOptions>
    {
        public PointageOptions CurrentValue => value;
        public PointageOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<PointageOptions, string?> listener) => null;
    }

    /// <summary>Chaque agent : une journée planifiée sans pointage (ABS N calculé).</summary>
    private sealed class FakePointage : IPointageQueryService
    {
        public Task<IReadOnlyList<PointageSuiviDto>> CalculerPourAsync(IReadOnlyList<int> userIds, DateOnly du, DateOnly au, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PointageSuiviDto>>(userIds.Select(id => new PointageSuiviDto(
                id, $"Agent {id}", null, [], du, au, 5, true, null,
                new PointageTotauxDto(0, 0, 0, 0, [], []),
                [Jour(PointageJourStatuts.SansPointage)])).ToList());

        public Task<PointageSuiviDto?> GetMonSuiviAsync(PointageViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PointageSuiviDto?> GetSuiviCollaborateurAsync(PointageViewer viewer, int userId, DateOnly du, DateOnly au, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PointageEquipeDto?> GetEquipeAsync(PointageViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PointageEquipeDetailDto?> GetEquipeDetailAsync(PointageViewer viewer, DateOnly du, DateOnly au, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PointageAujourdhuiDto?> GetAujourdhuiAsync(PointageViewer viewer, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PointageCorrespondanceDto>> GetCorrespondancesAsync(string? statut, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PointageCorrespondanceDto?> UpdateCorrespondanceAsync(string empCode, PointageCorrespondanceUpdateRequest request, PointageViewer viewer, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ValiderCorrespondancesAutoAsync(PointageViewer viewer, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<PointageEtatDto> GetEtatAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
