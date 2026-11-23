namespace Planning.Domain.Entities;

/// <summary>
/// v64 — Journal d'actions (Audit et conformité) : une ligne par opération métier tracée — qui, quand,
/// quel type d'opération, sur quel élément, valeurs avant / après. Les lignes ne sont jamais modifiées
/// ni supprimées par l'application.
/// </summary>
public class JournalAction
{
    public long Id { get; set; }
    public DateTime Horodatage { get; set; } = DateTime.UtcNow;

    /// <summary>Compte Planning de l'auteur (null : système ou compte inconnu du Planning).</summary>
    public int? ActeurId { get; set; }
    public Guid? ActeurGuid { get; set; }
    public string ActeurNom { get; set; } = string.Empty;
    /// <summary>Rôle de l'auteur au moment de l'action (jamais « Super Admin » : affiché « Admin »).</summary>
    public string? ActeurRole { get; set; }

    /// <summary>Employés, Réaffectations, Présences et absences, Congés et arrêts, Planning, Horaires,
    /// Imports et exports, Droits d'accès, Paramètres, Validations.</summary>
    public string Module { get; set; } = string.Empty;
    /// <summary>Création, Modification, Désactivation, Réactivation, Suppression, Réaffectation, Import,
    /// Export, Validation, Dévalidation, Génération, Correction manuelle…</summary>
    public string Operation { get; set; } = string.Empty;
    /// <summary>Type technique de l'élément (User, Conge, PresenceJour…).</summary>
    public string Entite { get; set; } = string.Empty;
    public string? EntiteId { get; set; }
    /// <summary>Élément concerné, lisible (« Absence du 02/11/2026 au 05/11/2026 — NOM Prénom »).</summary>
    public string Element { get; set; } = string.Empty;
    /// <summary>Employé concerné (filtre « Employé »), s'il y en a un.</summary>
    public int? CibleUserId { get; set; }

    /// <summary>Valeurs avant / après, en JSON { libellé: valeur } (identifiants sensibles masqués).</summary>
    public string? AnciennesValeurs { get; set; }
    public string? NouvellesValeurs { get; set; }
    public string? Details { get; set; }
    /// <summary>Origine technique (route appelée, consommateur…).</summary>
    public string? Source { get; set; }
}
