using System.Security.Claims;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Application.Iam;
using EmployeeDirectory.Infrastructure.Persistence;
using Kyntus.Iam;
using Kyntus.Messaging.Contracts;
using Kyntus.Messaging.Outbox;
using Microsoft.EntityFrameworkCore;

namespace EmployeeDirectory.API.Controllers;

/// <summary>
/// v64 — Modifications des droits d'accès (par rôle, par compte) publiées vers le journal d'actions du
/// Planning (Audit et conformité), par la boîte d'envoi (outbox). v67 : les actions du Super Admin ne
/// sont pas publiées (elles n'apparaissent pas au journal). Une erreur ici n'annule jamais la
/// modification des droits.
/// </summary>
public static class JournalDroitsPublisher
{
    private const string Module = "Droits d'accès";

    private static readonly Dictionary<string, string> Verbes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["View"] = "Consulter", ["Act"] = "Modifier", ["Validate"] = "Valider", ["Configure"] = "Configurer",
    };

    private static readonly Dictionary<string, string> Portees = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Global"] = "Toute l'entreprise", ["Pole"] = "Pôle", ["Cellule"] = "Cellule", ["Service"] = "Service", ["Self"] = "Soi-même",
    };

    public static string Role(string? role)
    {
        if (string.IsNullOrWhiteSpace(role)) return "—";
        var compact = role.Replace(" ", "").Replace("_", "").Replace("-", "");
        return compact.Contains("superadmin", StringComparison.OrdinalIgnoreCase) ? "Admin" : role.Trim();
    }

    /// <summary>« Absences du planning — ajouter › Modifier » à partir de « conges.planning-ajout.Act ».</summary>
    public static string Droit(string action)
    {
        if (action == "*") return "Tous les droits";
        var point = action.LastIndexOf('.');
        if (point <= 0) return action;
        var ecran = action[..point];
        var verbe = action[(point + 1)..];
        var libelle = PlatformAccessCatalog.Screens.FirstOrDefault(s => string.Equals(s.Id, ecran, StringComparison.OrdinalIgnoreCase))?.Label ?? ecran;
        return $"{libelle} › {Verbes.GetValueOrDefault(verbe, verbe)}";
    }

    public static string Portee(string? scope) => scope is null ? "—" : Portees.GetValueOrDefault(scope, scope);

    /// <summary>État actuel d'un droit de rôle (null : jamais réglé).</summary>
    public static async Task<bool?> EtatRoleAsync(DirectoryDbContext db, UpsertIamPermissionRequest r, CancellationToken ct)
    {
        if (!PlatformAccessCatalog.TryNormalizeGrant(r.Role, r.Action, r.Scope, out var role, out var action, out var scope, out _))
            return null;
        return await db.IamPermissions.AsNoTracking()
            .Where(p => p.Role == role && p.Action == action && p.Scope == scope)
            .Select(p => (bool?)p.IsAllowed)
            .FirstOrDefaultAsync(ct);
    }

    public static async Task PublierRolesAsync(
        ClaimsPrincipal user, DirectoryDbContext db, IOutboxWriter outbox,
        IReadOnlyList<(UpsertIamPermissionRequest Demande, bool? Avant)> changements, ILogger logger, CancellationToken ct)
    {
        // v67 — Les actions du Super Admin ne sont pas inscrites au journal d'actions.
        if (KyntusAdminClaims.IsSuperAdmin(user)) return;
        try
        {
            var reels = changements.Where(c => c.Avant != c.Demande.IsAllowed).ToList();
            if (reels.Count == 0) return;
            string Cle(UpsertIamPermissionRequest d) => $"{Role(d.Role)} › {Droit(d.Action)} ({Portee(d.Scope)})";
            string Etat(bool? v) => v switch { true => "Accordé", false => "Retiré", null => "Non défini" };
            var avant = reels.Take(100).GroupBy(c => Cle(c.Demande)).ToDictionary(g => g.Key, g => (string?)Etat(g.Last().Avant));
            var apres = reels.Take(100).GroupBy(c => Cle(c.Demande)).ToDictionary(g => g.Key, g => (string?)Etat(g.Last().Demande.IsAllowed));
            var roles = reels.Select(c => Role(c.Demande.Role)).Distinct().ToList();
            await outbox.EnqueueAsync(new JournalActionMessage
            {
                ActeurGuid = user.GetSubjectId(),
                ActeurEmail = user.GetEmail(),
                ActeurRole = Role(user.GetAuthRole()),
                Module = Module,
                Operation = "Modification des droits par rôle",
                Entite = "IamPermission",
                EntiteId = string.Join(",", roles),
                Element = $"Accès par rôle — {string.Join(", ", roles)}",
                AnciennesValeurs = avant,
                NouvellesValeurs = apres,
                Details = reels.Count > 100 ? $"{reels.Count} droits modifiés (100 premiers détaillés)." : null,
                Source = "Employee Directory › Accès par rôle",
            }, ct: ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Journal d'actions : modification des droits par rôle non publiée.");
        }
    }

    public static async Task PublierCompteAsync(
        ClaimsPrincipal user, DirectoryDbContext db, IOutboxWriter outbox, Guid compte, UserAccessUpdateRequest demande,
        UserAccessDto? avant, ILogger logger, CancellationToken ct)
    {
        // v67 — Les actions du Super Admin ne sont pas inscrites au journal d'actions.
        if (KyntusAdminClaims.IsSuperAdmin(user)) return;
        try
        {
            var changements = demande.Changes ?? [];
            if (changements.Count == 0) return;
            var employe = await db.Employees.AsNoTracking()
                .Where(e => e.Id == compte)
                .Select(e => new { e.LastName, e.FirstName })
                .FirstOrDefaultAsync(ct);
            var nom = employe is null ? compte.ToString() : $"{employe.LastName.Trim().ToUpperInvariant()} {employe.FirstName.Trim()}".Trim();
            string EtatAvant(string action)
            {
                var point = action.LastIndexOf('.');
                if (avant is null || point <= 0) return "—";
                var verbe = avant.Screens.FirstOrDefault(s => s.Id == action[..point])?.Verbs
                    .FirstOrDefault(v => string.Equals(v.Verb, action[(point + 1)..], StringComparison.OrdinalIgnoreCase));
                return verbe?.Exception switch { true => "Accordé (exception)", false => "Retiré (exception)", null => "Hérité du rôle" };
            }
            string Nouvel(string etat) => etat.ToLowerInvariant() switch
            {
                "accorde" => "Accordé (exception)",
                "retire" => "Retiré (exception)",
                _ => "Hérité du rôle",
            };
            await outbox.EnqueueAsync(new JournalActionMessage
            {
                ActeurGuid = user.GetSubjectId(),
                ActeurEmail = user.GetEmail(),
                ActeurRole = Role(user.GetAuthRole()),
                Module = Module,
                Operation = "Modification des droits du compte",
                Entite = "IamUserPermission",
                EntiteId = compte.ToString(),
                Element = $"Droits du compte — {nom} (rôle {Role(demande.Role)})",
                AnciennesValeurs = changements.Take(100).GroupBy(c => Droit(c.Action)).ToDictionary(g => g.Key, g => (string?)EtatAvant(g.Last().Action)),
                NouvellesValeurs = changements.Take(100).GroupBy(c => Droit(c.Action)).ToDictionary(g => g.Key, g => (string?)Nouvel(g.Last().Etat)),
                Source = "Employee Directory › Droits par compte",
            }, ct: ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Journal d'actions : modification des droits du compte non publiée.");
        }
    }
}
