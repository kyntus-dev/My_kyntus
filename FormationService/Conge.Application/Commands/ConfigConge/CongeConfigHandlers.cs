using Conge.Application.Abstractions;
using Conge.Application.DTOs;
using Conge.Domain.Entities;
using Conge.Domain.Interfaces;
using Kyntus.Iam;
using MediatR;

namespace Conge.Application.Commands.ConfigConge;

public record GetPeriodesInterditesQuery(
    string? ScopeKind = null,
    string? ScopeId = null,
    Guid? EmployeId = null,
    string? DepartmentId = null,
    string? PoleId = null,
    string? CelluleId = null,
    string? ServiceId = null) : IRequest<PeriodesInterditesDto>;

public record GetPeriodesInterditesScopesQuery : IRequest<PeriodesInterditesScopesDto>;

public record UpdatePeriodesInterditesCommand(
    IReadOnlyList<int> Mois,
    Guid? UpdatedBy = null,
    string? ScopeKind = null,
    string? ScopeId = null)
    : IRequest<PeriodesInterditesDto>;

public record DeletePeriodesInterditesScopeCommand(string ScopeKind, string ScopeId)
    : IRequest<bool>;

public record GetQuotasServiceQuery(Guid SuperviseurId) : IRequest<IReadOnlyList<QuotaCongeServiceDto>>;

public record UpsertQuotaServiceCommand(
    string ServiceId,
    int MaxAbsentsSimultanes,
    Guid SuperviseurId,
    string? ScopeKind = null)
    : IRequest<QuotaCongeServiceDto>;

public record GetQuotaServiceByIdQuery(string ServiceId) : IRequest<QuotaCongeServiceDto?>;

public record GetDisponibiliteCongeQuery(Guid EmployeId, DateTime Debut, DateTime Fin)
    : IRequest<CongeDisponibiliteDto>;

public class GetPeriodesInterditesHandler : IRequestHandler<GetPeriodesInterditesQuery, PeriodesInterditesDto>
{
    private readonly IPeriodeInterditeRepository _repo;
    private readonly IEmployeSnapshotRepository _employeRepo;
    private readonly IDirectoryOrgCatalog? _orgCatalog;

    public GetPeriodesInterditesHandler(
        IPeriodeInterditeRepository repo,
        IEmployeSnapshotRepository employeRepo,
        IDirectoryOrgCatalog? orgCatalog = null)
    {
        _repo = repo;
        _employeRepo = employeRepo;
        _orgCatalog = orgCatalog;
    }

    public async Task<PeriodesInterditesDto> Handle(GetPeriodesInterditesQuery request, CancellationToken ct)
    {
        var general = await _repo.GetOrCreateAsync(ct);
        var all = await _repo.GetAllAsync(ct);
        var requested = PeriodeInterditeScopeKinds.Normalize(request.ScopeKind);
        var requestedId = PeriodeInterditeScopeKinds.NormalizeScopeId(requested, request.ScopeId);

        PeriodeInterditeConge resolved;
        string deepestKind;
        string deepestId;

        if (request.EmployeId is Guid empId && empId != Guid.Empty)
        {
            var employe = await _employeRepo.GetByEmployeIdAsync(empId, ct);
            resolved = PeriodeInterditeConge.PickForEmploye(all, employe, general);
            deepestKind = PeriodeInterditeScopeKinds.Normalize(resolved.ScopeKind);
            deepestId = resolved.ScopeId ?? string.Empty;
        }
        else if (!string.IsNullOrWhiteSpace(request.DepartmentId)
                 || !string.IsNullOrWhiteSpace(request.PoleId)
                 || !string.IsNullOrWhiteSpace(request.CelluleId)
                 || !string.IsNullOrWhiteSpace(request.ServiceId))
        {
            resolved = PickChain(all, general, request);
            deepestKind = DeepestKind(request);
            deepestId = DeepestId(request);
        }
        else if (requested != PeriodeInterditeScopeKinds.General && requestedId.Length > 0)
        {
            resolved = all.FirstOrDefault(r =>
                           PeriodeInterditeScopeKinds.Normalize(r.ScopeKind) == requested
                           && string.Equals(r.ScopeId, requestedId, StringComparison.OrdinalIgnoreCase))
                       ?? general;
            deepestKind = requested;
            deepestId = requestedId;
        }
        else
        {
            resolved = general;
            deepestKind = PeriodeInterditeScopeKinds.General;
            deepestId = string.Empty;
        }

        var isOverride = PeriodeInterditeScopeKinds.Normalize(resolved.ScopeKind) == deepestKind
            && string.Equals(resolved.ScopeId ?? string.Empty, deepestId, StringComparison.OrdinalIgnoreCase)
            && deepestKind != PeriodeInterditeScopeKinds.General;

        return MapDto(resolved, general, isOverride, deepestKind, deepestId, await ResolveLabelAsync(resolved, ct));
    }

