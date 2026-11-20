using Kyntus.Messaging.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;
using Planning.Domain.Entities;
using Planning.Infrastructure.Hubs;
using Planning.Infrastructure.Messaging.Consumers;
using Planning.Infrastructure.Persistence;
using Planning.Infrastructure.Services.Notifications;

namespace Planning.UnitTests;

/// <summary>v62 : cloche de l'application — notification persistée, poussée en direct, liée à une page.</summary>
public class AppNotifierTests
{
    [Fact]
    public async Task Notification_persistee_poussee_et_regroupee_par_cle()
    {
        await using var db = await SeedAsync();
        var hub = new HubEnregistreur();
        var notifier = new AppNotifier(db, hub, NullLogger<AppNotifier>.Instance);

        var n = await notifier.NotifierAsync(
            new NotificationEmailRecipients(UserIds: [2]),
            new AppNotification("conges", "Congé validé", "Votre congé du 3 au 5 novembre a été validé.", "/mes-conges", "CONGE-1"));

        Assert.Equal(1, n);
        var ligne = await db.PlanningNotifications.SingleAsync();
        Assert.Equal(902, ligne.AuthUserId);
        Assert.Equal("NOTIF-CONGE-1", ligne.WeekCode);
        Assert.Equal("Congé validé", ligne.SubServiceName);
        Assert.Equal("/mes-conges", ligne.Lien);
        Assert.Equal("conges", ligne.Categorie);
        Assert.Equal(("user_902", "PlanningPublished"), Assert.Single(hub.Envois));

        // Même clé, non lue : mise à jour, pas de doublon.
        await notifier.NotifierAsync(
            new NotificationEmailRecipients(UserIds: [2]),
            new AppNotification("conges", "Congé annulé", "Votre congé validé a été annulé.", "/mes-conges", "CONGE-1"));
        Assert.Equal("Congé annulé", (await db.PlanningNotifications.SingleAsync()).SubServiceName);

        // Compte inactif, sans compte de connexion, rôle exclu ou lien externe : jamais de faille.
        Assert.Equal(0, await notifier.NotifierAsync(new NotificationEmailRecipients(UserIds: [3]),
            new AppNotification("presences", "x", "y", "/x")));
        Assert.Equal(0, await notifier.NotifierAsync(new NotificationEmailRecipients(UserIds: [2]),
            new AppNotification("documentation", "x", "y", "/x"), rolesExclus: ["Pilote"]));
        Assert.Equal("/notifications", AppNotifier.LienSur("https://exemple.com"));
        Assert.Equal("/notifications", AppNotifier.LienSur("//exemple.com"));
    }

    [Fact]
    public async Task Document_traite_dans_la_cloche_du_collaborateur_pas_pour_un_role()
    {
        var pourAgent = new NotificationEmailRequestedMessage
        {
            Source = "documentation", Title = "Demande de document validée", Body = "Votre demande a été validée.",
            Route = "/documentation", EmployeeIds = [Guid.NewGuid()],
        };
        var pourRh = new NotificationEmailRequestedMessage { Source = "documentation", Body = "Nouvelle demande", Roles = ["RH"] };
        Assert.True(NotificationEmailRequestedConsumer.DocumentPourCollaborateur(pourAgent));
        Assert.False(NotificationEmailRequestedConsumer.DocumentPourCollaborateur(pourRh));
        Assert.False(NotificationEmailRequestedConsumer.DocumentPourCollaborateur(pourAgent with { Source = "prime" }));
        Assert.Equal("/documentation/tracking", NotificationEmailRequestedConsumer.LienDocument("/documentation"));
        Assert.Equal("/documentation/tracking", NotificationEmailRequestedConsumer.LienDocument(null));
        Assert.Equal("/documentation/my-docs", NotificationEmailRequestedConsumer.LienDocument("/documentation/my-docs"));
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Roles.Add(new Role { Id = 1, Name = "Pilote" });
        db.Users.AddRange(
            new User { Id = 2, RoleId = 1, FirstName = "Agent", LastName = "Alpha", Email = "a@kyntus.com", AuthUserId = 902, IsActive = true },
            new User { Id = 3, RoleId = 1, FirstName = "Agent", LastName = "Parti", Email = "b@kyntus.com", AuthUserId = 903, IsActive = false });
        await db.SaveChangesAsync();
        return db;
    }

    private sealed class HubEnregistreur : IHubContext<PlanningHub>
    {
        public List<(string Groupe, string Methode)> Envois { get; } = [];
        public IHubClients Clients => new Clients_(this);
        public IGroupManager Groups { get; } = new Groupes();

        private sealed class Clients_(HubEnregistreur hub) : IHubClients
        {
            public IClientProxy All => new Proxy(hub, "all");
            public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => new Proxy(hub, "all");
            public IClientProxy Client(string connectionId) => new Proxy(hub, connectionId);
            public IClientProxy Clients(IReadOnlyList<string> connectionIds) => new Proxy(hub, "clients");
            public IClientProxy Group(string groupName) => new Proxy(hub, groupName);
            public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => new Proxy(hub, groupName);
            public IClientProxy Groups(IReadOnlyList<string> groupNames) => new Proxy(hub, "groups");
            public IClientProxy User(string userId) => new Proxy(hub, userId);
            public IClientProxy Users(IReadOnlyList<string> userIds) => new Proxy(hub, "users");
        }

        private sealed class Groupes : IGroupManager
        {
            public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
            public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        }

        private sealed class Proxy(HubEnregistreur hub, string groupe) : IClientProxy
        {
            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                hub.Envois.Add((groupe, method));
                return Task.CompletedTask;
            }
        }
    }
}
