using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Prime.Domain.Entities;
using Prime.Infrastructure.Services;

namespace Prime.Infrastructure.Persistence;

/// <summary>
/// Enrichissement idempotent de <c>prime_db</c> avec données fictives (Bogus, contexte marocain),
/// adapté à la structure organisationnelle déjà présente en base (IDs GUID).
/// </summary>
public static class PrimeDbEnrichmentSeeder
{
    public const int Version = 4;
    private const string MarkerAction = "DemoSeedApplied";
    public const string EnrichTemplateId = "enrich-template-v4";

    public static async Task<PrimeEnrichmentResult> EnrichAsync(
        PrimeDbContext db,
        bool force = false,
        CancellationToken cancellationToken = default,
        ILogger? logger = null)
    {
        if (!await db.Poles.AnyAsync(cancellationToken))
        {
            logger?.LogWarning("PRIME enrichissement ignoré : aucun pôle en base.");
            return PrimeEnrichmentResult.Skipped("no_poles");
        }

        if (force)
        {
            var markers = await db.AuditLogs.Where(x => x.Action == MarkerAction).ToListAsync(cancellationToken);
            if (markers.Count > 0)
            {
                db.AuditLogs.RemoveRange(markers);
                await db.SaveChangesAsync(cancellationToken);
                logger?.LogInformation("PRIME enrichissement : marqueur(s) supprimé(s) ({Count}).", markers.Count);
            }
        }
        else if (await IsVersionAppliedAsync(db, cancellationToken) && await HasEnrichmentDataAsync(db, cancellationToken))
        {
            logger?.LogInformation("PRIME enrichissement déjà appliqué (version {Version}).", Version);
            return PrimeEnrichmentResult.Skipped("already_applied");
        }

        var before = await SnapshotCountsAsync(db, cancellationToken);
        var data = new PrimeMoroccanDataFactory();
        var org = await PrimeOrgSnapshot.LoadAsync(db, cancellationToken);

        // Event-only : ne crée pas de collaborateurs emp-ma / staff cellule.
        logger?.LogInformation(
            "PRIME enrichissement event-only : {Poles} pôles, {Employees} employés existants.",
            org.Poles.Count,
            org.Employees.Count);

        if (org.Employees.Count == 0)
        {
            logger?.LogWarning("PRIME enrichissement ignoré : aucun employé existant.");
            return PrimeEnrichmentResult.Skipped("no_employees");
        }

        await SeedServiceIndicatorsAsync(db, org, cancellationToken);
        await SeedDraftsAndFichesAsync(db, data, org, cancellationToken);
        await SeedAuditLogsAsync(db, data, cancellationToken);
        await SeedAnomaliesAsync(db, org, cancellationToken);
        await SeedGlobalPoolApprovalsAsync(db, org, cancellationToken);
        await MarkVersionAppliedAsync(db, cancellationToken);

        var after = await SnapshotCountsAsync(db, cancellationToken);
        var result = PrimeEnrichmentResult.FromCounts(before, after);
        logger?.LogInformation(
            "PRIME enrichissement v{Version} terminé : +{Fiches} fiches, +{Audit} logs, +{Anomalies} anomalies, {EnrichEmployees} collaborateurs emp-ma.",
            Version,
            after.Fiches - before.Fiches,
            after.AuditLogs - before.AuditLogs,
            after.Anomalies - before.Anomalies,
            after.EnrichEmployees);
        return result;
    }

    public static async Task<bool> IsVersionAppliedAsync(PrimeDbContext db, CancellationToken ct = default) =>
        await IsVersionAppliedInternalAsync(db, ct);

    public static async Task<bool> HasEnrichmentDataAsync(PrimeDbContext db, CancellationToken ct = default) =>
        await db.Employees.AnyAsync(e => e.Id.StartsWith(PrimeMoroccanDataFactory.EnrichEmployeeIdPrefix), ct)
        || await db.SupervisorCellulePrimeDrafts.AnyAsync(d => d.TemplateId == EnrichTemplateId, ct);

    public static async Task<PrimeEnrichmentCounts> SnapshotCountsAsync(PrimeDbContext db, CancellationToken ct = default) =>
        new(
            await db.Employees.CountAsync(e => e.Id.StartsWith(PrimeMoroccanDataFactory.EnrichEmployeeIdPrefix), ct),
            await db.SupervisorCellulePrimeDrafts.CountAsync(d => d.TemplateId == EnrichTemplateId, ct),
            await db.EmployeePrimeServiceFiches.CountAsync(ct),
            await db.AuditLogs.CountAsync(x => x.Action != MarkerAction, ct),
            await db.Anomalies.CountAsync(ct),
            await db.ServicePrimeIndicators.CountAsync(ct));

