using System;
using System.ComponentModel.DataAnnotations;

namespace Planning.Domain.Entities
{
    /// <summary>
    /// Notification persistée envoyée à un employé lors de la publication de son planning.
    /// Doublée d'un push SignalR temps réel ; la persistance garantit que la notification
    /// reste visible même si l'employé n'était pas connecté au moment de la publication.
    /// </summary>
    public class PlanningNotification
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Id interne Planning de l'employé destinataire.</summary>
        public int UserId { get; set; }

        /// <summary>Id Auth (nameidentifier JWT) du destinataire — sert au filtrage côté front.</summary>
        public int AuthUserId { get; set; }

        public int? WeeklyPlanningId { get; set; }

        [Required]
        public string WeekCode { get; set; } = string.Empty;

        public string SubServiceName { get; set; } = string.Empty;

        [Required]
        public string Message { get; set; } = string.Empty;

        public bool IsRead { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReadAt { get; set; }

        /// <summary>v62 : page ouverte au clic (ex. « /mes-conges ») ; null = déduite du type (anciennes notifications).</summary>
        public string? Lien { get; set; }

        /// <summary>v62 : presences, conges, documentation, affectation, planning…</summary>
        public string? Categorie { get; set; }
    }
}
