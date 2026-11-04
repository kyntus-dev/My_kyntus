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
