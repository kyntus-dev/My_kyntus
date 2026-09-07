using Microsoft.EntityFrameworkCore;
using Prime.Application.Abstractions;
using Prime.Domain.Entities;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;
using Xunit;

namespace PrimeBackend.Tests;

public class PrimeFicheMergedPreviewAccessRhTests
{
    [Fact]
    public async Task Rh_CanPreviewFiche_WhenPresentInGlobalSynthesis_WithoutRbacGlobal()
    {
        await using var db = NewDb();
        var ficheId = Guid.NewGuid();
        var scopeId = Guid.NewGuid();

        db.GlobalPoolScopeSyntheses.Add(new GlobalPoolScopeSynthesisEntity
        {
            Id = scopeId,
            Period = "2026-07",
            ScopeType = "Service",
            ScopeId = "svc-1",
            ScopeDisplayName = "BLO / FTTO",
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.GlobalPoolSynthesisLines.Add(new GlobalPoolSynthesisLineEntity
        {
            Id = Guid.NewGuid(),
            ScopeSynthesisId = scopeId,
            FicheId = ficheId,
            EmployeeId = "emp-loubna",
            ServiceId = "svc-1",
            LineStatus = GlobalPoolSynthesisLineStatuses.PendingReview,
            RhDecision = GlobalPoolLineDecisions.Pending,
            ManagerDecision = GlobalPoolLineDecisions.Pending,
        });
        await db.SaveChangesAsync();

        var access = CreateAccess(db);
        var ru = new PrimeResolvedUser(
            "rh-1",
            "RH",
            new Employee { Id = "rh-1", Role = "RH", FirstName = "Rh", LastName = "User" });
        var fiche = new EmployeePrimeServiceFiche
        {
            Id = ficheId,
            EmployeeId = "emp-loubna",
            ServiceId = "svc-1",
            CelluleId = "cell-1",
            Period = "2026-07",
            FillingStatus = "Complete",
            ValidationStatus = "Chef de projet Approved",
        };

        Assert.True(await access.CanAccessMergedPreviewAsync(ru, fiche));
    }

    [Fact]
    public async Task Rh_CannotPreviewFiche_WhenNotInAnySynthesis()
    {
        await using var db = NewDb();
        var access = CreateAccess(db);
        var ru = new PrimeResolvedUser(
            "rh-1",
            "RH",
            new Employee { Id = "rh-1", Role = "RH", FirstName = "Rh", LastName = "User" });
        var fiche = new EmployeePrimeServiceFiche
        {
            Id = Guid.NewGuid(),
            EmployeeId = "emp-x",
            ServiceId = "svc-1",
            CelluleId = "cell-1",
            Period = "2026-07",
            FillingStatus = "Complete",
            ValidationStatus = "Chef de projet Approved",
        };

        Assert.False(await access.CanAccessMergedPreviewAsync(ru, fiche));
    }

    private static PrimeFicheMergedPreviewAccessService CreateAccess(PrimeDbContext db)
    {
        var org = new PrimeOrgScopeService(db);
        var rbac = new PrimeRbacReadService(db, org);
        var runtime = new PrimeValidationWorkflowRuntime(db);
        var history = new PrimeFicheValidationHistoryService(db);
        var submission = new PrimeFicheValidationSubmissionService(db, runtime, org, history);
        var poolWf = new GlobalPoolWorkflowService(db);
        var resolver = new CommonLinePonderationResolver(db);
        return new PrimeFicheMergedPreviewAccessService(db, rbac, org, submission, poolWf, resolver);
    }

    private static PrimeDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<PrimeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new PrimeDbContext(options);
    }
}
