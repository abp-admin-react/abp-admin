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
    /// 操作者是否对任意角色授予均免检（持 admin 超管角色，或数据范围快照 IsAll）。
    /// 调用方用此短路昂贵的角色/OU 角色列举，再对受限操作者走 <see cref="IsRoleAttachable"/>。
    /// </summary>
    public static bool IsPrivilegedOperator(IEnumerable<string> operatorRoleNames, bool operatorSnapshotIsAll)
    {
        if (operatorSnapshotIsAll)
        {
            return true;
        }

        foreach (var role in operatorRoleNames)
        {
            if (string.Equals(role, AbpRoleConsts.AdminRoleName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 校验「授予/挂接 <paramref name="targetRoleName"/>」是否在操作者权限等价范围内。
    /// 通过条件（任一）：操作者持 admin 超管角色；数据范围快照 IsAll；
    /// 目标角色是操作者自己已持有的角色。
    /// 覆盖通道（run-3 起含批量导入的 RoleNames 直授与 OU 挂接两条）共用同一判定。
    /// </summary>
    /// <remarks>布尔判定供调用方自选错误形态：交互面抛 403 语义、导入面落行级文案。</remarks>
    public static bool IsRoleAttachable(string? targetRoleName, IEnumerable<string> operatorRoleNames, bool operatorSnapshotIsAll)
    {
        if (IsPrivilegedOperator(operatorRoleNames, operatorSnapshotIsAll))
        {
            return true;
        }

        var roles = new HashSet<string>(operatorRoleNames, StringComparer.OrdinalIgnoreCase);
        return !string.IsNullOrWhiteSpace(targetRoleName) && roles.Contains(targetRoleName);
    }

    /// <summary>
    /// <see cref="IsRoleAttachable"/> 的抛出形态：不通过即抛 RoleDataScopeEscalation
    /// （同族错误码，语义同为「反提权」），供交互面（OU 管理）继续使用。
    /// </summary>
    public static void EnsureRoleAttachable(string? targetRoleName, IEnumerable<string> operatorRoleNames, bool operatorSnapshotIsAll)
    {
        if (!IsRoleAttachable(targetRoleName, operatorRoleNames, operatorSnapshotIsAll))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.DataScopes.RoleDataScopeEscalation)
                .WithData("ScopeType", $"role conferral: {targetRoleName}");
        }
    }
}
