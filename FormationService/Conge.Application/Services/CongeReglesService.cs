using Conge.Domain.Entities;
using Conge.Domain.Interfaces;

namespace Conge.Application.Services;

public record CongeDisponibiliteResult(
    bool Ok,
    string? Motif,
    IReadOnlyList<int> MoisInterdits,
    IReadOnlyList<string> JoursSatures);

/// <summary>
/// Règles période interdite + quota cellule/service (absents simultanés).
/// </summary>
public class CongeReglesService
{
    private readonly IPeriodeInterditeRepository _periodeRepo;
    private readonly IQuotaCongeServiceRepository _quotaRepo;
    private readonly IDemandeCongeRepository _demandeRepo;
    private readonly IEmployeSnapshotRepository _employeRepo;

    public CongeReglesService(
        IPeriodeInterditeRepository periodeRepo,
        IQuotaCongeServiceRepository quotaRepo,
        IDemandeCongeRepository demandeRepo,
        IEmployeSnapshotRepository employeRepo)
    {
        _periodeRepo = periodeRepo;
        _quotaRepo = quotaRepo;
        _demandeRepo = demandeRepo;
        _employeRepo = employeRepo;
    }

    public Task AssertHorsPeriodeInterditeAsync(DateTime debut, DateTime fin, CancellationToken ct = default)
        => AssertHorsPeriodeInterditeAsync(debut, fin, employe: null, ct);

    public async Task AssertHorsPeriodeInterditeAsync(
        DateTime debut,
        DateTime fin,
        EmployeSnapshot? employe,
        CancellationToken ct = default)
    {
        var config = await ResolvePeriodeAsync(employe, ct);
        if (!config.ChevauchePeriode(debut, fin))
            return;

        var mois = config.GetMois();
        var labels = string.Join(", ", mois.Select(MoisLabel));
        var scope = config.IsGeneral
            ? string.Empty
            : $" ({PeriodeInterditeScopeKinds.LabelFr(config.ScopeKind)})";
        throw new InvalidOperationException(
            $"La période chevauche un mois interdit aux congés{scope} ({labels}).");
    }

    /// <summary>Quota sur un nœud org (service ou cellule Directory).</summary>
    public Task AssertQuotaServiceDisponibleAsync(
        Guid serviceId,
        DateTime debut,
        DateTime fin,
        Guid? excludeDemandeId = null,
        CancellationToken ct = default)
        => AssertQuotaServiceDisponibleAsync(serviceId.ToString(), debut, fin, excludeDemandeId, ct);

    public async Task AssertQuotaServiceDisponibleAsync(
        string serviceId,
        DateTime debut,
        DateTime fin,
        Guid? excludeDemandeId = null,
        CancellationToken ct = default)
    {
        var nodeId = QuotaCongeService.NormalizeNodeId(serviceId);
        if (nodeId is null)
            return;

        var quota = await _quotaRepo.GetByServiceIdAsync(nodeId, ct);
        if (quota is null)
            return;

        var employes = await _employeRepo.GetByOrgNodeIdAsync(nodeId, ct);
        var employeIds = employes.Select(e => e.EmployeId).ToList();
        if (employeIds.Count == 0)
            return;

        var occupying = await _demandeRepo.GetOccupyingQuotaAsync(
            employeIds, debut, fin, excludeDemandeId, ct);

        var saturated = FindSaturatedDays(debut, fin, occupying, quota.MaxAbsentsSimultanes);
        if (saturated.Count == 0)
            return;

        var scopeLabel = QuotaScopeKinds.Normalize(quota.ScopeKind) == QuotaScopeKinds.Cellule
            ? "cellule"
            : "service";
        throw new InvalidOperationException(
            $"Quota {scopeLabel} atteint le {saturated[0]:dd/MM/yyyy} " +
            $"(max {quota.MaxAbsentsSimultanes} absent(s) simultané(s)).");
    }

    /// <summary>Applique d’abord le quota cellule de l’employé, sinon le quota service.</summary>
    public async Task AssertQuotaForEmployeAsync(
        EmployeSnapshot employe,
        DateTime debut,
        DateTime fin,
        Guid? excludeDemandeId = null,
        CancellationToken ct = default)
    {
        var resolved = await ResolveQuotaContextAsync(employe, ct);
        if (resolved is null)
            return;

        await AssertQuotaServiceDisponibleAsync(
            resolved.Value.NodeId, debut, fin, excludeDemandeId, ct);
    }

