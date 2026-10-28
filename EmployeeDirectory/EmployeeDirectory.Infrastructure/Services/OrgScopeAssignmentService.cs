using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using DomainAssignmentKind = EmployeeDirectory.Domain.Enums.OrgAssignmentKind;

namespace EmployeeDirectory.Infrastructure.Services;

/// <summary>
/// Réorganisation 2026 — responsabilités par périmètre métier.
/// Développe un périmètre (activité / cellule) en nœuds existants puis délègue à
/// <see cref="IDirectoryWriteService.ReconcileEmployeeStructuralAssignmentsAsync"/> :
/// mêmes événements outbox, même historique, même exclusivité inter-rôles.
/// </summary>
public sealed class OrgScopeAssignmentService(
    DirectoryDbContext db,
    IDirectoryWriteService write,
    IConfiguration? configuration = null) : IOrgScopeAssignmentService
{
    private const string DefaultReason = "Affectation par périmètre (activité / cellule)";
    private const string ExpandReason = "Extension au périmètre complet (activité / cellule) — règle réorganisation 2026";
    private const string PropagationReason = "Extension automatique du périmètre (nouveau nœud dans le périmètre couvert)";

    /// <summary>Interrupteur de sécurité : Directory__OrgScopePropagation__Enabled=false désactive l'extension auto.</summary>
    private bool PropagationEnabled =>
        !bool.TryParse(configuration?["Directory:OrgScopePropagation:Enabled"], out var enabled) || enabled;

    public async Task<OrgScopeReconcileResult> ReconcileScopeAsync(
        string kind,
        Guid employeeId,
        IReadOnlyList<string> scopeIds,
        string primaryScopeId,
        Guid? changedBy,
        string? reason,
        CancellationToken ct = default)
    {
        var assignmentKind = ParseScopedKind(kind);

        var scopes = (scopeIds ?? [])
            .Select(s => s?.Trim() ?? string.Empty)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (scopes.Count == 0)
            throw new ArgumentException("Au moins un périmètre est requis.");

        var primary = (primaryScopeId ?? string.Empty).Trim();
        if (primary.Length == 0)
            throw new ArgumentException("primaryScopeId est requis.");
        primary = scopes.FirstOrDefault(s => string.Equals(s, primary, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException("primaryScopeId doit faire partie de scopeIds.");

        // Le périmètre principal en premier : l'ancre (PoleId / CelluleId / ServiceId) en découle.
        scopes.Remove(primary);
        scopes.Insert(0, primary);

        var nodesByScope = assignmentKind == DomainAssignmentKind.ReferentTechnique
            ? await ServicesByCelluleAsync(scopes, ct)
            : await NodesByActivityAsync(assignmentKind, scopes, ct);

        var nodeIds = new List<string>();
        foreach (var scope in scopes)
        {
            foreach (var node in nodesByScope[scope])
            {
                if (!nodeIds.Contains(node, StringComparer.Ordinal))
                    nodeIds.Add(node);
            }
        }

        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId, ct)
            ?? throw new KeyNotFoundException("Employé introuvable.");

        var primaryNodes = nodesByScope[primary];
        var anchor = CurrentAnchor(employee, assignmentKind);
        var primaryNode = anchor is not null && primaryNodes.Contains(anchor, StringComparer.Ordinal)
            ? anchor
            : primaryNodes[0];

        var result = await write.ReconcileEmployeeStructuralAssignmentsAsync(
            assignmentKind.ToString(),
            employeeId,
            nodeIds,
            primaryNode,
            changedBy,
            string.IsNullOrWhiteSpace(reason) ? DefaultReason : reason,
            ct);

        return new OrgScopeReconcileResult(
            assignmentKind.ToString(),
            employeeId.ToString(),
            assignmentKind == DomainAssignmentKind.ReferentTechnique ? OrgScopeLevels.Cellule : OrgScopeLevels.Activite,
            scopes,
            primary,
            result);
    }

    public async Task<IReadOnlyList<OrgScopeCoverageRowDto>> GetCoverageReportAsync(CancellationToken ct = default)
    {
        var departments = await db.BusinessDepartments.AsNoTracking()
            .Where(d => d.Kind == BusinessDepartmentKind.Operational)
            .Select(d => new { d.Id, d.Code, d.Name })
            .ToListAsync(ct);
        var poles = await db.OrgPoles.AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.BusinessDepartmentId })
            .ToListAsync(ct);
        var cellules = await db.OrgCellules.AsNoTracking()
            .Select(c => new { c.Id, c.Name, c.PoleId })
            .ToListAsync(ct);
        var services = await db.OrgServices.AsNoTracking()
            .Select(s => new { s.Id, s.CelluleId })
            .ToListAsync(ct);

        var deptByPole = poles.ToDictionary(p => p.Id, p => p.BusinessDepartmentId, StringComparer.Ordinal);
        var poleByCellule = cellules.ToDictionary(c => c.Id, c => c.PoleId, StringComparer.Ordinal);
        var celluleByService = services.ToDictionary(s => s.Id, s => s.CelluleId, StringComparer.Ordinal);

        Guid? DeptOfCellule(string celluleId) =>
            poleByCellule.TryGetValue(celluleId, out var p) && deptByPole.TryGetValue(p, out var d) ? d : null;

        var scopedKinds = new[]
        {
            DomainAssignmentKind.ChefDeProjet,
            DomainAssignmentKind.Superviseur,
            DomainAssignmentKind.ReferentTechnique,
        };

        var rows = await (
            from a in db.OrgAssignments.AsNoTracking()
            join e in db.Employees.AsNoTracking() on a.EmployeeId equals e.Id
            where a.EffectiveTo == null && scopedKinds.Contains(a.Kind)
            select new { a.Kind, a.NodeId, e.Id, e.FirstName, e.LastName, e.Email, e.Role })
            .ToListAsync(ct);

        var report = new List<OrgScopeCoverageRowDto>();
        foreach (var group in rows.GroupBy(r => new { r.Id, r.Kind }))
        {
            var first = group.First();
            var name = $"{first.FirstName} {first.LastName}".Trim();

            if (group.Key.Kind == DomainAssignmentKind.ReferentTechnique)
            {
                var byCellule = group
                    .Select(r => r.NodeId)
                    .Distinct(StringComparer.Ordinal)
                    .GroupBy(n => celluleByService.TryGetValue(n, out var c) ? c : string.Empty);
                foreach (var g in byCellule)
                {
                    var total = g.Key.Length == 0 ? 0 : services.Count(s => s.CelluleId == g.Key);
                    var label = cellules.FirstOrDefault(c => c.Id == g.Key)?.Name ?? "(service orphelin)";
                    report.Add(new OrgScopeCoverageRowDto(
                        first.Id.ToString(), name, first.Email, first.Role, group.Key.Kind.ToString(),
                        OrgScopeLevels.Cellule, g.Key, label, g.Count(), total, total > 0 && g.Count() >= total));
                }
                continue;
            }

            var byDept = group
                .Select(r => r.NodeId)
                .Distinct(StringComparer.Ordinal)
                .GroupBy(n => group.Key.Kind == DomainAssignmentKind.ChefDeProjet
                    ? (deptByPole.TryGetValue(n, out var d) ? d : null)
                    : DeptOfCellule(n));
            foreach (var g in byDept)
            {
                int total;
                if (g.Key is null)
                {
                    total = 0;
                }
                else if (group.Key.Kind == DomainAssignmentKind.ChefDeProjet)
                {
                    total = poles.Count(p => p.BusinessDepartmentId == g.Key);
                }
                else
                {
                    total = cellules.Count(c => DeptOfCellule(c.Id) == g.Key);
                }

                var dept = g.Key is null ? null : departments.FirstOrDefault(d => d.Id == g.Key);
                var label = dept is null ? "(sans activité)" : $"{dept.Code} — {dept.Name}";
                report.Add(new OrgScopeCoverageRowDto(
                    first.Id.ToString(), name, first.Email, first.Role, group.Key.Kind.ToString(),
                    OrgScopeLevels.Activite, g.Key?.ToString() ?? string.Empty, label,
                    g.Count(), total, total > 0 && g.Count() >= total));
            }
        }

        return report
            .OrderBy(r => r.Kind)
            .ThenBy(r => r.ScopeLabel)
            .ThenBy(r => r.EmployeeName)
            .ToList();
    }

    public async Task<int> PropagateAfterCelluleCreatedAsync(string celluleId, Guid? changedBy, CancellationToken ct = default)
    {
        if (!PropagationEnabled) return 0;
        var id = (celluleId ?? string.Empty).Trim();
        var cell = await db.OrgCellules.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cell is null) return 0;
        var deptId = await db.OrgPoles.AsNoTracking()
            .Where(p => p.Id == cell.PoleId)
            .Select(p => p.BusinessDepartmentId)
            .FirstOrDefaultAsync(ct);
        if (deptId is null) return 0;

        var others = (await CellulesOfDepartmentAsync(deptId.Value, ct))
            .Where(c => c != id)
            .ToList();
        return await ExtendFullCoverageAsync(DomainAssignmentKind.Superviseur, others, [id], changedBy, ct);
    }

    public async Task<int> PropagateAfterServiceCreatedAsync(string serviceId, Guid? changedBy, CancellationToken ct = default)
    {
        if (!PropagationEnabled) return 0;
        var id = (serviceId ?? string.Empty).Trim();
        var svc = await db.OrgServices.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (svc is null) return 0;

        var others = await db.OrgServices.AsNoTracking()
            .Where(s => s.CelluleId == svc.CelluleId && s.Id != id)
            .Select(s => s.Id)
            .ToListAsync(ct);
        return await ExtendFullCoverageAsync(DomainAssignmentKind.ReferentTechnique, others, [id], changedBy, ct);
    }

    public async Task<int> PropagateAfterPoleAttachedAsync(string poleId, Guid? changedBy, CancellationToken ct = default)
    {
        if (!PropagationEnabled) return 0;
        var id = (poleId ?? string.Empty).Trim();
        var pole = await db.OrgPoles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pole?.BusinessDepartmentId is null) return 0;
        var deptId = pole.BusinessDepartmentId.Value;

        var otherPoles = await db.OrgPoles.AsNoTracking()
            .Where(p => p.BusinessDepartmentId == deptId && p.Id != id)
            .Select(p => p.Id)
            .ToListAsync(ct);
        var extended = await ExtendFullCoverageAsync(DomainAssignmentKind.ChefDeProjet, otherPoles, [id], changedBy, ct);

        var poleCellules = await db.OrgCellules.AsNoTracking()
            .Where(c => c.PoleId == id)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (poleCellules.Count > 0)
        {
            var otherCellules = (await CellulesOfDepartmentAsync(deptId, ct))
                .Where(c => !poleCellules.Contains(c))
                .ToList();
            extended += await ExtendFullCoverageAsync(
                DomainAssignmentKind.Superviseur, otherCellules, poleCellules, changedBy, ct);
        }

        return extended;
    }

    public async Task<int> ExpandAssignmentToScopeAsync(
        string kind,
        Guid employeeId,
        string nodeId,
        Guid? changedBy,
        CancellationToken ct = default)
    {
        if (!PropagationEnabled) return 0;
        if (!Enum.TryParse<DomainAssignmentKind>(kind?.Trim(), true, out var k)
            || k is not (DomainAssignmentKind.ChefDeProjet
                or DomainAssignmentKind.Superviseur
                or DomainAssignmentKind.ReferentTechnique))
        {
            return 0;
        }

        var id = (nodeId ?? string.Empty).Trim();
        List<string> scopeNodes;
        switch (k)
        {
            case DomainAssignmentKind.ChefDeProjet:
            {
                var deptId = await db.OrgPoles.AsNoTracking()
                    .Where(p => p.Id == id).Select(p => p.BusinessDepartmentId).FirstOrDefaultAsync(ct);
                if (deptId is null) return 0;
                scopeNodes = await db.OrgPoles.AsNoTracking()
                    .Where(p => p.BusinessDepartmentId == deptId)
                    .Select(p => p.Id).ToListAsync(ct);
                break;
            }
            case DomainAssignmentKind.Superviseur:
            {
                var poleId = await db.OrgCellules.AsNoTracking()
                    .Where(c => c.Id == id).Select(c => c.PoleId).FirstOrDefaultAsync(ct);
                if (poleId is null) return 0;
                var deptId = await db.OrgPoles.AsNoTracking()
                    .Where(p => p.Id == poleId).Select(p => p.BusinessDepartmentId).FirstOrDefaultAsync(ct);
                if (deptId is null) return 0;
                scopeNodes = await CellulesOfDepartmentAsync(deptId.Value, ct);
                break;
            }
            default:
            {
                var celluleId = await db.OrgServices.AsNoTracking()
                    .Where(s => s.Id == id).Select(s => s.CelluleId).FirstOrDefaultAsync(ct);
                if (celluleId is null) return 0;
                scopeNodes = await db.OrgServices.AsNoTracking()
                    .Where(s => s.CelluleId == celluleId)
                    .Select(s => s.Id).ToListAsync(ct);
                break;
            }
        }

        var current = await db.OrgAssignments.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.Kind == k && a.EffectiveTo == null)
            .OrderBy(a => a.EffectiveFrom)
            .Select(a => a.NodeId)
            .ToListAsync(ct);
        if (!current.Contains(id, StringComparer.Ordinal))
            return 0; // l'affectation d'origine n'a pas abouti : ne rien inférer

        var missing = scopeNodes.Where(n => !current.Contains(n, StringComparer.Ordinal)).ToList();
        if (missing.Count == 0) return 0;

        await write.ReconcileEmployeeStructuralAssignmentsAsync(
            k.ToString(), employeeId, current.Concat(missing).ToList(), id, changedBy, ExpandReason, ct);
        return missing.Count;
    }

    /// <summary>
    /// Étend aux <paramref name="newNodes"/> les titulaires actifs qui détiennent TOUS les
    /// <paramref name="existingNodes"/> (périmètre complet). Ne fait rien si le périmètre
    /// existant est vide (aucune inférence possible).
    /// </summary>
    private async Task<int> ExtendFullCoverageAsync(
        DomainAssignmentKind kind,
        IReadOnlyList<string> existingNodes,
        IReadOnlyList<string> newNodes,
        Guid? changedBy,
        CancellationToken ct)
    {
        if (existingNodes.Count == 0 || newNodes.Count == 0)
            return 0;

        var holders = await db.OrgAssignments.AsNoTracking()
            .Where(a => a.Kind == kind && a.EffectiveTo == null && existingNodes.Contains(a.NodeId))
            .Select(a => new { a.EmployeeId, a.NodeId })
            .ToListAsync(ct);

        var fullHolders = holders
            .GroupBy(h => h.EmployeeId)
            .Where(g => g.Select(x => x.NodeId).Distinct(StringComparer.Ordinal).Count() >= existingNodes.Count)
            .Select(g => g.Key)
            .ToList();

        var extended = 0;
        foreach (var employeeId in fullHolders)
        {
            var current = await db.OrgAssignments.AsNoTracking()
                .Where(a => a.EmployeeId == employeeId && a.Kind == kind && a.EffectiveTo == null)
                .OrderBy(a => a.EffectiveFrom)
                .Select(a => a.NodeId)
                .ToListAsync(ct);
            var desired = current.Concat(newNodes).Distinct(StringComparer.Ordinal).ToList();
            if (desired.Count == current.Distinct(StringComparer.Ordinal).Count())
                continue;

            var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(e => e.Id == employeeId, ct);
            if (employee is null || !employee.IsActive)
                continue;

            var anchor = CurrentAnchor(employee, kind);
            var primary = anchor is not null && current.Contains(anchor, StringComparer.Ordinal)
                ? anchor
                : current[0];

            await write.ReconcileEmployeeStructuralAssignmentsAsync(
                kind.ToString(), employeeId, desired, primary, changedBy, PropagationReason, ct);
            extended++;
        }

        return extended;
    }

    private async Task<Dictionary<string, List<string>>> NodesByActivityAsync(
        DomainAssignmentKind kind,
        IReadOnlyList<string> scopes,
        CancellationToken ct)
    {
        var deptIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in scopes)
        {
            if (!Guid.TryParse(scope, out var g))
                throw new ArgumentException($"Identifiant d'activité invalide : {scope}");
            deptIds[scope] = g;
        }

        var ids = deptIds.Values.ToList();
        var depts = await db.BusinessDepartments.AsNoTracking()
            .Where(d => ids.Contains(d.Id))
            .ToListAsync(ct);

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (scope, deptId) in deptIds)
        {
            var dept = depts.FirstOrDefault(d => d.Id == deptId)
                ?? throw new ArgumentException($"Activité introuvable : {scope}");
            if (dept.Kind != BusinessDepartmentKind.Operational)
                throw new ArgumentException($"« {dept.Name} » n'est pas un département de production.");
            if (!dept.IsActive)
                throw new ArgumentException($"L'activité « {dept.Name} » est inactive.");

            List<string> nodes;
            if (kind == DomainAssignmentKind.ChefDeProjet)
            {
                nodes = await db.OrgPoles.AsNoTracking()
                    .Where(p => p.BusinessDepartmentId == deptId)
                    .OrderBy(p => p.Name).ThenBy(p => p.Id)
                    .Select(p => p.Id)
                    .ToListAsync(ct);
                if (nodes.Count == 0)
                    throw new ArgumentException($"L'activité « {dept.Name} » ne contient encore aucun pôle.");
            }
            else
            {
                nodes = await CellulesOfDepartmentAsync(deptId, ct);
                if (nodes.Count == 0)
                    throw new ArgumentException($"L'activité « {dept.Name} » ne contient encore aucune cellule.");
            }

            result[scope] = nodes;
        }

        return result;
    }

    private async Task<Dictionary<string, List<string>>> ServicesByCelluleAsync(
        IReadOnlyList<string> scopes,
        CancellationToken ct)
    {
        var cellules = await db.OrgCellules.AsNoTracking()
            .Where(c => scopes.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var scope in scopes)
        {
            var cell = cellules.FirstOrDefault(c => string.Equals(c.Id, scope, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Cellule introuvable : {scope}");
            var services = await db.OrgServices.AsNoTracking()
                .Where(s => s.CelluleId == cell.Id)
                .OrderBy(s => s.Name).ThenBy(s => s.Id)
                .Select(s => s.Id)
                .ToListAsync(ct);
            if (services.Count == 0)
                throw new ArgumentException(
                    $"La cellule « {cell.Name} » n'a aucun service : créez son service avant d'y affecter un référent technique.");
            result[scope] = services;
        }

        return result;
    }

    private async Task<List<string>> CellulesOfDepartmentAsync(Guid deptId, CancellationToken ct)
    {
        var poleIds = await db.OrgPoles.AsNoTracking()
            .Where(p => p.BusinessDepartmentId == deptId)
            .Select(p => p.Id)
            .ToListAsync(ct);
        return await db.OrgCellules.AsNoTracking()
            .Where(c => poleIds.Contains(c.PoleId))
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(ct);
    }

    private static DomainAssignmentKind ParseScopedKind(string kind)
    {
        if (!Enum.TryParse<DomainAssignmentKind>(kind?.Trim(), true, out var k)
            || k is not (DomainAssignmentKind.ChefDeProjet
                or DomainAssignmentKind.Superviseur
                or DomainAssignmentKind.ReferentTechnique))
        {
            throw new ArgumentException(
                "Le périmètre est réservé aux kinds ChefDeProjet, Superviseur et ReferentTechnique.");
        }

        return k;
    }

    private static string? CurrentAnchor(Employee employee, DomainAssignmentKind kind) => kind switch
    {
        DomainAssignmentKind.ChefDeProjet => employee.PoleId,
        DomainAssignmentKind.Superviseur => employee.CelluleId,
        DomainAssignmentKind.ReferentTechnique => employee.ServiceId,
        _ => null,
    };
}
