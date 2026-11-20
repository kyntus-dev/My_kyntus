using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Application.Iam;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmployeeDirectory.Infrastructure.Services;

public sealed class DirectoryIamAdminService(DirectoryDbContext db) : IIamAdminService
{
    public Task<IamCatalogDto> GetCatalogAsync(CancellationToken ct = default)
    {
        var screens = PlatformAccessCatalog.Screens
            .Select(s => new IamScreenDto(s.Id, s.Label, s.Module, PlatformAccessCatalog.Verbs))
            .ToList();
        return Task.FromResult(new IamCatalogDto(
            PlatformAccessCatalog.Roles,
            PlatformAccessCatalog.Verbs,
            PlatformAccessCatalog.Scopes,
            screens));
    }

    public async Task<IReadOnlyList<IamPermissionRowDto>> ListAsync(string? role, CancellationToken ct = default)
    {
        var q = db.IamPermissions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(role))
        {
            var normalized = PlatformAccessCatalog.NormalizeRole(role);
            q = q.Where(p => p.Role == normalized);
        }

        var rows = await q.OrderBy(p => p.Role).ThenBy(p => p.Action).ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<IamMatrixDto> GetMatrixAsync(string role, string? scope = null, CancellationToken ct = default)
    {
        var r = PlatformAccessCatalog.NormalizeRole(role);
        var selectedScope = string.IsNullOrWhiteSpace(scope)
            ? PlatformAccessCatalog.DefaultScope(r)
            : PlatformAccessCatalog.NormalizeScope(scope);
        var rows = await db.IamPermissions.AsNoTracking()
            .Where(p => p.Role == r && p.IsAllowed)
            .ToListAsync(ct);
        var keys = rows.Select(p => p.Action + ":" + p.Scope).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var wildcard = keys.Contains("*:Global");

        var matrix = PlatformAccessCatalog.Screens.Select(s =>
        {
            var verbs = PlatformAccessCatalog.Verbs.ToDictionary(
                v => v,
                v => wildcard
                     || keys.Contains($"{s.Id}.{v}:{selectedScope}")
                     || keys.Contains($"{s.Id}.{v}:Global"),
                StringComparer.OrdinalIgnoreCase);
            return new IamMatrixRowDto(s.Id, s.Label, s.Module, selectedScope, verbs);
        }).ToList();

        return new IamMatrixDto(r, selectedScope, matrix);
    }

    public async Task<IamPermissionRowDto> UpsertAsync(UpsertIamPermissionRequest request, CancellationToken ct = default)
    {
        if (!PlatformAccessCatalog.TryNormalizeGrant(
                request.Role, request.Action, request.Scope,
                out var role, out var action, out var scope, out var error))
            throw new ArgumentException(error ?? "grant invalide", nameof(request));

        var row = await db.IamPermissions
            .FirstOrDefaultAsync(p => p.Role == role && p.Action == action && p.Scope == scope, ct);
        if (row is null)
        {
            row = new IamPermission
            {
                Id = Guid.NewGuid(),
                Role = role,
                Action = action,
                Scope = scope,
                IsAllowed = request.IsAllowed,
            };
            db.IamPermissions.Add(row);
        }
        else
        {
            row.Role = role;
            row.Action = action;
            row.Scope = scope;
            row.IsAllowed = request.IsAllowed;
        }

        if (!request.IsAllowed)
        {
            var siblings = await db.IamPermissions
                .Where(p => p.Role == role && p.Action == action && p.Id != row.Id)
                .ToListAsync(ct);
            foreach (var sibling in siblings)
                sibling.IsAllowed = false;
        }

        await db.SaveChangesAsync(ct);
        return Map(row);
    }

    public async Task<int> UpsertBulkAsync(IReadOnlyList<UpsertIamPermissionRequest> items, CancellationToken ct = default)
    {
        var count = 0;
        foreach (var item in items)
        {
            await UpsertAsync(item, ct);
            count++;
        }

        return count;
    }

    /// <summary>
    /// Droits par défaut — <b>uniquement à la toute première installation</b> (table vide).
    /// <para>
    /// v54 : sur une base déjà en service, plus rien n'est ajouté ni réécrit au démarrage. Avant,
    /// chaque redémarrage recréait les lignes manquantes et réimposait le périmètre du Superviseur :
    /// la configuration faite dans « Accès par rôle » revenait en arrière après chaque mise à jour.
    /// Seule la fusion des anciens noms (Manager, Coach, Chef de projet) continue de s'exécuter :
    /// elle reprend les droits sur le rôle cible et ne s'applique qu'une fois.
    /// </para>
    /// </summary>
    public async Task SeedDefaultsIfEmptyAsync(CancellationToken ct = default)
    {
        var existing = await db.IamPermissions
            .Select(p => p.Role + "\n" + p.Action + "\n" + p.Scope)
            .ToListAsync(ct);
        var keys = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var premiereInstallation = existing.Count == 0;

        var rows = new List<IamPermission>();
        if (!premiereInstallation)
        {
            await FusionnerAliasAsync(ct);
            await AjouterRubriquesAjouteesAsync(ct);
            return;
        }

        foreach (var role in PlatformAccessCatalog.Roles)
        {
            foreach (var (action, scope) in PlatformAccessCatalog.SeedRowsForRole(role))
            {
                var key = role + "\n" + action + "\n" + scope;
                if (!keys.Add(key)) continue;
                rows.Add(new IamPermission
                {
                    Id = Guid.NewGuid(),
                    Role = role,
                    Action = action,
                    Scope = scope,
                    IsAllowed = true,
                });
            }
        }

        if (rows.Count > 0)
        {
            db.IamPermissions.AddRange(rows);
            await db.SaveChangesAsync(ct);
        }

        await FusionnerAliasAsync(ct);
        await EnsureSuperviseurPlanningPerimeterAsync(ct);
    }

