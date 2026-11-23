namespace Kyntus.Messaging.Contracts;

/// <summary>
/// Publié vers Planning quand un congé est validé.
/// </summary>
public record CongeValideMessage
{
    public Guid DemandeId { get; init; }
    public Guid EmployeId { get; init; }
    public DateTime DateDebut { get; init; }
    public DateTime DateFin { get; init; }
    public double NombreJours { get; init; }
    public DateTime DateValidation { get; init; }
    /// <summary>Annuel, Exceptionnel, Maternite, Paternite, Maladie.</summary>
    public string TypeConge { get; init; } = "Annuel";
    public string? TypeExceptionnel { get; init; }
    public Guid? ValidateurId { get; init; }
    public string? ValidateurNom { get; init; }
    /// <summary>
    /// v64.2 — vrai quand les dates d'un congé déjà validé ont été modifiées (Admin / RH) :
    /// Planning met à jour l'absence existante (ancienne période rendue, nouvelle posée)
    /// et l'employé reçoit « Congé modifié » au lieu d'une nouvelle validation.
    /// </summary>
    public bool Modification { get; init; }
    public DateTime? AncienneDateDebut { get; init; }
    public DateTime? AncienneDateFin { get; init; }
}

/// <summary>
/// Publié quand une demande de congé est soumise (notification manager).
/// </summary>
public record CongeDemandeMessage
{
    public Guid DemandeId { get; init; }
    public Guid EmployeId { get; init; }
    public Guid ManagerId { get; init; }
    public DateTime DateDebut { get; init; }
    public DateTime DateFin { get; init; }
}

/// <summary>
/// Publié quand un congé est refusé ou annulé après validation — retire l'absence Planning.
/// </summary>
public record CongeRefuseMessage
{
    public Guid DemandeId { get; init; }
    public Guid EmployeId { get; init; }
    public string Motif { get; init; } = string.Empty;
    public Guid? ValidateurId { get; init; }
    public string? ValidateurNom { get; init; }
}
