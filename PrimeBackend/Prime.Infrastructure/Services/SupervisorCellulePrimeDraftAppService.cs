using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Prime.Application;
using Prime.Application.Abstractions;
using Prime.Application.DTOs;
using Prime.Application.Rollover;
using Prime.Domain.Entities;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;

namespace Prime.Infrastructure.Services;

public sealed class SupervisorCellulePrimeDraftAppService(
    PrimeDbContext db,
    PrimeOrgScopeService org,
    PrimeFicheValidationSubmissionService submission,
    ICommonLinePonderationResolver ponderationResolver,
    PrimeValidationWorkflowRuntime wfRuntime) : ISupervisorCellulePrimeDraftAppService
{
    private static SupervisorCellulePrimeDraftResponseDto Map(SupervisorCellulePrimeDraft e) =>
        new()
        {
            Id = e.Id,
            SupervisorUserId = e.SupervisorUserId,
            CelluleId = e.CelluleId,
            Period = e.Period,
            TemplateId = e.TemplateId,
            TemplateDisplayName = e.TemplateDisplayName,
            TemplateFormatVersion = e.TemplateFormatVersion,
            Status = e.Status,
            SchemaJson = e.SchemaJson,
            CelluleSaisieJson = e.CelluleSaisieJson,
            ComputedJson = e.ComputedJson,
            TemplateCalcSnapshotJson = e.TemplateCalcSnapshotJson,
            UpdatedAt = e.UpdatedAt,
        };

    public async Task<SupervisorCellulePrimeDraftResponseDto?> GetAsync(
        string supervisorUserId,
        string? celluleId,
        string? poleId,
        string period,
        string templateId,
        CancellationToken ct = default)
    {
        var rawKey = !string.IsNullOrWhiteSpace(celluleId) ? celluleId.Trim() : (poleId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(supervisorUserId) || string.IsNullOrWhiteSpace(rawKey) ||
            string.IsNullOrWhiteSpace(period) || string.IsNullOrWhiteSpace(templateId))
            throw new ArgumentException("supervisorUserId, celluleId (ou poleId), period et templateId sont requis.");

        var celluleCanon = await org.NormalizeSupervisorDraftCelluleKeyAsync(supervisorUserId, rawKey, ct)
            ?? throw new UnauthorizedAccessException("Accès refusé pour ce périmètre.");

        var entity = await db.SupervisorCellulePrimeDrafts.AsNoTracking().FirstOrDefaultAsync(
            x => x.SupervisorUserId == supervisorUserId.Trim() && x.CelluleId == celluleCanon &&
                 x.Period == period.Trim() && x.TemplateId == templateId.Trim(), ct);
        return entity is null ? null : Map(entity);
    }

    public async Task<IReadOnlyList<SupervisorCellulePrimeDraftListItemDto>> ListActiveAsync(
        string supervisorUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(supervisorUserId))
            throw new ArgumentException("supervisorUserId est requis.");

        var supTrim = supervisorUserId.Trim();
        var celluleIds = await org.GetSupervisedCelluleIdsAsync(supTrim, ct);
        if (celluleIds.Count == 0) return [];

        var drafts = await db.SupervisorCellulePrimeDrafts.AsNoTracking()
            .Where(d => d.SupervisorUserId == supTrim && celluleIds.Contains(d.CelluleId))
            .ToListAsync(ct);
        if (drafts.Count == 0) return [];

        var draftIds = drafts.Select(d => d.Id).ToList();
        var fiches = await db.EmployeePrimeServiceFiches.AsNoTracking()
            .Where(f => draftIds.Contains(f.CellulePrimeDraftId))
            .ToListAsync(ct);
        var fichesByDraft = fiches
            .GroupBy(f => f.CellulePrimeDraftId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var employeesByPole = await org.GetEmployeeCountsByCelluleAsync(celluleIds, ct);

        var result = new List<SupervisorCellulePrimeDraftListItemDto>();
        foreach (var draft in drafts
                     .GroupBy(d => new { d.RootPoleId, d.Period })
                     .Select(g => g.OrderByDescending(x => x.UpdatedAt).First()))
        {
            var total = employeesByPole.TryGetValue(draft.CelluleId, out var t) ? t : 0;
            fichesByDraft.TryGetValue(draft.Id, out var draftFiches);

            var complete = 0;
            var inProgress = 0;
            if (draftFiches != null)
            {
                foreach (var f in draftFiches)
                {
                    if (string.Equals(f.FillingStatus, "Complete", StringComparison.OrdinalIgnoreCase)) complete++;
                    else if (string.Equals(f.FillingStatus, "InProgress", StringComparison.OrdinalIgnoreCase)) inProgress++;
                }
            }
            var notStarted = Math.Max(0, total - complete - inProgress);

            var isValidated = string.Equals(draft.Status, "Validated", StringComparison.OrdinalIgnoreCase);
            var isFullyComplete = isValidated && total > 0 && complete == total;
            if (isFullyComplete) continue;

            result.Add(new SupervisorCellulePrimeDraftListItemDto
            {
                Id = draft.Id,
                SupervisorUserId = draft.SupervisorUserId,
                CelluleId = draft.CelluleId,
                Period = draft.Period,
                TemplateId = draft.TemplateId,
                TemplateDisplayName = draft.TemplateDisplayName,
                TemplateFormatVersion = draft.TemplateFormatVersion,
                Status = draft.Status,
                TotalEmployees = total,
                CompleteEmployees = complete,
                InProgressEmployees = inProgress,
                NotStartedEmployees = notStarted,
                IsFullyComplete = false,
                UpdatedAt = draft.UpdatedAt,
                HasGlobalPoolFile = draft.GlobalPoolExcelContent is { Length: > 0 },
                PoolDistributionUnlocked = GlobalPoolWorkflowService.LegacyPoolUnlocked(draft),
            });
        }

        return result
            .OrderByDescending(r => r.Period)
            .ThenByDescending(r => r.UpdatedAt)
            .ToList();
    }

    public async Task<SupervisorCellulePrimeDraftResponseDto> UpsertAsync(
        UpsertSupervisorCellulePrimeDraftRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.CelluleId) && !string.IsNullOrWhiteSpace(body.PoleId))
            body.CelluleId = body.PoleId.Trim();
        if (!string.IsNullOrWhiteSpace(body.PoleSaisieJson) &&
            (string.IsNullOrWhiteSpace(body.CelluleSaisieJson) || string.Equals(body.CelluleSaisieJson.Trim(), "{}", StringComparison.Ordinal)))
            body.CelluleSaisieJson = body.PoleSaisieJson.Trim();

        if (string.IsNullOrWhiteSpace(body.SupervisorUserId) || string.IsNullOrWhiteSpace(body.CelluleId) ||
            string.IsNullOrWhiteSpace(body.Period) || string.IsNullOrWhiteSpace(body.TemplateId))
            throw new ArgumentException("Champs obligatoires manquants.");

        var celluleCanon = await org.NormalizeSupervisorDraftCelluleKeyAsync(body.SupervisorUserId, body.CelluleId, ct)
            ?? throw new UnauthorizedAccessException(
                "Accès refusé pour ce périmètre (identifiant cellule / pôle non reconnu pour ce superviseur).");
        body.CelluleId = celluleCanon;

        var poleSaisieToStore = CelluleDraftPayloadNormalizer.NormalizeCelluleSaisieJson(body.SchemaJson, body.CelluleSaisieJson);

        if (string.Equals(body.Status?.Trim(), "Validated", StringComparison.OrdinalIgnoreCase))
        {
            var unconfirmed = PrimeSaisieRolloverMerger.CountUnconfirmedCarriedLines(poleSaisieToStore);
            if (unconfirmed > 0)
                throw new ArgumentException(
                    $"{unconfirmed} ligne(s) reprise(s) du mois précédent ne sont pas encore confirmées. Utilisez « Tout confirmer » ou modifiez les valeurs mesurées.");
        }

        var supTrim = body.SupervisorUserId.Trim();
        var poleTrim = body.CelluleId.Trim();
        var periodTrim = body.Period.Trim();
        var templateTrim = body.TemplateId.Trim();

        var rootPoleId = await org.ResolveRootPoleIdForCelluleAsync(poleTrim, ct);
        if (string.IsNullOrWhiteSpace(rootPoleId))
            throw new ArgumentException(
                "Impossible de résoudre le pôle racine pour cette cellule. Vérifiez la structure RH (prime_pole / prime_cellule).");

        var now = DateTimeOffset.UtcNow;
        var entity = await db.SupervisorCellulePrimeDrafts.FirstOrDefaultAsync(
            x => x.SupervisorUserId == supTrim && x.RootPoleId == rootPoleId && x.Period == periodTrim, ct);

        if (entity is null)
        {
            var stale = await db.SupervisorCellulePrimeDrafts
                .Where(x => x.SupervisorUserId == supTrim && x.RootPoleId == rootPoleId && x.Period == periodTrim)
                .ToListAsync(ct);
            if (stale.Count > 0) db.SupervisorCellulePrimeDrafts.RemoveRange(stale);

            entity = new SupervisorCellulePrimeDraft
            {
                Id = Guid.NewGuid(),
                SupervisorUserId = supTrim,
                RootPoleId = rootPoleId,
                CelluleId = poleTrim,
                Period = periodTrim,
                TemplateId = templateTrim,
                TemplateDisplayName = (body.TemplateDisplayName ?? "").Trim(),
                TemplateFormatVersion = body.TemplateFormatVersion,
                Status = string.IsNullOrWhiteSpace(body.Status) ? "Draft" : body.Status!.Trim(),
                SchemaJson = body.SchemaJson,
                CelluleSaisieJson = poleSaisieToStore,
                ComputedJson = body.ComputedJson,
                TemplateCalcSnapshotJson = body.TemplateCalcSnapshotJson,
                UpdatedAt = now,
            };
            db.SupervisorCellulePrimeDrafts.Add(entity);
        }
        else
        {
            entity.RootPoleId = rootPoleId;
            entity.CelluleId = poleTrim;
            entity.TemplateId = templateTrim;
            entity.TemplateDisplayName = (body.TemplateDisplayName ?? "").Trim();
            entity.TemplateFormatVersion = body.TemplateFormatVersion;
            if (!string.IsNullOrWhiteSpace(body.Status)) entity.Status = body.Status.Trim();
            entity.SchemaJson = body.SchemaJson;
            entity.CelluleSaisieJson = poleSaisieToStore;
            entity.ComputedJson = body.ComputedJson;
            entity.TemplateCalcSnapshotJson = body.TemplateCalcSnapshotJson;
            entity.UpdatedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            throw new PrimeApiException(409, DbExceptionMessages.FromSaveChanges(ex));
        }

        await submission.SyncForDraftAsync(entity.Id, ct);
        await db.SaveChangesAsync(ct);

        return Map(entity);
    }

    public async Task<CelluleDraftRolloverResultDto> RolloverAsync(
        RolloverCellulePrimeDraftRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.CelluleId) && !string.IsNullOrWhiteSpace(body.PoleId))
            body.CelluleId = body.PoleId.Trim();

        if (string.IsNullOrWhiteSpace(body.SupervisorUserId) || string.IsNullOrWhiteSpace(body.CelluleId) ||
            string.IsNullOrWhiteSpace(body.TargetPeriod))
            throw new ArgumentException("supervisorUserId, celluleId et targetPeriod sont requis.");

        var supTrim = body.SupervisorUserId.Trim();
        var celluleCanon = await org.NormalizeSupervisorDraftCelluleKeyAsync(supTrim, body.CelluleId, ct)
            ?? throw new UnauthorizedAccessException("Accès refusé pour ce périmètre.");

        var targetPeriod = body.TargetPeriod.Trim();
        var sourcePeriod = string.IsNullOrWhiteSpace(body.SourcePeriod)
            ? PreviousPeriod(targetPeriod)
            : body.SourcePeriod!.Trim();

        var rootPoleId = await org.ResolveRootPoleIdForCelluleAsync(celluleCanon, ct)
            ?? throw new ArgumentException("Impossible de résoudre le pôle racine pour cette cellule.");

        var sourceDraft = await db.SupervisorCellulePrimeDrafts.AsNoTracking()
            .Where(x => x.SupervisorUserId == supTrim && x.RootPoleId == rootPoleId && x.Period == sourcePeriod)
            .OrderByDescending(x => x.UpdatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException($"Aucune fiche commune trouvée pour la période {sourcePeriod}.");

        if (!body.AllowUnvalidatedSource &&
            !string.Equals(sourceDraft.Status, "Validated", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException(
                $"La fiche source ({sourcePeriod}) n'est pas validée. Validez-la ou autorisez la reconduction depuis un brouillon.");

        var existingTarget = await db.SupervisorCellulePrimeDrafts
            .FirstOrDefaultAsync(x => x.SupervisorUserId == supTrim && x.RootPoleId == rootPoleId && x.Period == targetPeriod, ct);
        if (existingTarget is not null && !body.Overwrite)
            throw new PrimeApiException(409, $"Une fiche existe déjà pour {targetPeriod}. Utilisez overwrite pour remplacer.");

        var at = CommonLinePonderationPeriod.StartOfUtcDay(ParsePeriodStart(targetPeriod));
        var hints = TemplateSchemaPonderationHints.FromSchemaJson(sourceDraft.SchemaJson);
        var prevHints = await ponderationResolver.BuildPreviousPeriodHintsAsync(celluleCanon, sourceDraft.TemplateId, at, ct);
        var resolved = await ponderationResolver.ResolveAsync(null, celluleCanon, sourceDraft.TemplateId, at, hints, prevHints, ct);
        var pondMap = resolved.ToDictionary(
            r => r.TemplateStableId,
            r => (r.PonderationPrimePct, r.PonderationChallengePct),
            StringComparer.OrdinalIgnoreCase);

        var merge = PrimeSaisieRolloverMerger.MergeTemplatePayload(
            sourceDraft.CelluleSaisieJson,
            sourceDraft.SchemaJson,
            sourcePeriod,
            pondMap);

        var warnings = new List<string>();
        if (merge.LinesNew.Count > 0)
            warnings.Add($"{merge.LinesNew.Count} nouveau(x) KPI à saisir.");
        if (merge.LinesDropped.Count > 0)
            warnings.Add($"{merge.LinesDropped.Count} ligne(s) disparue(s) du gabarit.");

        var now = DateTimeOffset.UtcNow;
        SupervisorCellulePrimeDraft entity;
        if (existingTarget is not null)
        {
            entity = existingTarget;
            entity.CelluleId = celluleCanon;
            entity.TemplateId = sourceDraft.TemplateId;
            entity.TemplateDisplayName = sourceDraft.TemplateDisplayName;
            entity.TemplateFormatVersion = sourceDraft.TemplateFormatVersion;
            entity.Status = "Draft";
            entity.SchemaJson = sourceDraft.SchemaJson;
            entity.CelluleSaisieJson = CelluleDraftPayloadNormalizer.NormalizeCelluleSaisieJson(
                sourceDraft.SchemaJson, merge.MergedJson);
            entity.ComputedJson = null;
            entity.TemplateCalcSnapshotJson = sourceDraft.TemplateCalcSnapshotJson;
            entity.UpdatedAt = now;
        }
        else
        {
            entity = new SupervisorCellulePrimeDraft
            {
                Id = Guid.NewGuid(),
                SupervisorUserId = supTrim,
                RootPoleId = rootPoleId,
                CelluleId = celluleCanon,
                Period = targetPeriod,
                TemplateId = sourceDraft.TemplateId,
                TemplateDisplayName = sourceDraft.TemplateDisplayName,
                TemplateFormatVersion = sourceDraft.TemplateFormatVersion,
                Status = "Draft",
                SchemaJson = sourceDraft.SchemaJson,
                CelluleSaisieJson = CelluleDraftPayloadNormalizer.NormalizeCelluleSaisieJson(
                    sourceDraft.SchemaJson, merge.MergedJson),
                ComputedJson = null,
                TemplateCalcSnapshotJson = sourceDraft.TemplateCalcSnapshotJson,
                UpdatedAt = now,
            };
            db.SupervisorCellulePrimeDrafts.Add(entity);
        }

        await db.SaveChangesAsync(ct);

        var fichesCreated = 0;
        var skipped = new List<CelluleDraftRolloverSkippedFicheDto>();
        if (body.IncludeEmployeeFiches)
        {
            (fichesCreated, skipped) = await RolloverEmployeeFichesAsync(
                entity, sourcePeriod, targetPeriod, supTrim, ct);
            await db.SaveChangesAsync(ct);
        }

        return new CelluleDraftRolloverResultDto
        {
            DraftId = entity.Id,
            SourcePeriod = sourcePeriod,
            TargetPeriod = targetPeriod,
            TemplateId = entity.TemplateId,
            LinesCarried = merge.LinesCarried,
            LinesNew = merge.LinesNew,
            LinesDropped = merge.LinesDropped,
            FichesCreated = fichesCreated,
            FichesSkipped = skipped,
            Warnings = warnings,
        };
    }

    private async Task<(int Created, List<CelluleDraftRolloverSkippedFicheDto> Skipped)> RolloverEmployeeFichesAsync(
        SupervisorCellulePrimeDraft newDraft,
        string sourcePeriod,
        string targetPeriod,
        string supervisorUserId,
        CancellationToken ct)
    {
        var skipped = new List<CelluleDraftRolloverSkippedFicheDto>();
        var created = 0;

        var pilots = await org.GetPilotsInCelluleAsync(newDraft.CelluleId, ct);
        if (pilots.Count == 0) return (0, skipped);

        var sourceDraftId = await db.SupervisorCellulePrimeDrafts.AsNoTracking()
            .Where(d => d.SupervisorUserId == supervisorUserId && d.CelluleId == newDraft.CelluleId && d.Period == sourcePeriod)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => d.Id)
            .FirstOrDefaultAsync(ct);

        if (sourceDraftId == Guid.Empty) return (0, skipped);

        var sourceFiches = await db.EmployeePrimeServiceFiches
            .Where(f => f.CellulePrimeDraftId == sourceDraftId && f.Period == sourcePeriod)
            .ToListAsync(ct);
        var sourceByEmp = sourceFiches.ToDictionary(f => f.EmployeeId, StringComparer.Ordinal);

        foreach (var emp in pilots)
        {
            var existingTarget = await db.EmployeePrimeServiceFiches
                .FirstOrDefaultAsync(f => f.EmployeeId == emp.Id && f.Period == targetPeriod, ct);
            if (existingTarget is not null)
            {
                if (await wfRuntime.IsTerminalStatusAsync(existingTarget.ValidationStatus, ct))
                {
                    skipped.Add(new CelluleDraftRolloverSkippedFicheDto
                    {
                        EmployeeId = emp.Id,
                        Reason = "Fiche cible déjà validée (statut terminal).",
                    });
                    continue;
                }

                skipped.Add(new CelluleDraftRolloverSkippedFicheDto
                {
                    EmployeeId = emp.Id,
                    Reason = "Fiche cible déjà existante.",
                });
                continue;
            }

            var serviceJson = "{}";
            if (sourceByEmp.TryGetValue(emp.Id, out var src))
                serviceJson = MarkServiceSaisieCarried(src.ServiceSaisieJson, sourcePeriod);

            var indicators = await db.ServicePrimeIndicators.AsNoTracking()
                .Where(i => i.ServiceId == emp.ServiceId)
                .OrderBy(i => i.SortOrder)
                .ToListAsync(ct);
            var status = PrimeServiceFicheStatusHelper.ComputeFillingStatus(serviceJson, indicators);

            db.EmployeePrimeServiceFiches.Add(new EmployeePrimeServiceFiche
            {
                Id = Guid.NewGuid(),
                CellulePrimeDraftId = newDraft.Id,
                SupervisorUserId = supervisorUserId,
                EmployeeId = emp.Id,
                ServiceId = emp.ServiceId,
                CelluleId = emp.CelluleId,
                Period = targetPeriod,
                ServiceSaisieJson = serviceJson,
                FillingStatus = status,
                ValidationStatus = PrimeValidationWorkflowService.AwaitingData,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            created++;
        }

        return (created, skipped);
    }

    private static string MarkServiceSaisieCarried(string sourceJson, string sourcePeriod)
    {
        try
        {
            var root = JsonNode.Parse(string.IsNullOrWhiteSpace(sourceJson) ? "{}" : sourceJson) as JsonObject
                       ?? new JsonObject();
            if (root["rows"] is JsonArray rows)
            {
                foreach (var row in rows)
                {
                    if (row is not JsonObject ro) continue;
                    if (!RowHasMeasureValues(ro)) continue;
                    ro["carriedFrom"] = sourcePeriod;
                    ro["carriedConfirmed"] = false;
                }
            }

            root["carriedFrom"] = sourcePeriod;
            root["carriedConfirmed"] = false;
            return root.ToJsonString();
        }
        catch
        {
            return sourceJson;
        }
    }

    private static bool RowHasMeasureValues(JsonObject row)
    {
        foreach (var prop in row)
        {
            var key = prop.Key;
            if (key is "carriedFrom" or "carriedConfirmed" or "indicatorId") continue;
            if (key.Contains("ponderation", StringComparison.OrdinalIgnoreCase)) continue;
            if (key.Contains("kpiPoint", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("kpiChallenge", StringComparison.OrdinalIgnoreCase)) continue;
            if (prop.Value is JsonValue jv &&
                (jv.TryGetValue(out string? s) ? !string.IsNullOrWhiteSpace(s) :
                    jv.TryGetValue(out double d) && double.IsFinite(d)))
                return true;
        }

        return false;
    }

    private static string PreviousPeriod(string period)
    {
        var m = System.Text.RegularExpressions.Regex.Match(period, @"^(\d{4})-(\d{2})$");
        if (!m.Success) return period;
        var y = int.Parse(m.Groups[1].Value);
        var mo = int.Parse(m.Groups[2].Value);
        var d = new DateTime(y, mo, 1).AddMonths(-1);
        return $"{d.Year}-{d.Month:D2}";
    }

    private static DateTimeOffset ParsePeriodStart(string period)
    {
        var m = System.Text.RegularExpressions.Regex.Match(period, @"^(\d{4})-(\d{2})$");
        if (!m.Success) return DateTimeOffset.UtcNow;
        return new DateTimeOffset(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 1, 0, 0, 0, TimeSpan.Zero);
    }

    public async Task DeleteAsync(Guid id, string supervisorUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(supervisorUserId))
            throw new ArgumentException("supervisorUserId est requis.");

        var entity = await db.SupervisorCellulePrimeDrafts.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new KeyNotFoundException();

        if (!string.Equals(entity.SupervisorUserId, supervisorUserId.Trim(), StringComparison.Ordinal) ||
            !await org.SupervisorOwnsCelluleAsync(supervisorUserId, entity.CelluleId, ct))
            throw new UnauthorizedAccessException("Accès refusé pour ce périmètre.");

        db.SupervisorCellulePrimeDrafts.Remove(entity);
        await db.SaveChangesAsync(ct);
    }
}