    /// <summary>
    /// Fusion des anciens noms : les droits « Coach » s'ajoutent à « Référent technique » (rien n'est
    /// perdu pour les référents, dont le compte de connexion porte encore ce code), puis les lignes
    /// « Coach » et « Manager » sont supprimées. Les comptes « Manager » prennent les droits du
    /// Superviseur. Idempotent : sans effet une fois la fusion faite.
    /// </summary>
    public async Task FusionnerAliasAsync(CancellationToken ct = default)
    {
        var anciens = PlatformAccessCatalog.Alias.Keys.ToList();
        var lignes = await db.IamPermissions.Where(p => anciens.Contains(p.Role)).ToListAsync(ct);
        if (lignes.Count == 0) return;

        // v54 — Les droits cochés sur un ancien nom sont repris sur le rôle cible : rien n'est perdu
        // (« Chef de projet » → « RP » notamment). Seul « Manager » reste sans reprise : ses droits
        // n'ont jamais été ceux du Superviseur.
        foreach (var ancienne in lignes.Where(p => p.IsAllowed && p.Role != "Manager"))
        {
            var cible = PlatformAccessCatalog.Alias[ancienne.Role];
            var existante = await db.IamPermissions.FirstOrDefaultAsync(
                p => p.Role == cible && p.Action == ancienne.Action && p.Scope == ancienne.Scope, ct);
            if (existante is null)
                db.IamPermissions.Add(new IamPermission
                {
                    Id = Guid.NewGuid(),
                    Role = cible,
                    Action = ancienne.Action,
                    Scope = ancienne.Scope,
                    IsAllowed = true,
                });
            else
                existante.IsAllowed = true;
        }

        db.IamPermissions.RemoveRange(lignes);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// v61 — Droits par défaut d'une rubrique ajoutée après l'installation (ex. « Présences des
    /// superviseurs et référents » : RP), posés une seule fois : seulement si la rubrique n'a encore
    /// aucune ligne. Les droits existants des autres rubriques ne sont jamais touchés.
    /// </summary>
    public async Task AjouterRubriquesAjouteesAsync(CancellationToken ct = default)
    {
        foreach (var rubrique in PlatformAccessCatalog.RubriquesAjoutees)
        {
            var prefixe = rubrique + ".";
            if (await db.IamPermissions.AnyAsync(p => p.Action.StartsWith(prefixe), ct))
                continue;
            foreach (var role in PlatformAccessCatalog.Roles.Where(r => !string.Equals(r, "Admin", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var (action, scope) in PlatformAccessCatalog.SeedRowsForRole(role).Where(r => r.Action.StartsWith(prefixe, StringComparison.Ordinal)))
                {
                    db.IamPermissions.Add(new IamPermission
                    {
                        Id = Guid.NewGuid(),
                        Role = role,
                        Action = action,
                        Scope = scope,
                        IsAllowed = true,
                    });
                }
            }
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Superviseur : validation + horaires au périmètre Cellule (y compris bases déjà seedées).
    /// </summary>
    public async Task EnsureSuperviseurPlanningPerimeterAsync(CancellationToken ct = default)
    {
        const string role = "Superviseur";
        var globals = await db.IamPermissions
            .Where(p => p.Role == role
                        && p.Scope == "Global"
                        && (p.Action.StartsWith("planning.shift-config")
                            || p.Action.StartsWith("planning.validation")))
            .ToListAsync(ct);
        foreach (var row in globals)
            row.IsAllowed = false;

        await UpsertAsync(new UpsertIamPermissionRequest(role, "planning.shift-config.View", "Cellule", true), ct);
        await UpsertAsync(new UpsertIamPermissionRequest(role, "planning.shift-config.Configure", "Cellule", true), ct);
        await UpsertAsync(new UpsertIamPermissionRequest(role, "planning.validation.View", "Cellule", true), ct);
    }

    private static IamPermissionRowDto Map(IamPermission p) =>
        new(p.Id, p.Role, p.Action, p.Scope, p.IsAllowed);
}
