using AbpAdmin.ClientErrors;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.Identity;

/// <summary>
/// OU 角色授予守卫单测（安全审计 run-2 C-2 回归锁）：OU 挂角色 = 给全体成员授该角色
/// （OU 派生角色并集进完整成员资格）。仅 admin 豁免，其他操作者只能授予己有角色。
/// </summary>
public class OrganizationUnitRoleConferralGuardTests
{
    [Fact]
    public void Admin_Operator_Can_Attach_Any_Role()
    {
        OrganizationUnitRoleConferralGuard.IsRoleAttachable("finance-boss", ["admin"])
            .ShouldBeTrue();
        OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("finance-boss", ["admin"]);
    }

    [Fact]
    public void Non_Admin_All_Data_Role_Is_Not_Privileged()
    {
        OrganizationUnitRoleConferralGuard.IsRoleAttachable("finance-boss", ["operator", "all-data"])
            .ShouldBeFalse();
    }

    [Fact]
    public void Restricted_Operator_Can_Attach_Own_Role()
    {
        // 与 SetRolesAsync「非 admin 只能授己有角色」等价口径
        OrganizationUnitRoleConferralGuard.IsRoleAttachable("operator", ["Operator"])
            .ShouldBeTrue();
        OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("operator", ["Operator"]);
    }

    [Fact]
    public void Restricted_Operator_Cannot_Attach_Role_Beyond_Own_Authority()
    {
        OrganizationUnitRoleConferralGuard.IsRoleAttachable("admin", ["operator"])
            .ShouldBeFalse();

        var ex = Should.Throw<BusinessException>(() =>
            OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("admin", ["operator"]));
        ex.Code.ShouldBe(ClientErrorCodes.Forbidden);
        ex.Data["RoleName"].ShouldBe("admin");
    }

    [Fact]
    public void Null_Or_Whitespace_Target_Is_Not_Attachable_For_Restricted_Operator()
    {
        OrganizationUnitRoleConferralGuard.IsRoleAttachable(null, ["operator"])
            .ShouldBeFalse();
        OrganizationUnitRoleConferralGuard.IsRoleAttachable("  ", ["operator"])
            .ShouldBeFalse();
    }

    [Fact]
    public void IsPrivilegedOperator_Only_Allows_Admin()
    {
        OrganizationUnitRoleConferralGuard.IsPrivilegedOperator(["admin"])
            .ShouldBeTrue();
        OrganizationUnitRoleConferralGuard.IsPrivilegedOperator(["helpdesk", "all-data"])
            .ShouldBeFalse();
    }

    [Fact]
    public void Role_Name_Comparison_Is_Case_Insensitive()
    {
        // CurrentUser.Roles 与角色名的大小写由规范化层保证；守卫自身按 OrdinalIgnoreCase 比较
        OrganizationUnitRoleConferralGuard.IsRoleAttachable("Finance-Boss", ["operator"])
            .ShouldBeFalse();
        Should.Throw<BusinessException>(() =>
            OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("Finance-Boss", ["operator"]));
        OrganizationUnitRoleConferralGuard.IsRoleAttachable("Finance-Boss", ["FINANCE-BOSS"])
            .ShouldBeTrue();
        OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("Finance-Boss", ["FINANCE-BOSS"]);
    }
}
