using Microsoft.EntityFrameworkCore;
using Prime.Domain.Entities;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;
using Xunit;

namespace PrimeBackend.Tests;

public class PrimeFicheValidationSubmissionServiceTests
{
    [Fact]
    public async Task ReconcileReadySubmissionsForPeriodAsync_PromotesAwaitingData_WhenRootPoleDraftValidated()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);

        db.Poles.Add(new PoleEntity { Id = "pole-1", Name = "Pôle A" });
        db.Cellules.Add(new CelluleEntity { Id = "cell-1", Name = "Cellule 1", PoleId = "pole-1" });
        db.Services.Add(new ServiceEntity { Id = "svc-1", Name = "Service 1", CelluleId = "cell-1" });

        var draftId = Guid.NewGuid();
        db.SupervisorCellulePrimeDrafts.Add(new SupervisorCellulePrimeDraft
        {
            Id = draftId,
            SupervisorUserId = "sup-1",
            RootPoleId = "pole-1",
            CelluleId = "pole-1",
            Period = "2026-07",
            Status = "Validated",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        db.Employees.Add(new EmployeeEntity
        {
            Id = "sup-1",
            FirstName = "Mohammed",
            LastName = "Draoui",
            Role = "Superviseur",
            Email = "sup@kyntus.ma",
            CelluleId = "cell-1",
            PoleId = "pole-1",
        });

        var ficheId = Guid.NewGuid();
        db.EmployeePrimeServiceFiches.Add(new EmployeePrimeServiceFiche
        {
            Id = ficheId,
            CellulePrimeDraftId = Guid.Empty,
            SupervisorUserId = "sup-1",
            EmployeeId = "pilote-1",
            ServiceId = "svc-1",
            CelluleId = "cell-1",
            Period = "2026-07",
            FillingStatus = "Complete",
            ValidationStatus = PrimeValidationWorkflowService.AwaitingData,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var org = new PrimeOrgScopeService(db);
        var runtime = new PrimeValidationWorkflowRuntime(db);
        var history = new PrimeFicheValidationHistoryService(db);
        var submission = new PrimeFicheValidationSubmissionService(db, runtime, org, history);

        var changed = await submission.ReconcileReadySubmissionsForPeriodAsync("2026-07");

        Assert.Equal(1, changed);
        var fiche = await db.EmployeePrimeServiceFiches.SingleAsync(f => f.Id == ficheId);
        Assert.Equal(PrimeValidationWorkflowService.Pending, fiche.ValidationStatus);
        Assert.Equal(draftId, fiche.CellulePrimeDraftId);

        var submitted = await db.EmployeePrimeFicheValidationHistories.SingleAsync(h => h.FicheId == ficheId);
        Assert.Equal(PrimeFicheValidationHistoryActions.Submitted, submitted.Action);
        Assert.Equal(PrimeValidationWorkflowService.AwaitingData, submitted.FromStatus);
        Assert.Equal(PrimeValidationWorkflowService.Pending, submitted.ToStatus);
        Assert.Equal("sup-1", submitted.ActorUserId);
        Assert.Equal("Superviseur", submitted.ActorRole);
    }

    [Fact]
    public async Task SyncForDraftAsync_BackfillsSubmittedHistoryForExistingPendingFiche_OnlyOnce()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new PrimeDbContext(options);

        db.Poles.Add(new PoleEntity { Id = "pole-1", Name = "Pôle A" });
        db.Cellules.Add(new CelluleEntity { Id = "cell-1", Name = "Cellule 1", PoleId = "pole-1" });
        db.Services.Add(new ServiceEntity { Id = "svc-1", Name = "Service 1", CelluleId = "cell-1" });
        db.Employees.Add(new EmployeeEntity
        {
            Id = "sup-1",
            FirstName = "Mohammed",
            LastName = "Draoui",
            Role = "Superviseur",
            Email = "sup@kyntus.ma",
            CelluleId = "cell-1",
            PoleId = "pole-1",
        });

        var draftId = Guid.NewGuid();
        db.SupervisorCellulePrimeDrafts.Add(new SupervisorCellulePrimeDraft
        {
            Id = draftId,
            SupervisorUserId = "sup-1",
            RootPoleId = "pole-1",
            CelluleId = "cell-1",
            Period = "2026-07",
            Status = "Validated",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var ficheId = Guid.NewGuid();
        db.EmployeePrimeServiceFiches.Add(new EmployeePrimeServiceFiche
        {
            Id = ficheId,
            CellulePrimeDraftId = draftId,
            SupervisorUserId = "sup-1",
            EmployeeId = "pilote-1",
            ServiceId = "svc-1",
            CelluleId = "cell-1",
            Period = "2026-07",
            FillingStatus = "Complete",
            ValidationStatus = PrimeValidationWorkflowService.Pending,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        var org = new PrimeOrgScopeService(db);
        var submission = new PrimeFicheValidationSubmissionService(
            db,
            new PrimeValidationWorkflowRuntime(db),
            org,
            new PrimeFicheValidationHistoryService(db));

        await submission.SyncForDraftAsync(draftId);
        await db.SaveChangesAsync();
        await submission.SyncForDraftAsync(draftId);
        await db.SaveChangesAsync();

        var rows = await db.EmployeePrimeFicheValidationHistories
            .Where(h => h.FicheId == ficheId && h.Action == PrimeFicheValidationHistoryActions.Submitted)
            .ToListAsync();
        Assert.Single(rows);
        Assert.Equal(PrimeValidationWorkflowService.AwaitingData, rows[0].FromStatus);
        Assert.Equal(PrimeValidationWorkflowService.Pending, rows[0].ToStatus);
    }
}
