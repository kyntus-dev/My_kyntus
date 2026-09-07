using System.Text.Json;
using System.Text.Json.Serialization;
using Kyntus.Caching;
using Microsoft.EntityFrameworkCore;
using Prime.Application;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Domain.Entities;
using Prime.Infrastructure.Persistence;

namespace Prime.Infrastructure.Services;

public sealed class CommonLinePonderationResolver(
    PrimeDbContext db,
    ICacheService? cache = null) : ICommonLinePonderationResolver
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<IReadOnlyList<EffectiveCommonLinePonderationDto>> ResolveAsync(
        string? serviceId,
        string? celluleId,
        string templateId,
        DateTimeOffset at,
        IReadOnlyList<TemplateCommonLineHint>? templateLines = null,
        IReadOnlyList<TemplateCommonLineHint>? previousPeriodLines = null,
        CancellationToken ct = default)
    {
        var atDay = CommonLinePonderationPeriod.StartOfUtcDay(at);
        var tid = (templateId ?? "").Trim();
        var sid = (serviceId ?? "").Trim();
        var cid = (celluleId ?? "").Trim();
        var hasHints = (templateLines is { Count: > 0 }) || (previousPeriodLines is { Count: > 0 });

        if (cache is null || hasHints)
            return await ResolveCoreAsync(sid, cid, tid, atDay, templateLines, previousPeriodLines, ct);

        var scope = sid.Length > 0 && cid.Length > 0 ? "svc+cell"
            : sid.Length > 0 ? CommonLinePonderationScopes.Service
            : CommonLinePonderationScopes.Cellule;
        var scopeId = sid.Length > 0 && cid.Length > 0 ? $"{sid}:{cid}"
            : sid.Length > 0 ? sid
            : cid;
        var day = atDay.UtcDateTime.ToString("yyyy-MM-dd");
        var key = KyntusCacheKeys.PrimePonderation(scope, scopeId, tid, day);

        return await cache.GetOrCreateAsync(
            key,
            token => ResolveCoreAsync(sid, cid, tid, atDay, null, null, token),
            KyntusCacheTtl.Computation,
            ct);
    }

    private async Task<IReadOnlyList<EffectiveCommonLinePonderationDto>> ResolveCoreAsync(
        string sid,
        string cid,
        string tid,
        DateTimeOffset atDay,
        IReadOnlyList<TemplateCommonLineHint>? templateLines,
        IReadOnlyList<TemplateCommonLineHint>? previousPeriodLines,
        CancellationToken ct)
    {
        var serviceRows = sid.Length == 0
            ? []
            : await LoadActiveAsync(CommonLinePonderationScopes.Service, sid, tid, atDay, ct);
        if (sid.Length > 0 && serviceRows.Count == 0)
            serviceRows = await LoadLegacyServiceFallbackAsync(sid, atDay, ct);
        var celluleRows = cid.Length == 0
            ? []
            : await LoadActiveAsync(CommonLinePonderationScopes.Cellule, cid, tid, atDay, ct);

        var serviceByStable = PickBestByStableId(serviceRows, tid);
        var celluleByStable = PickBestByStableId(celluleRows, tid);
        var prevByStable = IndexHints(previousPeriodLines);
        var tplByStable = IndexHints(templateLines);

        var result = new List<EffectiveCommonLinePonderationDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddResolved(string stableId, TemplateCommonLineHint? hint)
        {
            if (!seen.Add(stableId)) return;
            if (serviceByStable.TryGetValue(stableId, out var svc))
            {
                result.Add(Map(svc, CommonLinePonderationSources.Service, inherited: false, hint));
                return;
            }

            if (celluleByStable.TryGetValue(stableId, out var cell))
            {
                result.Add(Map(cell, CommonLinePonderationSources.Cellule, inherited: sid.Length > 0, hint));
                return;
            }

            if (prevByStable.TryGetValue(stableId, out var prev) &&
                (prev.TemplatePrimePct is not null || prev.TemplateChallengePct is not null))
            {
                result.Add(new EffectiveCommonLinePonderationDto
                {
                    TemplateStableId = stableId,
                    Label = string.IsNullOrWhiteSpace(prev.Label)
                        ? (hint?.Label ?? stableId)
                        : prev.Label,
                    Contract = string.IsNullOrWhiteSpace(prev.Contract)
                        ? (hint?.Contract ?? "")
                        : prev.Contract,
                    SortOrder = prev.SortOrder != 0 ? prev.SortOrder : (hint?.SortOrder ?? result.Count),
                    PonderationPrimePct = prev.TemplatePrimePct,
                    PonderationChallengePct = prev.TemplateChallengePct,
                    SourceScope = CommonLinePonderationSources.PreviousPeriod,
                    SourceScopeId = null,
                    Inherited = false,
                    EffectiveFrom = null,
                    VersionId = null,
                });
                return;
            }

            var tpl = hint ?? (tplByStable.TryGetValue(stableId, out var h) ? h : null);
            result.Add(new EffectiveCommonLinePonderationDto
            {
                TemplateStableId = stableId,
                Label = tpl?.Label ?? stableId,
                Contract = tpl?.Contract ?? "",
                SortOrder = tpl?.SortOrder ?? result.Count,
                PonderationPrimePct = tpl?.TemplatePrimePct,
                PonderationChallengePct = tpl?.TemplateChallengePct,
                SourceScope = tpl?.TemplatePrimePct is not null || tpl?.TemplateChallengePct is not null
                    ? CommonLinePonderationSources.Template
                    : CommonLinePonderationSources.Undefined,
                SourceScopeId = null,
                Inherited = false,
                EffectiveFrom = null,
                VersionId = null,
            });
        }

        // Ordre d’énumération : lignes template (schéma) d’abord, puis surcharges DB seules.
        if (templateLines is { Count: > 0 })
        {
            foreach (var hint in templateLines)
            {
                var stable = (hint.TemplateStableId ?? "").Trim();
                if (stable.Length == 0) continue;
                AddResolved(stable, hint);
            }
        }
        else if (previousPeriodLines is { Count: > 0 })
        {
            foreach (var hint in previousPeriodLines)
            {
                var stable = (hint.TemplateStableId ?? "").Trim();
                if (stable.Length == 0) continue;
                AddResolved(stable, hint);
            }
        }

        foreach (var kv in serviceByStable.OrderBy(x => x.Value.SortOrder).ThenBy(x => x.Key, StringComparer.Ordinal))
            AddResolved(kv.Key, null);
        foreach (var kv in celluleByStable.OrderBy(x => x.Value.SortOrder).ThenBy(x => x.Key, StringComparer.Ordinal))
            AddResolved(kv.Key, null);

        return result
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.TemplateStableId, StringComparer.Ordinal)
            .ToList();
    }

    public async Task FreezeOntoFicheIfMissingAsync(
        EmployeePrimeServiceFiche fiche,
        string templateId,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(fiche.PonderationsSnapshotJson))
            return;

        var at = CommonLinePonderationPeriod.ForLiveResolve();
        var hints = await LoadHintsFromLatestDraftAsync(fiche.CelluleId, templateId, ct);
        var prev = await BuildPreviousPeriodHintsAsync(fiche.CelluleId, templateId, at, ct);
        var resolved = await ResolveAsync(
            fiche.ServiceId,
            fiche.CelluleId,
            templateId,
            at,
            hints,
            prev,
            ct);
        fiche.PonderationsSnapshotJson = SerializeSnapshot(resolved, at);
    }

    public static string SerializeSnapshot(
        IReadOnlyList<EffectiveCommonLinePonderationDto> items,
        DateTimeOffset resolvedAt) =>
        JsonSerializer.Serialize(
            new CommonLinePonderationSnapshotV1
            {
                Version = 1,
                ResolvedAt = resolvedAt,
                Items = items.ToList(),
            },
            JsonOpts);

    public static IReadOnlyList<EffectiveCommonLinePonderationDto>? TryParseSnapshot(string? json)
    {
        var t = (json ?? "").Trim();
        if (t.Length == 0) return null;
        try
        {
            var snap = JsonSerializer.Deserialize<CommonLinePonderationSnapshotV1>(t, JsonOpts);
            if (snap is null || snap.Version != 1) return null;
            return snap.Items;
        }
        catch
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<TemplateCommonLineHint>> LoadHintsFromLatestDraftAsync(
        string? celluleId,
        string templateId,
        CancellationToken ct)
    {
        var cid = (celluleId ?? "").Trim();
        var tid = (templateId ?? "").Trim();
        if (cid.Length == 0) return [];

        var q = db.SupervisorCellulePrimeDrafts.AsNoTracking()
            .Where(d => d.CelluleId == cid);
        if (tid.Length > 0)
            q = q.Where(d => d.TemplateId == tid);

        var schema = await q
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => d.SchemaJson)
            .FirstOrDefaultAsync(ct);

        return TemplateSchemaPonderationHints.FromSchemaJson(schema);
    }

    public async Task<IReadOnlyList<TemplateCommonLineHint>> BuildPreviousPeriodHintsAsync(
        string? celluleId,
        string templateId,
        DateTimeOffset at,
        CancellationToken ct)
    {
        var cid = (celluleId ?? "").Trim();
        if (cid.Length == 0) return [];

        var atDay = CommonLinePonderationPeriod.StartOfUtcDay(at);
        var prevMonthEnd = new DateTimeOffset(atDay.Year, atDay.Month, 1, 0, 0, 0, TimeSpan.Zero)
            .AddDays(-1);
        var tid = (templateId ?? "").Trim();

        // Résolution « pure » DB (sans défauts) à la fin du mois précédent.
        var prevResolved = await ResolveAsync(
            serviceId: null,
            celluleId: cid,
            templateId: tid,
            at: prevMonthEnd,
            templateLines: null,
            previousPeriodLines: null,
            ct);

        return prevResolved
            .Where(x =>
                string.Equals(x.SourceScope, CommonLinePonderationSources.Cellule, StringComparison.OrdinalIgnoreCase) &&
                (x.PonderationPrimePct is not null || x.PonderationChallengePct is not null))
            .Select(x => new TemplateCommonLineHint
            {
                TemplateStableId = x.TemplateStableId,
                Label = x.Label,
                Contract = x.Contract,
                SortOrder = x.SortOrder,
                TemplatePrimePct = x.PonderationPrimePct,
                TemplateChallengePct = x.PonderationChallengePct,
            })
            .ToList();
    }

    private async Task<List<CommonLinePonderationEntity>> LoadActiveAsync(
        string scopeType,
        string scopeId,
        string templateId,
        DateTimeOffset at,
        CancellationToken ct)
    {
        return await db.CommonLinePonderations.AsNoTracking()
            .Where(x =>
                x.ScopeType == scopeType &&
                x.ScopeId == scopeId &&
                (x.TemplateId == templateId || x.TemplateId == "") &&
                x.EffectiveFrom <= at &&
                (x.EffectiveTo == null || x.EffectiveTo >= at))
            .ToListAsync(ct);
    }

    private async Task<List<CommonLinePonderationEntity>> LoadLegacyServiceFallbackAsync(
        string serviceId,
        DateTimeOffset at,
        CancellationToken ct)
    {
        var legacy = await db.ServicePoleLinePonderations.AsNoTracking()
            .Where(x => x.ServiceId == serviceId)
            .ToListAsync(ct);
        return legacy.Select(x => new CommonLinePonderationEntity
        {
            Id = x.Id,
            ScopeType = CommonLinePonderationScopes.Service,
            ScopeId = x.ServiceId,
            TemplateId = "",
            TemplateStableId = x.TemplateStableId,
            Label = x.Label,
            SortOrder = x.SortOrder,
            PonderationPrimePct = x.PonderationPrimePct,
            PonderationChallengePct = x.PonderationChallengePct,
            EffectiveFrom = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CreatedAt = x.CreatedAt,
        }).Where(x => x.EffectiveFrom <= at).ToList();
    }

    private static Dictionary<string, CommonLinePonderationEntity> PickBestByStableId(
        IEnumerable<CommonLinePonderationEntity> rows,
        string templateId)
    {
        var map = new Dictionary<string, CommonLinePonderationEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows
                     .OrderByDescending(x => string.Equals(x.TemplateId, templateId, StringComparison.Ordinal))
                     .ThenByDescending(x => x.EffectiveFrom)
                     .ThenByDescending(x => x.CreatedAt))
        {
            var key = (row.TemplateStableId ?? "").Trim();
            if (key.Length == 0) continue;
            map.TryAdd(key, row);
        }

        return map;
    }

    private static Dictionary<string, TemplateCommonLineHint> IndexHints(
        IReadOnlyList<TemplateCommonLineHint>? lines)
    {
        var map = new Dictionary<string, TemplateCommonLineHint>(StringComparer.OrdinalIgnoreCase);
        if (lines is null) return map;
        foreach (var h in lines)
        {
            var key = (h.TemplateStableId ?? "").Trim();
            if (key.Length == 0) continue;
            map.TryAdd(key, h);
        }

        return map;
    }

    private static EffectiveCommonLinePonderationDto Map(
        CommonLinePonderationEntity e,
        string source,
        bool inherited,
        TemplateCommonLineHint? hint) =>
        new()
        {
            TemplateStableId = e.TemplateStableId,
            Label = string.IsNullOrWhiteSpace(e.Label) ? (hint?.Label ?? e.TemplateStableId) : e.Label,
            Contract = string.IsNullOrWhiteSpace(e.Contract) ? (hint?.Contract ?? "") : e.Contract,
            SortOrder = e.SortOrder,
            PonderationPrimePct = e.PonderationPrimePct,
            PonderationChallengePct = e.PonderationChallengePct,
            SourceScope = source,
            SourceScopeId = e.ScopeId,
            Inherited = inherited,
            EffectiveFrom = e.EffectiveFrom,
            VersionId = e.Id,
        };

    private sealed class CommonLinePonderationSnapshotV1
    {
        public int Version { get; set; } = 1;
        public DateTimeOffset ResolvedAt { get; set; }
        public List<EffectiveCommonLinePonderationDto> Items { get; set; } = [];
    }
}
