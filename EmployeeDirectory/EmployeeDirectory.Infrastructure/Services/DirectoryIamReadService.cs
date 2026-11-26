using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Application.Iam;
using EmployeeDirectory.Infrastructure.Persistence;
using Kyntus.Caching;
using Kyntus.Iam;
using Microsoft.EntityFrameworkCore;

namespace EmployeeDirectory.Infrastructure.Services;

public sealed class DirectoryIamReadService(
    DirectoryDbContext db,
    IOrgResponsibilityResolver responsibility,
    ICacheService cache) : IIamReadService, IPermissionCatalog, IRebacClient, ISubjectPermissionOverrides
{
    public async Task<EffectivePermissionsDto> GetEffectivePermissionsAsync(Guid subjectId, string role, CancellationToken ct = default)
    {
        var keys = await GetEffectivePermissionKeysAsync(role, ct);
        var r = PlatformAccessCatalog.NormalizeRole(role);
        if (string.Equals(r, "Admin", StringComparison.OrdinalIgnoreCase))
            return new EffectivePermissionsDto(subjectId.ToString(), role, keys);

        var exceptions = subjectId == Guid.Empty
            ? new List<(string Action, bool IsAllowed)>()
            : (await db.IamUserPermissions.AsNoTracking()
                    .Where(p => p.SubjectId == subjectId)
                    .Select(p => new { p.Action, p.IsAllowed })
                    .ToListAsync(ct))
                .Select(e => (e.Action, e.IsAllowed))
                .ToList();
        keys = AppliquerExceptions(keys, exceptions);

        // v66 — Les retraits explicites du rôle (action décochée sur tous ses périmètres) et du compte
        // s'appliquent avec la même règle que le contrôle serveur : un écran retiré (Consulter) emporte
        // tous ses verbes. Sinon « Modifier », resté coché, rouvrait l'écran dans le menu.
        var lignesRole = (await db.IamPermissions.AsNoTracking()
                .Where(p => p.Role == r)
                .Select(p => new { p.Action, p.IsAllowed })
                .ToListAsync(ct))
            .Select(l => (l.Action, l.IsAllowed));
        keys = AdminRestrictionRules.RetirerRevoquees(keys, AdminRestrictionRules.RevocationsExplicites(lignesRole, exceptions));
        return new EffectivePermissionsDto(subjectId.ToString(), role, keys);
    }

    /// <summary>
    /// Droits du rôle corrigés par les exceptions du compte : une action retirée disparaît (tous
    /// périmètres), une action accordée est ajoutée sur le périmètre « Self ».
    /// <para>
    /// v66 — Hiérarchie stricte : les ajouts sont posés d'abord, puis les retraits l'emportent ; un
    /// retrait de « Consulter » retire l'écran entier (tous ses verbes). Aucun ancien droit ne reste actif.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> AppliquerExceptions(IReadOnlyList<string> cles, IEnumerable<(string Action, bool Accorde)> exceptions)
    {
        var liste = cles.ToList();
        var toutes = exceptions.ToList();
        foreach (var (action, _) in toutes.Where(e => e.Accorde))
        {
            if (!liste.Any(k => k.StartsWith(action + ":", StringComparison.Ordinal)))
                liste.Add(action + ":Self");
        }
        foreach (var (action, _) in toutes.Where(e => !e.Accorde))
        {
            var parts = AdminRestrictionRules.Split(action);
            var ecranRetire = parts.HasValue && parts.Value.Verb == "View" ? parts.Value.Screen : null;
            liste.RemoveAll(k =>
            {
                if (k.StartsWith(action + ":", StringComparison.Ordinal)) return true;
                if (ecranRetire is null) return false;
                var cle = AdminRestrictionRules.Split(k.Split(':')[0]);
                return cle.HasValue && cle.Value.Screen == ecranRetire;
            });
        }
        return liste;
    }

    public async Task<bool?> GetOverrideAsync(Guid subjectId, string action, CancellationToken ct = default)
    {
        var a = (action ?? string.Empty).Trim();
        var ligne = await db.IamUserPermissions.AsNoTracking()
            .Where(p => p.SubjectId == subjectId && p.Action == a)
            .Select(p => (bool?)p.IsAllowed)
            .FirstOrDefaultAsync(ct);
        return ligne;
    }

    public async Task<bool> EvaluateAsync(Guid subjectId, string role, string action, string resourceType, string? resourceId, CancellationToken ct = default)
    {
        var evaluator = new PolicyEvaluator(this, this, Microsoft.Extensions.Logging.Abstractions.NullLogger<PolicyEvaluator>.Instance, this);
        var decision = await evaluator.EvaluateAsync(new PolicyRequest(subjectId, role, action, resourceType, resourceId), ct);
        return decision.Allowed;
    }

    public Task<bool> RoleHasActionAsync(string role, string action, string scope, CancellationToken ct = default)
    {
        var r = PlatformAccessCatalog.NormalizeRole(role);
        var a = (action ?? string.Empty).Trim();
        var s = PlatformAccessCatalog.NormalizeScope(scope);
        return db.IamPermissions.AsNoTracking()
            .AnyAsync(
                p => p.IsAllowed
                     && p.Role == r
                     && (
                         (p.Action == a && p.Scope == s)
                         || (p.Action == "*" && p.Scope == "Global")),
                ct);
    }

    public async Task<IReadOnlyList<string>> GetEffectivePermissionKeysAsync(string role, CancellationToken ct = default)
    {
        var r = PlatformAccessCatalog.NormalizeRole(role);
        var rows = await db.IamPermissions.AsNoTracking()
            .Where(p => p.Role == r && p.IsAllowed)
            .Select(p => p.Action + ":" + p.Scope)
            .Distinct()
            .ToListAsync(ct);
        return rows;
    }

    public async Task<bool> IsDescendantAsync(Guid viewerId, Guid targetId, CancellationToken ct = default)
    {
        var read = new DirectoryReadService(db, cache);
        return await read.IsDescendantAsync(viewerId, targetId, ct);
    }

    public async Task<IReadOnlyList<string>> GetManagedNodeIdsAsync(Guid employeeId, string kind, CancellationToken ct = default)
    {
        var read = new DirectoryReadService(db, cache);
        var dto = await read.GetManagedNodesAsync(employeeId, kind, ct);
        return dto.NodeIds;
    }

    public Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(Guid actorId, CancellationToken ct = default) =>
        responsibility.GetManagedEmployeeIdsAsync(actorId, ct);

    public Task<bool> CanActOnAsync(Guid actorId, Guid targetEmployeeId, CancellationToken ct = default) =>
        responsibility.CanActOnAsync(actorId, targetEmployeeId, ct);

    public async Task<IReadOnlyList<Guid>> GetResponsibleIdsAsync(string kind, string nodeId, CancellationToken ct = default)
    {
        var list = await responsibility.GetResponsiblesAsync(kind, nodeId, ct);
        return list
            .Select(r => Guid.TryParse(r.EmployeeId, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();
    }
}
