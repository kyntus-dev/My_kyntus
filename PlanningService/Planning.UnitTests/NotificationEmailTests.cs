using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;
using Planning.Domain.Entities;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services.NotificationEmail;

namespace Planning.UnitTests;

public class NotificationEmailTests
{
    // ── Gabarit ───────────────────────────────────────────────────────────

    [Fact]
    public void Composer_encodes_content_and_adds_link_only_when_configured()
    {
        var html = NotificationEmailComposer.BuildHtml("Yasmine", "conge", "Congé validé", "Du 3 <b>au</b> 5", link: null);
        Assert.Contains("Bonjour Yasmine,", html);
        Assert.Contains("CONGÉS", html);
        Assert.Contains("Du 3 &lt;b&gt;au&lt;/b&gt; 5", html);
        Assert.DoesNotContain("href", html);

        Assert.Null(NotificationEmailComposer.BuildLink(null, "/mes-conges"));
        Assert.Null(NotificationEmailComposer.BuildLink("javascript:alert(1)", "/x"));
        Assert.Equal("http://10.10.10.25:8200/mes-conges", NotificationEmailComposer.BuildLink("http://10.10.10.25:8200/", "/mes-conges"));
        Assert.Equal("http://10.10.10.25:8200/", NotificationEmailComposer.BuildLink("http://10.10.10.25:8200", "//evil.example"));
        Assert.Equal("My Kyntus — Congé validé", NotificationEmailComposer.Subject("Congé validé"));
    }

    [Theory]
    [InlineData(5, "Votre planning 2026-W44 est disponible !", true)]
    [InlineData(5, "Votre planning 2026-W44 a été mis à jour — veuillez le consulter.", true)]
    [InlineData(null, "Votre planning 2026-W44 est disponible !", false)]
    [InlineData(5, "Le planning de votre équipe a été publié.", false)]
    public void Planning_publication_notification_is_not_mailed_twice(int? planningId, string message, bool covered) =>
        Assert.Equal(covered, PlanningNotificationEmailRules.IsCoveredByPlanningEmail(planningId, message));

    [Fact]
    public void Planning_notifications_are_classified_by_source()
    {
        Assert.Equal(("formation", "Formation"), PlanningNotificationEmailRules.Classify("TRAIN-QUIZ-OK-1", ""));
        Assert.Equal(("formation", "Formation"), PlanningNotificationEmailRules.Classify("INIT-DOCS-1", ""));
        Assert.Equal(("planning", "Déséquilibre samedi"), PlanningNotificationEmailRules.Classify("SAT-IMBALANCE-3", ""));
        Assert.Equal(("planning", "Demande de renfort"), PlanningNotificationEmailRules.Classify("2026-W44", "Demande de renfort"));
    }

    // ── File d'envoi ──────────────────────────────────────────────────────

    [Fact]
    public async Task Queue_uses_work_email_skips_inactive_opted_out_and_duplicates()
    {
        await using var db = await SeedAsync();
        db.NotificationEmailPreferences.Add(new NotificationEmailPreference { UserId = 3, EmailEnabled = false });
        await db.SaveChangesAsync();
        var queue = CreateQueue(db);

        var content = new NotificationEmailContent("conge", "Congé validé", "Votre congé est validé.", "/mes-conges", "evt-1");
        var recipients = new NotificationEmailRecipients(UserIds: [1, 2, 3]);

        Assert.Equal(1, await queue.EnqueueNotificationAsync(recipients, content));
        Assert.Equal(0, await queue.EnqueueNotificationAsync(recipients, content)); // événement rejoué

        var row = await db.EmailOutboxMessages.SingleAsync();
        Assert.Equal("yasmine@kyntus.com", row.ToEmail);
        Assert.Equal("conge", row.Category);
        Assert.Equal("evt-1:u1", row.DedupKey);
        Assert.Contains("http://app.local/mes-conges", row.HtmlBody);
    }

    [Fact]
    public async Task Queue_resolves_recipients_by_role_guid_and_auth_id()
    {
        await using var db = await SeedAsync();
        var queue = CreateQueue(db);

        Assert.Equal(1, await queue.EnqueueNotificationAsync(
            new NotificationEmailRecipients(Roles: ["rh"]),
            new NotificationEmailContent("contract", "Contrat", "Fin de CDD proche.")));
        Assert.Equal(1, await queue.EnqueueNotificationAsync(
            new NotificationEmailRecipients(EmployeeIds: [YasmineGuid]),
            new NotificationEmailContent("prime", "Prime approuvée", "Votre prime a été approuvée.")));
        Assert.Equal(1, await queue.EnqueueNotificationAsync(
            new NotificationEmailRecipients(AuthUserIds: [101]),
            new NotificationEmailContent("reclamation", "Réclamation", "Mise à jour.")));

        var to = await db.EmailOutboxMessages.OrderBy(m => m.Id).Select(m => m.ToEmail).ToListAsync();
        Assert.Equal(new[] { "sara@kyntus.com", "yasmine@kyntus.com", "yasmine@kyntus.com" }, to);
    }

