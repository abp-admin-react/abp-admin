using System.Collections.Generic;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// Host 专属 JobType 集合。
/// 集合内的作业 handler 会关多租户过滤器做跨租户巡检/入队（如广播看门狗），作业行虽可落在
/// 任意租户，但语义上只允许 Host 侧配置与驱动——ScheduledJobs 权限租户侧可用，
/// 若不在 AppService 显式校验，租户管理员就能自建/启用这类作业触发 Host 级跨租户扫描。
/// 消费方：ScheduledJobAppService 的全部生命周期变更守卫
/// （Create/Update/Delete/SetEnabled/Trigger，AbpAdmin:HostSideOnly）。
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
        // 订单补状态（收款/订单计划第 2 节）：扫的是无租户列的 OrdOrders/ColCollections，
        // 语义上只允许 Host 侧配置与驱动；种子只给 Host 插行（OrderingJobDataSeedContributor）。
        ScheduledJobConsts.OrderStatusSyncJobType,
        // 余额流水合计核对（收款/订单计划第 5.5 节）：扫 ColBalances/ColBalanceTxns，同样无租户列。
        ScheduledJobConsts.BalanceAuditJobType,
        // 金库核对（收款/订单计划第 57、291 行）：读链 balanceOf + ColBalances，无租户列。
        ScheduledJobConsts.VaultAuditJobType,
    ];
}
