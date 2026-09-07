using Kyntus.Caching;
using Kyntus.Iam;
using Microsoft.EntityFrameworkCore;
using Prime.Infrastructure.Persistence;
using Prime.Application.DTOs;
using Prime.Domain.Entities;

namespace Prime.Infrastructure.Services;

/// <summary>
/// Périmètre organisationnel et hiérarchie issus de PostgreSQL,
/// enrichi par les nœuds managés Directory (ReBAC) pour la multi-responsabilité.
/// </summary>
public sealed class PrimeOrgScopeService(PrimeDbContext? db, IRebacClient? rebac = null, ICacheService? cache = null)
{
    public bool IsDatabaseAvailable => db != null;

    public async Task<string?> GetCelluleIdForServiceAsync(string serviceId, CancellationToken ct = default)
    {
        if (db == null) return null;
        var sid = serviceId.Trim();
        return await db.Services.AsNoTracking()
            .Where(s => s.Id == sid)
            .Select(s => s.CelluleId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> SupervisorOwnsCelluleAsync(string supervisorUserId, string celluleId, CancellationToken ct = default)
    {
        if (db == null) return false;
        var supervised = await GetSupervisedCelluleIdsAsync(supervisorUserId, ct);
        return supervised.Contains(celluleId.Trim());
    }

    /// <summary>
    /// Certains clients envoient l’identifiant du <b>pôle</b> (ex. <c>d1</c>) là où l’API attend la <b>cellule</b> (ex. <c>p1</c>).
    /// Renvoie l’id cellule canonique pour un superviseur, ou <c>null</c> si le couple (superviseur, clé) est invalide.
    /// </summary>
    public async Task<string?> NormalizeSupervisorDraftCelluleKeyAsync(
        string supervisorUserId,
        string celluleIdOrLegacyPoleId,
        CancellationToken ct = default)
    {
        if (db == null) return null;
        var emp = await GetEmployeeAsync(supervisorUserId, ct);
        if (emp is null || !string.Equals(emp.Role, "Superviseur", StringComparison.OrdinalIgnoreCase))
            return null;
        var key = celluleIdOrLegacyPoleId.Trim();
        if (string.IsNullOrEmpty(key)) return null;

        var supervised = await GetSupervisedCelluleIdsAsync(supervisorUserId, ct);
        if (supervised.Contains(key))
            return key;

        var underPole = await db.Cellules.AsNoTracking()
            .Where(c => c.PoleId == key && supervised.Contains(c.Id))
            .Select(c => c.Id)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(underPole))
            return underPole;

        if (string.Equals(emp.CelluleId, key, StringComparison.Ordinal))
            return emp.CelluleId;
        if (string.Equals(emp.PoleId, key, StringComparison.Ordinal))
            return emp.CelluleId;
        return null;
    }

    /// <summary>
    /// Cellules du périmètre superviseur : nœuds managés Directory (ReBAC) + repli
    /// sur <c>emp.CelluleId</c> / <c>emp.PoleId</c> si ReBAC est vide ou injoignable.
    /// Les nœuds pôle sont développés en cellules.
    /// </summary>
    public async Task<HashSet<string>> GetSupervisedCelluleIdsAsync(string supervisorUserId, CancellationToken ct = default)
    {
        if (db == null) return new HashSet<string>(StringComparer.Ordinal);
        var u = supervisorUserId.Trim();

        if (cache is not null)
        {
            var ids = await cache.GetOrCreateAsync(
                KyntusCacheKeys.PrimeSupervisedCellules(u),
                token => LoadSupervisedCelluleIdsAsync(u, token),
                KyntusCacheTtl.Entity,
                ct);
            return ids.ToHashSet(StringComparer.Ordinal);
        }

        var loaded = await LoadSupervisedCelluleIdsAsync(u, ct);
        return loaded.ToHashSet(StringComparer.Ordinal);
    }

    private async Task<List<string>> LoadSupervisedCelluleIdsAsync(string u, CancellationToken ct)
    {
        var emp = await GetEmployeeAsync(u, ct);
        if (emp is null) return [];
        if (!string.Equals(emp.Role?.Trim(), "Superviseur", StringComparison.OrdinalIgnoreCase))
            return [];

        var nodeIds = new List<string>();
        var fromRebac = await TryGetManagedNodeIdsAsync(u, "Superviseur", ct);
        foreach (var n in fromRebac)
        {
            var t = (n ?? "").Trim();
            if (t.Length > 0) nodeIds.Add(t);
        }

        if (!string.IsNullOrWhiteSpace(emp.CelluleId))
            nodeIds.Add(emp.CelluleId.Trim());
        if (nodeIds.Count == 0 && !string.IsNullOrWhiteSpace(emp.PoleId))
            nodeIds.Add(emp.PoleId.Trim());

        if (nodeIds.Count == 0)
            return [];

        var distinct = nodeIds.Distinct(StringComparer.Ordinal).ToList();
        var set = await ExpandRebacNodesToCelluleIdsAsync(distinct, ct);
        if (set.Count > 0)
            return set.ToList();

        if (!string.IsNullOrWhiteSpace(emp.CelluleId))
            return [emp.CelluleId.Trim()];
        return [];
    }

    public async Task<IReadOnlyList<string>> GetManagedPoleIdsAsync(string chefUserId, CancellationToken ct = default)
    {
        var nodeIds = await TryGetManagedNodeIdsAsync(chefUserId, "ChefDeProjet", ct);
        var poles = await ExpandRebacNodesToPoleIdsAsync(nodeIds, ct);

        if (db == null) return poles.ToList();

        var emp = await GetEmployeeAsync(chefUserId, ct);
        if (emp is not null && IsChefDeProjetRole(emp.Role) && !string.IsNullOrWhiteSpace(emp.PoleId))
            poles.Add(emp.PoleId.Trim());

        return poles.ToList();
    }

    public async Task<IReadOnlyList<string>> GetManagedServiceIdsAsync(string referentUserId, CancellationToken ct = default)
    {
        var merged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var nodeIds = await TryGetManagedNodeIdsAsync(referentUserId, "ReferentTechnique", ct);
        foreach (var id in await ExpandRebacNodesToServiceIdsAsync(nodeIds, ct))
            merged.Add(id);

        if (merged.Count == 0)
        {
            var coachNodes = await TryGetManagedNodeIdsAsync(referentUserId, "Coach", ct);
            foreach (var id in await ExpandRebacNodesToServiceIdsAsync(coachNodes, ct))
                merged.Add(id);
        }

        if (db == null)
            return merged.ToList();

        var emp = await GetEmployeeAsync(referentUserId, ct);
        if (emp is not null && IsReferentTechniqueRole(emp.Role))
        {
            if (!string.IsNullOrWhiteSpace(emp.ServiceId))
                merged.Add(emp.ServiceId.Trim());
            else if (!string.IsNullOrWhiteSpace(emp.CelluleId))
            {
                var svcIds = await db.Services.AsNoTracking()
                    .Where(s => s.CelluleId == emp.CelluleId.Trim())
                    .Select(s => s.Id)
                    .ToListAsync(ct);
                foreach (var id in svcIds) merged.Add(id);
            }
        }

        return merged.ToList();
    }

    /// <summary>
    /// Périmètre validation RT sur une fiche : services gérés (ReBAC + repli scalar),
    /// indépendamment de la date de création de la fiche ou du lien pilote historique.
    /// </summary>
    public async Task<bool> IsFicheUnderReferentResponsibilityAsync(
        string referentUserId,
        EmployeePrimeServiceFiche fiche,
        string? actingReferentRole = null,
        CancellationToken ct = default)
    {
        var referent = await GetEmployeeAsync(referentUserId, ct);
        if (referent is null) return false;
        var roleForScope = string.IsNullOrWhiteSpace(actingReferentRole)
            ? referent.Role
            : actingReferentRole.Trim();
        if (!IsReferentTechniqueRole(roleForScope)) return false;

        var ficheServiceId = fiche.ServiceId?.Trim() ?? "";
        if (ficheServiceId.Length > 0)
        {
            var managedServices = await GetManagedServiceIdsAsync(referentUserId, ct);
            if (managedServices.Contains(ficheServiceId, StringComparer.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(referent.ServiceId) &&
                string.Equals(ficheServiceId, referent.ServiceId.Trim(), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(referent.CelluleId) &&
            !string.IsNullOrWhiteSpace(fiche.CelluleId) &&
            string.Equals(fiche.CelluleId.Trim(), referent.CelluleId.Trim(), StringComparison.OrdinalIgnoreCase))
            return true;

        return await IsPiloteUnderReferentResponsibilityAsync(
            referentUserId,
            fiche.EmployeeId,
            actingReferentRole,
            ct);
    }

    /// <summary>
    /// Périmètre validation Superviseur : cellules supervisées (ReBAC + repli scalar),
    /// indépendamment de la date de création de la fiche.
    /// </summary>
    public async Task<bool> IsFicheUnderSupervisorResponsibilityAsync(
        string supervisorUserId,
        EmployeePrimeServiceFiche fiche,
        string? actingSupervisorRole = null,
        CancellationToken ct = default)
    {
        var supervisor = await GetEmployeeAsync(supervisorUserId, ct);
        if (supervisor is null) return false;
        var roleForScope = string.IsNullOrWhiteSpace(actingSupervisorRole)
            ? supervisor.Role
            : actingSupervisorRole.Trim();
        if (!IsSuperviseurRole(roleForScope)) return false;

        var ficheCelluleId = fiche.CelluleId?.Trim() ?? "";
        if (ficheCelluleId.Length == 0) return false;

        var supervised = await GetSupervisedCelluleIdsAsync(supervisorUserId, ct);
        if (supervised.Contains(ficheCelluleId))
            return true;

        return !string.IsNullOrWhiteSpace(supervisor.CelluleId) &&
               string.Equals(ficheCelluleId, supervisor.CelluleId.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Périmètre validation Chef de projet : pôles managés (ReBAC + repli <c>PoleId</c>),
    /// indépendamment de la date de création de la fiche.
    /// </summary>
    public async Task<bool> IsFicheUnderChefResponsibilityAsync(
        string chefUserId,
        EmployeePrimeServiceFiche fiche,
        string? actingChefRole = null,
        CancellationToken ct = default)
    {
        if (db == null) return false;
        var chef = await GetEmployeeAsync(chefUserId, ct);
        if (chef is null) return false;
        var roleForScope = string.IsNullOrWhiteSpace(actingChefRole)
            ? chef.Role
            : actingChefRole.Trim();
        if (!IsChefDeProjetRole(roleForScope)) return false;

        var ficheCelluleId = fiche.CelluleId?.Trim() ?? "";
        var fichePoleId = string.IsNullOrWhiteSpace(ficheCelluleId)
            ? null
            : await db.Cellules.AsNoTracking()
                .Where(c => c.Id == ficheCelluleId)
                .Select(c => c.PoleId)
                .FirstOrDefaultAsync(ct);

        if (string.IsNullOrWhiteSpace(fichePoleId))
            return false;

        var managedPoles = await GetManagedPoleIdsAsync(chefUserId, ct);
        if (managedPoles.Contains(fichePoleId.Trim(), StringComparer.Ordinal))
            return true;

        return !string.IsNullOrWhiteSpace(chef.PoleId) &&
               string.Equals(fichePoleId.Trim(), chef.PoleId.Trim(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Interprète les nœuds ReBAC comme pôles, cellules ou services ; développe en ids pôle.
    /// </summary>
    private async Task<HashSet<string>> ExpandRebacNodesToPoleIdsAsync(
        IReadOnlyList<string> nodeIds,
        CancellationToken ct)
    {
        if (db == null || nodeIds.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var nodeSet = nodeIds
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var asPoles = await db.Poles.AsNoTracking()
            .Where(p => nodeSet.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);

        var fromCellules = await db.Cellules.AsNoTracking()
            .Where(c => nodeSet.Contains(c.Id))
            .Select(c => c.PoleId)
            .ToListAsync(ct);

        var fromServices = await db.Services.AsNoTracking()
            .Where(s => nodeSet.Contains(s.Id))
            .Join(db.Cellules.AsNoTracking(), s => s.CelluleId, c => c.Id, (_, c) => c.PoleId)
            .ToListAsync(ct);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in asPoles)
        {
            if (!string.IsNullOrWhiteSpace(id)) result.Add(id);
        }
        foreach (var id in fromCellules)
        {
            if (!string.IsNullOrWhiteSpace(id)) result.Add(id);
        }
        foreach (var id in fromServices)
        {
            if (!string.IsNullOrWhiteSpace(id)) result.Add(id);
        }
        return result;
    }

    /// <summary>
    /// Interprète les nœuds ReBAC comme services, cellules ou pôles ; développe en ids service feuille.
    /// </summary>
    private async Task<HashSet<string>> ExpandRebacNodesToServiceIdsAsync(
        IReadOnlyList<string> nodeIds,
        CancellationToken ct)
    {
        if (db == null || nodeIds.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var nodeSet = nodeIds
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var asServices = await db.Services.AsNoTracking()
            .Where(s => nodeSet.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(ct);

        var asCellules = await db.Cellules.AsNoTracking()
            .Where(c => nodeSet.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);

        var fromCellules = asCellules.Count == 0
            ? []
            : await db.Services.AsNoTracking()
                .Where(s => asCellules.Contains(s.CelluleId))
                .Select(s => s.Id)
                .ToListAsync(ct);

        var poleNodes = await db.Poles.AsNoTracking()
            .Where(p => nodeSet.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);

        var fromPoles = poleNodes.Count == 0
            ? []
            : await db.Services.AsNoTracking()
                .Where(s => db.Cellules.Any(c => c.Id == s.CelluleId && poleNodes.Contains(c.PoleId)))
                .Select(s => s.Id)
                .ToListAsync(ct);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in asServices) result.Add(id);
        foreach (var id in fromCellules) result.Add(id);
        foreach (var id in fromPoles) result.Add(id);
        return result;
    }

    /// <summary>
    /// Interprète les nœuds ReBAC comme cellules et/ou pôles ; développe les pôles en cellules.
    /// </summary>
    private async Task<HashSet<string>> ExpandRebacNodesToCelluleIdsAsync(
        IReadOnlyList<string> nodeIds,
        CancellationToken ct)
    {
        if (db == null || nodeIds.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        var nodeSet = nodeIds
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var asCellules = await db.Cellules.AsNoTracking()
            .Where(c => nodeSet.Contains(c.Id))
            .Select(c => c.Id)
            .ToListAsync(ct);

        var poleNodes = await db.Poles.AsNoTracking()
            .Where(p => nodeSet.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync(ct);

        var fromPoles = poleNodes.Count == 0
            ? []
            : await db.Cellules.AsNoTracking()
                .Where(c => poleNodes.Contains(c.PoleId))
                .Select(c => c.Id)
                .ToListAsync(ct);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in asCellules) result.Add(id);
        foreach (var id in fromPoles) result.Add(id);
        return result;
    }

    private async Task<IReadOnlyList<string>> TryGetManagedNodeIdsAsync(
        string employeeId,
        string kind,
        CancellationToken ct)
    {
        if (rebac is null || !Guid.TryParse(employeeId, out var guid))
            return [];
        try
        {
            return await rebac.GetManagedNodeIdsAsync(guid, kind, ct);
        }
        catch
        {
            return [];
        }
    }

    public static bool IsPilotRole(string? role) =>
        string.Equals(role?.Trim(), "Pilote", StringComparison.OrdinalIgnoreCase);

    public static bool IsReferentTechniqueRole(string? role)
    {
        var r = role?.Trim() ?? "";
        return string.Equals(r, "Référent technique", StringComparison.Ordinal) ||
               string.Equals(r, "Coach", StringComparison.Ordinal);
    }

    public static bool IsSuperviseurRole(string? role) =>
        string.Equals(role?.Trim(), "Superviseur", StringComparison.OrdinalIgnoreCase);

    public static bool IsChefDeProjetRole(string? role)
    {
        var r = role?.Trim() ?? "";
        return string.Equals(r, "Chef de projet", StringComparison.Ordinal) ||
               string.Equals(r, "RP", StringComparison.Ordinal) ||
               string.Equals(r, "ChefDeProjet", StringComparison.Ordinal);
    }

    /// <summary>
    /// Périmètre validation RT : ReBAC <c>CanActOn</c>, sinon pilote direct (ParentId)
    /// ou pilote d’un RT du même superviseur / même cellule.
    /// </summary>
    public async Task<bool> IsPiloteUnderReferentResponsibilityAsync(
        string referentUserId,
        string pilotEmployeeId,
        string? actingReferentRole = null,
        CancellationToken ct = default)
    {
        var pilote = await GetEmployeeAsync(pilotEmployeeId, ct);
        if (pilote is null || !IsPilotRole(pilote.Role))
            return false;

        if (!string.IsNullOrWhiteSpace(pilote.ReferentTechniqueId) &&
            string.Equals(pilote.ReferentTechniqueId.Trim(), referentUserId, StringComparison.Ordinal))
            return true;

        var canAct = await TryCanActOnAsync(referentUserId, pilotEmployeeId, ct);
        if (canAct == true)
            return true;

        return await IsPilotInReferentValidationScopeAsync(referentUserId, pilotEmployeeId, actingReferentRole, ct);
    }

    private async Task<bool?> TryCanActOnAsync(string actorId, string targetEmployeeId, CancellationToken ct)
    {
        if (rebac is null
            || !Guid.TryParse(actorId, out var actor)
            || !Guid.TryParse(targetEmployeeId, out var target))
            return null;
        try
        {
            return await rebac.CanActOnAsync(actor, target, ct);
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsPilotInReferentValidationScopeAsync(
        string referentUserId,
        string pilotEmployeeId,
        string? actingReferentRole = null,
        CancellationToken ct = default)
    {
        if (db == null) return false;
        var referent = await GetEmployeeAsync(referentUserId, ct);
        var pilote = await GetEmployeeAsync(pilotEmployeeId, ct);
        if (referent is null || pilote is null || !IsPilotRole(pilote.Role)) return false;
        var roleForScope = string.IsNullOrWhiteSpace(actingReferentRole)
            ? referent.Role
            : actingReferentRole.Trim();
        if (!IsReferentTechniqueRole(roleForScope)) return false;

        if (string.Equals(pilote.ParentId, referent.Id, StringComparison.Ordinal))
            return true;

        if (!string.IsNullOrWhiteSpace(referent.ParentId))
        {
            var supervisorId = referent.ParentId.Trim();
            var referentIds = await db.Employees.AsNoTracking()
                .Where(e =>
                    e.ParentId == supervisorId &&
                    (e.Role == "Référent technique" || e.Role == "Coach"))
                .Select(e => e.Id)
                .ToListAsync(ct);
            referentIds.Add(referent.Id);
            if (!string.IsNullOrWhiteSpace(pilote.ParentId) &&
                referentIds.Contains(pilote.ParentId, StringComparer.Ordinal))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(referent.CelluleId) &&
            string.Equals(pilote.CelluleId, referent.CelluleId, StringComparison.Ordinal))
            return true;

        var managedServices = await GetManagedServiceIdsAsync(referentUserId, ct);
        if (managedServices.Count > 0
            && !string.IsNullOrWhiteSpace(pilote.ServiceId)
            && managedServices.Contains(pilote.ServiceId.Trim(), StringComparer.Ordinal))
            return true;

        if (!string.IsNullOrWhiteSpace(referent.ServiceId) &&
            string.Equals(pilote.ServiceId, referent.ServiceId, StringComparison.Ordinal))
            return true;

        return false;
    }

    public async Task<List<EmployeeEntity>> GetEmployeesInServiceAsync(string serviceId, CancellationToken ct = default)
    {
        if (db == null) return [];
        var sid = serviceId.Trim();
        return await db.Employees.AsNoTracking()
            .Where(e => e.ServiceId == sid)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(ct);
    }

    /// <summary>Employés en rôle Pilote pour un service (fiches PRIME partie personnalisée).</summary>
    public async Task<List<EmployeeEntity>> GetPilotsInServiceAsync(string serviceId, CancellationToken ct = default)
    {
        if (db == null) return [];
        var sid = serviceId.Trim();
        return await db.Employees.AsNoTracking()
            .Where(e => e.ServiceId == sid && e.Role == "Pilote")
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(ct);
    }

    public async Task<List<EmployeeEntity>> GetPilotsInCelluleAsync(string celluleId, CancellationToken ct = default)
    {
        if (db == null) return [];
        var cid = celluleId.Trim();
        var serviceIds = await db.Services.AsNoTracking()
            .Where(s => s.CelluleId == cid)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (serviceIds.Count == 0) return [];
        return await db.Employees.AsNoTracking()
            .Where(e => serviceIds.Contains(e.ServiceId) && e.Role == "Pilote")
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(ct);
    }

    public async Task<EmployeeEntity?> GetEmployeeAsync(string employeeId, CancellationToken ct = default)
    {
        if (db == null) return null;
        return await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == employeeId.Trim(), ct);
    }

    public async Task<List<(string ServiceId, string ServiceName, string CelluleId)>> GetServicesForCellulesAsync(
        IEnumerable<string> celluleIds,
        CancellationToken ct = default)
    {
        if (db == null) return [];
        var set = celluleIds.Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        return await db.Services.AsNoTracking()
            .Where(s => set.Contains(s.CelluleId))
            .OrderBy(s => s.CelluleId)
            .ThenBy(s => s.Name)
            .Select(s => new ValueTuple<string, string, string>(s.Id, s.Name, s.CelluleId))
            .ToListAsync(ct);
    }

    public async Task<int> CountEmployeesForCelluleAsync(string celluleId, CancellationToken ct = default)
    {
        if (db == null) return 0;
        var cid = celluleId.Trim();
        var serviceIds = await db.Services.AsNoTracking()
            .Where(s => s.CelluleId == cid)
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (serviceIds.Count == 0) return 0;
        return await db.Employees.CountAsync(e => serviceIds.Contains(e.ServiceId) && e.Role == "Pilote", ct);
    }

    public async Task<Dictionary<string, int>> GetEmployeeCountsByCelluleAsync(
        IEnumerable<string> celluleIds,
        CancellationToken ct = default)
    {
        var dict = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in celluleIds.Distinct(StringComparer.Ordinal))
        {
            var id = raw.Trim();
            dict[id] = await CountEmployeesForCelluleAsync(id, ct);
        }
        return dict;
    }

    /// <summary>Même arbre « Department → Pole → Cellule → Team » que <see cref="PrimeInMemoryStore.HydrateOrganizationFromDatabase"/> pour compat JSON Angular (legacy 3 niveaux — préférer <see cref="GetOperationalOrgTreeAsync"/>).</summary>
    public async Task<List<Department>> GetLegacyDepartmentTreeAsync(CancellationToken ct = default)
    {
        if (db == null) return [];
        var rows = await db.Poles
            .AsNoTracking()
            .Include(d => d.Cellules)
            .ThenInclude(p => p.Services)
            .OrderBy(d => d.Id)
            .ToListAsync(ct);
        var list = new List<Department>();
        foreach (var d in rows)
        {
            list.Add(new Department
            {
                Id = d.Id,
                Name = d.Name,
                Poles = d.Cellules.OrderBy(p => p.Id).Select(p => new Pole
                {
                    Id = p.Id,
                    Name = p.Name,
                    PoleId = d.Id,
                    Cellules = p.Services.OrderBy(s => s.Id).Select(s => new Cellule
                    {
                        Id = s.Id,
                        Name = s.Name,
                        CelluleId = p.Id,
                        Services =
                        [
                            new Team
                            {
                                Id = s.Id + "-team",
                                Name = s.Name,
                                CelluleId = p.Id,
                                ServiceId = s.Id,
                            },
                        ],
                    }).ToList(),
                }).ToList(),
            });
        }
        return list;
    }

    /// <summary>Arbre opérationnel 4 niveaux : département → pôle → cellule → service.</summary>
    public async Task<OperationalOrgTreeDto> GetOperationalOrgTreeAsync(CancellationToken ct = default)
    {
        if (db == null)
            return new OperationalOrgTreeDto([], []);

        var poles = await db.Poles.AsNoTracking()
            .Include(p => p.Cellules)
            .ThenInclude(c => c.Services)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        var operationalDepts = await db.BusinessDepartments.AsNoTracking()
            .Include(d => d.PoleAssignments)
            .Where(d => d.IsActive && d.Kind == "Operational")
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        var poleById = poles.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var assignedPoleIds = operationalDepts
            .SelectMany(d => d.PoleAssignments.Select(a => a.PoleId))
            .ToHashSet(StringComparer.Ordinal);

        static OrgPoleOverviewDto MapPole(PoleEntity p) => new(
            p.Id,
            p.Name,
            p.Cellules.OrderBy(c => c.Name).Select(c => new OrgCelluleOverviewDto(
                c.Id,
                c.Name,
                c.Services.OrderBy(s => s.Name).Select(s => new OrgServiceOverviewDto(s.Id, s.Name)).ToList())).ToList());

        var operationalDepartments = operationalDepts.Select(d => new OperationalDepartmentOverviewDto(
            d.Id,
            d.Code,
            d.Name,
            d.ManagerEmployeeId,
            d.PoleAssignments
                .Select(a => a.PoleId)
                .Where(poleById.ContainsKey)
                .Select(id => MapPole(poleById[id]))
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList())).ToList();

        var unassignedPoles = poles
            .Where(p => !assignedPoleIds.Contains(p.Id))
            .Select(MapPole)
            .ToList();

        return new OperationalOrgTreeDto(operationalDepartments, unassignedPoles);
    }

    public async Task<List<OperationalDepartmentOverviewDto>> GetOperationalDepartmentTreeAsync(CancellationToken ct = default)
    {
        var tree = await GetOperationalOrgTreeAsync(ct);
        return tree.OperationalDepartments.ToList();
    }

    public async Task<List<Employee>> GetLegacyEmployeesAsync(CancellationToken ct = default)
    {
        if (db == null) return [];
        var rows = await db.Employees.AsNoTracking()
            .OrderBy(e => e.Id)
            .ToListAsync(ct);
        return rows.Select(e => new Employee
        {
            Id = e.Id,
            FirstName = e.FirstName,
            LastName = e.LastName,
            Role = e.Role,
            ParentId = e.ParentId,
            PoleId = e.PoleId,
            CelluleId = e.CelluleId,
            ServiceId = e.ServiceId,
            Email = e.Email,
            Avatar = e.Avatar,
            BusinessDepartmentId = e.BusinessDepartmentId,
            BusinessDepartmentKind = e.BusinessDepartmentKind,
        }).ToList();
    }

    public async Task<List<PrimeResult>> GetPrimeResultsFromFichesAsync(int take, CancellationToken ct = default)
    {
        if (db == null) return [];
        var fiches = await db.EmployeePrimeServiceFiches.AsNoTracking()
            .OrderByDescending(f => f.UpdatedAt)
            .Take(take)
            .ToListAsync(ct);
        return fiches.Select(f => new PrimeResult
        {
            Id = f.Id.ToString("N"),
            EmployeeId = f.EmployeeId,
            PrimeTypeId = "fiche-service",
            Score = (int)Math.Clamp(f.TotalAmount ?? f.PrimeAmount ?? 0, 0, int.MaxValue),
            Amount = (int)Math.Clamp(f.PrimeAmount ?? 0, 0, int.MaxValue),
            Status = f.ValidationStatus,
            Period = f.Period,
            ApprovedBy = f.LastApproverUserId,
            Date = f.UpdatedAt.ToString("yyyy-MM-dd"),
        }).ToList();
    }

    /// <summary>Statistiques tableau de bord (contrat Angular <c>prime-dashboard-standard</c>).</summary>
    public async Task<object> BuildDashboardStatsAsync(CancellationToken ct = default)
    {
        if (db == null)
        {
            return new
            {
                totalPrimesThisMonth = 0,
                budgetConsumption = 0,
                topTeams = new[] { new { name = "(aucune donnée)", amount = 0 } },
                topEmployees = new[] { new { name = "-", amount = 0 } },
                primeByDepartment = Array.Empty<object>(),
                primeEvolution = Array.Empty<object>(),
            };
        }

        var now = DateTimeOffset.UtcNow;
        var ym = $"{now:yyyy-MM}";
        var fichesMonth = await db.EmployeePrimeServiceFiches.AsNoTracking()
            .Where(f => f.Period == ym)
            .ToListAsync(ct);
        var totalAmount = (int)Math.Round(fichesMonth.Sum(f => (double)(f.TotalAmount ?? 0)));

        var byService = await (
            from f in db.EmployeePrimeServiceFiches.AsNoTracking()
            join s in db.Services.AsNoTracking() on f.ServiceId equals s.Id
            group f by new { s.Id, s.Name } into g
            select new { g.Key.Name, Sum = g.Sum(x => x.TotalAmount ?? 0) }
        ).OrderByDescending(x => x.Sum).Take(5).ToListAsync(ct);

        var byEmployee = await (
            from f in db.EmployeePrimeServiceFiches.AsNoTracking()
            join e in db.Employees.AsNoTracking() on f.EmployeeId equals e.Id
            group f by new { e.Id, e.FirstName, e.LastName } into g
            select new { Name = g.Key.FirstName + " " + g.Key.LastName, Sum = g.Sum(x => x.TotalAmount ?? 0) }
        ).OrderByDescending(x => x.Sum).Take(5).ToListAsync(ct);

        var byPole = await (
            from f in db.EmployeePrimeServiceFiches.AsNoTracking()
            join e in db.Employees.AsNoTracking() on f.EmployeeId equals e.Id
            join p in db.Poles.AsNoTracking() on e.PoleId equals p.Id
            group f by p.Name into g
            select new { name = g.Key, value = g.Sum(x => x.TotalAmount ?? 0) }
        ).ToListAsync(ct);

        var evolution = await db.EmployeePrimeServiceFiches.AsNoTracking()
            .GroupBy(f => f.Period)
            .Select(g => new { month = g.Key, amount = g.Sum(x => x.TotalAmount ?? 0) })
            .OrderBy(x => x.month)
            .Take(12)
            .ToListAsync(ct);

        var budgetCap = Math.Max(totalAmount, 1) * 2;
        var budgetPct = Math.Min(100, (int)Math.Round(100.0 * totalAmount / budgetCap));

        var topTeams = byService.Select(x => new { name = x.Name, amount = (int)Math.Round((double)x.Sum) }).ToList();
        if (topTeams.Count == 0)
            topTeams.Add(new { name = "(aucune donnée)", amount = 0 });

        var topEmployees = byEmployee.Select(x => new { name = x.Name, amount = (int)Math.Round((double)x.Sum) }).ToList();
        if (topEmployees.Count == 0)
            topEmployees.Add(new { name = "-", amount = 0 });

        return new
        {
            totalPrimesThisMonth = Math.Max(totalAmount, 0),
            budgetConsumption = budgetPct,
            topTeams,
            topEmployees,
            primeByDepartment = byPole.Select(x => new { name = x.name, value = (int)Math.Round((double)x.value) }).ToList(),
            primeEvolution = evolution.Select(x => new
            {
                month = x.month.Length >= 7 ? x.month[5..7] : x.month,
                amount = (int)Math.Round((double)x.amount),
            }).ToList(),
        };
    }

    private static string NewOrgHierarchyChildId(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    /// <summary>
    /// Garantit qu’un pôle racine (<c>prime_pole</c>) possède au moins une cellule et un service feuille,
    /// nécessaire pour l’affectation « chef de projet » (ancre hiérarchique dans <see cref="PrimeInMemoryStore"/>).
    /// </summary>
    public async Task EnsureRootPoleHasMinimalChildrenAsync(string rootPoleId, CancellationToken ct = default)
    {
        if (db == null) return;
        var key = rootPoleId.Trim();
        var pole = await db.Poles
            .Include(p => p.Cellules)
            .ThenInclude(c => c.Services)
            .FirstOrDefaultAsync(p => p.Id == key, ct);
        if (pole is null) return;

        if (pole.Cellules.Any(c => c.Services.Count > 0))
            return;

        CelluleEntity targetCell;
        if (pole.Cellules.Count == 0)
        {
            var cellId = NewOrgHierarchyChildId("p");
            while (await db.Cellules.AnyAsync(c => c.Id == cellId, ct))
                cellId = NewOrgHierarchyChildId("p");
            targetCell = new CelluleEntity { Id = cellId, Name = "Cellule principale", PoleId = pole.Id };
            db.Cellules.Add(targetCell);
        }
        else
            targetCell = pole.Cellules.OrderBy(c => c.Id).First();

        var srvId = NewOrgHierarchyChildId("c");
        while (await db.Services.AnyAsync(s => s.Id == srvId, ct))
            srvId = NewOrgHierarchyChildId("c");
        db.Services.Add(new ServiceEntity { Id = srvId, Name = "Service principal", CelluleId = targetCell.Id });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Résout le pôle racine EF (<c>prime_pole</c>) à partir d’un id cellule (<c>prime_cellule</c>).</summary>
    public async Task<string?> ResolveRootPoleIdForCelluleAsync(string celluleId, CancellationToken ct = default)
    {
        if (db == null) return null;
        var key = celluleId.Trim();
        var fromCellule = await db.Cellules.AsNoTracking()
            .Where(c => c.Id == key)
            .Select(c => c.PoleId)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(fromCellule))
            return fromCellule;
        var isRootPole = await db.Poles.AsNoTracking().AnyAsync(p => p.Id == key, ct);
        return isRootPole ? key : null;
    }

    /// <summary>Pôles supervisés (RH) avec cellules et services — pour indicateurs et filtres UI.</summary>
    public async Task<List<SupervisorOrgScopePoleDto>> GetSupervisorOrganizationalScopeAsync(
        string supervisorUserId,
        CancellationToken ct = default)
    {
        if (db == null) return [];
        var celluleIds = await GetSupervisedCelluleIdsAsync(supervisorUserId, ct);
        if (celluleIds.Count == 0) return [];

        var cellules = await db.Cellules.AsNoTracking()
            .Where(c => celluleIds.Contains(c.Id))
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        var services = await db.Services.AsNoTracking()
            .Where(s => celluleIds.Contains(s.CelluleId))
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        var poleIds = cellules.Select(c => c.PoleId).Distinct(StringComparer.Ordinal).ToList();
        var poles = await db.Poles.AsNoTracking()
            .Where(p => poleIds.Contains(p.Id))
            .OrderBy(p => p.Name)
            .ToListAsync(ct);

        return poles.Select(p => new SupervisorOrgScopePoleDto
        {
            Id = p.Id,
            Name = p.Name,
            Cellules = cellules
                .Where(c => c.PoleId == p.Id)
                .Select(c => new SupervisorOrgScopeCelluleDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    RootPoleId = p.Id,
                    Services = services
                        .Where(s => s.CelluleId == c.Id)
                        .Select(s => new SupervisorOrgScopeServiceDto { Id = s.Id, Name = s.Name })
                        .ToList(),
                })
                .ToList(),
        }).ToList();
    }
}
