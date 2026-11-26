using Kyntus.Iam;

namespace EmployeeDirectory.Application.Iam;

/// <summary>Restrictions de l'appelant (lues par tous les services, JWT relayé).</summary>
public sealed record AdminRestrictionsMeDto(bool SuperAdmin, bool Restricted, IReadOnlyList<string> Denied);

/// <summary>
/// Un verbe d'une rubrique pour un administrateur : réglage « tous les administrateurs », réglage du
/// compte (null = aucun), et résultat (true = permis).
/// </summary>
public sealed record AdminRestrictionVerbDto(string Verb, bool? TousLesAdmins, bool? Compte, bool Effectif);

public sealed record AdminRestrictionScreenDto(string Id, string Label, string Module, IReadOnlyList<AdminRestrictionVerbDto> Verbs);

public sealed record AdminRestrictionsDto(
    Guid SubjectId,
    bool TousLesAdmins,
    int Retraits,
    DateTime? ModifieLe,
    string? ModifiePar,
    IReadOnlyList<AdminRestrictionScreenDto> Screens);

/// <summary>Changement demandé : « herite » (supprime la ligne), « accorde » ou « retire ».</summary>
public sealed record AdminRestrictionChange(string Action, string Etat);

public sealed record AdminRestrictionsUpdateRequest(IReadOnlyList<AdminRestrictionChange>? Changes);

public sealed record AdminRestrictionSummaryDto(Guid SubjectId, int Retraits, int Accordes, DateTime ModifieLe);

public sealed record AdminRestrictionCatalogDto(IReadOnlyList<string> Verbs, IReadOnlyList<IamScreenCatalogEntryDto> Screens);

public sealed record IamScreenCatalogEntryDto(string Id, string Label, string Module);

/// <summary>Gestion des restrictions des administrateurs — réservée au Super Admin (sauf « me »).</summary>
public interface IIamAdminRestrictionService
{
    Task<IReadOnlyList<string>> GetDeniedAsync(Guid subjectId, CancellationToken ct = default);
    /// <summary>v66 — Retraits explicites d'un compte non administrateur (rôle + compte, compte prioritaire).</summary>
    Task<IReadOnlyList<string>> GetRevocationsAsync(Guid subjectId, string role, CancellationToken ct = default);
    Task<AdminRestrictionsDto> GetDetailAsync(Guid subjectId, CancellationToken ct = default);
    Task<AdminRestrictionsDto> UpdateAsync(Guid subjectId, AdminRestrictionsUpdateRequest request, string? auteur, CancellationToken ct = default);
    Task<IReadOnlyList<AdminRestrictionSummaryDto>> SummaryAsync(CancellationToken ct = default);
}

/// <summary>Catalogue et calculs liés aux administrateurs restreints (purs, testables).</summary>
public static class AdminAccessCatalog
{
    /// <summary>Toutes les fonctionnalités réglables : chaque rubrique × chaque verbe administrateur.</summary>
    public static IReadOnlyList<string> AllActions { get; } = PlatformAccessCatalog.Screens
        .SelectMany(s => AdminRestrictionRules.Verbs.Select(v => $"{s.Id}.{v}"))
        .ToList();

    private static readonly HashSet<string> Known = new(AllActions, StringComparer.Ordinal);

    public static bool IsKnownAction(string? action) => Known.Contains((action ?? string.Empty).Trim());

    public static AdminRestrictionCatalogDto Catalog() => new(
        AdminRestrictionRules.Verbs,
        PlatformAccessCatalog.Screens.Select(s => new IamScreenCatalogEntryDto(s.Id, s.Label, s.Module)).ToList());

    /// <summary>
    /// Droits effectifs d'un administrateur restreint : toutes les rubriques et tous les verbes au
    /// périmètre « Global », sauf ce que le Super Admin a retiré. Sans restriction : « *:Global »
    /// (comportement historique).
    /// </summary>
    public static IReadOnlyList<string> EffectiveKeys(IReadOnlyCollection<string> denied)
    {
        if (denied.Count == 0)
            return ["*:Global"];
        var set = denied as IReadOnlySet<string> ?? new HashSet<string>(denied, StringComparer.Ordinal);
        return PlatformAccessCatalog.Screens
            .SelectMany(s => AdminRestrictionRules.Verbs
                .Where(v => !AdminRestrictionRules.IsDenied(set, s.Id, v))
                .Select(v => $"{s.Id}.{v}:Global"))
            .ToList();
    }

    /// <summary>
    /// Un administrateur restreint ne voit, dans « Accès par rôle » / « Accès par compte », que les
    /// rubriques dont il dispose lui-même, et ne peut accorder ou retirer que celles-ci.
    /// </summary>
    public static bool ScreenVisible(IReadOnlyCollection<string> denied, string screenId) =>
        denied.Count == 0
        || !AdminRestrictionRules.IsDenied(
            denied as IReadOnlySet<string> ?? new HashSet<string>(denied, StringComparer.Ordinal), screenId, "View");

    public static bool ActionAllowed(IReadOnlyCollection<string> denied, string? action)
    {
        if (denied.Count == 0) return true;
        var a = (action ?? string.Empty).Trim();
        if (a == "*") return false;
        var parts = AdminRestrictionRules.Split(a);
        if (parts is null) return true;
        var set = denied as IReadOnlySet<string> ?? new HashSet<string>(denied, StringComparer.Ordinal);
        return !AdminRestrictionRules.IsDenied(set, parts.Value.Screen, parts.Value.Verb);
    }
}
