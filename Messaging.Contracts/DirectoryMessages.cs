namespace Kyntus.Messaging.Contracts;

/// <summary>Publié par Employee Directory (source canonique) après mutation employé.</summary>
public record DirectoryEmployeeChangedMessage
{
    public Guid EmployeeId { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Role { get; init; } = string.Empty;
    public Guid? ParentId { get; init; }
    public string? ServiceId { get; init; }
    public string? CelluleId { get; init; }
    public string? PoleId { get; init; }
    public Guid? BusinessDepartmentId { get; init; }
    public string? BusinessDepartmentKind { get; init; }
    public bool IsActive { get; init; } = true;
    public bool IsDeleted { get; init; }
    public DateTime? HireDate { get; init; }
    public Guid? ChefDeProjetId { get; init; }
    public Guid? SuperviseurId { get; init; }
    public Guid? ReferentTechniqueId { get; init; }
    public int? IdTechnicien { get; init; }
    public string? HtelCode { get; init; }
    /// <summary>Libellé technicien HTEL (NOM Prénom) — utilisé pour l’export planning.</summary>
    public string? HtelTechnicienName { get; init; }
}

/// <summary>Profil RH canonique publié après mutation.</summary>
public record DirectoryEmployeeHrProfileChangedMessage
{
    public Guid EmployeeId { get; init; }
    public DateOnly? DateNaissance { get; init; }
    public string? VilleNaissance { get; init; }
    public string? Nationalite { get; init; }
    public string? NumeroCarteAutoentrepreneur { get; init; }
    public string? Sexe { get; init; }
    public string? SituationFamiliale { get; init; }
    public int? NombreEnfants { get; init; }
    public string? Cin { get; init; }
    public string? Adresse { get; init; }
    public string? EmailPersonnel { get; init; }
    public string? Telephone1 { get; init; }
    public string? TelephoneUrgence { get; init; }
    public string? RelationUrgence { get; init; }
    public string? Rib { get; init; }
    public string? ImmatriculationInterne { get; init; }
    public string? ImmatriculationCnss { get; init; }
    public DateOnly? DateEntree { get; init; }
    public DateOnly? DateEmbauche { get; init; }
    public DateOnly? DateAnciennete { get; init; }
    public DateOnly? DateSortie { get; init; }
    public DateOnly? DateEvolutionPoste { get; init; }
    public string? AncienPoste { get; init; }
    public string? AncienService { get; init; }
    public string? NiveauScolaire { get; init; }
    public string? IntitulesEtudes { get; init; }
    public bool EnFormation { get; init; }
    public DateOnly? DateDebutFormation { get; init; }
    public DateOnly? DateFinFormationPrevue { get; init; }
    public int? NiveauExpertiseMetier { get; init; }
    public bool IsDeleted { get; init; }
}

/// <summary>Publié après mutation d'un département métier (Support / Operational).</summary>
public record DirectoryBusinessDepartmentChangedMessage
{
    public Guid BusinessDepartmentId { get; init; }
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = "Operational";
    public Guid? ManagerEmployeeId { get; init; }
    public IReadOnlyList<string> PoleIds { get; init; } = Array.Empty<string>();
    public bool IsDeleted { get; init; }
}

/// <summary>Publié après création/renommage d'un nœud org.</summary>
public record DirectoryOrgNodeChangedMessage
{
    public string NodeId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public OrgNodeLevel Level { get; init; }
    public string? ParentNodeId { get; init; }
    public bool IsDeleted { get; init; }
}

/// <summary>Publié après changement d'affectation structurelle.</summary>
public record DirectoryAssignmentChangedMessage
{
    public OrgAssignmentKind Kind { get; init; }
    public string NodeId { get; init; } = string.Empty;
    public OrgNodeLevel NodeLevel { get; init; }
    public Guid EmployeeId { get; init; }
    public string? EmployeeEmail { get; init; }
    public string? NewRole { get; init; }
    public bool Removed { get; init; }
}

/// <summary>
/// Snapshot des responsables actifs d'un nœud après mutation d'affectation.
/// Permet aux consommateurs de recalculer l'habilitation multi-responsables.
/// </summary>
public record DirectoryEmployeeResponsiblesChangedMessage
{
    public OrgAssignmentKind Kind { get; init; }
    public string NodeId { get; init; } = string.Empty;
    public OrgNodeLevel NodeLevel { get; init; }
    public IReadOnlyList<ResponsibleEntry> Responsibles { get; init; } = Array.Empty<ResponsibleEntry>();
    public DateTime ChangedAt { get; init; } = DateTime.UtcNow;
}

public record ResponsibleEntry
{
    public Guid ResponsibleId { get; init; }
    public string? Email { get; init; }
    public string? DisplayName { get; init; }
}
