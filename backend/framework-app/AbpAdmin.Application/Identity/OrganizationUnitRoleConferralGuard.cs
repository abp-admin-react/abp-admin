using System;
using System.Collections.Generic;
using AbpAdmin.ClientErrors;
using Volo.Abp;
using Volo.Abp.Roles;

namespace AbpAdmin.Identity;

/// <summary>
/// OU 通道的角色授予守卫（纯决策函数，便于脱离 DB 单测）。
/// ABP 把 OU 派生角色并集进完整角色成员资格（IdentityUserStore.GetRolesAsync），
/// OU 挂角色 = 给全体成员授该角色，必须遵守 ABP 的角色分配边界：
/// 仅 admin 可授予任意角色；其他操作者只能授予自己已持有的角色。
/// 业务数据可见范围不代表身份管理权限，不参与此判定。
/// </summary>
public static class OrganizationUnitRoleConferralGuard
{
    /// <summary>
    /// 操作者是否持有可授予任意角色的 admin 超管角色。
    /// 调用方用此短路昂贵的角色/OU 角色列举，再对受限操作者走 <see cref="IsRoleAttachable"/>。
    /// </summary>
    public static bool IsPrivilegedOperator(IEnumerable<string> operatorRoleNames)
    {
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
    /// 部门挂角色是否允许：操作者是 admin，或目标角色是操作者已持有的角色。
    /// 用户编辑的可授名单在 FilterRolesByCurrentUserAsync，不经过本守卫。
    /// </summary>
    public static bool IsRoleAttachable(string? targetRoleName, IEnumerable<string> operatorRoleNames)
    {
        if (IsPrivilegedOperator(operatorRoleNames))
        {
            return true;
        }

        var roles = new HashSet<string>(operatorRoleNames, StringComparer.OrdinalIgnoreCase);
        return !string.IsNullOrWhiteSpace(targetRoleName) && roles.Contains(targetRoleName);
    }

    /// <summary>
    /// <see cref="IsRoleAttachable"/> 的抛出形态：交互面统一返回已登记的禁止访问错误。
    /// </summary>
    public static void EnsureRoleAttachable(string? targetRoleName, IEnumerable<string> operatorRoleNames)
    {
        if (!IsRoleAttachable(targetRoleName, operatorRoleNames))
        {
            throw new BusinessException(ClientErrorCodes.Forbidden)
                .WithData("RoleName", targetRoleName ?? string.Empty);
        }
    }
}
