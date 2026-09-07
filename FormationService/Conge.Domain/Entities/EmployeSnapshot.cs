namespace Conge.Domain.Entities;

/// <summary>
/// Snapshot des données employé reçues via RabbitMQ depuis le service RH/Planning.
/// On ne stocke que les infos nécessaires à la gestion des congés.
/// </summary>
public class EmployeSnapshot
{
    public Guid Id { get; private set; }
    public Guid EmployeId { get; private set; }
    public string Nom { get; private set; } = string.Empty;
    public string Prenom { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public Guid ManagerId { get; private set; }
    /// <summary>Legacy Guid service (compat quota / anciennes projections).</summary>
    public Guid ServiceId { get; private set; }
    public string ServiceNom { get; private set; } = string.Empty;
    public DateTime DateEmbauche { get; private set; }
    public bool EstMineur { get; private set; }
    public DateTime DerniereModification { get; private set; }

    public string Role { get; private set; } = string.Empty;

    /// <summary>IDs Directory (périmètre org multi-responsables).</summary>
    public string? PoleId { get; private set; }
    public string? CelluleId { get; private set; }
    /// <summary>ID nœud service Directory (string), distinct du legacy <see cref="ServiceId"/>.</summary>
    public string? OrgServiceId { get; private set; }
    public Guid? BusinessDepartmentId { get; private set; }
    /// <summary>True si l’employé Directory est inactif/supprimé mais des demandes ouvertes existent.</summary>
    public bool IsArchived { get; private set; }

    private EmployeSnapshot() { }

    public static EmployeSnapshot Creer(
        Guid employeId,
        string nom,
        string prenom,
        string email,
        Guid managerId,
        Guid serviceId,
        string serviceNom,
        DateTime dateEmbauche,
        bool estMineur = false,
        string role = "Employee",
        string? poleId = null,
        string? celluleId = null,
        string? orgServiceId = null,
        Guid? businessDepartmentId = null)
    {
        return new EmployeSnapshot
        {
            Id = Guid.NewGuid(),
            EmployeId = employeId,
            Nom = nom,
            Prenom = prenom,
            Email = email,
            ManagerId = managerId,
            ServiceId = serviceId,
            ServiceNom = serviceNom,
            DateEmbauche = AsUtc(dateEmbauche),
            EstMineur = estMineur,
            Role = role,
            PoleId = NormalizeId(poleId),
            CelluleId = NormalizeId(celluleId),
            OrgServiceId = NormalizeId(orgServiceId),
            BusinessDepartmentId = businessDepartmentId,
            DerniereModification = DateTime.UtcNow
        };
    }

    public void MettreAJour(
        string nom,
        string prenom,
        string email,
        Guid managerId,
        Guid serviceId,
        string serviceNom,
        string role = "Employee",
        DateTime? dateEmbauche = null,
        string? poleId = null,
        string? celluleId = null,
        string? orgServiceId = null,
        Guid? businessDepartmentId = null)
    {
        Nom = nom;
        Prenom = prenom;
        Email = email;
        ManagerId = managerId;
        ServiceId = serviceId;
        ServiceNom = serviceNom;
        Role = role;
        if (dateEmbauche.HasValue)
            DateEmbauche = AsUtc(dateEmbauche.Value);
        MettreAJourPerimetre(poleId, celluleId, orgServiceId, businessDepartmentId);
        DerniereModification = DateTime.UtcNow;
    }

    public void MettreAJourPerimetre(
        string? poleId,
        string? celluleId,
        string? orgServiceId,
        Guid? businessDepartmentId)
    {
        PoleId = NormalizeId(poleId);
        CelluleId = NormalizeId(celluleId);
        OrgServiceId = NormalizeId(orgServiceId);
        BusinessDepartmentId = businessDepartmentId;
        DerniereModification = DateTime.UtcNow;
    }

    public void MettreAJourIdentite(string nom, string prenom, string email)
    {
        Nom = nom;
        Prenom = prenom;
        Email = email;
        DerniereModification = DateTime.UtcNow;
    }

    public void MettreAJourRole(string role, Guid? managerId = null)
    {
        Role = role;
        if (managerId is { } mid && mid != Guid.Empty)
            ManagerId = mid;
        DerniereModification = DateTime.UtcNow;
    }

    public void AppliquerNoeudOrg(string? poleId, string? celluleId, string? orgServiceId)
    {
        if (poleId is not null)
            PoleId = NormalizeId(poleId);
        if (celluleId is not null)
            CelluleId = NormalizeId(celluleId);
        if (orgServiceId is not null)
            OrgServiceId = NormalizeId(orgServiceId);
        DerniereModification = DateTime.UtcNow;
    }

    public void Archiver()
    {
        IsArchived = true;
        DerniereModification = DateTime.UtcNow;
    }

    public void Reactiver()
    {
        IsArchived = false;
        DerniereModification = DateTime.UtcNow;
    }

    public void MettreAJourEstMineur(bool estMineur)
    {
        EstMineur = estMineur;
        DerniereModification = DateTime.UtcNow;
    }

    /// <summary>Vrai si l'employé a moins de 18 ans à la date du jour (UTC).</summary>
    public static bool ComputeEstMineur(DateOnly? dateNaissance)
    {
        if (dateNaissance is null)
            return false;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return dateNaissance.Value.AddYears(18) > today;
    }

    public int GetAncienneteAnnees()
    {
        var today = DateTime.Today;
        var years = today.Year - DateEmbauche.Year;
        if (DateEmbauche.Date > today.AddYears(-years)) years--;
        return years;
    }

    public DateTime DateEligibiliteCongeAnnuel()
    {
        var eligible = DateEmbauche.Date.AddMonths(PolitiqueConge.MoisAncienneteMinimum);
        return DateTime.SpecifyKind(eligible, DateTimeKind.Utc);
    }

    /// <summary>
    /// Droit au congé annuel à partir de la date d’embauche + 6 mois.
    /// La demande peut être déposée avant : c’est la date de début du congé qui doit être éligible.
    /// </summary>
    public bool EstEligibleCongeAnnuel(DateTime? dateDebutConge = null)
    {
        var reference = (dateDebutConge ?? DateTime.Today).Date;
        return reference >= DateEligibiliteCongeAnnuel().Date;
    }

    public string NomComplet => $"{Prenom} {Nom}".Trim();

    private static string? NormalizeId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Npgsql refuse Kind=Unspecified sur timestamp with time zone.
    /// DateOnly.ToDateTime et JSON Directory arrivent souvent Unspecified.
    /// </summary>
    internal static DateTime AsUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
