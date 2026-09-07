using Conge.Domain.Entities;
using Conge.Domain.Enums;
using Conge.Domain.Interfaces;
using Conge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Conge.Infrastructure.Persistence.Repositories;

public class DemandeCongeRepository : IDemandeCongeRepository
{
    private readonly CongeDbContext _context;

    public DemandeCongeRepository(CongeDbContext context) => _context = context;

    public async Task<DemandeConge?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.DemandeConges
            .Include(d => d.Decisions)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IEnumerable<DemandeConge>> GetByEmployeIdAsync(Guid employeId, CancellationToken ct = default)
        => await _context.DemandeConges
            .Include(d => d.Decisions)
            .Where(d => d.EmployeId == employeId)
            .OrderByDescending(d => d.DateDemande)
            .ToListAsync(ct);

    public async Task<IEnumerable<DemandeConge>> GetByManagerIdAsync(
        Guid managerId,
        IEnumerable<string>? validationNodeIds = null,
        CancellationToken ct = default)
    {
        var nodes = validationNodeIds?
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        var q = _context.DemandeConges.Include(d => d.Decisions).AsQueryable();
        if (nodes.Count > 0)
        {
            q = q.Where(d =>
                d.ManagerId == managerId
                || (d.ValidationNodeId != null && nodes.Contains(d.ValidationNodeId)));
        }
        else
        {
            q = q.Where(d => d.ManagerId == managerId);
        }

        return await q.OrderByDescending(d => d.DateDemande).ToListAsync(ct);
    }

    public async Task<IEnumerable<DemandeConge>> GetByStatutAsync(StatutDemande statut, CancellationToken ct = default)
        => await _context.DemandeConges
            .Include(d => d.Decisions)
            .Where(d => d.Statut == statut)
            .ToListAsync(ct);

    public async Task<IEnumerable<DemandeConge>> GetHistoriqueAsync(Guid employeId, int annee, CancellationToken ct = default)
        => await _context.DemandeConges
            .Include(d => d.Decisions)
            .Where(d => d.EmployeId == employeId && d.DateDebut.Year == annee)
            .OrderByDescending(d => d.DateDemande)
            .ToListAsync(ct);

    public async Task<IEnumerable<DemandeConge>> GetByAnneeAsync(int annee, CancellationToken ct = default)
        => await _context.DemandeConges
            .Include(d => d.Decisions)
            .Where(d => d.DateDebut.Year == annee || d.DateFin.Year == annee)
            .OrderByDescending(d => d.DateDemande)
            .ToListAsync(ct);

    public async Task AddAsync(DemandeConge demande, CancellationToken ct = default)
        => await _context.DemandeConges.AddAsync(demande, ct);

    public void Update(DemandeConge demande)
    {
        var entry = _context.Entry(demande);
        if (entry.State == EntityState.Detached)
            _context.Attach(demande);

        foreach (var decision in demande.Decisions)
        {
            var dEntry = _context.Entry(decision);
            if (dEntry.State is EntityState.Detached or EntityState.Modified)
                dEntry.State = EntityState.Added;
        }
    }
    public async Task<bool> ExistsCongeEnChevauchementAsync(Guid employeId, DateTime debut, DateTime fin, CancellationToken ct = default)
        => await _context.DemandeConges.AnyAsync(d =>
            d.EmployeId == employeId &&
            d.Statut != StatutDemande.Refusee &&
            d.Statut != StatutDemande.Annulee &&
            d.DateDebut <= fin && d.DateFin >= debut, ct);

    public async Task<IReadOnlyList<DemandeConge>> GetOccupyingQuotaAsync(
        IEnumerable<Guid> employeIds,
        DateTime debut,
        DateTime fin,
        Guid? excludeDemandeId = null,
        CancellationToken ct = default)
    {
        var ids = employeIds.Distinct().ToList();
        if (ids.Count == 0) return Array.Empty<DemandeConge>();

        var occupants = CongeQuotaStatuts.Occupants;
        var q = _context.DemandeConges.Where(d =>
            ids.Contains(d.EmployeId) &&
            occupants.Contains(d.Statut) &&
            d.DateDebut <= fin &&
            d.DateFin >= debut);

        if (excludeDemandeId.HasValue)
            q = q.Where(d => d.Id != excludeDemandeId.Value);

        return await q.ToListAsync(ct);
    }
}

public class SoldeCongeRepository : ISoldeCongeRepository
{
    private readonly CongeDbContext _context;

