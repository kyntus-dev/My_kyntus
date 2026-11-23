using Microsoft.EntityFrameworkCore;
using Planning.Application.Pointage;
using Planning.Application.Presence;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Persistence.JournalActions;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

/// <summary>v64 — contrat (entrée / sortie), couverture des absences, rotation des fins de journée, journal d'actions.
/// Données fictives uniquement.</summary>
public class PlanningV64Tests
{
    // ── Période sous contrat ────────────────────────────────────────────────────────────────

    [Fact]
    public void Le_contrat_commence_a_la_date_d_entree_de_la_fiche_puis_a_l_embauche()
    {
        var avecEntree = ContratPeriode.De(new UserHrProfile { DateEntree = new DateOnly(2026, 11, 4), DateEmbauche = new DateOnly(2026, 11, 2) },
            new DateTime(2026, 11, 1));
        Assert.Equal(new DateOnly(2026, 11, 4), avecEntree.Entree);
        Assert.True(avecEntree.AvantEntree(new DateOnly(2026, 11, 3)));
        Assert.True(avecEntree.Couvre(new DateOnly(2026, 11, 4)));

        // Sans fiche : pas de borne (la date d'embauche du compte n'est pas fiable pour les comptes de l'annuaire).
        var sansFiche = ContratPeriode.De(null, new DateTime(2026, 11, 9));
        Assert.Null(sansFiche.Entree);
        Assert.Null(sansFiche.Sortie);
        Assert.True(sansFiche.Couvre(new DateOnly(2020, 1, 1)));

        var embaucheRh = ContratPeriode.De(new UserHrProfile { DateEmbauche = new DateOnly(2026, 11, 2) });
        Assert.Equal(new DateOnly(2026, 11, 2), embaucheRh.Entree);
    }

    [Fact]
    public void Les_jours_apres_la_sortie_sont_hors_contrat_sauf_reembauche()
    {
        var sorti = ContratPeriode.De(new UserHrProfile { DateEntree = new DateOnly(2025, 1, 6), DateSortie = new DateOnly(2026, 11, 14) }, null);
        Assert.True(sorti.Couvre(new DateOnly(2026, 11, 14)));
        Assert.True(sorti.ApresSortie(new DateOnly(2026, 11, 16)));
        Assert.NotNull(sorti.MotifHorsContrat(new DateOnly(2026, 11, 16)));

        // Réembauché le 01/09/2026 après une sortie le 30/06/2026 : l'ancienne sortie ne s'applique plus.
        var reembauche = ContratPeriode.De(
            new UserHrProfile { DateEntree = new DateOnly(2024, 1, 8), DateEmbauche = new DateOnly(2026, 9, 1), DateSortie = new DateOnly(2026, 6, 30) },
            new DateTime(2026, 9, 1));
        Assert.Null(reembauche.Sortie);
        Assert.True(reembauche.Couvre(new DateOnly(2026, 11, 20)));
    }

    [Fact]
    public void Un_jour_hors_contrat_n_a_ni_presence_ni_absence()
    {
        var jour = new PointageJourDto(new DateOnly(2026, 11, 2), PointageJourStatuts.HorsContrat, "09:00 - 18:00", null, 60,
            null, null, [], 0, 0, 0, 0);
        Assert.Null(PresenceRegles.CodeAuto(jour, null, false));
        Assert.Null(PresenceRegles.CodeAuto(jour, "AM", false));
    }

    // ── Couverture des absences ─────────────────────────────────────────────────────────────

    private static readonly CouvertureAbsences.Creneau[] Creneaux =
    [
        new(1, 2, true, 1),   // ouverture
        new(2, 4, false, 2),
        new(3, 3, false, 3),
        new(4, 2, true, 4),   // fermeture
    ];

    [Fact]
    public void Effectif_complet_quotas_d_origine()
    {
        var r = CouvertureAbsences.Repartir(Creneaux, 11);
        Assert.Equal(new[] { 2, 4, 3, 2 }, new[] { r[1], r[2], r[3], r[4] });
    }

    [Theory]
    [InlineData(10)]
    [InlineData(8)]
    [InlineData(6)]
    [InlineData(4)]
    [InlineData(2)]
    public void Effectif_reduit_tout_le_monde_est_place_critiques_d_abord_sans_depasser_les_quotas(int disponibles)
    {
        var r = CouvertureAbsences.Repartir(Creneaux, disponibles);
        Assert.Equal(disponibles, r.Values.Sum());
        foreach (var c in Creneaux) Assert.InRange(r[c.Id], 0, c.Requis);
        Assert.True(r[1] >= 1 && r[4] >= 1); // ouverture et fermeture toujours couvertes
        if (disponibles >= 4) Assert.All(Creneaux, c => Assert.True(r[c.Id] >= 1));
    }

