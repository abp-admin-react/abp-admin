using System;
using Shouldly;
using Xunit;

namespace AbpAdmin.Identity;

/// <summary>
/// 批量导入通道与共享守卫的接线锁（安全审计 run-3）：
/// Import 的 RoleNames / OU 派生角色路径调用 <see cref="OrganizationUnitRoleConferralGuard.IsRoleAttachable"/>
/// （非 Ensure），拒绝形态是 UserFriendlyException + UserImport:*BeyondAuthority。
/// 判定矩阵本身由 <see cref="OrganizationUnitRoleConferralGuardTests"/> 覆盖；本文件只锁
/// 「导入面用的是 bool API + 特权短路」，避免再维护一份重复的角色矩阵。
/// 行级 UFE 文案与受限操作者 ImportAsync 端到端由 sectest a05/a07 覆盖。
/// </summary>
public class UserImportConferralGuardTests
{
    [Fact]
    public void Import_Channel_Uses_IsRoleAttachable_Not_Ensure_Semantics()
    {
        // 受限操作者：越权目标为 false（AppService 据此抛 UserFriendlyException）
        OrganizationUnitRoleConferralGuard.IsRoleAttachable("admin", ["helpdesk"], operatorSnapshotIsAll: false)
            .ShouldBeFalse();
        // 特权短路：与 AssignOrganizationUnitsAsync 的 IsPrivilegedOperator 跳过 GetRolesAsync 同口径
        OrganizationUnitRoleConferralGuard.IsPrivilegedOperator(["admin"], operatorSnapshotIsAll: false)
            .ShouldBeTrue();
        OrganizationUnitRoleConferralGuard.IsPrivilegedOperator(["helpdesk"], operatorSnapshotIsAll: true)
            .ShouldBeTrue();
    }
}