    public SoldeCongeRepository(CongeDbContext context) => _context = context;

    public async Task<SoldeConge?> GetByEmployeAndAnneeAsync(Guid employeId, int annee, CancellationToken ct = default)
        => await _context.SoldeConges
            .FirstOrDefaultAsync(s => s.EmployeId == employeId && s.Annee == annee, ct);

    public async Task<IEnumerable<SoldeConge>> GetAllByEmployeAsync(Guid employeId, CancellationToken ct = default)
        => await _context.SoldeConges
            .Where(s => s.EmployeId == employeId)
            .OrderByDescending(s => s.Annee)
            .ToListAsync(ct);

    public async Task AddAsync(SoldeConge solde, CancellationToken ct = default)
        => await _context.SoldeConges.AddAsync(solde, ct);

    public void Update(SoldeConge solde)
        => _context.SoldeConges.Update(solde);
}

public class EmployeSnapshotRepository : IEmployeSnapshotRepository
{
    private readonly CongeDbContext _context;

    public EmployeSnapshotRepository(CongeDbContext context) => _context = context;

    public async Task<EmployeSnapshot?> GetByEmployeIdAsync(Guid employeId, CancellationToken ct = default)
        => await _context.EmployeSnapshots
            .FirstOrDefaultAsync(e => e.EmployeId == employeId, ct);

    public async Task<EmployeSnapshot?> GetByEmployeIdOrEmailAsync(
        Guid employeId,
        string? email,
        CancellationToken ct = default)
    {
        var row = await GetByEmployeIdAsync(employeId, ct);
        if (row is not null)
            return row;

        if (string.IsNullOrWhiteSpace(email))
            return null;

        var normalized = email.Trim().ToLowerInvariant();
        return await _context.EmployeSnapshots
            .FirstOrDefaultAsync(e => e.Email.ToLower() == normalized, ct);
    }