    [Fact]
    public void Un_seul_present_va_au_premier_creneau_critique()
    {
        var r = CouvertureAbsences.Repartir(Creneaux, 1);
        Assert.Equal(1, r[1]);
        Assert.Equal(0, r[2] + r[3] + r[4]);
    }

    [Fact]
    public void Quotas_du_jour_null_quand_l_effectif_suffit()
    {
        var configs = new List<SubServiceShiftConfig>
        {
            new() { Id = 1, RequiredCount = 2, ShiftKind = ShiftKind.Opening },
            new() { Id = 2, RequiredCount = 3 },
        };
        Assert.Null(CouvertureAbsences.QuotasDuJour(configs, 5));
        var reduit = CouvertureAbsences.QuotasDuJour(configs, 3)!;
        Assert.Equal(3, reduit.Values.Sum());
        Assert.True(reduit[1] >= 1);
    }

    // ── Rotation des fins de journée ────────────────────────────────────────────────────────

    private static SubServiceShiftConfig Horaire(int id, int heure) =>
        new() { Id = id, Label = $"H{id}", StartTime = new TimeOnly(heure, 0), WorkHours = 8, BreakDurationMinutes = 60 };

    [Fact]
    public void Les_fins_tardives_tournent_vers_les_agents_qui_en_ont_eu_le_moins()
    {
        var configs = new[] { Horaire(1, 8), Horaire(2, 9), Horaire(3, 11) }.ToDictionary(c => c.Id);
        var agents = Enumerable.Range(1, 3).ToDictionary(i => i, i => new User { Id = i, Level = 1 });
        var lundi = new DateOnly(2026, 11, 23);
        // Agent 1 sur l'horaire tardif (11h) ; il a déjà fini tard ces dernières semaines.
        var semaine = new List<ShiftAssignment>
        {
            new() { UserId = 1, AssignedDate = lundi, SubServiceShiftConfigId = 3 },
            new() { UserId = 2, AssignedDate = lundi, SubServiceShiftConfigId = 1 },
            new() { UserId = 3, AssignedDate = lundi, SubServiceShiftConfigId = 2 },
        };
        var historique = new Dictionary<int, double> { [1] = 12, [2] = -6, [3] = 0 };

        var echanges = RotationFinJournee.Equilibrer(semaine, semaine, configs, agents, historique);

        Assert.True(echanges >= 1);
        Assert.Equal(3, semaine.Single(a => a.UserId == 2).SubServiceShiftConfigId); // le moins chargé prend la fin tardive
        Assert.Equal(new[] { 1, 2, 3 }, semaine.Select(a => a.SubServiceShiftConfigId!.Value).Order().ToArray()); // quotas inchangés
    }

    [Fact]
    public void Aucun_echange_entre_niveaux_differents_ni_sur_une_journee_figee()
    {
        var configs = new[] { Horaire(1, 8), Horaire(2, 11) }.ToDictionary(c => c.Id);
        var agents = new Dictionary<int, User> { [1] = new() { Id = 1, Level = 1 }, [2] = new() { Id = 2, Level = 3 }, [3] = new() { Id = 3, Level = 1 } };
        var lundi = new DateOnly(2026, 11, 23);
        var semaine = new List<ShiftAssignment>
        {
            new() { UserId = 1, AssignedDate = lundi, SubServiceShiftConfigId = 2 },
            new() { UserId = 2, AssignedDate = lundi, SubServiceShiftConfigId = 1 },
            new() { UserId = 3, AssignedDate = lundi, SubServiceShiftConfigId = 1, IsManagerOverride = true },
        };
        var echanges = RotationFinJournee.Equilibrer(semaine, semaine, configs, agents, new Dictionary<int, double> { [1] = 20 });
        Assert.Equal(0, echanges);
        Assert.Equal(2, semaine[0].SubServiceShiftConfigId);
    }

