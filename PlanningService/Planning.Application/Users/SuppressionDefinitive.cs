namespace Planning.Application.Users;

/// <summary>v67 — Confirmation de la suppression définitive : l'adresse e-mail du compte, recopiée.</summary>
public sealed record SuppressionDefinitiveRequest(string? Confirmation);

/// <summary>v67 — Issue d'une suppression définitive.</summary>
public enum SuppressionDefinitiveIssue
{
    Supprime,
    Introuvable,
    /// <summary>Des données à conserver (planning, demandes, pointages…) sont rattachées au compte.</summary>
    DonneesAConserver,
}

/// <summary>
/// v67 — Résultat : <paramref name="Bloquants"/> nomme, en clair, les données qui imposent de garder
/// le compte (il peut alors seulement être désactivé). <paramref name="Identite"/> : ce qui est
/// inscrit au journal d'actions (nom, e-mail, rôle…), conservé après la suppression.
/// </summary>
public sealed record SuppressionDefinitiveResultat(
    SuppressionDefinitiveIssue Issue,
    IReadOnlyList<string> Bloquants,
    IReadOnlyDictionary<string, string?> Identite,
    string? Collaborateur = null);

/// <summary>
/// v67 — Suppression définitive d'un compte (Admin et RH), distincte de la désactivation.
/// <para>
/// Règles de conservation : un compte auquel sont rattachées des données que la base protège
/// (planning, demandes, pointages, renforts…) n'est pas supprimé — rien n'est effacé et la
/// désactivation reste la voie normale. Sinon le compte (Planning, annuaire, connexion) et ses
/// réglages personnels sont supprimés en une transaction.
/// </para>
/// <para>
/// Traçabilité : l'identité du compte supprimé et l'auteur de la suppression sont inscrits au journal
/// d'actions, qui n'est pas lié au compte et reste consultable.
/// </para>
/// </summary>
public interface IUtilisateurSuppressionDefinitive
{
    Task<SuppressionDefinitiveResultat> SupprimerDefinitivementAsync(int id, CancellationToken ct = default);
}

/// <summary>Règles pures (testées) de la suppression définitive.</summary>
public static class SuppressionDefinitiveRegles
{
    private static readonly Dictionary<string, string> Libelles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ShiftAssignments"] = "affectations de planning",
        ["Declarations"] = "déclarations",
        ["PlanningConsultations"] = "consultations de planning",
        ["PlanningChangeRequests"] = "demandes de modification du planning",
        ["PlanningExceptionalRequests"] = "demandes exceptionnelles de planning",
        ["PlanningReinforcementRequests"] = "demandes de renfort",
        ["PlanningReinforcementVolunteers"] = "volontariats de renfort",
        ["WeeklyEmployeeShiftModes"] = "rotations hebdomadaires",
        ["WeeklyPlannings"] = "plannings hebdomadaires",
        ["PlanningComments"] = "commentaires de planning",
        ["SaturdayGroups"] = "groupes du samedi",
        ["SaturdayHistories"] = "historique des samedis",
        ["Conges"] = "congés",
        ["Contracts"] = "contrats",
        ["PointageCorrespondances"] = "correspondances de pointeuse",
    };

    /// <summary>Libellé lisible d'une table qui protège ses données (contrainte de clé étrangère).</summary>
    public static string Libelle(string? table)
    {
        if (string.IsNullOrWhiteSpace(table)) return "données rattachées au compte";
        var t = table.Trim().Trim('"');
        return Libelles.TryGetValue(t, out var l) ? l : $"données « {t} »";
    }

    /// <summary>
    /// La confirmation saisie correspond-elle à l'adresse e-mail du compte ? (casse et espaces ignorés ;
    /// un compte sans e-mail se confirme avec le mot SUPPRIMER).
    /// </summary>
    public static bool ConfirmationValide(string? saisie, string? email)
    {
        var s = (saisie ?? string.Empty).Trim();
        if (s.Length == 0) return false;
        var attendu = string.IsNullOrWhiteSpace(email) ? "SUPPRIMER" : email.Trim();
        return string.Equals(s, attendu, StringComparison.OrdinalIgnoreCase);
    }
}