    private static PeriodeInterditeConge PickChain(
        IReadOnlyList<PeriodeInterditeConge> all,
        PeriodeInterditeConge general,
        GetPeriodesInterditesQuery request)
    {
        static PeriodeInterditeConge? Find(IReadOnlyList<PeriodeInterditeConge> rows, string kind, string? id)
        {
            id = (id ?? string.Empty).Trim();
            if (id.Length == 0) return null;
            return rows.FirstOrDefault(r =>
                PeriodeInterditeScopeKinds.Normalize(r.ScopeKind) == kind
                && string.Equals(r.ScopeId, id, StringComparison.OrdinalIgnoreCase));
        }

        return Find(all, PeriodeInterditeScopeKinds.Service, request.ServiceId)
            ?? Find(all, PeriodeInterditeScopeKinds.Cellule, request.CelluleId)
            ?? Find(all, PeriodeInterditeScopeKinds.Pole, request.PoleId)
            ?? Find(all, PeriodeInterditeScopeKinds.Department, request.DepartmentId)
            ?? general;
    }

    private static string DeepestKind(GetPeriodesInterditesQuery request)
    {
        if (!string.IsNullOrWhiteSpace(request.ServiceId)) return PeriodeInterditeScopeKinds.Service;
        if (!string.IsNullOrWhiteSpace(request.CelluleId)) return PeriodeInterditeScopeKinds.Cellule;
        if (!string.IsNullOrWhiteSpace(request.PoleId)) return PeriodeInterditeScopeKinds.Pole;
        if (!string.IsNullOrWhiteSpace(request.DepartmentId)) return PeriodeInterditeScopeKinds.Department;
        return PeriodeInterditeScopeKinds.General;
    }

    private static string DeepestId(GetPeriodesInterditesQuery request)
    {
        if (!string.IsNullOrWhiteSpace(request.ServiceId)) return request.ServiceId.Trim();
        if (!string.IsNullOrWhiteSpace(request.CelluleId)) return request.CelluleId.Trim();
        if (!string.IsNullOrWhiteSpace(request.PoleId)) return request.PoleId.Trim();
        if (!string.IsNullOrWhiteSpace(request.DepartmentId)) return request.DepartmentId.Trim();
        return string.Empty;
    }

    private async Task<string?> ResolveLabelAsync(PeriodeInterditeConge row, CancellationToken ct)
    {
        if (row.IsGeneral) return "Configuration générale";
        if (_orgCatalog is null) return null;
        return await _orgCatalog.ResolveNodeNameAsync(row.ScopeId, ct);
    }

    internal static PeriodesInterditesDto MapDto(
        PeriodeInterditeConge resolved,
        PeriodeInterditeConge general,
        bool isOverride,
        string requestedKind,
        string requestedId,
        string? label)
    {
        var kind = PeriodeInterditeScopeKinds.Normalize(resolved.ScopeKind);
        return new PeriodesInterditesDto(
            resolved.GetMois().ToList(),
            resolved.UpdatedAt,
            isOverride ? kind : requestedKind,
            isOverride ? (resolved.ScopeId ?? string.Empty) : requestedId,
            isOverride,
            kind,
            label ?? PeriodeInterditeScopeKinds.LabelFr(kind));
    }
}

