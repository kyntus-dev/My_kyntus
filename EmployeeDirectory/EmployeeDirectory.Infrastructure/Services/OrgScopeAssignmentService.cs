using EmployeeDirectory.Application.Abstractions;
using EmployeeDirectory.Application.Dtos;
using EmployeeDirectory.Domain.Entities;
using EmployeeDirectory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using DomainAssignmentKind = EmployeeDirectory.Domain.Enums.OrgAssignmentKind;

namespace EmployeeDirectory.Infrastructure.Services;

/// <summary>
/// Réorganisation 2026 — responsabilités par périmètre métier, tel que défini dans le fichier
/// « Périmètre » : chaque responsable reçoit une ou plusieurs activités, pôles ou cellules.
/// <list type="bullet">
/// <item>Chef de projet : activité(s) et/ou pôle(s) → affectations sur les pôles ;</item>
/// <item>Superviseur : activité(s), pôle(s) et/ou cellule(s) → affectations sur les cellules ;</item>
/// <item>Référent technique : activité(s), pôle(s) et/ou cellule(s) → affectations sur les services des cellules.</item>
/// </list>
/// Développe chaque périmètre en nœuds existants puis délègue à
/// <see cref="IDirectoryWriteService.ReconcileEmployeeStructuralAssignmentsAsync"/> :
/// mêmes événements outbox, même historique, même exclusivité inter-rôles.
/// </summary>
public sealed class OrgScopeAssignmentService(
    DirectoryDbContext db,
    IDirectoryWriteService write,
    IConfiguration? configuration = null) : IOrgScopeAssignmentService
{
    private const string DefaultReason = "Affectation par périmètre (activité / pôle / cellule)";
    private const string ExpandReason = "Extension aux services de la cellule (référent technique)";
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

        var (nodesByScope, levels) = await ResolveScopeNodesAsync(assignmentKind, scopes, ct);

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

        var distinctLevels = levels.Values.Distinct().ToList();
        return new OrgScopeReconcileResult(
            assignmentKind.ToString(),
            employeeId.ToString(),
            distinctLevels.Count == 1 ? distinctLevels[0] : OrgScopeLevels.Mixte,
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

        // Superviseurs qui couvrent toute l'activité, puis ceux qui couvrent tout le pôle de la cellule.
        var others = (await CellulesOfDepartmentAsync(deptId.Value, ct))
            .Where(c => c != id)
            .ToList();
        var extended = await ExtendFullCoverageAsync(DomainAssignmentKind.Superviseur, others, [id], changedBy, ct);
        var poleOthers = await db.OrgCellules.AsNoTracking()
            .Where(c => c.PoleId == cell.PoleId && c.Id != id)
            .Select(c => c.Id)
            .ToListAsync(ct);
        extended += await ExtendFullCoverageAsync(DomainAssignmentKind.Superviseur, poleOthers, [id], changedBy, ct);
        return extended;
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

        // Périmètre exact du fichier Périmètre : un superviseur ou un chef de projet affecté à une
        // cellule / un pôle garde ce périmètre (pas d'extension à l'activité entière).
        // Seul le référent technique est étendu aux services de sa cellule (miroirs techniques).
        if (k != DomainAssignmentKind.ReferentTechnique)
            return 0;

        var id = (nodeId ?? string.Empty).Trim();
        var celluleId = await db.OrgServices.AsNoTracking()
            .Where(s => s.Id == id).Select(s => s.CelluleId).FirstOrDefaultAsync(ct);
        if (celluleId is null) return 0;
        var scopeNodes = await db.OrgServices.AsNoTracking()
            .Where(s => s.CelluleId == celluleId)
            .Select(s => s.Id).ToListAsync(ct);

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

    /// <summary>
    /// Développe chaque périmètre (activité, pôle ou cellule) en nœuds d'affectation du rôle :
    /// pôles (chef de projet), cellules (superviseur) ou services (référent technique).
    /// </summary>
    private async Task<(Dictionary<string, List<string>> Nodes, Dictionary<string, string> Levels)> ResolveScopeNodesAsync(
        DomainAssignmentKind kind,
        IReadOnlyList<string> scopes,
        CancellationToken ct)
    {
        var nodes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var levels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var scope in scopes)
        {
            // 1) Activité (département de production)
            if (Guid.TryParse(scope, out var deptId)
                && await db.BusinessDepartments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deptId, ct) is { } dept)
            {
                if (dept.Kind != BusinessDepartmentKind.Operational)
                    throw new ArgumentException($"« {dept.Name} » n'est pas un département de production.");
                if (!dept.IsActive)
                    throw new ArgumentException($"L'activité « {dept.Name} » est inactive.");
                var poleIds = await db.OrgPoles.AsNoTracking()
                    .Where(p => p.BusinessDepartmentId == deptId)
                    .OrderBy(p => p.Name).ThenBy(p => p.Id)
                    .Select(p => p.Id)
                    .ToListAsync(ct);
                nodes[scope] = await NodesOfPolesAsync(kind, poleIds, $"L'activité « {dept.Name} »", ct);
                levels[scope] = OrgScopeLevels.Activite;
                continue;
            }

            // 2) Pôle
            if (await db.OrgPoles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == scope, ct) is { } pole)
            {
                nodes[scope] = await NodesOfPolesAsync(kind, [pole.Id], $"Le pôle « {pole.Name} »", ct);
                levels[scope] = OrgScopeLevels.Pole;
                continue;
            }

            // 3) Cellule
            if (await db.OrgCellules.AsNoTracking().FirstOrDefaultAsync(c => c.Id == scope, ct) is { } cell)
            {
                if (kind == DomainAssignmentKind.ChefDeProjet)
                    throw new ArgumentException(
                        $"Un chef de projet est rattaché à une activité ou à des pôles, pas à la cellule « {cell.Name} ».");
                nodes[scope] = kind == DomainAssignmentKind.Superviseur
                    ? new List<string> { cell.Id }
                    : await ServicesOfCellulesAsync([cell.Id], requireEach: true, ct);
                levels[scope] = OrgScopeLevels.Cellule;
                continue;
            }

            throw new ArgumentException($"Périmètre introuvable : {scope}");
        }

        return (nodes, levels);
    }

    private async Task<List<string>> NodesOfPolesAsync(
        DomainAssignmentKind kind,
        IReadOnlyList<string> poleIds,
        string label,
        CancellationToken ct)
    {
        if (kind == DomainAssignmentKind.ChefDeProjet)
        {
            if (poleIds.Count == 0)
                throw new ArgumentException($"{label} ne contient encore aucun pôle.");
            return poleIds.ToList();
        }

        var celluleIds = await db.OrgCellules.AsNoTracking()
            .Where(c => poleIds.Contains(c.PoleId))
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Select(c => c.Id)
            .ToListAsync(ct);
        if (celluleIds.Count == 0)
            throw new ArgumentException($"{label} ne contient encore aucune cellule.");
        if (kind == DomainAssignmentKind.Superviseur)
            return celluleIds;

        var services = await ServicesOfCellulesAsync(celluleIds, requireEach: false, ct);
        if (services.Count == 0)
            throw new ArgumentException($"{label} ne contient aucun service : créez les services de ses cellules.");
        return services;
    }

    private async Task<List<string>> ServicesOfCellulesAsync(
        IReadOnlyList<string> celluleIds,
        bool requireEach,
        CancellationToken ct)
    {
        var result = new List<string>();
        foreach (var celluleId in celluleIds)
        {
            var services = await db.OrgServices.AsNoTracking()
                .Where(s => s.CelluleId == celluleId)
                .OrderBy(s => s.Name).ThenBy(s => s.Id)
                .Select(s => s.Id)
                .ToListAsync(ct);
            if (services.Count == 0 && requireEach)
            {
                var name = await db.OrgCellules.AsNoTracking()
                    .Where(c => c.Id == celluleId).Select(c => c.Name).FirstOrDefaultAsync(ct) ?? celluleId;
                throw new ArgumentException(
                    $"La cellule « {name} » n'a aucun service : créez son service avant d'y affecter un référent technique.");
            }
            result.AddRange(services);
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