    public async Task<IEnumerable<EmployeSnapshot>> GetByManagerIdAsync(Guid managerId, CancellationToken ct = default)
        => await _context.EmployeSnapshots
            .Where(e => e.ManagerId == managerId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EmployeSnapshot>> GetByPerimeterAsync(
        Guid managerId,
        IReadOnlyList<string>? orgNodeIds,
        CancellationToken ct = default)
    {
        var nodes = orgNodeIds?
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (nodes.Count == 0)
        {
            return await _context.EmployeSnapshots
                .AsNoTracking()
                .Where(e => !e.IsArchived && e.ManagerId == managerId)
                .ToListAsync(ct);
        }

        // Contient côté SQL (PostgreSQL) : IDs Directory en général en casse stable.
        var nodeGuids = nodes
            .Select(n => Guid.TryParse(n, out var g) ? g : (Guid?)null)
            .Where(g => g is Guid id && id != Guid.Empty)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();

        return await _context.EmployeSnapshots
            .AsNoTracking()
            .Where(e =>
                !e.IsArchived
                && (e.ManagerId == managerId
                || (e.CelluleId != null && nodes.Contains(e.CelluleId))
                || (e.OrgServiceId != null && nodes.Contains(e.OrgServiceId))
                || (e.PoleId != null && nodes.Contains(e.PoleId))
                || (e.ServiceId != Guid.Empty && nodeGuids.Contains(e.ServiceId))))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmployeSnapshot>> GetAllAsync(CancellationToken ct = default)
        => await _context.EmployeSnapshots.AsNoTracking()
            .Where(e => !e.IsArchived)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EmployeSnapshot>> GetByServiceIdAsync(Guid serviceId, CancellationToken ct = default)
        => await GetByOrgNodeIdAsync(serviceId.ToString(), ct);

    public async Task<IReadOnlyList<EmployeSnapshot>> GetByOrgNodeIdAsync(string orgNodeId, CancellationToken ct = default)
    {
        var id = QuotaCongeService.NormalizeNodeId(orgNodeId);
        if (id is null)
            return Array.Empty<EmployeSnapshot>();

        Guid.TryParse(id, out var asGuid);
        return await _context.EmployeSnapshots
            .AsNoTracking()
            .Where(e =>
                !e.IsArchived
                && ((asGuid != Guid.Empty && e.ServiceId == asGuid)
                || (e.OrgServiceId != null && e.OrgServiceId == id)
                || (e.CelluleId != null && e.CelluleId == id)))
            .ToListAsync(ct);
    }

    public async Task<EmployeSnapshot?> GetAdminOuRhAsync(CancellationToken ct = default)
    {
        return await _context.EmployeSnapshots
            .Where(e => e.Role == "Admin" || e.Role == "RH")
            .OrderBy(e => e.Role) // Admin en premier
            .FirstOrDefaultAsync(ct);
    }
    public async Task AddAsync(EmployeSnapshot employe, CancellationToken ct = default)
        => await _context.EmployeSnapshots.AddAsync(employe, ct);

    public void Update(EmployeSnapshot employe)
        => _context.EmployeSnapshots.Update(employe);

    public void Remove(EmployeSnapshot employe)
        => _context.EmployeSnapshots.Remove(employe);

    public async Task<bool> ExistsAsync(Guid employeId, CancellationToken ct = default)
        => await _context.EmployeSnapshots.AnyAsync(e => e.EmployeId == employeId, ct);
}

public class PeriodeInterditeRepository : IPeriodeInterditeRepository
{
    private readonly CongeDbContext _context;

    public PeriodeInterditeRepository(CongeDbContext context) => _context = context;

    public async Task<PeriodeInterditeConge> GetOrCreateAsync(CancellationToken ct = default)
    {
        var row = await FindGeneralAsync(ct);
        if (row is not null) return row;

        row = PeriodeInterditeConge.CreerParDefaut();
        await _context.PeriodesInterdites.AddAsync(row, ct);
        await _context.SaveChangesAsync(ct);
        return row;
    }

    public async Task<PeriodeInterditeConge?> GetByScopeAsync(
        string scopeKind,
        string? scopeId,
        CancellationToken ct = default)
    {
        var kind = PeriodeInterditeScopeKinds.Normalize(scopeKind);
        var id = PeriodeInterditeScopeKinds.NormalizeScopeId(kind, scopeId);
        if (kind == PeriodeInterditeScopeKinds.General)
            return await FindGeneralAsync(ct);

        return await _context.PeriodesInterdites
            .FirstOrDefaultAsync(p => p.ScopeKind == kind && p.ScopeId == id, ct);
    }

    public async Task<IReadOnlyList<PeriodeInterditeConge>> GetAllAsync(CancellationToken ct = default)
        => await _context.PeriodesInterdites.AsNoTracking().ToListAsync(ct);

    public Task AddAsync(PeriodeInterditeConge config, CancellationToken ct = default)
        => _context.PeriodesInterdites.AddAsync(config, ct).AsTask();

    public void Update(PeriodeInterditeConge config)
        => _context.PeriodesInterdites.Update(config);

    public void Remove(PeriodeInterditeConge config)
        => _context.PeriodesInterdites.Remove(config);

    private async Task<PeriodeInterditeConge?> FindGeneralAsync(CancellationToken ct)
    {
        var general = PeriodeInterditeScopeKinds.General;
        return await _context.PeriodesInterdites.FirstOrDefaultAsync(
            p => p.ScopeKind == general && (p.ScopeId == null || p.ScopeId == ""),
            ct)
            ?? await _context.PeriodesInterdites.FirstOrDefaultAsync(
                p => p.Id == PeriodeInterditeConge.GeneralId, ct);
    }
}

public class QuotaCongeServiceRepository : IQuotaCongeServiceRepository
{
    private readonly CongeDbContext _context;

    public QuotaCongeServiceRepository(CongeDbContext context) => _context = context;

    public async Task<QuotaCongeService?> GetByServiceIdAsync(string serviceId, CancellationToken ct = default)
    {
        var id = QuotaCongeService.NormalizeNodeId(serviceId);
        if (id is null) return null;
        return await _context.QuotasCongeService.FirstOrDefaultAsync(q => q.ServiceId == id, ct);
    }

    public async Task<IReadOnlyList<QuotaCongeService>> GetByServiceIdsAsync(
        IEnumerable<string> serviceIds,
        CancellationToken ct = default)
    {
        var ids = serviceIds
            .Select(QuotaCongeService.NormalizeNodeId)
            .Where(id => id is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0) return Array.Empty<QuotaCongeService>();
        return await _context.QuotasCongeService
            .Where(q => ids.Contains(q.ServiceId))
            .ToListAsync(ct);
    }

    public async Task AddAsync(QuotaCongeService quota, CancellationToken ct = default)
        => await _context.QuotasCongeService.AddAsync(quota, ct);

    public void Update(QuotaCongeService quota)
        => _context.QuotasCongeService.Update(quota);
}