public class GetPeriodesInterditesScopesHandler
    : IRequestHandler<GetPeriodesInterditesScopesQuery, PeriodesInterditesScopesDto>
{
    private readonly IPeriodeInterditeRepository _repo;
    private readonly IDirectoryOrgCatalog? _orgCatalog;

    public GetPeriodesInterditesScopesHandler(
        IPeriodeInterditeRepository repo,
        IDirectoryOrgCatalog? orgCatalog = null)
    {
        _repo = repo;
        _orgCatalog = orgCatalog;
    }

    public async Task<PeriodesInterditesScopesDto> Handle(
        GetPeriodesInterditesScopesQuery request,
        CancellationToken ct)
    {
        var general = await _repo.GetOrCreateAsync(ct);
        var all = await _repo.GetAllAsync(ct);
        var overrides = new List<PeriodesInterditesDto>();
        foreach (var row in all.Where(r => !r.IsGeneral)
                     .OrderByDescending(r => PeriodeInterditeScopeKinds.Rank(r.ScopeKind))
                     .ThenBy(r => r.ScopeId))
        {
            var label = _orgCatalog is null || string.IsNullOrWhiteSpace(row.ScopeId)
                ? null
                : await _orgCatalog.ResolveNodeNameAsync(row.ScopeId, ct);
            overrides.Add(GetPeriodesInterditesHandler.MapDto(
                row, general, true, row.ScopeKind, row.ScopeId ?? string.Empty,
                label ?? PeriodeInterditeScopeKinds.LabelFr(row.ScopeKind)));
        }

        return new PeriodesInterditesScopesDto(
            GetPeriodesInterditesHandler.MapDto(
                general, general, false,
                PeriodeInterditeScopeKinds.General, string.Empty,
                "Configuration générale"),
            overrides);
    }
}

public class UpdatePeriodesInterditesHandler : IRequestHandler<UpdatePeriodesInterditesCommand, PeriodesInterditesDto>
{
    private readonly IPeriodeInterditeRepository _repo;
    private readonly IUnitOfWork _uow;
    private readonly IDirectoryOrgCatalog? _orgCatalog;

    public UpdatePeriodesInterditesHandler(
        IPeriodeInterditeRepository repo,
        IUnitOfWork uow,
        IDirectoryOrgCatalog? orgCatalog = null)
    {
        _repo = repo;
        _uow = uow;
        _orgCatalog = orgCatalog;
    }

    public async Task<PeriodesInterditesDto> Handle(UpdatePeriodesInterditesCommand request, CancellationToken ct)
    {
        var kind = PeriodeInterditeScopeKinds.Normalize(request.ScopeKind);
        var scopeId = PeriodeInterditeScopeKinds.NormalizeScopeId(kind, request.ScopeId);
        if (kind != PeriodeInterditeScopeKinds.General && string.IsNullOrWhiteSpace(scopeId))
            throw new InvalidOperationException("Choisissez un département, pôle, cellule ou service.");

        PeriodeInterditeConge row;
        if (kind == PeriodeInterditeScopeKinds.General)
        {
            row = await _repo.GetOrCreateAsync(ct);
            row.MettreAJour(request.Mois, request.UpdatedBy);
            _repo.Update(row);
        }
        else
        {
            var existing = await _repo.GetByScopeAsync(kind, scopeId, ct);
            if (existing is null)
            {
                row = PeriodeInterditeConge.CreerPourScope(kind, scopeId, request.Mois, request.UpdatedBy);
                await _repo.AddAsync(row, ct);
            }
            else
            {
                row = existing;
                row.MettreAJour(request.Mois, request.UpdatedBy);
                _repo.Update(row);
            }
        }

        await _uow.SaveChangesAsync(ct);
        var label = kind == PeriodeInterditeScopeKinds.General
            ? "Configuration générale"
            : await (_orgCatalog?.ResolveNodeNameAsync(scopeId, ct) ?? Task.FromResult<string?>(null));
        return GetPeriodesInterditesHandler.MapDto(
            row, row, kind != PeriodeInterditeScopeKinds.General, kind, scopeId,
            label ?? PeriodeInterditeScopeKinds.LabelFr(kind));
    }
}

public class DeletePeriodesInterditesScopeHandler : IRequestHandler<DeletePeriodesInterditesScopeCommand, bool>
{
    private readonly IPeriodeInterditeRepository _repo;
    private readonly IUnitOfWork _uow;

    public DeletePeriodesInterditesScopeHandler(IPeriodeInterditeRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task<bool> Handle(DeletePeriodesInterditesScopeCommand request, CancellationToken ct)
    {
        var kind = PeriodeInterditeScopeKinds.Normalize(request.ScopeKind);
        if (kind == PeriodeInterditeScopeKinds.General)
            throw new InvalidOperationException("La configuration générale ne peut pas être supprimée.");

        var row = await _repo.GetByScopeAsync(kind, request.ScopeId, ct);
        if (row is null) return false;
        _repo.Remove(row);
        await _uow.SaveChangesAsync(ct);
        return true;
    }
}

public class GetQuotasServiceHandler : IRequestHandler<GetQuotasServiceQuery, IReadOnlyList<QuotaCongeServiceDto>>
{
    private readonly IEmployeSnapshotRepository _employeRepo;
    private readonly IQuotaCongeServiceRepository _quotaRepo;
    private readonly IDirectoryOrgCatalog? _orgCatalog;
    private readonly IRebacClient? _rebac;

