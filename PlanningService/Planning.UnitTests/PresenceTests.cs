using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;
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
    public async Task Grille_du_superviseur_montre_ses_agents_en_formation_en_lecture_v37()
    {
        await using var db = await SeedAsync();
        var sup = await db.Users.SingleAsync(u => u.Id == 1);
        // Agent 4 : en formation, rattaché au périmètre ; agent 5 : en formation, fiche « superviseur » = sup ;
        // agent 6 : en formation, ni périmètre ni fiche.
        db.Users.AddRange(
            new User { Id = 4, RoleId = 1, FirstName = "Agent", LastName = "Form1", Email = "f1@kyntus.com", SubServiceId = 10, IsActive = true },
            new User { Id = 5, RoleId = 1, FirstName = "Agent", LastName = "Form2", Email = "f2@kyntus.com", IsActive = true },
            new User { Id = 6, RoleId = 1, FirstName = "Agent", LastName = "Form3", Email = "f3@kyntus.com", IsActive = true });
        db.UserHrProfiles.AddRange(
            new UserHrProfile { UserId = 4, EnFormation = true },
            new UserHrProfile { UserId = 5, EnFormation = true, SuperviseurId = sup.Guid },
            new UserHrProfile { UserId = 6, EnFormation = true });
        await db.SaveChangesAsync();
        var svc = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()));
        var viewer = new PresenceViewer(500, null, null, "Superviseur", false);

        var grille = await svc.GetGrilleAsync(viewer, Lundi, Lundi, true);

        Assert.Equal(new[] { 2, 4, 5 }, grille!.Agents.Select(a => a.UserId).OrderBy(i => i).ToArray());
        Assert.True(grille.Agents.Single(a => a.UserId == 4).EnFormation);
        Assert.Contains("dont 2 en formation", grille.Perimetre);

        // Saisie : réservée à l'équipe formation.
        var refus = await Assert.ThrowsAsync<PresenceRefusException>(() =>
            svc.SaisirAsync(viewer, new PresenceSaisieRequest(4, Lundi, null, "AM", false, false, null, null), Lundi, Lundi));
        Assert.True(refus.HorsPerimetre);
    }

    [Fact]
    public async Task Superviseur_ne_peut_pas_autoriser_un_retard_v46()
    {
        await using var db = await SeedAsync();
        var svc = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()));
        var sup = new PresenceViewer(500, null, null, "Superviseur", Global: false);

        var refus = await Assert.ThrowsAsync<PresenceRefusException>(() =>
            svc.SaisirAsync(sup, new PresenceSaisieRequest(2, Lundi, null, "P", true, false, "prévenu", null), Lundi, Lundi));
        Assert.Contains("chef de projet", refus.Message);

        // Sans toucher à l'autorisation, la saisie reste possible.
        var ligne = await svc.SaisirAsync(sup, new PresenceSaisieRequest(2, Lundi, null, "P", false, false, null, "ok"), Lundi, Lundi);
        Assert.Equal("P", ligne.Jours.Single().Code);
        var grille = await svc.GetGrilleAsync(sup, Lundi, Lundi, true);
        Assert.False(grille!.PeutAutoriserRetard);
    }

    [Fact]
    public async Task Chef_de_projet_voit_superviseurs_et_referents_de_son_perimetre_v46()
    {
        await using var db = await SeedAsync();
        db.Roles.AddRange(new Role { Id = 2, Name = "Superviseur" }, new Role { Id = 3, Name = "Chef de projet" }, new Role { Id = 4, Name = "Référent technique" });
        var supGuid = Guid.NewGuid();
        db.Users.AddRange(
            new User { Id = 7, RoleId = 3, FirstName = "Chef", LastName = "Projet", Email = "cp@kyntus.com", AuthUserId = 700, IsActive = true },
            new User { Id = 8, RoleId = 2, FirstName = "Sup", LastName = "Designe", Email = "s8@kyntus.com", Guid = supGuid, IsActive = true },
            new User { Id = 9, RoleId = 4, FirstName = "Ref", LastName = "Place", Email = "r9@kyntus.com", SubServiceId = 10, IsActive = true },
            new User { Id = 11, RoleId = 2, FirstName = "Sup", LastName = "Ailleurs", Email = "s11@kyntus.com", SubServiceId = 20, IsActive = true });
        await db.SaveChangesAsync();
        var profil = await db.UserHrProfiles.SingleAsync(p => p.UserId == 2);
        profil.SuperviseurId = supGuid;
        await db.SaveChangesAsync();
        var svc = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()));

        var grille = await svc.GetGrilleAsync(new PresenceViewer(700, null, null, "Chef de projet", false), Lundi, Lundi, true);

        Assert.Equal(new[] { 2, 8, 9 }, grille!.Agents.Select(a => a.UserId).OrderBy(i => i).ToArray());
        Assert.Equal("Superviseur", grille.Agents.Single(a => a.UserId == 8).Role);
        Assert.True(grille.PeutAutoriserRetard);
    }

    [Fact]
    public async Task Rubrique_presences_superviseurs_et_referents_v61()
    {
        await using var db = await SeedAsync();
        db.Roles.AddRange(new Role { Id = 2, Name = "Superviseur" }, new Role { Id = 3, Name = "RP" }, new Role { Id = 4, Name = "Référent technique" });
        var supGuid = Guid.NewGuid();
        db.Users.AddRange(
            new User { Id = 7, RoleId = 3, FirstName = "Resp", LastName = "Projet", Email = "rp@kyntus.com", AuthUserId = 700, IsActive = true },
            new User { Id = 8, RoleId = 2, FirstName = "Sup", LastName = "Designe", Email = "s8@kyntus.com", Guid = supGuid, IsActive = true },
            new User { Id = 9, RoleId = 4, FirstName = "Ref", LastName = "Place", Email = "r9@kyntus.com", SubServiceId = 10, IsActive = true });
        await db.SaveChangesAsync();
        (await db.UserHrProfiles.SingleAsync(p => p.UserId == 2)).SuperviseurId = supGuid;
        await db.SaveChangesAsync();
        var svc = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()));

        // Consulter + Modifier (défaut du RP) : listes dédiées, périmètre du superviseur = cellule de ses agents.
        var complet = new PresenceViewer(700, null, null, "RP", false, EncadrementVoir: true, EncadrementAgir: true);
        var grille = await svc.GetGrilleAsync(complet, Lundi, Lundi, true);
        Assert.True(grille!.EncadrementDedie);
        Assert.True(grille.EncadrementModifiable);
        Assert.Equal(new[] { 2, 8, 9 }, grille.Agents.Select(a => a.UserId).OrderBy(i => i).ToArray());
        var sup = grille.Agents.Single(a => a.UserId == 8);
        Assert.Equal(new[] { "pole-1" }, sup.PoleIdsEncadres);
        Assert.Equal(new[] { "Cellule A" }, sup.CellulesEncadrees);
        Assert.Null(grille.Agents.Single(a => a.UserId == 2).PoleIdsEncadres);

        // Consulter seulement : visibles, saisie refusée sur un superviseur, permise sur un agent.
        var lecture = new PresenceViewer(700, null, null, "RP", false, EncadrementVoir: true, EncadrementAgir: false);
        Assert.False((await svc.GetGrilleAsync(lecture, Lundi, Lundi, true))!.EncadrementModifiable);
        await Assert.ThrowsAsync<PresenceRefusException>(() =>
            svc.SaisirAsync(lecture, new PresenceSaisieRequest(8, Lundi, null, "P", false, false, null, null), Lundi, Lundi));
        var agent = await svc.SaisirAsync(lecture, new PresenceSaisieRequest(2, Lundi, null, "P", false, false, null, null), Lundi, Lundi);
        Assert.Equal("P", agent.Jours.Single().Code);

        // Droit retiré : ni superviseur ni référent, même placé dans le service du RP.
        var sans = new PresenceViewer(700, null, null, "RP", false, EncadrementVoir: false, EncadrementAgir: false);
        var reduite = await svc.GetGrilleAsync(sans, Lundi, Lundi, true);
        Assert.False(reduite!.EncadrementDedie);
        Assert.Equal(new[] { 2 }, reduite.Agents.Select(a => a.UserId).ToArray());
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

    [Fact]
    public async Task Export_planning_rh_limite_au_perimetre_avec_horaires()
    {
        await using var db = await SeedAsync();
        db.WeeklyPlannings.AddRange(
            new WeeklyPlanning { Id = 1, SubServiceId = 10, WeekCode = "2026-W45", WeekStartDate = Lundi, Status = PlanningStatus.Published },
            new WeeklyPlanning { Id = 2, SubServiceId = 20, WeekCode = "2026-W45", WeekStartDate = Lundi, Status = PlanningStatus.Published });
        db.SubServiceShiftConfigs.Add(new SubServiceShiftConfig
        {
            Id = 1, SubServiceId = 10, Label = "Matin", StartTime = new TimeOnly(8, 0), WorkHours = 8, BreakDurationMinutes = 60,
        });
        db.ShiftAssignments.AddRange(
            new ShiftAssignment { Id = 1, WeeklyPlanningId = 1, UserId = 2, AssignedDate = Lundi, SubServiceShiftConfigId = 1 },
            new ShiftAssignment { Id = 2, WeeklyPlanningId = 1, UserId = 2, AssignedDate = Lundi.AddDays(1), IsOnLeave = true },
            new ShiftAssignment { Id = 3, WeeklyPlanningId = 2, UserId = 3, AssignedDate = Lundi, SubServiceShiftConfigId = 1 });
        await db.SaveChangesAsync();
        var presences = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()));
        var export = new Planning.Infrastructure.Services.PlanningExport.PlanningExportService(db, presences);

        var sup = await export.GetPlanningRhAsync(new PresenceViewer(500, null, null, "Superviseur", false), Lundi, Lundi.AddDays(1));
        var agent = Assert.Single(sup.Agents);
        Assert.Equal("ALPHA Agent", agent.Nom);
        Assert.Equal(("08:00", "17:00", 480, "Matin"), (agent.Jours[0].Debut, agent.Jours[0].Fin, agent.Jours[0].Minutes, agent.Jours[0].Libelle));
        Assert.True(agent.Jours[1].Conge);

        var rh = await export.GetPlanningRhAsync(new PresenceViewer(null, null, null, "RH", true), Lundi, Lundi);
        Assert.Equal(2, rh.Agents.Count);
    }

    [Fact]
    public async Task Arret_maladie_saisi_retire_le_shift_puis_le_rend_et_previent_l_agent_v62()
    {
        await using var db = await SeedAsync();
        (await db.Users.SingleAsync(u => u.Id == 2)).AuthUserId = 902;
        var semaine = new WeeklyPlanning { Id = 50, SubServiceId = 10, WeekCode = "S45", WeekStartDate = Lundi, Status = PlanningStatus.Published };
        db.WeeklyPlannings.Add(semaine);
        db.ShiftAssignments.AddRange(
            new ShiftAssignment { WeeklyPlanningId = 50, UserId = 2, AssignedDate = Lundi, DayOfWeek = DayOfWeekEnum.Monday, ShiftId = 1, BreakTime = new TimeOnly(13, 0) },
            new ShiftAssignment { WeeklyPlanningId = 50, UserId = 2, AssignedDate = Lundi.AddDays(1), DayOfWeek = DayOfWeekEnum.Tuesday, ShiftId = 1 });
        await db.SaveChangesAsync();
        var notifier = new NotifierEnregistreur();
        var svc = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()), notifier: notifier);
        var sup = new PresenceViewer(500, null, null, "Superviseur", Global: false);

        // AM du lundi au mardi : plus de shift, créneau et pause gardés pour le retour.
        await svc.SaisirAsync(sup, new PresenceSaisieRequest(2, Lundi, Lundi.AddDays(1), "AM", false, false, null, null), Lundi, Lundi);
        var jours = await db.ShiftAssignments.AsNoTracking().Where(a => a.UserId == 2).OrderBy(a => a.AssignedDate).ToListAsync();
        Assert.All(jours, a => Assert.True(a.IsOnLeave));
        Assert.Equal(new TimeOnly(13, 0), jours[0].BreakTime);
        Assert.Equal("Absence enregistrée : Arrêt maladie", Assert.Single(notifier.Envois).Titre);
        Assert.Equal("/mes-plannings", notifier.Envois[0].Lien);

        // Motif retiré le mardi : le shift revient ce jour-là seulement.
        await svc.SaisirAsync(sup, new PresenceSaisieRequest(2, Lundi.AddDays(1), null, null, false, false, null, null), Lundi, Lundi);
        jours = await db.ShiftAssignments.AsNoTracking().Where(a => a.UserId == 2).OrderBy(a => a.AssignedDate).ToListAsync();
        Assert.True(jours[0].IsOnLeave);
        Assert.False(jours[1].IsOnLeave);
        Assert.Equal("Absence retirée", notifier.Envois[1].Titre);

        // Absence partielle : l'agent reste planifié.
        await svc.SaisirAsync(sup, new PresenceSaisieRequest(2, Lundi.AddDays(1), null, "ABS", false, false, null, null, AbsencePartielle: true), Lundi, Lundi);
        Assert.False((await db.ShiftAssignments.AsNoTracking().SingleAsync(a => a.UserId == 2 && a.AssignedDate == Lundi.AddDays(1))).IsOnLeave);
    }

    [Fact]
    public async Task Motif_retire_ne_rend_pas_un_jour_couvert_par_un_conge_valide_v62()
    {
        await using var db = await SeedAsync();
        db.WeeklyPlannings.Add(new WeeklyPlanning { Id = 51, SubServiceId = 10, WeekCode = "S45", WeekStartDate = Lundi });
        db.ShiftAssignments.Add(new ShiftAssignment { WeeklyPlanningId = 51, UserId = 2, AssignedDate = Lundi, DayOfWeek = DayOfWeekEnum.Monday, ShiftId = 1 });
        db.Conges.Add(new Conge { UserId = 2, StartDate = Lundi, EndDate = Lundi, Status = CongeStatus.Approved });
        await db.SaveChangesAsync();
        var svc = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()));
        var sup = new PresenceViewer(500, null, null, "Superviseur", Global: false);

        await svc.SaisirAsync(sup, new PresenceSaisieRequest(2, Lundi, null, "AM", false, false, null, null), Lundi, Lundi);
        await svc.SaisirAsync(sup, new PresenceSaisieRequest(2, Lundi, null, null, false, false, null, null), Lundi, Lundi);

        Assert.True((await db.ShiftAssignments.AsNoTracking().SingleAsync()).IsOnLeave);
    }

    [Fact]
    public void Motifs_de_presence_vus_par_le_planning_v62()
    {
        Assert.True(PresenceCodes.RetireDuPlanning("AM"));
        Assert.True(PresenceCodes.RetireDuPlanning("abs n"));
        Assert.False(PresenceCodes.RetireDuPlanning("P"));
        Assert.False(PresenceCodes.RetireDuPlanning("FOR"));
        Assert.False(PresenceCodes.RetireDuPlanning("F"));
        Assert.False(PresenceCodes.RetireDuPlanning(null));
        Assert.Equal(AbsenceType.ArretMaladie, PresenceCodes.VersAbsenceType("AM"));

        var conges = PresenceAbsencesPlanning.Convertir([(2, Lundi, "AM"), (2, Lundi.AddDays(1), "P"), (3, Lundi, "CP")]);
        Assert.Equal(2, conges.Count);
        Assert.All(conges, c => Assert.Equal(PresenceAbsencesPlanning.Raison, c.Reason));

        var periodes = PresenceAbsencesPlanning.Periodes([Lundi.AddDays(3), Lundi, Lundi.AddDays(1), Lundi.AddDays(1)]);
        Assert.Equal(new[] { (Lundi, Lundi.AddDays(1)), (Lundi.AddDays(3), Lundi.AddDays(3)) }, periodes);
    }

    [Fact]
    public async Task RH_voit_les_listes_superviseurs_et_referents_meme_non_places_v62()
    {
        await using var db = await SeedAsync();
        db.Roles.Add(new Role { Id = 2, Name = "Superviseur" });
        db.Users.Add(new User { Id = 8, RoleId = 2, FirstName = "Sup", LastName = "Organisation", Email = "s8@kyntus.com", IsActive = true });
        await db.SaveChangesAsync();
        var svc = new PresenceService(db, new FakePointage(), new FakePerimetre([10]), new Monitor(new PointageOptions()));

        var rh = new PresenceViewer(null, null, null, "RH", true, EncadrementVoir: true, EncadrementAgir: false);
        var grille = await svc.GetGrilleAsync(rh, Lundi, Lundi, true);
        Assert.True(grille!.FiltreEncadrement);
        Assert.False(grille.EncadrementModifiable);
        Assert.False(grille.EncadrementDedie); // tableau principal des RH inchangé
        Assert.Contains(grille.Agents, a => a.UserId == 8);
        await Assert.ThrowsAsync<PresenceRefusException>(() =>
            svc.SaisirAsync(rh, new PresenceSaisieRequest(8, Lundi, null, "P", false, false, null, null), Lundi, Lundi));

        var sansDroit = new PresenceViewer(null, null, null, "RH", true, EncadrementVoir: false, EncadrementAgir: false);
        var reduite = await svc.GetGrilleAsync(sansDroit, Lundi, Lundi, true);
        Assert.False(reduite!.FiltreEncadrement);
        Assert.DoesNotContain(reduite.Agents, a => a.UserId == 8);
    }

    private sealed class NotifierEnregistreur : IAppNotifier
    {
        public List<AppNotification> Envois { get; } = [];

        public Task<int> NotifierAsync(NotificationEmailRecipients destinataires, AppNotification notification,
            IReadOnlyCollection<string>? rolesExclus = null, CancellationToken ct = default)
        {
            Envois.Add(notification);
            return Task.FromResult(1);
        }
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
        public Task<bool> EnregistrerChoixProductionAsync(PointageViewer viewer, string? choix, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EnregistrerChoixProductionJourAsync(PointageViewer viewer, DateOnly jour, string? choix, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChoixProductionJourDto>> GetMesChoixProductionAsync(PointageViewer viewer, DateOnly au, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
