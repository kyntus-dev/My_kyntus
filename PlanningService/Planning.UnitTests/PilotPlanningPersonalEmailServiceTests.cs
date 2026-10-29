using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;
using Planning.Domain.Entities;
using Planning.Domain.Enums;
using Planning.Infrastructure.Hubs;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

public class PilotPlanningPersonalEmailServiceTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }

    [Fact]
    public async Task Queues_planning_to_every_active_agent_work_email()
    {
        await using var db = await SeedAsync();
        var queue = new RecordingQueue();
        var svc = new PilotPlanningPersonalEmailService(db, queue, NullLogger<PilotPlanningPersonalEmailService>.Instance);

        await svc.SendToAssignedPilotsAsync(1, updated: false);

        Assert.Equal(
            new[] { "mehdi@kyntus.ma", "sara@kyntus.ma", "yasmine@kyntus.ma" },
            queue.Direct.Select(d => d.To).Order().ToArray());
        Assert.All(queue.Direct, d => Assert.Equal("planning", d.Category));
        Assert.All(queue.Direct, d => Assert.Equal("Planning 2026-W36 disponible", d.Subject));
        Assert.All(queue.Direct, d => Assert.StartsWith("planning:1:pub:", d.DedupKey));

        var yasmine = queue.Direct.Single(d => d.To == "yasmine@kyntus.ma").Html;
        Assert.DoesNotContain("href", yasmine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/mes-plannings", yasmine);
        Assert.Contains("08:00", yasmine);
        Assert.Contains("EL IDRISSI Yasmine", yasmine);
        Assert.Contains("31-Aug", yasmine);
        Assert.DoesNotContain("Chraibi", yasmine);
        Assert.DoesNotContain("17:00", yasmine);
    }

    [Fact]
    public async Task Skips_inactive_agents_and_invalid_addresses()
    {
        await using var db = await SeedAsync();
        (await db.Users.SingleAsync(u => u.Id == 20)).IsActive = false;
        (await db.Users.SingleAsync(u => u.Id == 30)).Email = "pas-une-adresse";
        await db.SaveChangesAsync();
        var queue = new RecordingQueue();
        var svc = new PilotPlanningPersonalEmailService(db, queue, NullLogger<PilotPlanningPersonalEmailService>.Instance);

        await svc.SendToAssignedPilotsAsync(1, updated: true);

        var only = Assert.Single(queue.Direct);
        Assert.Equal("yasmine@kyntus.ma", only.To);
        Assert.Equal("Planning 2026-W36 mis à jour", only.Subject);
    }

    [Fact]
    public async Task Queues_nothing_when_mail_is_not_configured()
    {
        await using var db = await SeedAsync();
        var queue = new RecordingQueue { Enabled = false };
        var svc = new PilotPlanningPersonalEmailService(db, queue, NullLogger<PilotPlanningPersonalEmailService>.Instance);

        await svc.SendToAssignedPilotsAsync(1, updated: false);

        Assert.Empty(queue.Direct);
    }

    [Fact]
    public async Task Notify_republish_sends_update_mail_even_when_draft()
    {
        await using var db = await SeedAsync();
        var planning = await db.WeeklyPlannings.SingleAsync();
        planning.Status = PlanningStatus.Draft;
        await db.SaveChangesAsync();

        var recorder = new RecordingPilotEmailService();
        var svc = new PlanningService(
            db,
            new FakePlanningHubContext(),
            new PlanningPerimeterResolver(db),
            pilotEmails: recorder);

        await svc.NotifyPlanningRepublishedAsync(1, "regen congés");

        Assert.Single(recorder.Calls);
        Assert.Equal((1, true), recorder.Calls[0]);
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = CreateDb();
        db.Roles.AddRange(
            new Role { Id = 1, Name = "Pilote", CreatedAt = DateTime.UtcNow },
            new Role { Id = 2, Name = "Superviseur", CreatedAt = DateTime.UtcNow });
        db.Services.Add(new Service { Id = 1, Name = "Support", Code = "SUP" });
        db.SubServices.Add(new SubService { Id = 1, ServiceId = 1, Name = "Satisfaction", Code = "SAT" });
        db.Users.AddRange(
            new User
            {
                Id = 10,
                RoleId = 1,
                FirstName = "Yasmine",
                LastName = "El Idrissi",
                Email = "yasmine@kyntus.ma",
                IsActive = true,
            },
            new User
            {
                Id = 20,
                RoleId = 1,
                FirstName = "Mehdi",
                LastName = "Chraibi",
                Email = "mehdi@kyntus.ma",
                IsActive = true,
            },
            new User
            {
                Id = 30,
                RoleId = 2,
                FirstName = "Sara",
                LastName = "Manager",
                Email = "sara@kyntus.ma",
                IsActive = true,
            });
        db.UserHrProfiles.AddRange(
            new UserHrProfile { UserId = 10, EmailPersonnel = "yasmine.perso@gmail.com" },
            new UserHrProfile { UserId = 30, EmailPersonnel = "sara.perso@gmail.com" });
        db.WeeklyPlannings.Add(new WeeklyPlanning
        {
            Id = 1,
            SubServiceId = 1,
            WeekCode = "2026-W36",
            WeekStartDate = new DateOnly(2026, 8, 31),
            Status = PlanningStatus.Published,
            ShiftAssignments = [],
        });
        db.SubServiceShiftConfigs.Add(new SubServiceShiftConfig
        {
            Id = 1,
            SubServiceId = 1,
            Label = "8h",
            StartTime = new TimeOnly(8, 0),
            WorkHours = 8,
            BreakDurationMinutes = 60,
        });
        db.ShiftAssignments.AddRange(
            new ShiftAssignment
            {
                Id = 1,
                WeeklyPlanningId = 1,
                UserId = 10,
                SubServiceShiftConfigId = 1,
                AssignedDate = new DateOnly(2026, 8, 31),
                DayOfWeek = DayOfWeekEnum.Monday,
                BreakTime = new TimeOnly(12, 0),
            },
            new ShiftAssignment
            {
                Id = 2,
                WeeklyPlanningId = 1,
                UserId = 20,
                SubServiceShiftConfigId = 1,
                AssignedDate = new DateOnly(2026, 8, 31),
                DayOfWeek = DayOfWeekEnum.Monday,
            },
            new ShiftAssignment
            {
                Id = 3,
                WeeklyPlanningId = 1,
                UserId = 30,
                SubServiceShiftConfigId = 1,
                AssignedDate = new DateOnly(2026, 8, 31),
                DayOfWeek = DayOfWeekEnum.Monday,
            });
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class RecordingQueue : INotificationEmailQueue
    {
        public bool Enabled { get; init; } = true;
        public List<(int UserId, string To, string Category, string Subject, string Html, string? DedupKey)> Direct { get; } = [];

        public Task<bool> IsEnabledAsync(CancellationToken ct = default) => Task.FromResult(Enabled);

        public Task<int> EnqueueNotificationAsync(
            NotificationEmailRecipients recipients,
            NotificationEmailContent content,
            CancellationToken ct = default) => Task.FromResult(0);

        public Task<bool> EnqueueDirectAsync(
            int recipientUserId,
            string toEmail,
            string category,
            string subject,
            string htmlBody,
            string? dedupKey = null,
            CancellationToken ct = default)
        {
            Direct.Add((recipientUserId, toEmail, category, subject, htmlBody, dedupKey));
            return Task.FromResult(true);
        }
    }

    private sealed class RecordingPilotEmailService : IPilotPlanningPersonalEmailService
    {
        public List<(int PlanningId, bool Updated)> Calls { get; } = [];

        public Task SendToAssignedPilotsAsync(int planningId, bool updated, CancellationToken ct = default)
        {
            Calls.Add((planningId, updated));
            return Task.CompletedTask;
        }
    }

    private sealed class FakePlanningHubContext : IHubContext<PlanningHub>
    {
        public IHubClients Clients { get; } = new FakeClients();
        public IGroupManager Groups { get; } = new FakeGroups();

        private sealed class FakeClients : IHubClients
        {
            public IClientProxy All => new FakeProxy();
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new FakeProxy();
            public IClientProxy Client(string connectionId) => new FakeProxy();
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new FakeProxy();
            public IClientProxy Group(string groupName) => new FakeProxy();
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new FakeProxy();
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => new FakeProxy();
            public IClientProxy User(string userId) => new FakeProxy();
            public IClientProxy Users(IReadOnlyList<string> userIds) => new FakeProxy();
        }

        private sealed class FakeGroups : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;

            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }

        private sealed class FakeProxy : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) =>
                Task.CompletedTask;
        }
    }
}