    public GetQuotasServiceHandler(
        IEmployeSnapshotRepository employeRepo,
        IQuotaCongeServiceRepository quotaRepo,
        IDirectoryOrgCatalog? orgCatalog = null,
        IRebacClient? rebac = null)
    {
        _employeRepo = employeRepo;
        _quotaRepo = quotaRepo;
        _orgCatalog = orgCatalog;
        _rebac = rebac;
    }

    public async Task<IReadOnlyList<QuotaCongeServiceDto>> Handle(GetQuotasServiceQuery request, CancellationToken ct)
    {
        var catalog = _orgCatalog is null
            ? DirectoryOrgCatalogSnapshot.Empty
            : await _orgCatalog.GetSnapshotAsync(ct);

        var team = await CongeQuotaPerimeter.ResolveTeamAsync(
            _employeRepo, _rebac, request.SuperviseurId, ct);
        var nodes = await CongeQuotaPerimeter.ResolveQuotaNodesAsync(
            _employeRepo, _rebac, request.SuperviseurId, team, catalog, ct);

        var quotas = await _quotaRepo.GetByServiceIdsAsync(nodes.Select(n => n.NodeId), ct);
        var byId = quotas.ToDictionary(q => q.ServiceId, StringComparer.OrdinalIgnoreCase);

        return nodes.Select(n =>
        {
            byId.TryGetValue(n.NodeId, out var q);
            var scope = q is not null
                ? QuotaScopeKinds.Normalize(q.ScopeKind)
                : n.ScopeKind;
            return new QuotaCongeServiceDto(
                n.NodeId,
                n.Label,
                q?.MaxAbsentsSimultanes,
                n.Effectif,
                scope);
        }).OrderBy(x => x.ScopeKind).ThenBy(x => x.ServiceNom).ToList();
    }
}

public class UpsertQuotaServiceHandler : IRequestHandler<UpsertQuotaServiceCommand, QuotaCongeServiceDto>
{
    private readonly IEmployeSnapshotRepository _employeRepo;
    private readonly IQuotaCongeServiceRepository _quotaRepo;
    private readonly IUnitOfWork _uow;
    private readonly IDirectoryOrgCatalog? _orgCatalog;
    private readonly IRebacClient? _rebac;

    public UpsertQuotaServiceHandler(
        IEmployeSnapshotRepository employeRepo,
        IQuotaCongeServiceRepository quotaRepo,
        IUnitOfWork uow,
        IDirectoryOrgCatalog? orgCatalog = null,
        IRebacClient? rebac = null)
    {
        _employeRepo = employeRepo;
        _quotaRepo = quotaRepo;
        _uow = uow;
        _orgCatalog = orgCatalog;
        _rebac = rebac;
    }

