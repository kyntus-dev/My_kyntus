using System.Security.Claims;

namespace Kyntus.Iam;

/// <summary>
/// Super Admin / restrictions des administrateurs (v60).
/// <para>
/// Le Super Admin est un compte « Admin » marqué par la claim <see cref="SuperAdminClaim"/> (posée par
/// AuthService) : il garde donc tout ce que l'Admin a aujourd'hui, partout dans la plateforme, sans
/// aucune restriction. Il est le seul à pouvoir restreindre les administrateurs.
/// </para>
/// <para>
/// Un administrateur a, par défaut, tous les droits (comportement historique, inchangé). Le Super Admin
/// peut retirer — puis rendre — chaque fonctionnalité (« écran.Verbe ») à tous les administrateurs ou à
/// un administrateur précis. Ces restrictions sont lues dynamiquement : elles s'appliquent sans
/// reconnexion (quelques secondes de cache).
/// </para>
/// </summary>
public static class KyntusAdminClaims
{
    /// <summary>Claim JWT posée par AuthService sur le compte Super Admin (valeur « true »).</summary>
    public const string SuperAdminClaim = "kyntus_super_admin";

    public const string AdminRole = "Admin";

    /// <summary>Rôle de connexion (première claim de rôle, comme KyntusClaimsPrincipalExtensions.GetAuthRole).</summary>
    public static string? Role(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return null;
        return user.FindFirst(ClaimTypes.Role)?.Value?.Trim()
               ?? user.FindFirst("role")?.Value?.Trim();
    }

    public static bool IsAdmin(ClaimsPrincipal? user) =>
        string.Equals(Role(user), AdminRole, StringComparison.OrdinalIgnoreCase);

    /// <summary>Super Admin = rôle Admin + claim signée <see cref="SuperAdminClaim"/>.</summary>
    public static bool IsSuperAdmin(ClaimsPrincipal? user) =>
        IsAdmin(user)
        && string.Equals(user!.FindFirst(SuperAdminClaim)?.Value?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    public static Guid? SubjectId(ClaimsPrincipal? user)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return null;
        foreach (var c in user.FindAll("sub"))
            if (Guid.TryParse(c.Value, out var g) && g != Guid.Empty)
                return g;
        foreach (var c in user.FindAll(ClaimTypes.NameIdentifier))
            if (c.Value.Contains('-') && Guid.TryParse(c.Value, out var g) && g != Guid.Empty)
                return g;
        return null;
    }
}

/// <summary>Règles de calcul des restrictions (pures, testables).</summary>
public static class AdminRestrictionRules
{
    /// <summary>SubjectId réservé aux restrictions qui s'appliquent à tous les administrateurs.</summary>
    public static readonly Guid AllAdmins = Guid.Empty;

    /// <summary>
    /// Verbes réglables pour un administrateur. Les quatre premiers sont ceux d'« Accès par rôle » ;
    /// « Delete » (suppressions) et « Export » (exports, téléchargements) affinent le contrôle.
    /// </summary>
    public static readonly string[] Verbs = ["View", "Act", "Validate", "Configure", "Delete", "Export"];

    public static bool IsKnownVerb(string? verb) =>
        Verbs.Contains(verb ?? string.Empty, StringComparer.Ordinal);

    /// <summary>« écran.Verbe » → (écran, verbe) ; null si mal formé.</summary>
    public static (string Screen, string Verb)? Split(string? action)
    {
        var a = (action ?? string.Empty).Trim();
        var dot = a.LastIndexOf('.');
        if (dot <= 0 || dot == a.Length - 1)
            return null;
        return (a[..dot], a[(dot + 1)..]);
    }

    /// <summary>
    /// Actions refusées à un administrateur : la ligne propre au compte l'emporte sur la ligne « tous
    /// les administrateurs » ; sans ligne, l'action est permise (tous les droits par défaut).
    /// </summary>
    public static IReadOnlyCollection<string> DeniedActions(
        Guid subjectId,
        IEnumerable<(Guid SubjectId, string Action, bool IsAllowed)> rows)
    {
        var final = new Dictionary<string, bool>(StringComparer.Ordinal);
        var list = rows.ToList();
        foreach (var r in list.Where(r => r.SubjectId == AllAdmins))
            final[r.Action] = r.IsAllowed;
        if (subjectId != AllAdmins)
            foreach (var r in list.Where(r => r.SubjectId == subjectId))
                final[r.Action] = r.IsAllowed;
        return final.Where(kv => !kv.Value).Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
    }

    /// <summary>Refusé si l'écran entier (View) ou ce verbe précis est retiré.</summary>
    public static bool IsDenied(IReadOnlySet<string> denied, string screen, string verb) =>
        denied.Count > 0 && (denied.Contains(screen + ".View") || denied.Contains(screen + "." + verb));
}

/// <summary>Restrictions applicables à l'utilisateur courant.</summary>
public sealed class AdminRestrictionSnapshot
{
    public static readonly AdminRestrictionSnapshot None = new(false, []);
    public static readonly AdminRestrictionSnapshot SuperAdminSnapshot = new(true, []);

    private readonly HashSet<string> _denied;

    public AdminRestrictionSnapshot(bool superAdmin, IEnumerable<string> denied)
    {
        SuperAdmin = superAdmin;
        _denied = superAdmin
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(denied ?? [], StringComparer.Ordinal);
    }

    public bool SuperAdmin { get; }

    /// <summary>Au moins une fonctionnalité retirée.</summary>
    public bool Restricted => !SuperAdmin && _denied.Count > 0;

    public IReadOnlyCollection<string> Denied => _denied;

    public bool IsDenied(string screen, string verb) =>
        Restricted && AdminRestrictionRules.IsDenied(_denied, screen, verb);

    public bool IsDenied(string action)
    {
        var parts = AdminRestrictionRules.Split(action);
        return parts is not null && IsDenied(parts.Value.Screen, parts.Value.Verb);
    }
}

/// <summary>Fournit les restrictions d'un administrateur (Directory : base ; autres services : HTTP + cache).</summary>
public interface IAdminRestrictionProvider
{
    Task<AdminRestrictionSnapshot> GetAsync(Guid subjectId, CancellationToken ct = default);
}
