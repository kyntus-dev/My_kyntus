using Planning.Infrastructure.Services;

namespace Planning.UnitTests;

public class ContractualLevelAccessTests
{
    private static readonly Guid PlanningGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AuthSub = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void MatchesActor_prefers_AuthUserId_when_JWT_sub_differs()
    {
        Assert.True(ContractualLevelAccess.MatchesActor(
            PlanningGuid, "sup@kyntus.ma", 33, AuthSub, "other@kyntus.ma", 33));
    }

    [Fact]
    public void MatchesActor_uses_email_when_Guid_and_AuthUserId_differ()
    {
        Assert.True(ContractualLevelAccess.MatchesActor(
            PlanningGuid, "sup@kyntus.ma", 10, AuthSub, "sup@kyntus.ma", 99));
    }

    [Fact]
    public void MatchesActor_falls_back_to_Planning_Guid()
    {
        Assert.True(ContractualLevelAccess.MatchesActor(
            PlanningGuid, "sup@kyntus.ma", null, PlanningGuid, null, null));
        Assert.False(ContractualLevelAccess.MatchesActor(
            PlanningGuid, "sup@kyntus.ma", null, AuthSub, null, null));
    }

    [Fact]
    public void AllowUpdate_trusts_Directory_when_available()
    {
        Assert.True(ContractualLevelAccess.AllowUpdate(rebacAllowed: true, rebacUnavailable: false, localFallbackAllowed: false));
        Assert.False(ContractualLevelAccess.AllowUpdate(rebacAllowed: false, rebacUnavailable: false, localFallbackAllowed: true));
    }

    [Fact]
    public void AllowUpdate_uses_local_cellules_when_Directory_is_down()
    {
        Assert.True(ContractualLevelAccess.AllowUpdate(rebacAllowed: false, rebacUnavailable: true, localFallbackAllowed: true));
        Assert.False(ContractualLevelAccess.AllowUpdate(rebacAllowed: false, rebacUnavailable: true, localFallbackAllowed: false));
    }

    [Fact]
    public void Superviseur_is_allowed_operational_role_employee_is_not()
    {
        Assert.True(ContractualLevelAccess.IsAllowedOperationalRole("Superviseur"));
        Assert.True(ContractualLevelAccess.IsPrivileged("RH"));
        Assert.False(ContractualLevelAccess.IsAllowedOperationalRole("Pilote"));
        Assert.False(ContractualLevelAccess.IsAllowedOperationalRole("Employee"));
    }
}