    public async Task<QuotaCongeServiceDto> Handle(UpsertQuotaServiceCommand request, CancellationToken ct)
    {
        var nodeId = QuotaCongeService.NormalizeNodeId(request.ServiceId)
            ?? throw new ArgumentException("ServiceId requis.");

        var catalog = _orgCatalog is null
            ? DirectoryOrgCatalogSnapshot.Empty
            : await _orgCatalog.GetSnapshotAsync(ct);

        var team = await CongeQuotaPerimeter.ResolveTeamAsync(
            _employeRepo, _rebac, request.SuperviseurId, ct);
        var nodes = await CongeQuotaPerimeter.ResolveQuotaNodesAsync(
            _employeRepo, _rebac, request.SuperviseurId, team, catalog, ct);
        var inScope = nodes.FirstOrDefault(n =>
                string.Equals(n.NodeId, nodeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Ce périmètre n'appartient pas à votre scope.");

        var scopeKind = QuotaScopeKinds.Normalize(request.ScopeKind ?? inScope.ScopeKind);
        var existing = await _quotaRepo.GetByServiceIdAsync(nodeId, ct);
        if (existing is null)
        {
            existing = QuotaCongeService.Creer(
                nodeId, request.MaxAbsentsSimultanes, request.SuperviseurId, scopeKind);
            await _quotaRepo.AddAsync(existing, ct);
        }
        else
        {
            existing.MettreAJour(request.MaxAbsentsSimultanes, request.SuperviseurId, scopeKind);
            _quotaRepo.Update(existing);
        }

        await _uow.SaveChangesAsync(ct);
        return new QuotaCongeServiceDto(
            nodeId,
            inScope.Label,
            existing.MaxAbsentsSimultanes,
            inScope.Effectif,
            existing.ScopeKind);
    }
}

public class GetQuotaServiceByIdHandler : IRequestHandler<GetQuotaServiceByIdQuery, QuotaCongeServiceDto?>
{
    private readonly IQuotaCongeServiceRepository _quotaRepo;
    private readonly IEmployeSnapshotRepository _employeRepo;
    private readonly IDirectoryOrgCatalog? _orgCatalog;

    public GetQuotaServiceByIdHandler(
        IQuotaCongeServiceRepository quotaRepo,
        IEmployeSnapshotRepository employeRepo,
        IDirectoryOrgCatalog? orgCatalog = null)
    {
        _quotaRepo = quotaRepo;
        _employeRepo = employeRepo;
        _orgCatalog = orgCatalog;
    }

    public async Task<QuotaCongeServiceDto?> Handle(GetQuotaServiceByIdQuery request, CancellationToken ct)
    {
        var nodeId = QuotaCongeService.NormalizeNodeId(request.ServiceId);
        if (nodeId is null) return null;

        var catalog = _orgCatalog is null
            ? DirectoryOrgCatalogSnapshot.Empty
            : await _orgCatalog.GetSnapshotAsync(ct);

        var q = await _quotaRepo.GetByServiceIdAsync(nodeId, ct);
        var peers = await _employeRepo.GetByOrgNodeIdAsync(nodeId, ct);
        var scope = q is not null
            ? QuotaScopeKinds.Normalize(q.ScopeKind)
            : CongeQuotaPerimeter.InferScopeKind(peers.FirstOrDefault(), nodeId);
        var nom = CongeQuotaPerimeter.ResolveNodeLabel(peers.FirstOrDefault(), nodeId, scope, catalog);
        var effectif = catalog.GetHeadcount(nodeId, scope);
        if (effectif == 0)
            effectif = peers.Count;
        if (q is null)
            return new QuotaCongeServiceDto(nodeId, nom, null, effectif, scope);
        return new QuotaCongeServiceDto(nodeId, nom, q.MaxAbsentsSimultanes, effectif, scope);
    }
}

public class GetDisponibiliteCongeHandler : IRequestHandler<GetDisponibiliteCongeQuery, CongeDisponibiliteDto>
{
    private readonly Services.CongeReglesService _regles;

    public GetDisponibiliteCongeHandler(Services.CongeReglesService regles) => _regles = regles;

    public async Task<CongeDisponibiliteDto> Handle(GetDisponibiliteCongeQuery request, CancellationToken ct)
    {
        var r = await _regles.EvaluerDisponibiliteAsync(request.EmployeId, request.Debut, request.Fin, ct);
        return new CongeDisponibiliteDto(r.Ok, r.Motif, r.MoisInterdits.ToList(), r.JoursSatures.ToList());
    }
}

/// <summary>Résolution du périmètre quotas (ReBAC org + ManagerId legacy + catalogue Directory).</summary>
internal static class CongeQuotaPerimeter
{
    internal sealed record QuotaNode(string NodeId, string ScopeKind, string Label, int Effectif);

    internal static async Task<IReadOnlyList<EmployeSnapshot>> ResolveTeamAsync(
        IEmployeSnapshotRepository employeRepo,
        IRebacClient? rebac,
        Guid actorId,
        CancellationToken ct)
    {
        var actor = await employeRepo.GetByEmployeIdAsync(actorId, ct);
        if (IsRhOrAdmin(actor?.Role))
            return await employeRepo.GetAllAsync(ct);

        var managed = await TryGetManagedNodeIdsAsync(rebac, actorId, ct);
        return await employeRepo.GetByPerimeterAsync(
            actorId,
            managed is { Count: > 0 } ? managed : null,
            ct);
    }

    internal static async Task<IReadOnlyList<QuotaNode>> ResolveQuotaNodesAsync(
        IEmployeSnapshotRepository employeRepo,
        IRebacClient? rebac,
        Guid actorId,
        IReadOnlyList<EmployeSnapshot> team,
        DirectoryOrgCatalogSnapshot catalog,
        CancellationToken ct)
    {
        var actor = await employeRepo.GetByEmployeIdAsync(actorId, ct);
        var map = new Dictionary<string, QuotaNode>(StringComparer.OrdinalIgnoreCase);

        if (IsRhOrAdmin(actor?.Role))
        {
            foreach (var e in team)
                AddEmployeeNodes(map, e, catalog);
            return map.Values.ToList();
        }

        if (rebac is not null)
        {
            try
            {
                var cellules = await rebac.GetManagedNodeIdsAsync(actorId, "Superviseur", ct);
                var managedCelluleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in cellules)
                {
                    TryAddManagedNode(map, raw, QuotaScopeKinds.Cellule, team, catalog);
                    var id = QuotaCongeService.NormalizeNodeId(raw);
                    if (id is not null)
                        managedCelluleIds.Add(id);
                }

                var services = await rebac.GetManagedNodeIdsAsync(actorId, "ReferentTechnique", ct);
                foreach (var raw in services)
                {
                    var svcId = QuotaCongeService.NormalizeNodeId(raw);
                    if (managedCelluleIds.Count > 0
                        && svcId is not null
                        && !catalog.IsServiceUnderCellules(svcId, managedCelluleIds))
                        continue;
                    TryAddManagedNode(map, raw, QuotaScopeKinds.Service, team, catalog);
                }

                // Superviseur d'une cellule : exposer aussi les quotas des services
                // des agents de l'équipe rattachés à ces cellules (pas seulement ReferentTechnique).
                // Un OrgServiceId obsolète (ex. autre cellule) est exclu via l'arbre Directory.
                if (managedCelluleIds.Count > 0)
                {
                    foreach (var e in team)
                    {
                        var empCellule = QuotaCongeService.NormalizeNodeId(e.CelluleId);
                        if (empCellule is null || !managedCelluleIds.Contains(empCellule))
                            continue;
                        if (!TryResolveServiceId(e, out var serviceId))
                            continue;
                        if (!catalog.IsServiceUnderCellules(serviceId, managedCelluleIds))
                            continue;
                        UpsertBucket(map, serviceId, QuotaScopeKinds.Service, e, catalog);
                    }
                }
            }
            catch
            {
                /* soft-wire */
            }
        }

        if (map.Count == 0)
        {
            foreach (var e in team)
                AddEmployeeNodes(map, e, catalog);
        }

        return map.Values.ToList();
    }

    private static void TryAddManagedNode(
        Dictionary<string, QuotaNode> map,
        string raw,
        string scopeKind,
        IReadOnlyList<EmployeSnapshot> team,
        DirectoryOrgCatalogSnapshot catalog)
    {
        var id = QuotaCongeService.NormalizeNodeId(raw);
        if (id is null) return;

        var peers = team.Where(e => MatchesNode(e, id)).ToList();
        var label = ResolveNodeLabel(peers.FirstOrDefault(), id, scopeKind, catalog);
        var effectif = catalog.GetHeadcount(id, scopeKind);
        if (effectif == 0)
            effectif = peers.Count;
        map[id] = new QuotaNode(id, scopeKind, label, effectif);
    }

    private static void AddEmployeeNodes(
        Dictionary<string, QuotaNode> map,
        EmployeSnapshot e,
        DirectoryOrgCatalogSnapshot catalog)
    {
        var celluleId = QuotaCongeService.NormalizeNodeId(e.CelluleId);
        if (celluleId is not null)
            UpsertBucket(map, celluleId, QuotaScopeKinds.Cellule, e, catalog);

        if (TryResolveServiceId(e, out var serviceId))
            UpsertBucket(map, serviceId, QuotaScopeKinds.Service, e, catalog);
    }

    private static void UpsertBucket(
        Dictionary<string, QuotaNode> map,
        string id,
        string scopeKind,
        EmployeSnapshot sample,
        DirectoryOrgCatalogSnapshot catalog)
    {
        var label = ResolveNodeLabel(sample, id, scopeKind, catalog);
        var catalogCount = catalog.GetHeadcount(id, scopeKind);
        if (map.TryGetValue(id, out var cur))
        {
            var effectif = catalogCount > 0 ? catalogCount : cur.Effectif + 1;
            map[id] = cur with
            {
                Effectif = effectif,
                Label = PreferNom(cur.Label, label)
            };
        }
        else
        {
            map[id] = new QuotaNode(id, scopeKind, label, catalogCount > 0 ? catalogCount : 1);
        }
    }

    internal static bool MatchesNode(EmployeSnapshot e, string nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId)) return false;
        return string.Equals(e.OrgServiceId, nodeId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(e.CelluleId, nodeId, StringComparison.OrdinalIgnoreCase)
            || (e.ServiceId != Guid.Empty
                && string.Equals(e.ServiceId.ToString(), nodeId, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryResolveServiceId(EmployeSnapshot e, out string serviceId)
    {
        var org = QuotaCongeService.NormalizeNodeId(e.OrgServiceId);
        if (org is not null)
        {
            serviceId = org;
            return true;
        }

        if (e.ServiceId != Guid.Empty)
        {
            serviceId = e.ServiceId.ToString();
            return true;
        }

        serviceId = string.Empty;
        return false;
    }

    internal static string InferScopeKind(EmployeSnapshot? e, string nodeId)
    {
        if (e is null) return QuotaScopeKinds.Service;
        if (string.Equals(e.CelluleId, nodeId, StringComparison.OrdinalIgnoreCase))
            return QuotaScopeKinds.Cellule;
        return QuotaScopeKinds.Service;
    }

    internal static string ResolveNodeLabel(
        EmployeSnapshot? e,
        string nodeId,
        string scopeKind,
        DirectoryOrgCatalogSnapshot? catalog = null)
    {
        var fromCatalog = catalog?.GetName(nodeId);
        if (!string.IsNullOrWhiteSpace(fromCatalog))
            return fromCatalog;

        var shortId = nodeId.Length <= 12 ? nodeId : nodeId[..12];
        var fallback = scopeKind == QuotaScopeKinds.Cellule
            ? $"Cellule {shortId}"
            : $"Service {shortId}";

        if (e is null)
            return fallback;

        // ServiceNom n'est un vrai libellé que s'il ne ressemble pas à un id org.
        var nom = (e.ServiceNom ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(nom)
            || LooksLikeOrgId(nom)
            || string.Equals(nom, e.OrgServiceId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(nom, e.CelluleId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(nom, nodeId, StringComparison.OrdinalIgnoreCase))
        {
            return fallback;
        }

        // Ne pas réutiliser le nom de service pour une cellule.
        if (scopeKind == QuotaScopeKinds.Cellule
            && !string.Equals(e.CelluleId, nodeId, StringComparison.OrdinalIgnoreCase))
            return fallback;

        return nom;
    }

    private static bool LooksLikeOrgId(string value)
    {
        if (Guid.TryParse(value, out _)) return true;
        var v = value.Trim();
        return v.StartsWith("cell-", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("svc-", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("pole-", StringComparison.OrdinalIgnoreCase);
    }

    private static string PreferNom(string current, string candidate)
    {
        if ((current.StartsWith("Service ", StringComparison.Ordinal)
             || current.StartsWith("Cellule ", StringComparison.Ordinal))
            && !(candidate.StartsWith("Service ", StringComparison.Ordinal)
                 || candidate.StartsWith("Cellule ", StringComparison.Ordinal)))
            return candidate;
        return current;
    }

    private static async Task<IReadOnlyList<string>?> TryGetManagedNodeIdsAsync(
        IRebacClient? rebac,
        Guid actorId,
        CancellationToken ct)
    {
        if (rebac is null) return null;
        try
        {
            var superviseurNodes = await rebac.GetManagedNodeIdsAsync(actorId, "Superviseur", ct);
            var referentNodes = await rebac.GetManagedNodeIdsAsync(actorId, "ReferentTechnique", ct);
            var list = superviseurNodes.Concat(referentNodes)
                .Select(QuotaCongeService.NormalizeNodeId)
                .Where(n => n is not null)
                .Cast<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return list.Count > 0 ? list : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsRhOrAdmin(string? role)
    {
        var r = role?.Trim() ?? string.Empty;
        return r.Equals("RH", StringComparison.OrdinalIgnoreCase)
            || r.Equals("Admin", StringComparison.OrdinalIgnoreCase);
    }
}
