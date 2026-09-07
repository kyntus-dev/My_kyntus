using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planning.Application.Abstractions;
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
    public async Task Sends_only_to_pilots_with_personal_email()
    {
        await using var db = await SeedAsync();
        var sender = new RecordingEmailSender();
        var svc = new PilotPlanningPersonalEmailService(db, sender, NullLogger<PilotPlanningPersonalEmailService>.Instance);

        await svc.SendToAssignedPilotsAsync(1, updated: false);

        Assert.Single(sender.Sent);
        Assert.Equal("yasmine.perso@gmail.com", sender.Sent[0].To);
        Assert.Equal("Planning 2026-W36 disponible", sender.Sent[0].Subject);
        Assert.DoesNotContain("href", sender.Sent[0].Html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/mes-plannings", sender.Sent[0].Html);
        Assert.Contains("08:00", sender.Sent[0].Html);
        Assert.Contains("EL IDRISSI Yasmine", sender.Sent[0].Html);
        Assert.Contains("31-Aug", sender.Sent[0].Html);
        Assert.DoesNotContain("Chraibi", sender.Sent[0].Html);
        Assert.DoesNotContain("17:00", sender.Sent[0].Html);
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

    private sealed class RecordingEmailSender : IPlanningEmailSender
    {
        public bool IsConfigured => true;
        public List<(string To, string Subject, string Html)> Sent { get; } = [];

        public Task SendHtmlAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            Sent.Add((to, subject, htmlBody));
            return Task.CompletedTask;
        }

        public Task SendRequiredHtmlAsync(string to, string subject, string htmlBody, CancellationToken ct = default) =>
            SendHtmlAsync(to, subject, htmlBody, ct);
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
