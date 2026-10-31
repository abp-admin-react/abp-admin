using System;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Xunit;

namespace AbpAdmin.Identity;

/// <summary>
/// OU 角色授予守卫单测（安全审计 run-2 C-2 回归锁）：OU 挂角色 = 给全体成员授该角色
/// （OU 派生角色并集进完整成员资格），与 RoleDataScope 写面反提权守卫同一威胁模型。
/// 通过条件（任一）：操作者持 admin 超管角色；快照 IsAll；目标角色是操作者己有角色。
/// </summary>
public class OrganizationUnitRoleConferralGuardTests
{
    [Fact]
    public void Admin_Operator_Can_Attach_Any_Role()
    {
        OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("finance-boss", ["admin"], operatorSnapshotIsAll: false);
    }

    [Fact]
    public void IsAll_Snapshot_Operator_Can_Attach_Any_Role()
    {
        OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("finance-boss", ["operator"], operatorSnapshotIsAll: true);
    }

    [Fact]
    public void Restricted_Operator_Can_Attach_Own_Role()
    {
        // 与 SetRolesAsync「非 admin 只能授己有角色」等价口径
        OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("operator", ["Operator"], operatorSnapshotIsAll: false);
    }

    [Fact]
    public void Restricted_Operator_Cannot_Attach_Role_Beyond_Own_Authority()
    {
        var ex = Should.Throw<BusinessException>(() =>
            OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("admin", ["operator"], operatorSnapshotIsAll: false));
        ex.Code.ShouldBe(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeEscalation);
        ex.Data["ScopeType"]!.ToString()!.ShouldContain("OU role attach");
    }

    [Fact]
    public void Role_Name_Comparison_Is_Case_Insensitive()
    {
        // CurrentUser.Roles 与角色名的大小写由规范化层保证；守卫自身按 OrdinalIgnoreCase 比较
        Should.Throw<BusinessException>(() =>
            OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("Finance-Boss", ["operator"], operatorSnapshotIsAll: false));
        OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("Finance-Boss", ["FINANCE-BOSS"], operatorSnapshotIsAll: false);
    }
}