    public async Task<CongeDisponibiliteResult> EvaluerDisponibiliteAsync(
        Guid employeId,
        DateTime debut,
        DateTime fin,
        CancellationToken ct = default)
    {
        var employe = await _employeRepo.GetByEmployeIdAsync(employeId, ct);
        var config = await ResolvePeriodeAsync(employe, ct);
        var mois = config.GetMois().ToList();

        // Jours des mois interdits dans la plage (pour griser le calendrier).
        var forbiddenDays = ListForbiddenDaysInRange(debut, fin, mois);

        if (config.ChevauchePeriode(debut, fin))
        {
            var labels = string.Join(", ", mois.Select(MoisLabel));
            return new CongeDisponibiliteResult(
                false,
                $"La période chevauche un mois interdit aux congés ({labels}).",
                mois,
                forbiddenDays);
        }

        if (employe is null)
            return new CongeDisponibiliteResult(true, null, mois, Array.Empty<string>());

        var resolved = await ResolveQuotaContextAsync(employe, ct);
        if (resolved is null)
            return new CongeDisponibiliteResult(true, null, mois, Array.Empty<string>());

        var peers = await _employeRepo.GetByOrgNodeIdAsync(resolved.Value.NodeId, ct);
        var occupying = await _demandeRepo.GetOccupyingQuotaAsync(
            peers.Select(e => e.EmployeId), debut, fin, null, ct);

        var saturated = FindSaturatedDays(debut, fin, occupying, resolved.Value.Quota.MaxAbsentsSimultanes);
        var jours = saturated.Select(d => d.ToString("yyyy-MM-dd")).ToList();

        if (saturated.Count == 0)
            return new CongeDisponibiliteResult(true, null, mois, Array.Empty<string>());

        var scopeLabel = resolved.Value.ScopeKind == QuotaScopeKinds.Cellule ? "cellule" : "service";
        return new CongeDisponibiliteResult(
            false,
            $"Quota {scopeLabel} atteint le {saturated[0]:dd/MM/yyyy} (max {resolved.Value.Quota.MaxAbsentsSimultanes} absent(s)).",
            mois,
            jours);
    }

    public async Task<IReadOnlyList<int>> GetMoisInterditsAsync(CancellationToken ct = default)
    {
        var config = await ResolvePeriodeAsync(null, ct);
        return config.GetMois();
    }

    private async Task<PeriodeInterditeConge> ResolvePeriodeAsync(
        EmployeSnapshot? employe,
        CancellationToken ct)
    {
        var general = await _periodeRepo.GetOrCreateAsync(ct);
        if (employe is null)
            return general;
        var all = await _periodeRepo.GetAllAsync(ct);
        return PeriodeInterditeConge.PickForEmploye(all, employe, general);
    }

    private async Task<(string NodeId, QuotaCongeService Quota, string ScopeKind)?> ResolveQuotaContextAsync(
        EmployeSnapshot employe,
        CancellationToken ct)
    {
        // 1) Quota cellule (id Directory string)
        var celluleId = QuotaCongeService.NormalizeNodeId(employe.CelluleId);
        if (celluleId is not null)
        {
            var celluleQuota = await _quotaRepo.GetByServiceIdAsync(celluleId, ct);
            if (celluleQuota is not null)
            {
                return (celluleId, celluleQuota, QuotaScopeKinds.Normalize(celluleQuota.ScopeKind));
            }
        }

        // 2) Quota service (OrgServiceId Directory ou ServiceId legacy Guid)
        string? serviceId = QuotaCongeService.NormalizeNodeId(employe.OrgServiceId);
        if (serviceId is null && employe.ServiceId != Guid.Empty)
            serviceId = employe.ServiceId.ToString();

        if (serviceId is null)
            return null;

        var serviceQuota = await _quotaRepo.GetByServiceIdAsync(serviceId, ct);
        if (serviceQuota is null)
            return null;

        return (serviceId, serviceQuota, QuotaScopeKinds.Normalize(serviceQuota.ScopeKind));
    }

    private static List<DateTime> FindSaturatedDays(
        DateTime debut,
        DateTime fin,
        IReadOnlyList<DemandeConge> occupying,
        int maxAbsents)
    {
        var d = debut.Date;
        var f = fin.Date;
        if (f < d) (d, f) = (f, d);

        var saturated = new List<DateTime>();
        for (var day = d; day <= f; day = day.AddDays(1))
        {
            var count = occupying.Count(c => c.DateDebut.Date <= day && c.DateFin.Date >= day);
            if (count >= maxAbsents)
                saturated.Add(day);
        }

        return saturated;
    }

    private static IReadOnlyList<string> ListForbiddenDaysInRange(
        DateTime debut,
        DateTime fin,
        IReadOnlyList<int> moisInterdits)
    {
        if (moisInterdits.Count == 0)
            return Array.Empty<string>();

        var set = moisInterdits.ToHashSet();
        var d = debut.Date;
        var f = fin.Date;
        if (f < d) (d, f) = (f, d);

        var days = new List<string>();
        for (var day = d; day <= f; day = day.AddDays(1))
        {
            if (set.Contains(day.Month))
                days.Add(day.ToString("yyyy-MM-dd"));
        }

        return days;
    }

    private static string MoisLabel(int mois) => mois switch
    {
        1 => "janvier",
        2 => "février",
        3 => "mars",
        4 => "avril",
        5 => "mai",
        6 => "juin",
        7 => "juillet",
        8 => "août",
        9 => "septembre",
        10 => "octobre",
        11 => "novembre",
        12 => "décembre",
        _ => mois.ToString()
    };
}
