using System.Globalization;
using Planning.Application.DTOs;

namespace Planning.Infrastructure.Services.EmployeeImport;

public static class EmployeeDocumentFieldValueFlattener
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    public static Dictionary<string, string?> Flatten(UserDto user)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Set(values, "email", user.Email);
        Set(values, "firstName", user.FirstName);
        Set(values, "lastName", user.LastName);
        Set(values, "role", user.RoleName);
        Set(values, "pole", user.OrgPoleName);
        Set(values, "cellule", user.OrgCelluleName);
        Set(values, "service", user.SubServiceName ?? user.OrgServiceName);
        Set(values, "operationalDepartment", user.OrgOperationalDepartmentName);
        Set(values, "hireDate", FormatDate(user.HireDate));
        Set(values, "isActive", user.IsActive ? "true" : "false");
        Set(values, "level", user.Level.ToString(CultureInfo.InvariantCulture));
        Set(values, "niveauExpertiseMetier", user.NiveauExpertiseMetier?.ToString(CultureInfo.InvariantCulture));

        var hr = user.HrProfile;
        if (hr is not null)
        {
            Set(values, "dateNaissance", FormatDate(hr.DateNaissance));
            Set(values, "villeNaissance", hr.VilleNaissance);
            Set(values, "nationalite", hr.Nationalite);
            Set(values, "numeroCarteAutoentrepreneur", hr.NumeroCarteAutoentrepreneur);
            Set(values, "sexe", hr.Sexe);
            Set(values, "situationFamiliale", hr.SituationFamiliale);
            Set(values, "nombreEnfants", hr.NombreEnfants?.ToString(CultureInfo.InvariantCulture));
            Set(values, "cin", hr.Cin);
            Set(values, "adresse", hr.Adresse);
            Set(values, "emailPersonnel", hr.EmailPersonnel);
            Set(values, "telephone1", hr.Telephone1);
            Set(values, "telephoneUrgence", hr.TelephoneUrgence);
            Set(values, "relationUrgence", hr.RelationUrgence);
            Set(values, "rib", hr.Rib);
            Set(values, "immatriculationInterne", hr.ImmatriculationInterne);
            Set(values, "immatriculationCnss", hr.ImmatriculationCnss);
            Set(values, "dateEntree", FormatDate(hr.DateEntree));
            Set(values, "dateEmbauche", FormatDate(hr.DateEmbauche));
            Set(values, "dateAnciennete", FormatDate(hr.DateAnciennete));
            Set(values, "dateSortie", FormatDate(hr.DateSortie));
            Set(values, "dateEvolutionPoste", FormatDate(hr.DateEvolutionPoste));
            Set(values, "ancienPoste", hr.AncienPoste);
            Set(values, "ancienService", hr.AncienService);
            Set(values, "niveauScolaire", hr.NiveauScolaire);
            Set(values, "intitulesEtudes", hr.IntitulesEtudes);
            Set(values, "enFormation", hr.EnFormation ? "true" : "false");
            Set(values, "dateDebutFormation", FormatDate(hr.DateDebutFormation));
            Set(values, "dateFinFormationPrevue", FormatDate(hr.DateFinFormationPrevue));
        }

        foreach (var kv in user.CustomFields ?? [])
            Set(values, kv.Key, kv.Value);

        return values;
    }

    private static void Set(Dictionary<string, string?> values, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        values[key] = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? FormatDate(DateTime value) =>
        value == default ? null : value.ToString("dd/MM/yyyy", Fr);

    private static string? FormatDate(DateOnly? value) =>
        value is { } d ? d.ToString("dd/MM/yyyy", Fr) : null;
}