    [Fact]
    public void Un_echange_ne_cree_pas_deux_jours_de_suite_sur_le_meme_horaire()
    {
        var configs = new[] { Horaire(1, 8), Horaire(2, 11) }.ToDictionary(c => c.Id);
        var agents = Enumerable.Range(1, 2).ToDictionary(i => i, i => new User { Id = i, Level = 1 });
        var lundi = new DateOnly(2026, 11, 23);
        var mardi = lundi.AddDays(1);
        var aTraiter = new List<ShiftAssignment>
        {
            new() { UserId = 1, AssignedDate = mardi, SubServiceShiftConfigId = 2 },
            new() { UserId = 2, AssignedDate = mardi, SubServiceShiftConfigId = 1 },
        };
        // Lundi (figé) : l'agent 2 était déjà sur l'horaire tardif.
        var semaine = aTraiter.Concat([
            new ShiftAssignment { UserId = 1, AssignedDate = lundi, SubServiceShiftConfigId = 1 },
            new ShiftAssignment { UserId = 2, AssignedDate = lundi, SubServiceShiftConfigId = 2 },
        ]).ToList();
        RotationFinJournee.Equilibrer(aTraiter, semaine, configs, agents, new Dictionary<int, double> { [1] = 30 });
        Assert.Equal(1, aTraiter.Single(a => a.UserId == 2).SubServiceShiftConfigId);
    }

    // ── Journal d'actions ───────────────────────────────────────────────────────────────────

    private static AppDbContext Contexte() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("journal-v64-" + Guid.NewGuid()).Options);

    [Fact]
    public void Le_journal_capture_avant_apres_et_masque_les_identifiants()
    {
        using var db = Contexte();
        var profil = new UserHrProfile { UserId = 7, Adresse = "Adresse fictive", Cin = "ZZ123456", TypeEmploye = TypesEmploye.Salarie };
        db.UserHrProfiles.Add(profil);
        db.SaveChanges();

        profil.Adresse = "Nouvelle adresse fictive";
        profil.Cin = "ZZ999999";
        profil.TypeEmploye = TypesEmploye.SousTraitant;
        db.ChangeTracker.DetectChanges();
        var brouillons = JournalCapture.Capturer(db.ChangeTracker.Entries(), new HashSet<Type>());

        var b = Assert.Single(brouillons);
        Assert.Equal(JournalCapture.ModuleEmployes, b.Module);
        Assert.Equal(7, b.CibleUserId);
        Assert.Equal("Adresse fictive", b.Anciennes!["Adresse"]);
        Assert.Equal("Nouvelle adresse fictive", b.Nouvelles!["Adresse"]);
        Assert.Equal("••••3456", b.Anciennes["CIN"]);
        Assert.Equal("••••9999", b.Nouvelles["CIN"]);
        Assert.Equal("Sous-traitant", b.Nouvelles["Type d'employé"]);
        Assert.Equal("Salarié", b.Anciennes["Type d'employé"]);
    }

    [Fact]
    public void Desactivation_absence_et_silence_de_la_generation()
    {
        using var db = Contexte();
        var user = new User { Id = 3, FirstName = "Alpha", LastName = "FICTIF", Email = "fictif@kyntus.com", IsActive = true };
        db.Users.Add(user);
        db.SaveChanges();

        user.IsActive = false;
        db.Conges.Add(new Conge { UserId = 3, StartDate = new DateOnly(2026, 11, 2), EndDate = new DateOnly(2026, 11, 4), AbsenceType = AbsenceType.ArretMaladie });
        db.ShiftAssignments.Add(new ShiftAssignment { UserId = 3, AssignedDate = new DateOnly(2026, 11, 2) });
        db.ChangeTracker.DetectChanges();

        var brouillons = JournalCapture.Capturer(db.ChangeTracker.Entries(), new HashSet<Type>());
        Assert.Contains(brouillons, b => b.Operation == "Désactivation" && b.CibleUserId == 3);
        Assert.Contains(brouillons, b => b.Module == JournalCapture.ModuleConges && b.Element.StartsWith("Arrêt maladie du 02/11/2026 au 04/11/2026"));
        Assert.DoesNotContain(brouillons, b => b.Entite == nameof(ShiftAssignment)); // journées générées : tracées en bloc

        var silencieux = JournalCapture.Capturer(db.ChangeTracker.Entries(), new HashSet<Type> { typeof(Conge) });
        Assert.DoesNotContain(silencieux, b => b.Entite == nameof(Conge));
    }

    [Theory]
    [InlineData("SuperAdmin", "Admin")]
    [InlineData("Super Admin", "Admin")]
    [InlineData("super_admin", "Admin")]
    [InlineData("RH", "RH")]
    [InlineData(null, null)]
    public void Le_role_Super_Admin_n_apparait_jamais(string? role, string? attendu) =>
        Assert.Equal(attendu, JournalActeur.NormaliserRole(role));

    [Theory]
    [InlineData("=SOMME(A1)", "'=SOMME(A1)")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("ligne\nsuite", "ligne suite")]
    public void L_export_csv_neutralise_les_formules_et_les_separateurs(string valeur, string attendu) =>
        Assert.Equal(attendu, JournalActionsService.Csv(valeur));
}
