using System;
using System.Linq;
using Volo.Abp.Identity;

namespace AbpAdmin.Notifications;

/// <summary>
/// 广播目标用户查询构建器（T3.5）。NotificationDispatcher 的 COUNT 与
/// BroadcastNotificationJob 的分页共用同一过滤表达式——两处各写一份 switch 的话，
/// 新增目标类型漏改一处就会让 TotalCount 与实际 fan-out 口径分裂。
/// </summary>
public static class NotificationTargetUserQueryExtensions
{
    /// <summary>
    /// 按广播目标类型过滤用户（All = 不过滤；Role / OrganizationUnit 按关联表存在性过滤）。
    /// </summary>
    public static IQueryable<IdentityUser> ApplyBroadcastTarget(
        this IQueryable<IdentityUser> query, string targetType, Guid? targetId)
    {
        return targetType switch
        {
            NotificationBroadcastTargetTypes.Role =>
                query.Where(u => u.Roles.Any(r => r.RoleId == targetId)),
            NotificationBroadcastTargetTypes.OrganizationUnit =>
                query.Where(u => u.OrganizationUnits.Any(o => o.OrganizationUnitId == targetId)),
            _ => query
        };
    }
}
