namespace Planning.Infrastructure.Persistence;

/// <summary>
/// Roster contact centre (miroir de init/contactcentre/roster.json) pour seeds Docker.
/// GUIDs Auth-mappés = SubjectId AuthService ; les autres sont stables 22222222-….
/// </summary>
internal static class ContactCentreRoster
{
    internal sealed record Employee(
        string PrimeId,
        string FirstName,
        string LastName,
        string ContactEmail,
        string Role,
        string PoleId,
        string CelluleId,
        string ServiceId,
        Guid Guid,
        string? AuthEmail,
        string PlanningRole);

    internal static readonly Employee[] Employees =
    [
        new("e1", "Yasmine", "El Idrissi", "yasmine.elidrissi@contactcentre.ma", "Pilote", "d1", "p1", "c1",
            Guid.Parse("11111111-1111-4111-8111-111111111103"), "employee@kyntus.ma", "Pilote"),
        new("e2", "Mehdi", "Chraibi", "mehdi.chraibi@contactcentre.ma", "Pilote", "d1", "p1", "c1",
            Guid.Parse("22222222-2222-4222-8222-222222222002"), null, "Pilote"),
        new("e3", "Ghita", "Benkirane", "ghita.benkirane@contactcentre.ma", "Chef de projet", "d1", "p1", "c1",
            Guid.Parse("11111111-1111-4111-8111-111111111107"), "rp@kyntus.ma", "Chef de projet"),
        new("e4", "Imane", "Fassi", "imane.fassi@contactcentre.ma", "Pilote", "d1", "p1", "c1",
            Guid.Parse("22222222-2222-4222-8222-222222222004"), null, "Pilote"),
        new("e5", "Latifa", "Mansouri", "latifa.mansouri@contactcentre.ma", "RH", "d2", "p3", "c4",
            Guid.Parse("11111111-1111-4111-8111-111111111104"), "rh@kyntus.ma", "RH"),
        new("e6", "Hicham", "Benjelloun", "hicham.benjelloun@contactcentre.ma", "Chef de projet", "d1", "p1", "c1",
            Guid.Parse("11111111-1111-4111-8111-111111111110"), "formation@kyntus.ma", "EquipeFormation"),
        new("e7", "Laila", "Zahidi", "laila.zahidi@contactcentre.ma", "Audit", "d1", "p1", "c1",
            Guid.Parse("11111111-1111-4111-8111-111111111109"), "audit@kyntus.ma", "Audit"),
        new("e8", "Omar", "Tazi", "omar.tazi@contactcentre.ma", "Référent technique", "d1", "p1", "c1",
            Guid.Parse("11111111-1111-4111-8111-111111111106"), "coach@kyntus.ma", "Référent technique"),
        new("e9", "Kenza", "Alami", "kenza.alami@contactcentre.ma", "Superviseur", "d1", "p1", "c1",
            Guid.Parse("11111111-1111-4111-8111-111111111111"), "superviseur@kyntus.ma", "Superviseur"),
        new("e10", "Nadia", "Benchrif", "nadia.benchrif@contactcentre.ma", "Manager", "d1", "p1", "c1",
            Guid.Parse("11111111-1111-4111-8111-111111111105"), "manager@kyntus.ma", "Superviseur"),
        new("e11", "Karim", "Oufkir", "karim.oufkir@contactcentre.ma", "Comptabilité", "d1", "p1", "c1",
            Guid.Parse("22222222-2222-4222-8222-222222222011"), null, "Pilote"),
        new("e-admin", "Système", "Admin", "admin@contactcentre.ma", "Admin", "d1", "p1", "c1",
            Guid.Parse("11111111-1111-4111-8111-111111111108"), "admin@kyntus.ma", "Admin"),
    ];

    internal static string DisplayName(Employee e) => UserDisplayName.Format(e.FirstName, e.LastName);

    /// <summary>Email utilisé en base Planning (Auth si mappé, sinon contact centre).</summary>
    internal static string PlanningLoginEmail(Employee e) =>
        string.IsNullOrWhiteSpace(e.AuthEmail) ? e.ContactEmail : e.AuthEmail!;
}
