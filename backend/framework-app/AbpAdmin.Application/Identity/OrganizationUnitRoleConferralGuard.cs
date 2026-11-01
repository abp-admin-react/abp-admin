using System;
using System.Collections.Generic;
using Volo.Abp;
using Volo.Abp.Roles;

namespace AbpAdmin.Identity;

/// <summary>
/// OU 通道的角色授予守卫（纯决策函数，便于脱离 DB 单测）。
/// ABP 把 OU 派生角色并集进完整角色成员资格（IdentityUserStore.GetRolesAsync），
/// OU 挂角色 = 给全体成员授该角色（权限 + 数据范围一并生效）——与
/// RoleDataScopeAppService.EnsureOperatorCanGrantAsync 同一威胁模型：受限快照的操作者
/// 不得经配置写入把自己或他人推到超越自身权限的位置。等价口径与
/// AbpIdentity.Users.Update.ManageRoles 门后的 SetRolesAsync 一致：非 admin/非 IsAll
/// 操作者只能挂自己已持有的角色（安全审计 run-2 C-2）。
/// </summary>
public static class OrganizationUnitRoleConferralGuard
{
    /// <summary>
    /// 校验「把 <paramref name="targetRoleName"/> 挂到 OU」是否在操作者权限等价范围内。
    /// 通过条件（任一）：操作者持 admin 超管角色；数据范围快照 IsAll；
    /// 目标角色是操作者自己已持有的角色。否则抛 RoleDataScopeEscalation（同族错误码，
    /// 语义同为「反提权」）。
    /// </summary>
    public static void EnsureRoleAttachable(string? targetRoleName, IEnumerable<string> operatorRoleNames, bool operatorSnapshotIsAll)
    {
        var roles = new HashSet<string>(operatorRoleNames, StringComparer.OrdinalIgnoreCase);
        if (roles.Contains(AbpRoleConsts.AdminRoleName) || operatorSnapshotIsAll)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(targetRoleName) && roles.Contains(targetRoleName))
        {
            return;
        }

        throw new BusinessException(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeEscalation)
            .WithData("ScopeType", $"OU role attach: {targetRoleName}");
    }
}