    [Fact]
    public async Task Queue_does_nothing_when_mail_is_not_configured()
    {
        await using var db = await SeedAsync();
        var queue = CreateQueue(db, ready: false);

        Assert.Equal(0, await queue.EnqueueNotificationAsync(
            new NotificationEmailRecipients(UserIds: [1]),
            new NotificationEmailContent("planning", "Planning", "Message")));
        Assert.False(await queue.EnqueueDirectAsync(1, "yasmine@kyntus.com", "planning", "Planning", "<p/>"));
        Assert.Empty(db.EmailOutboxMessages);
    }

    // ── Collecte des notifications ────────────────────────────────────────

    [Fact]
    public async Task Harvest_starts_after_existing_history_then_mails_new_notifications()
    {
        await using var db = await SeedAsync();
        db.PlanningNotifications.Add(Notification(10, 1, null, "Ancienne notification"));
        await db.SaveChangesAsync();
        var processor = CreateProcessor(db, new RecordingSender());

        Assert.Equal(0, await processor.HarvestAsync()); // premier passage : rien de l'historique
        Assert.Empty(db.EmailOutboxMessages);

        db.PlanningNotifications.AddRange(
            Notification(11, 1, null, "Votre demande de changement a été acceptée.", "Demande de changement"),
            Notification(12, 1, 7, "Votre planning 2026-W44 est disponible !"));
        db.ContractNotifications.Add(new ContractNotification
        {
            Id = 1, ContractId = 1, Type = NotificationType.AvantFinCDD, Message = "Le CDD de Mehdi se termine dans 15 jours.",
        });
        await db.SaveChangesAsync();

        Assert.Equal(2, await processor.HarvestAsync());
        var rows = await db.EmailOutboxMessages.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal("My Kyntus — Demande de changement", rows[0].Subject);
        Assert.Equal("yasmine@kyntus.com", rows[0].ToEmail);
        Assert.Equal("My Kyntus — Contrat — fin de CDD proche", rows[1].Subject);
        Assert.Equal("sara@kyntus.com", rows[1].ToEmail);

        Assert.Equal(0, await processor.HarvestAsync()); // rien de nouveau
        var cursor = await db.NotificationEmailCursors.SingleAsync();
        Assert.Equal(12, cursor.LastPlanningNotificationId);
        Assert.Equal(1, cursor.LastContractNotificationId);
    }

    // ── Envoi ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_respects_rate_limit_and_marks_sent()
    {
        await using var db = await SeedAsync();
        for (var i = 0; i < 5; i++)
            db.EmailOutboxMessages.Add(Pending($"a{i}@kyntus.com"));
        await db.SaveChangesAsync();
        var sender = new RecordingSender();
        var processor = CreateProcessor(db, sender, maxPerMinute: 3);

        Assert.Equal(3, await processor.SendDueAsync());
        Assert.Equal(0, await processor.SendDueAsync()); // budget de la minute épuisé
        Assert.Equal(3, sender.Sent.Count);
        Assert.Equal(3, await db.EmailOutboxMessages.CountAsync(m => m.Status == EmailOutboxStatus.Sent && m.SentAt != null));
    }

    [Fact]
    public async Task Send_failure_is_retried_later_then_abandoned()
    {
        await using var db = await SeedAsync();
        db.EmailOutboxMessages.Add(Pending("x@kyntus.com"));
        await db.SaveChangesAsync();
        var processor = CreateProcessor(db, new RecordingSender { Error = new InvalidOperationException("5.7.60 SendAsDenied") });

        Assert.Equal(0, await processor.SendDueAsync());
        var row = await db.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxStatus.Pending, row.Status);
        Assert.Equal(1, row.Attempts);
        Assert.True(row.NextAttemptAt > DateTime.UtcNow.AddSeconds(30));
        Assert.Contains("SendAsDenied", row.LastError);

