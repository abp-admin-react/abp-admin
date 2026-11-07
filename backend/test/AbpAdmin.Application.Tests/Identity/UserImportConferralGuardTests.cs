using System;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.Identity;

/// <summary>
/// 批量导入面授权等价回归锁（安全审计 run-3，run-2 C-2 守卫的通道补全）：
/// IdentityUserAdminAppService 的 AssignRolesAsync（RoleNames 列）与
/// AssignOrganizationUnitsAsync（OrganizationUnitCodes 列 → OU 现挂角色）在写入前
/// 都逐项过 OrganizationUnitRoleConferralGuard——非 admin/非 IsAll 操作者经 CSV
/// 导入不能授予自己不持有的角色，与交互面（AbpIdentity 模块
/// FilterRolesByCurrentUserAsync / OrganizationUnitAppService 守卫）等价。
/// 端到端（真实 PermissionChecker + UserManager）由 sectest 黑盒栈 a05/a07 变体覆盖。
/// </summary>
public class UserImportConferralGuardTests
{
    [Fact]
    public void Role_Column_Beyond_Operator_Authority_Is_Rejected()
    {
        // 纯决策函数层：导入面复用同一守卫，admin/IsAll/己有之外的角色都会抛
        Should.Throw<BusinessException>(() =>
            OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("admin", ["helpdesk"], operatorSnapshotIsAll: false));
    }

    [Fact]
    public void Role_Column_Operator_Own_Role_Is_Allowed()
    {
        OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("helpdesk", ["helpdesk"], operatorSnapshotIsAll: false);
    }

    [Fact]
    public void Ou_Column_Attached_Role_Beyond_Operator_Authority_Is_Rejected()
    {
        // OU 列的等价判定：目标 OU 现挂的每个角色同样要落在操作者权限等价范围内
        Should.Throw<BusinessException>(() =>
            OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("finance-boss", ["helpdesk"], operatorSnapshotIsAll: false));
    }

    [Fact]
    public void Admin_And_IsAll_Operators_Pass_Both_Columns()
    {
        OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("admin", ["admin"], operatorSnapshotIsAll: false);
        OrganizationUnitRoleConferralGuard.EnsureRoleAttachable("admin", ["helpdesk"], operatorSnapshotIsAll: true);
    }
}
