namespace Planning.Infrastructure.Persistence;

/// <summary>Roster pôle pilotage performance (miroir init/pilotage/roster.json).</summary>
internal static class PilotagePerformanceRoster
{
    internal const string PoleId = "pole-pilotage-performance";
    internal const string PoleName = "pole pilotage performance";
    internal const string CelluleId = "cell-suivi-kpi";
    internal const string CelluleName = "cellule suivi KPI";
    internal const string ServiceId = "svc-analyse-operationnelle";
    internal const string ServiceName = "service analyse operationnelle";
    internal const string ServiceCode = "ANALYSE-OP";

    internal sealed record Employee(
        Guid Guid,
        string FirstName,
        string LastName,
        string Email,
        string PlanningRole);

    internal static readonly Employee[] Employees =
    [
        new(Guid.Parse("33333333-3333-4333-8333-333333333001"), "Malak", "Souiri", "malak.souiri@contactcentre.ma", "Chef de projet"),
        new(Guid.Parse("33333333-3333-4333-8333-333333333002"), "Salim", "Ouazzani", "salim.ouazzani@contactcentre.ma", "Superviseur"),
        new(Guid.Parse("33333333-3333-4333-8333-333333333003"), "Younes", "Elidrissi", "younes.elidrissi@contactcentre.ma", "Référent technique"),
        new(Guid.Parse("33333333-3333-4333-8333-333333333004"), "Chaima", "Benali", "chaima.benali@contactcentre.ma", "Pilote"),
        new(Guid.Parse("33333333-3333-4333-8333-333333333005"), "Hamid", "Fellah", "hamid.fellah@contactcentre.ma", "Pilote"),
        new(Guid.Parse("33333333-3333-4333-8333-333333333006"), "Othmane", "Kabbaj", "othmane.kabbaj@contactcentre.ma", "Pilote"),
        new(Guid.Parse("33333333-3333-4333-8333-333333333007"), "Asmae", "Tazi", "asmae.tazi@contactcentre.ma", "Pilote"),
        new(Guid.Parse("33333333-3333-4333-8333-333333333008"), "Rania", "Karimi", "rania.karimi@contactcentre.ma", "Pilote"),
    ];

    internal static string DisplayName(Employee e) => UserDisplayName.Format(e.FirstName, e.LastName);
}