        for (var i = 1; i < EmailOutboxProcessor.MaxAttempts; i++)
            EmailOutboxProcessor.RecordFailure(row, new Exception("boom"), DateTime.UtcNow);
        Assert.Equal(EmailOutboxStatus.Failed, row.Status);
    }

    [Fact]
    public async Task Send_keeps_messages_pending_when_mail_is_not_configured()
    {
        await using var db = await SeedAsync();
        db.EmailOutboxMessages.Add(Pending("x@kyntus.com"));
        await db.SaveChangesAsync();
        var processor = CreateProcessor(db, new RecordingSender { Error = new MailNotConfiguredException("non configuré") });

        Assert.Equal(0, await processor.SendDueAsync());
        var row = await db.EmailOutboxMessages.SingleAsync();
        Assert.Equal(EmailOutboxStatus.Pending, row.Status);
        Assert.Equal(0, row.Attempts);
    }

    [Fact]
    public async Task Old_pending_messages_expire()
    {
        await using var db = await SeedAsync();
        var old = Pending("x@kyntus.com");
        old.CreatedAt = DateTime.UtcNow.AddDays(-4);
        db.EmailOutboxMessages.Add(old);
        await db.SaveChangesAsync();
        var sender = new RecordingSender();

        await CreateProcessor(db, sender).SendDueAsync();

        Assert.Empty(sender.Sent);
        Assert.Equal(EmailOutboxStatus.Skipped, (await db.EmailOutboxMessages.SingleAsync()).Status);
    }

    [Fact]
    public void Throttling_errors_are_recognised() =>
        Assert.True(EmailOutboxProcessor.IsThrottling(new Exception("4.3.2 STOREDRV.ClientSubmit; sender thread limit exceeded")));

    // ── Outils ────────────────────────────────────────────────────────────

    private static readonly Guid YasmineGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        db.Roles.AddRange(
            new Role { Id = 1, Name = "Pilote", CreatedAt = DateTime.UtcNow },
            new Role { Id = 2, Name = "RH", CreatedAt = DateTime.UtcNow });
        db.Users.AddRange(
            new User { Id = 1, Guid = YasmineGuid, AuthUserId = 101, RoleId = 1, FirstName = "Yasmine", LastName = "El Idrissi", Email = "yasmine@kyntus.com", IsActive = true },
            new User { Id = 2, RoleId = 1, FirstName = "Mehdi", LastName = "Chraibi", Email = "mehdi@kyntus.com", IsActive = false },
            new User { Id = 3, RoleId = 1, FirstName = "Nora", LastName = "Alami", Email = "nora@kyntus.com", IsActive = true },
            new User { Id = 4, RoleId = 2, FirstName = "Sara", LastName = "Rh", Email = "sara@kyntus.com", IsActive = true });
        db.Contracts.Add(new Contract { Id = 1, UserId = 2, StartDate = DateTime.UtcNow.AddMonths(-5) });
        await db.SaveChangesAsync();
        return db;
    }

    private static PlanningNotification Notification(int id, int userId, int? planningId, string message, string sub = "") => new()
    {
        Id = id, UserId = userId, AuthUserId = 100 + userId, WeeklyPlanningId = planningId,
        WeekCode = "2026-W44", SubServiceName = sub, Message = message,
    };

    private static EmailOutboxMessage Pending(string to) => new()
    {
        ToEmail = to, Category = "planning", Subject = "S", HtmlBody = "<p/>", CreatedAt = DateTime.UtcNow,
        NextAttemptAt = DateTime.UtcNow.AddSeconds(-1),
    };

    private static IOptions<NotificationEmailOptions> Options(int maxPerMinute = 25) =>
        Microsoft.Extensions.Options.Options.Create(new NotificationEmailOptions
        {
            MaxPerMinute = maxPerMinute,
            PublicAppUrl = "http://app.local",
        });

    private static NotificationEmailQueue CreateQueue(AppDbContext db, bool ready = true) =>
        new(db, new FakeReadiness(ready), Options(), NullLogger<NotificationEmailQueue>.Instance);

    private static EmailOutboxProcessor CreateProcessor(AppDbContext db, RecordingSender sender, int maxPerMinute = 25) =>
        new(db, CreateQueue(db), sender, Options(maxPerMinute), NullLogger<EmailOutboxProcessor>.Instance);

    private sealed class FakeReadiness(bool ready) : IPlanningEmailReadiness
    {
        public Task<bool> IsReadyAsync(CancellationToken ct = default) => Task.FromResult(ready);
    }

    private sealed class RecordingSender : IPlanningEmailSender
    {
        public Exception? Error { get; init; }
        public List<string> Sent { get; } = [];
        public bool IsConfigured => true;

        public Task SendHtmlAsync(string to, string subject, string htmlBody, CancellationToken ct = default) =>
            SendRequiredHtmlAsync(to, subject, htmlBody, ct);

        public Task SendRequiredHtmlAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (Error is not null)
                throw Error;
            Sent.Add(to);
            return Task.CompletedTask;
        }
    }
}
