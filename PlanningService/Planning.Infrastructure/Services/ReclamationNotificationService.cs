// src/Services/ReclamationNotificationService.cs
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Planning.Infrastructure.Hubs;
using Planning.Application.Abstractions;
using Planning.Application.NotificationEmail;

namespace Planning.Infrastructure.Services
{
    /// <summary>
    /// Réclamations et propositions : notification temps réel (SignalR), doublée par e-mail
    /// (gestionnaires à la soumission, auteur à chaque changement de statut).
    /// </summary>
    public class ReclamationNotificationService : IReclamationNotificationService
    {
        /// <summary>Rôles qui traitent les réclamations et propositions (cf. ReclamationController).</summary>
        private static readonly string[] ManagerRoles = ["RH", "Manager", "RP", "Admin"];

        private readonly IHubContext<ReclamationHub> _hubContext;
        private readonly INotificationEmailQueue _emails;
        private readonly ILogger<ReclamationNotificationService> _logger;

        public ReclamationNotificationService(
            IHubContext<ReclamationHub> hubContext,
            INotificationEmailQueue emails,
            ILogger<ReclamationNotificationService> logger)
        {
            _hubContext = hubContext;
            _emails = emails;
            _logger = logger;
        }

        public async Task NotifyManagersAsync(string titre, string message, string type)
        {
            await _hubContext.Clients.Group("managers").SendAsync(
                "ReclamationNotification",
                new { titre, message, type, createdAt = DateTime.UtcNow.ToString("o") }
            );
            await EnqueueEmailAsync(new NotificationEmailRecipients(Roles: ManagerRoles), titre, message, "/reclamations-admin");
        }

        public async Task NotifyAuteurAsync(string auteurId, string titre, string message, string type)
        {
            await _hubContext.Clients.Group($"user_{auteurId}").SendAsync(
                "ReclamationNotification",
                new { titre, message, type, createdAt = DateTime.UtcNow.ToString("o") }
            );
            // L'auteur est identifié par son id de compte (claim NameIdentifier).
            if (int.TryParse(auteurId, out var authUserId) && authUserId > 0)
                await EnqueueEmailAsync(new NotificationEmailRecipients(AuthUserIds: [authUserId]), titre, message, "/reclamations");
        }

        private async Task EnqueueEmailAsync(NotificationEmailRecipients recipients, string titre, string message, string route)
        {
            try
            {
                var source = (titre ?? string.Empty).Contains("proposition", StringComparison.OrdinalIgnoreCase)
                    ? NotificationEmailSources.Proposition
                    : NotificationEmailSources.Reclamation;
                await _emails.EnqueueNotificationAsync(
                    recipients,
                    new NotificationEmailContent(source, titre ?? string.Empty, message ?? string.Empty, route),
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                // L'e-mail ne doit jamais bloquer la réclamation elle-même.
                _logger.LogWarning(ex, "E-mail de notification « {Titre} » non mis en file.", titre);
            }
        }
    }
}
