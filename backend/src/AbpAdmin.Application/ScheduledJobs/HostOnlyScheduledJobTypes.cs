using System.Collections.Generic;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// Host 专属 JobType 集合（round3 security F5 收口）。
/// 集合内的作业 handler 会关多租户过滤器做跨租户巡检/入队（如广播看门狗），作业行虽可落在
/// 任意租户，但语义上只允许 Host 侧配置与驱动——ScheduledJobs.Create/Update 权限租户侧可用，
/// 若不在 AppService 显式校验，租户管理员就能自建/启用这类作业触发 Host 级跨租户扫描。
/// 消费方：ScheduledJobAppService 的 Create/Update/SetEnabled/Trigger 守卫（AbpAdmin:HostSideOnly）。
/// 放 Application 层而非 Domain/Contracts：集合元素引用的是 Domain.Shared 的
/// <see cref="ScheduledJobConsts"/> 常量（watchdog handler 的 JobTypeName 也指向它），
/// 后续新增 Host 专属作业时往 All 里加 handler 的 JobType 常量即可。
/// </summary>
public static class HostOnlyScheduledJobTypes
{
    public static readonly HashSet<string> All =
    [
        // 广播断链看门狗：ExecuteAsync 关 IMultiTenant 过滤器扫全部租户的 Running 广播并入队续跑。
        ScheduledJobConsts.BroadcastNotificationWatchdogJobType,
    ];
}
