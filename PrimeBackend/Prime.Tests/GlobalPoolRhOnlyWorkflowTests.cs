using Prime.Domain.Entities;
using Prime.Infrastructure.Persistence;
using Prime.Infrastructure.Services;

namespace PrimeBackend.Tests;

public class GlobalPoolRhOnlyWorkflowTests
{
    [Theory]
    [InlineData("Pending", "Pending", "PendingReview")]
    [InlineData("Pending", "Approved", "PendingReview")]
    [InlineData("Approved", "Pending", "Approved")]
    [InlineData("Approved", "Approved", "Approved")]
    [InlineData("Rejected", "Pending", "LineRejected")]
    [InlineData("Rejected", "Approved", "LineRejected")]
    public void DeriveLineStatus_UsesRhDecisionOnly(string rh, string manager, string expected)
    {
        Assert.Equal(expected, GlobalPoolLineDecisions.DeriveLineStatus(rh, manager));
    }

    [Fact]
    public void LegacyScopePoolUnlocked_AfterRhWithoutManager()
    {
        var s = new GlobalPoolScopeSynthesisEntity { RhApprovedAt = DateTimeOffset.UtcNow };
        Assert.True(GlobalPoolWorkflowService.LegacyScopePoolUnlocked(s));
    }

    [Fact]
    public void LegacyScopePoolUnlocked_RefusesComptaWithoutRh_EvenIfManagerApproved()
    {
        var s = new GlobalPoolScopeSynthesisEntity { ManagerApprovedAt = DateTimeOffset.UtcNow };
        Assert.False(GlobalPoolWorkflowService.LegacyScopePoolUnlocked(s));
    }

    [Fact]
    public void LegacyPoolUnlocked_AfterRhWithoutManager()
    {
        var d = new SupervisorCellulePrimeDraft { GlobalPoolRhApprovedAt = DateTimeOffset.UtcNow };
        Assert.True(GlobalPoolWorkflowService.LegacyPoolUnlocked(d));
    }

    [Fact]
    public void RegularizationEditable_WhileRhPending()
    {
        Assert.True(GlobalPoolLineDecisions.RegularizationEditable(GlobalPoolLineDecisions.Pending));
        Assert.True(GlobalPoolLineDecisions.RegularizationEditable(null));
        var net = PrimeAbsenceSanctionCalculator.ComputeNetPayable(1000m, 0m, -25m);
        Assert.Equal(975m, net);
    }

    [Theory]
    [InlineData("Approved")]
    [InlineData("Rejected")]
    public void RegularizationLocked_AfterRhDecision(string rh)
    {
        Assert.False(GlobalPoolLineDecisions.RegularizationEditable(rh));
    }

    [Fact]
    public void IsPoolStakeholderRole_ExcludesManager()
    {
        Assert.False(PrimeGlobalPoolActorResolver.IsPoolStakeholderRole("Manager"));
        Assert.True(PrimeGlobalPoolActorResolver.IsPoolStakeholderRole("RH"));
        Assert.True(PrimeGlobalPoolActorResolver.IsPoolStakeholderRole("Comptabilité"));
        Assert.True(PrimeGlobalPoolActorResolver.IsPoolStakeholderRole("Admin"));
    }
}
