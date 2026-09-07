using Auth.Application;

namespace Auth.Application.UnitTests;

public class PlanningRoleToAuthRoleMapperTests
{
    [Theory]
    [InlineData("Pilote", "Employee")]
    [InlineData("Employee", "Employee")]
    [InlineData("Employé", "Employee")]
    [InlineData("RH", "RH")]
    [InlineData("Référent technique", "Coach")]
    [InlineData("Coach", "Coach")]
    [InlineData("Chef de projet", "RP")]
    [InlineData("RP", "RP")]
    [InlineData("Superviseur", "Superviseur")]
    [InlineData("Qualiticien", "Qualiticien")]
    [InlineData("Audit", "Audit")]
    [InlineData("Comptabilité", "Comptabilité")]
    [InlineData("Comptable", "Comptabilité")]
    [InlineData("comptabilite", "Comptabilité")]
    public void Maps_planning_role_to_auth_role(string planningRole, string authRole)
    {
        Assert.Equal(authRole, PlanningRoleToAuthRoleMapper.MapToAuthRoleName(planningRole));
    }
}
