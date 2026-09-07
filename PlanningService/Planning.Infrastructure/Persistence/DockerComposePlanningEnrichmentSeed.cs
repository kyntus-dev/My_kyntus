using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Planning.Application.Abstractions;
using Planning.Application.DTOs.Planning;
using Planning.Domain.Entities;
using Planning.Domain.Enums;

namespace Planning.Infrastructure.Persistence;

/// <summary>
/// Événements planning (réclamations, notifications, semaines si cellule peuplée)
/// sur employés **déjà présents** — jamais de création User / org.
/// </summary>
internal static class DockerComposePlanningEnrichmentSeed
{
    private const int EnrichmentVersion = 3;
    private const string MarkerAction = "DockerPlanningEnrichmentV2";
    private const string ChangeMarker = "DockerPlanningEnrichmentV3-Change";
    private const string ExceptionalMarker = "DockerPlanningEnrichmentV3-Exceptional";
    private const string ReinforcementMarker = "DockerPlanningEnrichmentV3-Renfort";
    private const string PropositionMarkerTitle = "Amélioration accueil inbound (démo)";
    private const string NewsletterMarkerTitle = "Info plateforme inbound (démo)";
    private const string ReclamationMarkerTitle = "Qualité audio — plateforme inbound grands comptes";

    internal static async Task ApplyIfEnabledAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken ct = default)
    {
        if (!IsEnabled(configuration))
            return;

        using var scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var planningService = scope.ServiceProvider.GetRequiredService<IPlanningService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Planning.EnrichmentSeed");

        // Préférer la cellule Prime c1 ; sinon la plus peuplée
        var sub = await context.SubServices
            .FirstOrDefaultAsync(s => s.PrimeServiceId == "c1" || s.Code == "c1", ct);
        if (sub is null)
        {
            var subId = await context.Users
                .Where(u => u.IsActive && u.SubServiceId != null)
                .GroupBy(u => u.SubServiceId!.Value)
                .Select(g => new { SubId = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Select(x => x.SubId)
                .FirstOrDefaultAsync(ct);

            if (subId == 0)
            {
                logger.LogWarning("Planning enrichment : aucune cellule peuplée — réclamations seules si auteurs présents.");
                var fallbackUsers = await ResolveAnyActiveUsersAsync(context, ct);
                await SeedReclamationsAsync(context, fallbackUsers, ct);
                await SeedReclamationFollowUpAsync(context, fallbackUsers, ct);
                await SeedPropositionsAsync(context, fallbackUsers, ct);
                await SeedNewslettersAsync(context, fallbackUsers, fallbackUsers.FirstOrDefault(), ct);
                await MarkAppliedWithoutWeekAsync(context, ct);
                return;
            }

            sub = await context.SubServices.FirstAsync(s => s.Id == subId, ct);
        }

        var manager = await context.Users
            .Include(u => u.Role)
            .Where(u => u.IsActive && u.Role.Name == "Superviseur")
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync(ct)
            ?? await context.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.Id)
                .FirstOrDefaultAsync(ct);

        if (manager is null)
        {
            logger.LogWarning("Planning enrichment ignoré : aucun utilisateur actif.");
            return;
        }

        var cellUsers = await context.Users
            .Where(u => u.SubServiceId == sub.Id && u.IsActive)
            .OrderBy(u => u.Id)
            .ToListAsync(ct);

        if (cellUsers.Count == 0)
        {
            logger.LogWarning("Planning enrichment : cellule vide — réclamations sur utilisateurs actifs globaux.");
            cellUsers = await ResolveAnyActiveUsersAsync(context, ct);
        }

        if (cellUsers.Count == 0)
        {
            logger.LogWarning("Planning enrichment ignoré : aucun employé actif.");
            return;
        }

        // Semaines : uniquement si absentes (événements sur org existante).
        var existingWeeks = await context.WeeklyPlannings.CountAsync(p => p.SubServiceId == sub.Id, ct);
        if (existingWeeks == 0)
        {
            await EnsureSaturdayGroupsAsync(context, cellUsers, manager.Id, ct);
            await SeedPlanningCongesAsync(context, cellUsers, ct);

            await planningService.SaveShiftTemplateAsync(new SaveShiftConfigDto
            {
                SubServiceId = sub.Id,
                Shifts = BuildShiftConfigItems(cellUsers.Count),
            });

            var weeks = new[] { -3, -2, -1, 0 };
            foreach (var offset in weeks)
            {
                var (weekCode, weekStart) = GetIsoWeek(offset);
                if (await context.WeeklyPlannings.AnyAsync(
                        p => p.SubServiceId == sub.Id && p.WeekCode == weekCode, ct))
                    continue;

                var created = await planningService.CreatePlanningAsync(new CreateWeeklyPlanningDto
                {
                    SubServiceId = sub.Id,
                    WeekCode = weekCode,
                    WeekStartDate = weekStart,
                    TotalEffectif = cellUsers.Count,
                });

                await planningService.GeneratePlanningFromConfigAsync(new GeneratePlanningFromConfigDto
                {
                    SubServiceId = sub.Id,
                    WeekCode = weekCode,
                    WeeklyPlanningId = created.Id,
                });

                await planningService.RecordConsultationAsync(created.Id, manager.Id);
                await planningService.PublishPlanningAsync(created.Id, manager.Id);
            }
        }
        else
        {
            logger.LogInformation(
                "Planning enrichment : {Count} semaines déjà présentes — pas de re-création.",
                existingWeeks);
            await SeedChangeRequestsOnExistingWeeksAsync(context, cellUsers, sub.Id, ct);
        }

        await SeedReclamationsAsync(context, cellUsers, ct);
        await SeedReclamationFollowUpAsync(context, cellUsers, ct);
        await SeedChangeRequestsAsync(context, cellUsers, sub.Id, manager.Id, ct);
        await SeedExceptionalRequestsAsync(context, cellUsers, sub.Id, manager.Id, ct);
        await SeedReinforcementRequestsAsync(context, cellUsers, sub.Id, manager.Id, ct);
        await SeedPropositionsAsync(context, cellUsers, ct);
        await SeedNewslettersAsync(context, cellUsers, manager, ct);
        await MarkAppliedAsync(context, ct);

        logger.LogInformation("Planning enrichment v{Version} terminé (event-only).", EnrichmentVersion);
    }

    private static async Task<List<User>> ResolveAnyActiveUsersAsync(AppDbContext context, CancellationToken ct) =>
        await context.Users.Where(u => u.IsActive).OrderBy(u => u.Id).Take(20).ToListAsync(ct);

    private static bool IsEnabled(IConfiguration configuration) =>
        string.Equals(configuration["KYNTUS_PLANNING_DEMO_SEED"], "true", StringComparison.OrdinalIgnoreCase)
        && string.Equals(configuration["KYNTUS_DEMO_ENRICHMENT"] ?? "false", "true", StringComparison.OrdinalIgnoreCase);

    private static async Task<bool> IsAlreadyAppliedAsync(AppDbContext context, CancellationToken ct) =>
        await context.PlanningComments.AnyAsync(c => c.Comment == MarkerAction, ct)
        || await context.Reclamations.AnyAsync(r => r.Titre == ReclamationMarkerTitle, ct);

    private static async Task SeedChangeRequestsOnExistingWeeksAsync(
        AppDbContext context,
        List<User> cellUsers,
        int subServiceId,
        CancellationToken ct)
        => await SeedChangeRequestsAsync(context, cellUsers, subServiceId, managerId: 0, ct);

    private static async Task SeedChangeRequestsAsync(
        AppDbContext context,
        List<User> cellUsers,
        int subServiceId,
        int managerId,
        CancellationToken ct)
    {
        if (cellUsers.Count == 0)
            return;
        if (await context.PlanningChangeRequests.AnyAsync(c => c.Reason.Contains(ChangeMarker), ct))
            return;

        var week = await context.WeeklyPlannings
            .Where(p => p.SubServiceId == subServiceId)
            .OrderByDescending(p => p.WeekStartDate)
            .FirstOrDefaultAsync(ct);
        if (week is null)
            return;

        var assignments = await context.ShiftAssignments
            .Where(a => a.WeeklyPlanningId == week.Id)
            .OrderBy(a => a.Id)
            .Take(6)
            .ToListAsync(ct);
        if (assignments.Count == 0)
            return;

        var supervisorId = managerId > 0
            ? managerId
            : cellUsers.FirstOrDefault()?.Id ?? assignments[0].UserId;
        var partner = cellUsers.Count > 1 ? cellUsers[1].Id : cellUsers[0].Id;
        var now = DateTime.UtcNow;

        var specs = new (PlanningChangeRequestStatus Status, int DaysAgo, string Reason, bool Partner)[]
        {
            (PlanningChangeRequestStatus.PendingPartner, 1, "Échange créneau matin — " + ChangeMarker, true),
            (PlanningChangeRequestStatus.PendingSupervisor, 2, "Switch après accord collègue — " + ChangeMarker, true),
            (PlanningChangeRequestStatus.Approved, 5, "Changement validé superviseur — " + ChangeMarker, true),
            (PlanningChangeRequestStatus.Rejected, 6, "Changement refusé charge inbound — " + ChangeMarker, true),
        };

        for (var i = 0; i < specs.Length; i++)
        {
            var assignment = assignments[i % assignments.Count];
            var requester = cellUsers.FirstOrDefault(u => u.Id == assignment.UserId) ?? cellUsers[i % cellUsers.Count];
            var spec = specs[i];
            context.PlanningChangeRequests.Add(new PlanningChangeRequest
            {
                WeekCode = week.WeekCode,
                RequesterUserId = requester.Id,
                CurrentAssignmentId = assignment.Id,
                ProposedSwapUserId = spec.Partner ? partner : null,
                Status = spec.Status,
                Reason = spec.Reason,
                CreatedAt = now.AddDays(-spec.DaysAgo),
                PartnerRespondedAt = spec.Status != PlanningChangeRequestStatus.PendingPartner
                    ? now.AddDays(-spec.DaysAgo + 1)
                    : null,
                SupervisorProcessedByUserId = spec.Status is PlanningChangeRequestStatus.Approved
                    or PlanningChangeRequestStatus.Rejected
                    ? supervisorId
                    : null,
                ProcessedByUserId = spec.Status is PlanningChangeRequestStatus.Approved
                    or PlanningChangeRequestStatus.Rejected
                    ? supervisorId
                    : null,
                ProcessedAt = spec.Status is PlanningChangeRequestStatus.Approved
                    or PlanningChangeRequestStatus.Rejected
                    ? now.AddDays(-spec.DaysAgo + 2)
                    : null,
                RejectionReason = spec.Status == PlanningChangeRequestStatus.Rejected
                    ? "Effectif matin insuffisant (démo)."
                    : null,
            });
        }

        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedExceptionalRequestsAsync(
        AppDbContext context,
        List<User> cellUsers,
        int subServiceId,
        int managerId,
        CancellationToken ct)
    {
        if (cellUsers.Count == 0)
            return;
        if (await context.PlanningExceptionalRequests.AnyAsync(r => r.Reason.Contains(ExceptionalMarker), ct))
            return;

        var week = await context.WeeklyPlannings
            .Where(p => p.SubServiceId == subServiceId)
            .OrderByDescending(p => p.WeekStartDate)
            .FirstOrDefaultAsync(ct);
        var weekCode = week?.WeekCode ?? GetIsoWeek(1).WeekCode;
        var weekStart = week?.WeekStartDate ?? GetIsoWeek(1).WeekStart;
        var templateId = await context.SubServiceShiftConfigs
            .Where(c => c.SubServiceId == subServiceId && c.IsTemplate)
            .OrderBy(c => c.DisplayOrder)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);

        var rh = await context.Users
            .Include(u => u.Role)
            .Where(u => u.IsActive && (u.Role.Name == "RH" || u.Email.ToLower() == "rh@kyntus.ma"))
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync(ct);
        var now = DateTime.UtcNow;
        var specs = new (PlanningExceptionalRequestStatus Status, int DayOffset, int DaysAgo, string Reason)[]
        {
            (PlanningExceptionalRequestStatus.PendingSupervisor, 2, 1, "RDV médical matin — " + ExceptionalMarker),
            (PlanningExceptionalRequestStatus.PendingRh, 3, 3, "Contrainte transport — " + ExceptionalMarker),
            (PlanningExceptionalRequestStatus.Approved, 1, 8, "Créneau exceptionnel validé — " + ExceptionalMarker),
            (PlanningExceptionalRequestStatus.Rejected, 4, 10, "Créneau exceptionnel refusé — " + ExceptionalMarker),
        };

        for (var i = 0; i < specs.Length; i++)
        {
            var user = cellUsers[i % cellUsers.Count];
            var spec = specs[i];
            context.PlanningExceptionalRequests.Add(new PlanningExceptionalRequest
            {
                WeekCode = weekCode,
                RequestedDate = weekStart.AddDays(Math.Clamp(spec.DayOffset, 0, 5)),
                RequesterUserId = user.Id,
                SubServiceId = subServiceId,
                RequestedShiftTemplateId = templateId,
                Reason = spec.Reason,
                Status = spec.Status,
                CreatedAt = now.AddDays(-spec.DaysAgo),
                SupervisorProcessedByUserId = spec.Status != PlanningExceptionalRequestStatus.PendingSupervisor
                    ? managerId
                    : null,
                SupervisorProcessedAt = spec.Status != PlanningExceptionalRequestStatus.PendingSupervisor
                    ? now.AddDays(-spec.DaysAgo + 1)
                    : null,
                RhProcessedByUserId = spec.Status is PlanningExceptionalRequestStatus.Approved
                    or PlanningExceptionalRequestStatus.Rejected
                    ? rh?.Id
                    : null,
                RhProcessedAt = spec.Status is PlanningExceptionalRequestStatus.Approved
                    or PlanningExceptionalRequestStatus.Rejected
                    ? now.AddDays(-spec.DaysAgo + 2)
                    : null,
                ProcessedByUserId = spec.Status is PlanningExceptionalRequestStatus.Approved
                    or PlanningExceptionalRequestStatus.Rejected
                    ? rh?.Id ?? managerId
                    : null,
                ProcessedAt = spec.Status is PlanningExceptionalRequestStatus.Approved
                    or PlanningExceptionalRequestStatus.Rejected
                    ? now.AddDays(-spec.DaysAgo + 2)
                    : null,
                RejectionReason = spec.Status == PlanningExceptionalRequestStatus.Rejected
                    ? "Couverture plateau insuffisante (démo)."
                    : null,
            });
        }

        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedReinforcementRequestsAsync(
        AppDbContext context,
        List<User> cellUsers,
        int subServiceId,
        int managerId,
        CancellationToken ct)
    {
        if (cellUsers.Count == 0)
            return;
        if (await context.PlanningReinforcementRequests.AnyAsync(r => r.Reason.Contains(ReinforcementMarker), ct))
            return;

        var week = await context.WeeklyPlannings
            .Where(p => p.SubServiceId == subServiceId)
            .OrderByDescending(p => p.WeekStartDate)
            .FirstOrDefaultAsync(ct);
        var weekCode = week?.WeekCode ?? GetIsoWeek(0).WeekCode;
        var saturday = (week?.WeekStartDate ?? GetIsoWeek(0).WeekStart).AddDays(5);
        var now = DateTime.UtcNow;
        var volunteerA = cellUsers[0];
        var volunteerB = cellUsers.Count > 1 ? cellUsers[1] : cellUsers[0];

        var open = new PlanningReinforcementRequest
        {
            WeekCode = weekCode,
            SaturdayDate = saturday.AddDays(7),
            SubServiceId = subServiceId,
            SlotsNeeded = 2,
            Reason = "Pic inbound samedi — appel volontaires — " + ReinforcementMarker,
            Status = PlanningReinforcementRequestStatus.Open,
            CreatedByUserId = managerId > 0 ? managerId : volunteerA.Id,
            CreatedAt = now.AddDays(-1),
            WeeklyPlanningId = week?.Id,
        };
        context.PlanningReinforcementRequests.Add(open);
        await context.SaveChangesAsync(ct);
        context.PlanningReinforcementVolunteers.AddRange(
            new PlanningReinforcementVolunteer
            {
                RequestId = open.Id,
                UserId = volunteerA.Id,
                Status = PlanningReinforcementVolunteerStatus.Pending,
            },
            new PlanningReinforcementVolunteer
            {
                RequestId = open.Id,
                UserId = volunteerB.Id,
                Status = PlanningReinforcementVolunteerStatus.Accepted,
                RespondedAt = now.AddHours(-6),
            });

        var filled = new PlanningReinforcementRequest
        {
            WeekCode = weekCode,
            SaturdayDate = saturday,
            SubServiceId = subServiceId,
            SlotsNeeded = 1,
            Reason = "Renfort samedi pourvu — " + ReinforcementMarker,
            Status = PlanningReinforcementRequestStatus.Filled,
            CreatedByUserId = managerId > 0 ? managerId : volunteerA.Id,
            SelectedByUserId = managerId > 0 ? managerId : volunteerA.Id,
            ClosedByUserId = managerId > 0 ? managerId : volunteerA.Id,
            CreatedAt = now.AddDays(-8),
            ClosedAt = now.AddDays(-6),
            WeeklyPlanningId = week?.Id,
        };
        context.PlanningReinforcementRequests.Add(filled);
        await context.SaveChangesAsync(ct);
        context.PlanningReinforcementVolunteers.Add(new PlanningReinforcementVolunteer
        {
            RequestId = filled.Id,
            UserId = volunteerB.Id,
            Status = PlanningReinforcementVolunteerStatus.Selected,
            RespondedAt = now.AddDays(-7),
            SelectedAt = now.AddDays(-6),
        });

        context.PlanningReinforcementRequests.Add(new PlanningReinforcementRequest
        {
            WeekCode = weekCode,
            SaturdayDate = saturday.AddDays(-7),
            SubServiceId = subServiceId,
            SlotsNeeded = 1,
            Reason = "Appel renfort annulé — " + ReinforcementMarker,
            Status = PlanningReinforcementRequestStatus.Cancelled,
            CreatedByUserId = managerId > 0 ? managerId : volunteerA.Id,
            CancelledByUserId = managerId > 0 ? managerId : volunteerA.Id,
            ClosedByUserId = managerId > 0 ? managerId : volunteerA.Id,
            CreatedAt = now.AddDays(-14),
            ClosedAt = now.AddDays(-13),
            WeeklyPlanningId = week?.Id,
        });

        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedPropositionsAsync(
        AppDbContext context,
        List<User> users,
        CancellationToken ct)
    {
        if (users.Count == 0)
            return;
        if (await context.Propositions.AnyAsync(p => p.Titre == PropositionMarkerTitle, ct))
            return;

        var author = users[0];
        var assignee = users.Count > 1 ? users[1] : author;
        var now = DateTime.UtcNow;
        var specs = new (string Titre, string Desc, PropositionStatus Status, Priority Prio, int DaysAgo)[]
        {
            (PropositionMarkerTitle, "Script d'accueil bilingue FR/Darija pour la file inbound.", PropositionStatus.Soumise, Priority.Normale, 2),
            ("Pause déjeuner décalée — open space", "Proposer 3 créneaux pause pour lisser le plateau.", PropositionStatus.EnEvaluation, Priority.Normale, 5),
            ("Casques antibruit postes 12–18", "Réduire le bruit de fond signalé en qualité.", PropositionStatus.Approuvee, Priority.Haute, 8),
            ("Atelier NPS mensuel cellule", "Rituel 45 min pour revisiter les motifs de contact.", PropositionStatus.Implementee, Priority.Normale, 20),
            ("Badge parking partagé soir", "Mutualiser les badges équipe 11h (démo).", PropositionStatus.Rejetee, Priority.Basse, 12),
        };

        foreach (var s in specs)
        {
            context.Propositions.Add(new Proposition
            {
                Titre = s.Titre,
                Description = s.Desc,
                BeneficeAttendu = "Meilleure qualité perçue inbound (démo).",
                Status = s.Status,
                Priorite = s.Prio,
                AuteurId = author.Guid.ToString(),
                AuteurNom = $"{author.FirstName} {author.LastName}",
                AuteurRole = "Pilote",
                AssigneeId = s.Status == PropositionStatus.Soumise ? null : assignee.Guid.ToString(),
                AssigneeNom = s.Status == PropositionStatus.Soumise ? null : $"{assignee.FirstName} {assignee.LastName}",
                CreatedAt = now.AddDays(-s.DaysAgo),
                UpdatedAt = now.AddDays(-Math.Max(0, s.DaysAgo - 1)),
                EvalueeAt = s.Status is PropositionStatus.Approuvee or PropositionStatus.Rejetee or PropositionStatus.Implementee
                    ? now.AddDays(-s.DaysAgo + 2)
                    : null,
                ImplementeeAt = s.Status == PropositionStatus.Implementee ? now.AddDays(-4) : null,
                CommentaireEvaluation = s.Status == PropositionStatus.Rejetee ? "Hors budget trimestre (démo)." : null,
            });
        }

        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedNewslettersAsync(
        AppDbContext context,
        List<User> users,
        User? author,
        CancellationToken ct)
    {
        if (await context.Newsletters.AnyAsync(n => n.Title == NewsletterMarkerTitle, ct))
            return;

        var createdBy = author?.Guid.ToString()
            ?? users.FirstOrDefault()?.Guid.ToString()
            ?? "seed";
        var now = DateTime.UtcNow;

        var published = new Newsletter
        {
            Title = NewsletterMarkerTitle,
            Subject = "Consigne qualité — file inbound grands comptes",
            HtmlContent = "<p>Rappel scripts NPS et escalade réclamations (démo).</p>",
            TextContent = "Rappel scripts NPS et escalade réclamations (démo).",
            CreatedAt = now.AddDays(-4),
            CreatedByUserId = createdBy,
        };
        var draft = new Newsletter
        {
            Title = "Brief samedi — rotation groupes (démo)",
            Subject = "Organisation samedi prochain",
            HtmlContent = "<p>Groupes 1 et 2 — horaires à confirmer (démo).</p>",
            TextContent = "Groupes 1 et 2 — horaires à confirmer (démo).",
            CreatedAt = now.AddDays(-1),
            CreatedByUserId = createdBy,
        };
        context.Newsletters.AddRange(published, draft);
        await context.SaveChangesAsync(ct);

        var campaign = new NewsletterCampaign
        {
            Name = "Diffusion inbound — démo",
            NewsletterId = published.Id,
            AudienceTarget = AudienceTarget.Employees,
            Status = CampaignStatus.Sent,
            PublishedAt = now.AddDays(-3),
            TotalRecipients = Math.Min(users.Count, 8),
            CreatedAt = now.AddDays(-3),
            CreatedByUserId = createdBy,
        };
        context.NewsletterCampaigns.Add(campaign);
        await context.SaveChangesAsync(ct);

        foreach (var user in users.Take(8))
        {
            context.CampaignAnalytics.Add(new CampaignAnalytics
            {
                CampaignId = campaign.Id,
                UserId = user.Guid.ToString(),
                IsRead = user.Id % 2 == 0,
                ReadAt = user.Id % 2 == 0 ? now.AddDays(-2) : null,
                ReceivedAt = now.AddDays(-3),
            });
        }

        context.NewsletterCampaigns.Add(new NewsletterCampaign
        {
            Name = "Brouillon RH — démo",
            NewsletterId = draft.Id,
            AudienceTarget = AudienceTarget.All,
            Status = CampaignStatus.Draft,
            TotalRecipients = 0,
            CreatedAt = now.AddDays(-1),
            CreatedByUserId = createdBy,
        });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedReclamationFollowUpAsync(
        AppDbContext context,
        List<User> users,
        CancellationToken ct)
    {
        if (users.Count == 0)
            return;

        var marker = await context.Reclamations
            .FirstOrDefaultAsync(r => r.Titre == ReclamationMarkerTitle, ct);
        if (marker is null)
            return;
        if (await context.ReclamationHistoriques.AnyAsync(h => h.ReclamationId == marker.Id, ct))
            return;

        var actor = users[0];
        var now = DateTime.UtcNow;
        context.ReclamationHistoriques.AddRange(
            new ReclamationHistorique
            {
                ReclamationId = marker.Id,
                Action = "Créée",
                Valeur = "Soumise",
                EffectueParId = actor.Guid.ToString(),
                EffectueParNom = $"{actor.FirstName} {actor.LastName}",
                CreatedAt = marker.CreatedAt,
            },
            new ReclamationHistorique
            {
                ReclamationId = marker.Id,
                Action = "Priorité",
                Valeur = "Haute",
                EffectueParId = actor.Guid.ToString(),
                EffectueParNom = $"{actor.FirstName} {actor.LastName}",
                CreatedAt = now.AddDays(-1),
            });

        context.TicketComments.Add(new TicketComment
        {
            OwnerType = MediaOwnerType.Reclamation,
            OwnerId = marker.Id,
            AuthorId = actor.Guid.ToString(),
            AuthorNom = $"{actor.FirstName} {actor.LastName}",
            Text = "Coupures reproduites en pic 10h–12h — à relayer SI (démo).",
            CreatedAt = now.AddHours(-6),
        });
        await context.SaveChangesAsync(ct);
    }

    private static async Task EnsureSaturdayGroupsAsync(
        AppDbContext context,
        List<User> users,
        int managerId,
        CancellationToken ct)
    {
        for (var i = 0; i < users.Count; i++)
        {
            var user = users[i];
            if (await context.SaturdayGroups.AnyAsync(sg => sg.UserId == user.Id, ct))
                continue;

            context.SaturdayGroups.Add(new SaturdayGroup
            {
                UserId = user.Id,
                GroupNumber = i % 2 == 0 ? 1 : 2,
                IsNewEmployee = false,
                AssignedBy = managerId,
                AssignedAt = DateTime.UtcNow,
            });
        }

        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedPlanningCongesAsync(
        AppDbContext context,
        List<User> users,
        CancellationToken ct)
    {
        var employee = users.FirstOrDefault(u =>
            u.Email.Equals("employee@kyntus.ma", StringComparison.OrdinalIgnoreCase)
            || u.Email.Equals("yasmine.elidrissi@contactcentre.ma", StringComparison.OrdinalIgnoreCase))
            ?? users.FirstOrDefault();
        if (employee is null)
            return;

        var (_, weekStart) = GetIsoWeek(2);
        var start = weekStart.AddDays(1);
        var end = start.AddDays(2);

        if (await context.Conges.AnyAsync(
                c => c.UserId == employee.Id && c.StartDate == start, ct))
            return;

        context.Conges.Add(new Conge
        {
            UserId = employee.Id,
            StartDate = start,
            EndDate = end,
            Reason = "Congé annuel — plateforme inbound",
            Status = CongeStatus.Approved,
            AbsenceType = AbsenceType.CongesPayes,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync(ct);
    }

    private static async Task SeedReclamationsAsync(
        AppDbContext context,
        List<User> users,
        CancellationToken ct)
    {
        if (users.Count == 0)
            return;

        if (await context.Reclamations.AnyAsync(r => r.Titre == ReclamationMarkerTitle, ct))
            return;

        var author = users.FirstOrDefault(u =>
            u.Email.Equals("employee@kyntus.ma", StringComparison.OrdinalIgnoreCase)
            || u.Email.Equals("yasmine.elidrissi@contactcentre.ma", StringComparison.OrdinalIgnoreCase))
            ?? users[0];

        var second = users.Count > 1 ? users[1] : author;
        var assigneeGuid = ContactCentreRoster.Employees.FirstOrDefault(e => e.PrimeId == "e10")?.Guid.ToString()
            ?? second.Guid.ToString();
        var assigneeNom = ContactCentreRoster.Employees.FirstOrDefault(e => e.PrimeId == "e10") is { } a
            ? ContactCentreRoster.DisplayName(a)
            : $"{second.FirstName} {second.LastName}";

        var now = DateTime.UtcNow;
        var specs = new (string Titre, string Desc, ReclamationType Type, ReclamationStatus Status, Priority Prio, int DaysAgo, bool Assign)[]
        {
            (ReclamationMarkerTitle, "Coupures intermittentes sur le softphone pendant les pics d'appels.", ReclamationType.Technique, ReclamationStatus.Soumise, Priority.Haute, 2, false),
            ("Délai traitement réclamations — cellule rétention", "Demande d'accélération du circuit validation pour offres de rétention.", ReclamationType.ServiceQualite, ReclamationStatus.EnCours, Priority.Normale, 5, true),
            ("Planning samedi — supervision connectivité ACD", "Rotation groupe samedi à rééquilibrer pour la cellule Agents 1er niveau.", ReclamationType.Administrative, ReclamationStatus.Soumise, Priority.Normale, 1, false),
            ("Casque micro défectueux — poste open space", "Remplacement matériel demandé avant le shift du soir.", ReclamationType.Technique, ReclamationStatus.EnCours, Priority.Haute, 3, true),
            ("Script NPS obsolète — campagne satisfaction", "Mettre à jour le script d'enquête NPS pour la file inbound.", ReclamationType.ServiceQualite, ReclamationStatus.Soumise, Priority.Normale, 4, false),
            ("Accès CRM temporairement bloqué", "Compte verrouillé après 3 tentatives — déblocage urgent.", ReclamationType.Technique, ReclamationStatus.Cloturee, Priority.Haute, 10, true),
            ("Salle formation — disponibilité créneau", "Réservation salle pour session softphone (démo).", ReclamationType.Administrative, ReclamationStatus.Cloturee, Priority.Basse, 12, false),
            ("Écart pause déjeuner — planning publié", "Pause non alignée avec le template de la cellule.", ReclamationType.Administrative, ReclamationStatus.Soumise, Priority.Normale, 0, false),
            ("Qualité appels — bruit de fond open space", "Demande isolation acoustique postes 12–18.", ReclamationType.ServiceQualite, ReclamationStatus.EnCours, Priority.Normale, 6, true),
            ("Badge accès parking — renouvellement", "Badge expiré pour agents équipe matin.", ReclamationType.Administrative, ReclamationStatus.Cloturee, Priority.Basse, 14, false),
        };

        foreach (var s in specs)
        {
            context.Reclamations.Add(new Reclamation
            {
                Titre = s.Titre,
                Description = s.Desc,
                Type = s.Type,
                Status = s.Status,
                Priorite = s.Prio,
                AuteurId = author.Guid.ToString(),
                AuteurNom = $"{author.FirstName} {author.LastName}",
                AuteurRole = "Pilote",
                AssigneeId = s.Assign ? assigneeGuid : null,
                AssigneeNom = s.Assign ? assigneeNom : null,
                CreatedAt = now.AddDays(-s.DaysAgo),
                UpdatedAt = now.AddDays(-Math.Max(0, s.DaysAgo - 1)),
            });
        }

        await context.SaveChangesAsync(ct);
    }

    private static async Task MarkAppliedAsync(AppDbContext context, CancellationToken ct)
    {
        if (await context.PlanningComments.AnyAsync(c => c.Comment == MarkerAction, ct))
            return;

        var planning = await context.WeeklyPlannings
            .OrderByDescending(p => p.WeekStartDate)
            .FirstOrDefaultAsync(ct);
        if (planning is null)
            return;

        context.PlanningComments.Add(new PlanningComment
        {
            WeeklyPlanningId = planning.Id,
            UserId = planning.ValidatedBy ?? 1,
            CreatedBy = planning.ValidatedBy ?? 1,
            Comment = MarkerAction,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync(ct);
    }

    private static async Task MarkAppliedWithoutWeekAsync(AppDbContext context, CancellationToken ct)
    {
        // Idempotence via titre réclamation marqueur (déjà seedé).
        await Task.CompletedTask;
    }

    private static List<ShiftConfigItemDto> BuildShiftConfigItems(int totalEffectif)
    {
        var perShift = Math.Max(1, totalEffectif / 4);
        var remainder = totalEffectif - perShift * 4;
        var counts = new[] { perShift, perShift, perShift, perShift + remainder };

        return new List<ShiftConfigItemDto>
        {
            new()
            {
                Label = "8h",
                StartTime = "08:00",
                WorkHours = 8,
                BreakDurationMinutes = 60,
                BreakRangeStart = "12:00",
                BreakRangeEnd = "13:00",
                RequiredCount = counts[0],
                DisplayOrder = 1,
            },
            new()
            {
                Label = "9h",
                StartTime = "09:00",
                WorkHours = 8,
                BreakDurationMinutes = 60,
                BreakRangeStart = "13:00",
                BreakRangeEnd = "14:00",
                RequiredCount = counts[1],
                DisplayOrder = 2,
            },
            new()
            {
                Label = "10h",
                StartTime = "10:00",
                WorkHours = 8,
                BreakDurationMinutes = 60,
                BreakRangeStart = "14:00",
                BreakRangeEnd = "15:00",
                RequiredCount = counts[2],
                DisplayOrder = 3,
            },
            new()
            {
                Label = "11h",
                StartTime = "11:00",
                WorkHours = 8,
                BreakDurationMinutes = 60,
                BreakRangeStart = "15:00",
                BreakRangeEnd = "16:00",
                RequiredCount = counts[3],
                DisplayOrder = 4,
            },
        };
    }

    private static (string WeekCode, DateOnly WeekStart) GetIsoWeek(int offsetFromCurrent)
    {
        var dt = DateTime.UtcNow.AddDays(offsetFromCurrent * 7);
        var year = ISOWeek.GetYear(dt);
        var week = ISOWeek.GetWeekOfYear(dt);
        var monday = DateOnly.FromDateTime(ISOWeek.ToDateTime(year, week, DayOfWeek.Monday));
        return ($"{year}-W{week:D2}", monday);
    }
}
