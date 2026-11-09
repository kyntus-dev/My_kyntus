using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Enums;
using EmployeeDirectory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace EmployeeDirectory.Infrastructure.Services;

/// <summary>
/// Source d'autorité pour l'habilitation multi-responsables : lit OrgAssignment uniquement.
/// ParentId n'est jamais consulté ici.
/// </summary>
public sealed class OrgResponsibilityResolver(DirectoryDbContext db) : IOrgResponsibilityResolver
{
    public async Task<IReadOnlyList<ResponsibleEmployeeDto>> GetResponsiblesAsync(
        string kind,
        string nodeId,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<OrgAssignmentKind>(kind, true, out var k))
            return [];

        var trimmed = nodeId.Trim();
        return await (
            from a in db.OrgAssignments.AsNoTracking()
            join e in db.Employees.AsNoTracking() on a.EmployeeId equals e.Id
            where a.Kind == k && a.NodeId == trimmed && a.EffectiveTo == null && e.IsActive
            orderby a.EffectiveFrom, a.Id
            select new ResponsibleEmployeeDto(
                e.Id.ToString(),
                e.FirstName,
                e.LastName,
                e.Email,
                k.ToString(),
                trimmed)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ManagedNodeDto>> GetManagedNodesDetailedAsync(
        Guid employeeId,
        string? kind = null,
        CancellationToken ct = default)
    {
        var q = db.OrgAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.EffectiveTo == null);

        if (!string.IsNullOrWhiteSpace(kind) && Enum.TryParse<OrgAssignmentKind>(kind, true, out var k))
            q = q.Where(a => a.Kind == k);

        var rows = await q
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.EffectiveFrom)
            .Select(a => new { a.Kind, a.NodeId, a.NodeLevel })
            .ToListAsync(ct);

        var result = new List<ManagedNodeDto>();
        foreach (var row in rows)
        {
            var label = await ResolveNodeLabelAsync(row.Kind, row.NodeId, ct);
            result.Add(new ManagedNodeDto(
                row.Kind.ToString(),
                row.NodeId,
                row.NodeLevel.ToString(),
                label));
        }

        return result;
    }

    public async Task<IReadOnlyList<Guid>> GetManagedEmployeeIdsAsync(
        Guid actorId,
        CancellationToken ct = default)
    {
        var assignments = await db.OrgAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == actorId && a.EffectiveTo == null)
            .Select(a => new { a.Kind, a.NodeId })
            .ToListAsync(ct);

        // v32 : chef de projet = tous les pôles de son activité (pas seulement ceux affectés).
        var polesActivite = await PerimetreActivite.PolesChefDeProjetAsync(db, actorId, ct);
        assignments = assignments
            .Where(a => a.Kind != OrgAssignmentKind.ChefDeProjet)
            .Concat(polesActivite.Select(p => new { Kind = OrgAssignmentKind.ChefDeProjet, NodeId = p }))
            .ToList();

        if (assignments.Count == 0)
            return [];

        var set = new HashSet<Guid>();
        foreach (var a in assignments)
        {
            switch (a.Kind)
            {
                case OrgAssignmentKind.ChefDeProjet:
                {
                    var celluleIds = await db.OrgCellules.AsNoTracking()
                        .Where(c => c.PoleId == a.NodeId)
                        .Select(c => c.Id)
                        .ToListAsync(ct);
                    var serviceIds = await db.OrgServices.AsNoTracking()
                        .Where(s => celluleIds.Contains(s.CelluleId))
                        .Select(s => s.Id)
                        .ToListAsync(ct);
                    var ids = await db.Employees.AsNoTracking()
                        .Where(e => e.IsActive && (
                            e.PoleId == a.NodeId
                            || (e.CelluleId != null && celluleIds.Contains(e.CelluleId))
                            || (e.ServiceId != null && serviceIds.Contains(e.ServiceId))))
                        .Select(e => e.Id)
                        .ToListAsync(ct);
                    foreach (var id in ids) set.Add(id);
                    break;
                }
                case OrgAssignmentKind.Superviseur:
                {
                    var serviceIds = await db.OrgServices.AsNoTracking()
                        .Where(s => s.CelluleId == a.NodeId)
                        .Select(s => s.Id)
                        .ToListAsync(ct);
                    var ids = await db.Employees.AsNoTracking()
                        .Where(e => e.IsActive && (
                            e.CelluleId == a.NodeId
                            || (e.ServiceId != null && serviceIds.Contains(e.ServiceId))))
                        .Select(e => e.Id)
                        .ToListAsync(ct);
                    foreach (var id in ids) set.Add(id);
                    break;
                }
                case OrgAssignmentKind.ReferentTechnique:
                case OrgAssignmentKind.Pilote:
                {
                    var ids = await db.Employees.AsNoTracking()
                        .Where(e => e.IsActive && e.ServiceId == a.NodeId)
                        .Select(e => e.Id)
                        .ToListAsync(ct);
                    foreach (var id in ids) set.Add(id);
                    break;
                }
            }
        }

        set.Remove(actorId);
        return set.ToList();
    }

    public async Task<bool> CanActOnAsync(
        Guid actorId,
        Guid targetEmployeeId,
        CancellationToken ct = default)
    {
        if (actorId == Guid.Empty || targetEmployeeId == Guid.Empty)
            return false;
        if (actorId == targetEmployeeId)
            return true;

        var managed = await GetManagedEmployeeIdsAsync(actorId, ct);
        return managed.Contains(targetEmployeeId);
    }

    public async Task<bool> CanActOnNodeAsync(
        Guid actorId,
        string kind,
        string nodeId,
        CancellationToken ct = default)
    {
        if (!Enum.TryParse<OrgAssignmentKind>(kind, true, out var k))
            return false;

        if (k == OrgAssignmentKind.ChefDeProjet)
            return (await PerimetreActivite.PolesChefDeProjetAsync(db, actorId, ct)).Contains(nodeId.Trim());

        return await db.OrgAssignments.AsNoTracking()
            .AnyAsync(a =>
                a.EmployeeId == actorId
                && a.Kind == k
                && a.NodeId == nodeId.Trim()
                && a.EffectiveTo == null, ct);
    }

    private async Task<string?> ResolveNodeLabelAsync(OrgAssignmentKind kind, string nodeId, CancellationToken ct) =>
        kind switch
        {
            OrgAssignmentKind.ChefDeProjet => await db.OrgPoles.AsNoTracking()
                .Where(p => p.Id == nodeId).Select(p => p.Name).FirstOrDefaultAsync(ct),
            OrgAssignmentKind.Superviseur => await db.OrgCellules.AsNoTracking()
                .Where(c => c.Id == nodeId).Select(c => c.Name).FirstOrDefaultAsync(ct),
            OrgAssignmentKind.ReferentTechnique or OrgAssignmentKind.Pilote => await db.OrgServices.AsNoTracking()
                .Where(s => s.Id == nodeId).Select(s => s.Name).FirstOrDefaultAsync(ct),
            _ => null,
        };
}