    private static async Task<bool> IsVersionAppliedInternalAsync(PrimeDbContext db, CancellationToken ct)
    {
        var logs = await db.AuditLogs.AsNoTracking()
            .Where(x => x.Action == MarkerAction)
            .Select(x => x.DetailJson)
            .ToListAsync(ct);
        foreach (var detail in logs)
        {
            if (string.IsNullOrEmpty(detail)) continue;
            try
            {
                using var doc = JsonDocument.Parse(detail);
                if (doc.RootElement.TryGetProperty("version", out var v) && v.GetInt32() >= Version)
                    return true;
            }
            catch
            {
                // ignore
            }
        }
        return false;
    }

    private static async Task MarkVersionAppliedAsync(PrimeDbContext db, CancellationToken ct)
    {
        if (await db.AuditLogs.AnyAsync(x => x.Action == MarkerAction && x.DetailJson != null && x.DetailJson.Contains($"\"version\":{Version}"), ct))
            return;

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            At = DateTimeOffset.UtcNow,
            UserId = "seed",
            UserDisplayName = "Enrichissement démo Maroc",
            Role = "Admin",
            Action = MarkerAction,
            EntityType = "PrimeDbEnrichment",
            EntityId = Version.ToString(),
            DetailJson = JsonSerializer.Serialize(new { version = Version, appliedAt = DateTimeOffset.UtcNow, locale = "fr-MA" }),
        });
        await db.SaveChangesAsync(ct);
    }

    private static async Task EnsureGlobalRolesAsync(
        PrimeDbContext db,
        PrimeMoroccanDataFactory data,
        PrimeOrgSnapshot org,
        CancellationToken ct)
    {
        var firstPole = org.Poles.FirstOrDefault();
        if (firstPole is null) return;

        var firstCellule = firstPole.Cellules.FirstOrDefault();
        var firstService = firstCellule?.Services.FirstOrDefault();
        if (firstCellule is null || firstService is null) return;

        var domain = PrimeMoroccanDataFactory.EmailDomainFromPoleName(firstPole.Name);
        var toAdd = new List<EmployeeEntity>();

        void MaybeAdd(string role, string? existingId)
        {
            if (!string.IsNullOrEmpty(existingId)) return;
            var p = data.Person(domain);
            toAdd.Add(new EmployeeEntity
            {
                Id = data.NewEnrichEmployeeId(),
                FirstName = p.FirstName,
                LastName = p.LastName,
                Role = role,
                PoleId = firstPole.Id,
                CelluleId = firstCellule.Id,
                ServiceId = firstService.Id,
                Email = p.Email,
            });
        }

        MaybeAdd("Admin", org.AdminId);
        MaybeAdd("RH", org.RhId);
        MaybeAdd("Manager", org.ManagerId);
        MaybeAdd("Comptabilité", org.ComptabiliteId);
        MaybeAdd("Audit", org.AuditId);

        if (org.ChefDeProjetForPole(firstPole.Id) is null)
        {
            var p = data.Person(domain);
            toAdd.Add(new EmployeeEntity
            {
                Id = data.NewEnrichEmployeeId(),
                FirstName = p.FirstName,
                LastName = p.LastName,
                Role = "Chef de projet",
                PoleId = firstPole.Id,
                CelluleId = firstCellule.Id,
                ServiceId = firstService.Id,
                Email = p.Email,
            });
        }

        if (toAdd.Count == 0) return;
        db.Employees.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedServiceIndicatorsAsync(PrimeDbContext db, PrimeOrgSnapshot org, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var toAdd = new List<ServicePrimeIndicatorEntity>();
        var serviceIndex = 0;

        foreach (var pole in org.Poles)
        {
            foreach (var cellule in pole.Cellules)
            {
                foreach (var service in cellule.Services)
                {
                    var existingCount = await db.ServicePrimeIndicators.CountAsync(i => i.ServiceId == service.Id, ct);
                    if (existingCount >= 3)
                    {
                        serviceIndex++;
                        continue;
                    }

                    var defs = PrimeMoroccanDataFactory.IndicatorSet(serviceIndex++);
                    var order = existingCount;
                    foreach (var def in defs)
                    {
                        if (await db.ServicePrimeIndicators.AnyAsync(
                                i => i.ServiceId == service.Id && i.TemplateStableId == def.Id, ct))
                            continue;

                        toAdd.Add(new ServicePrimeIndicatorEntity
                        {
                            Id = Guid.NewGuid(),
                            ServiceId = service.Id,
                            SortOrder = order++,
                            Label = def.Label,
                            PonderationPrimePct = def.PrimePct,
                            PonderationChallengePct = def.ChallengePct,
                            IsActive = true,
                            TemplateStableId = def.Id,
                            CreatedAt = now,
                        });
                    }
                }
            }
        }

        if (toAdd.Count == 0) return;
        db.ServicePrimeIndicators.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedDraftsAndFichesAsync(
        PrimeDbContext db,
        PrimeMoroccanDataFactory data,
        PrimeOrgSnapshot org,
        CancellationToken ct)
    {
        var periods = BuildEnrichmentPeriods();
        var now = DateTimeOffset.UtcNow;
        var statuses = new[]
        {
            PrimeValidationWorkflowService.AwaitingData,
            PrimeValidationWorkflowService.Pending,
            PrimeValidationWorkflowService.ReferentTechniqueApproved,
            PrimeValidationWorkflowService.SuperviseurApproved,
            PrimeValidationWorkflowService.ChefDeProjetApproved,
            PrimeValidationWorkflowService.Rejected,
        };

        var ficheIndex = 0;
        var firstCelluleForPool = org.Poles
            .SelectMany(p => p.Cellules)
            .FirstOrDefault(c => c.Services.Count > 0);

        foreach (var period in periods)
        {
            foreach (var pole in org.Poles)
            {
                foreach (var cellule in pole.Cellules)
                {
                    if (cellule.Services.Count == 0)
                        continue;

                    var supervisorId = org.SupervisorForCellule(cellule.Id);
                    if (supervisorId is null) continue;

                    var draft = await EnsureDraftAsync(db, org, cellule, period, supervisorId, now, ct, attachGlobalPool: cellule.Id == firstCelluleForPool?.Id && period == periods[^1]);
                    if (draft is null) continue;

                    var pilots = org.PilotsForCellule(cellule.Id).Take(12).ToList();
                    if (pilots.Count == 0)
                    {
                        pilots = org.Employees.Where(e => e.Role == "Pilote" && e.PoleId == pole.Id).Take(8).ToList();
                    }

                    foreach (var pilot in pilots)
                    {
                        if (await db.EmployeePrimeServiceFiches.AnyAsync(
                                f => f.EmployeeId == pilot.Id && f.Period == period, ct))
                            continue;

                        var serviceId = ResolveServiceId(pilot, cellule);
                        if (serviceId is null)
                            continue;

                        var status = statuses[ficheIndex % statuses.Length];
                        ficheIndex++;
                        var (prime, challenge) = data.Amounts(ficheIndex);

                        var fiche = new EmployeePrimeServiceFiche
                        {
                            Id = Guid.NewGuid(),
                            CellulePrimeDraftId = draft.Id,
                            SupervisorUserId = supervisorId,
                            EmployeeId = pilot.Id,
                            ServiceId = serviceId,
                            CelluleId = cellule.Id,
                            Period = period,
                            ServiceSaisieJson = data.PerformanceJson(ficheIndex),
                            FillingStatus = "Complete",
                            UpdatedAt = now.AddDays(-data.Faker.Random.Int(1, 25)),
                            ValidationStatus = status,
                            PrimeAmount = prime,
                            ChallengeAmount = challenge,
                            TotalAmount = prime + challenge,
                        };

                        ApplyValidationMeta(fiche, status, org, cellule.PoleId, now, data);
                        db.EmployeePrimeServiceFiches.Add(fiche);
                    }
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task<SupervisorCellulePrimeDraft?> EnsureDraftAsync(
        PrimeDbContext db,
        PrimeOrgSnapshot org,
        PrimeOrgCelluleNode cellule,
        string period,
        string supervisorUserId,
        DateTimeOffset now,
        CancellationToken ct,
        bool attachGlobalPool)
    {
        // Unicité DB : (SupervisorUserId, RootPoleId, Period) — une grille par superviseur et pôle.
        var existing = await db.SupervisorCellulePrimeDrafts
            .FirstOrDefaultAsync(
                d => d.SupervisorUserId == supervisorUserId
                     && d.RootPoleId == cellule.PoleId
                     && d.Period == period,
                ct);
        if (existing is not null) return existing;

        var pole = org.FindPole(cellule.PoleId);
        var draft = new SupervisorCellulePrimeDraft
        {
            Id = Guid.NewGuid(),
            SupervisorUserId = supervisorUserId,
            RootPoleId = cellule.PoleId,
            CelluleId = cellule.Id,
            Period = period,
            TemplateId = EnrichTemplateId,
            TemplateDisplayName = $"Grille PRIME — {cellule.Name} ({period})",
            TemplateFormatVersion = 1,
            Status = "Validated",
            SchemaJson = """{"fields":[{"id":"nps","label":"NPS (%)","type":"number"},{"id":"aht","label":"AHT","type":"number"}]}""",
            CelluleSaisieJson = """{"nps":74,"aht":278,"commentaire":"Saisie cellule — démo Maroc"}""",
            TemplateCalcSnapshotJson = """{"previewSheetName":"Synthèse","calcSheets":[]}""",
            UpdatedAt = now,
        };

        if (attachGlobalPool)
        {
            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Synthèse");
            ws.Cell(1, 1).Value = $"PRIME — synthèse globale — {pole?.Name ?? "Pôle"}";
            ws.Cell(2, 1).Value = period;
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            draft.GlobalPoolExcelContent = ms.ToArray();
            draft.GlobalPoolFileName = $"PRIME_synthese_{period}.xlsx";
            draft.GlobalPoolUploadedAt = now;
            draft.GlobalPoolUploadedByUserId = org.AdminId ?? supervisorUserId;
        }

        db.SupervisorCellulePrimeDrafts.Add(draft);
        await db.SaveChangesAsync(ct);
        return draft;
    }

    private static void ApplyValidationMeta(
        EmployeePrimeServiceFiche fiche,
        string status,
        PrimeOrgSnapshot org,
        string poleId,
        DateTimeOffset now,
        PrimeMoroccanDataFactory data)
    {
        var referentId = org.ReferentForCellule(fiche.CelluleId, fiche.ServiceId);
        var supervisorId = org.SupervisorForCellule(fiche.CelluleId) ?? fiche.SupervisorUserId;
        var chefId = org.ChefDeProjetForPole(poleId);

        switch (status)
        {
            case PrimeValidationWorkflowService.ReferentTechniqueApproved:
                if (referentId is not null)
                {
                    fiche.LastApproverUserId = referentId;
                    fiche.LastApprovedAt = now.AddDays(-3);
                }
                break;
            case PrimeValidationWorkflowService.SuperviseurApproved:
                fiche.LastApproverUserId = supervisorId;
                fiche.LastApprovedAt = now.AddDays(-2);
                break;
            case PrimeValidationWorkflowService.ChefDeProjetApproved:
                if (chefId is not null)
                {
                    fiche.LastApproverUserId = chefId;
                    fiche.LastApprovedAt = now.AddDays(-1);
                }
                break;
            case PrimeValidationWorkflowService.Rejected:
                fiche.RejectedByUserId = supervisorId;
                fiche.RejectedAt = now.AddDays(-1);
                fiche.RejectionReason = data.RejectionReason();
                break;
        }
    }

    private static string? ResolveServiceId(EmployeeEntity pilot, PrimeOrgCelluleNode cellule)
    {
        if (!string.IsNullOrWhiteSpace(pilot.ServiceId)
            && cellule.Services.Any(s => s.Id == pilot.ServiceId))
            return pilot.ServiceId;

        return cellule.Services.FirstOrDefault()?.Id;
    }

    private static string[] BuildEnrichmentPeriods()
    {
        var current = $"{DateTime.UtcNow:yyyy-MM}";
        return ["2026-01", "2026-02", "2026-03", "2026-04", current];
    }

    private static async Task SeedAuditLogsAsync(PrimeDbContext db, PrimeMoroccanDataFactory data, CancellationToken ct)
    {
        var existing = await db.AuditLogs.CountAsync(x => x.Action != MarkerAction, ct);
        if (existing >= 80) return;

        var employees = await db.Employees.AsNoTracking().Take(30).ToListAsync(ct);
        if (employees.Count == 0) return;

        var fiches = await db.EmployeePrimeServiceFiches.AsNoTracking().Take(50).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var actions = new[] { "ValidationApproved", "ValidationRejected", "OrgAssignmentChanged", "WorkflowConfigChanged", "Navigation" };
        var toAdd = new List<AuditLog>();

        for (var i = 0; i < 100; i++)
        {
            var emp = employees[i % employees.Count];
            var fiche = fiches.Count > 0 ? fiches[i % fiches.Count] : null;
            toAdd.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                At = now.AddHours(-i * 2),
                UserId = emp.Id,
                UserDisplayName = $"{emp.FirstName} {emp.LastName}",
                Role = emp.Role,
                Action = actions[i % actions.Length],
                EntityType = fiche is null ? "Employee" : "EmployeePrimeServiceFiche",
                EntityId = fiche?.Id.ToString() ?? emp.Id,
                DetailJson = JsonSerializer.Serialize(new
                {
                    period = fiche?.Period,
                    validationStatus = fiche?.ValidationStatus,
                    note = data.AuditNote(),
                }),
                IpAddress = $"10.10.{(i % 20)}.{(i % 200) + 1}",
            });
        }

        db.AuditLogs.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedAnomaliesAsync(PrimeDbContext db, PrimeOrgSnapshot org, CancellationToken ct)
    {
        if (await db.Anomalies.CountAsync(ct) >= 15) return;

        var fiches = await db.EmployeePrimeServiceFiches.AsNoTracking().OrderByDescending(f => f.UpdatedAt).Take(30).ToListAsync(ct);
        if (fiches.Count == 0) return;

        var now = DateTimeOffset.UtcNow;
        var types = new[] { "ComputationMismatch", "StaleValidation", "OutOfRange", "MissingApprover", "DuplicateFiche" };
        var severities = new[] { "Critical", "High", "Medium", "Low" };
        var statuses = new[] { "Open", "InReview", "Resolved", "Ignored" };
        var toAdd = new List<Anomaly>();
        var chefId = org.Poles.SelectMany(p => p.Cellules).Select(c => org.ChefDeProjetForPole(c.PoleId)).FirstOrDefault(id => id is not null);

        for (var i = 0; i < 20; i++)
        {
            var f = fiches[i % fiches.Count];
            var poleId = org.FindCellule(f.CelluleId)?.PoleId ?? f.CelluleId;
            var serviceName = org.FindCellule(f.CelluleId)?.Services.FirstOrDefault(s => s.Id == f.ServiceId)?.Name;
            var type = types[i % types.Length];
            var status = statuses[i % statuses.Length];
            toAdd.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                DetectedAt = now.AddDays(-i),
                UpdatedAt = now.AddDays(-i + 1),
                Type = type,
                Severity = severities[i % severities.Length],
                Status = status,
                Description = new PrimeMoroccanDataFactory().AnomalyDescription(type, f.Period, serviceName),
                TargetEntityType = "EmployeePrimeServiceFiche",
                TargetEntityId = f.Id.ToString(),
                Period = f.Period,
                ServiceId = f.ServiceId,
                CelluleId = f.CelluleId,
                PoleId = poleId,
                ResolvedByUserId = status == "Resolved" ? chefId : null,
                ResolvedAt = status == "Resolved" ? now.AddDays(-i + 2) : null,
                ResolutionNote = status == "Resolved" ? "Corrigé après resynchronisation ACD / contrôle qualité." : null,
            });
        }

        db.Anomalies.AddRange(toAdd);
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedGlobalPoolApprovalsAsync(PrimeDbContext db, PrimeOrgSnapshot org, CancellationToken ct)
    {
        var draft = await db.SupervisorCellulePrimeDrafts
            .Where(d => d.GlobalPoolExcelContent != null && d.TemplateId == EnrichTemplateId)
            .OrderByDescending(d => d.Period)
            .FirstOrDefaultAsync(ct);
        if (draft is null) return;

        if (await db.GlobalPoolApprovals.AnyAsync(a => a.DraftId == draft.Id, ct)) return;

        var steps = await db.GlobalPoolWorkflowSteps.AsNoTracking()
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);
        if (steps.Count == 0) return;

        var rhId = org.RhId ?? org.AdminId;
        if (rhId is null) return;

        var rhStep = steps.FirstOrDefault(s => s.ApproverRole == "RH");
        if (rhStep is null) return;

        var now = DateTimeOffset.UtcNow;
        db.GlobalPoolApprovals.Add(new GlobalPoolApprovalEntity
        {
            Id = Guid.NewGuid(),
            DraftId = draft.Id,
            StepId = rhStep.Id,
            UserId = rhId,
            ApprovedAt = now.AddDays(-1),
        });
        draft.GlobalPoolRhApprovedAt = now.AddDays(-1);
        draft.GlobalPoolRhApprovedByUserId = rhId;
        await db.SaveChangesAsync(ct);
    }
}

public sealed record PrimeEnrichmentCounts(
    int EnrichEmployees,
    int EnrichDrafts,
    int Fiches,
    int AuditLogs,
    int Anomalies,
    int Indicators);

public sealed record PrimeEnrichmentResult(
    bool Applied,
    string Reason,
    PrimeEnrichmentCounts? Before,
    PrimeEnrichmentCounts? After)
{
    public static PrimeEnrichmentResult Skipped(string reason) => new(false, reason, null, null);

    public static PrimeEnrichmentResult FromCounts(PrimeEnrichmentCounts before, PrimeEnrichmentCounts after) =>
        new(true, "ok", before, after);
}
