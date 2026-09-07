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

    public async Task SeedDefaultsIfEmptyAsync(CancellationToken ct = default)
    {
        var existing = await db.IamPermissions
            .Select(p => p.Role + "\n" + p.Action + "\n" + p.Scope)
            .ToListAsync(ct);
        var keys = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var rows = new List<IamPermission>();
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

        await EnsureSuperviseurPlanningPerimeterAsync(ct);
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
