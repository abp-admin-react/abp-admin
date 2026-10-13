namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// T3.3 定时作业字段长度常量。EF 配置与实体写入截断共用同一份，避免两边漂移。
/// </summary>
public static class ScheduledJobConsts
{
    /// <summary>
    /// 广播断链看门狗的 JobType。收口到 Domain.Shared（round4 design F1）：handler 在
    /// Application 层（要构造 BroadcastNotificationJobArgs），Domain 的种子贡献器不能反向
    /// 引用它，此前种子/HostOnly 守卫与 handler 三处靠字面量+注释同步，改名即静默漂移
    /// （存量行匹配不到 handler，HostOnly 守卫漏保护）。三处现在都引用本常量。
    /// </summary>
    public const string BroadcastNotificationWatchdogJobType = "AbpAdmin.BroadcastNotificationWatchdog";

    /// <summary>
    /// HTTP 探活作业的 JobType。handler 在 Application 层 Monitoring（要出站 HTTP 并发布失败事件），
    /// 常量收口到 Domain.Shared（同上条理由：DisplayKey、未来守卫/种子与 handler 三处单一出处）。
    /// </summary>
    public const string HttpProbeJobType = "AbpAdmin.HttpProbe";

    /// <summary>
    /// 订单补状态作业的 JobType（收款/订单计划第 2、3.3 节）：到账核对、过期关单、第二笔到账
    /// 与复活检查。handler 在 AbpAdmin.Ordering（要调收款集成服务），常量收口到 Domain.Shared，
    /// 框架在编译期要看到这些字符串（HostOnlyScheduledJobTypes 与作业种子共用同一出处）。
    /// Host 专用：这两套表没有租户列，租户上下文不种行、不能改不能启用（ScheduledJobAppService 守卫）。
    /// 后续里程碑再加：定时关单/渠道补单（M3）、金库核对（M5）。
    /// </summary>
    public const string OrderStatusSyncJobType = "AbpAdmin.OrderStatusSync";

    /// <summary>
    /// 流水合计作业的 JobType（收款/订单计划第 5.5 节）：按用户和币种核对
    /// 「流水之和 == 余额行」，不等则告警（告警清单里的「账本不平」）。
    /// handler 在 AbpAdmin.Collection，Host 专用（同 OrderStatusSync 的理由）。
    /// </summary>
    public const string BalanceAuditJobType = "AbpAdmin.BalanceAudit";

    /// <summary>
    /// 金库核对作业的 JobType（收款/订单计划第 57、291 行）：每日按币 balanceOf(金库) vs
    /// 用户余额合计并追加 ColVaultChecks。handler 在 AbpAdmin.Collection，Host 专用。
    /// 扫链/归集不是定时作业——是宿主注册的 BackgroundWorker（AddCollectionChainWorkers）。
    /// </summary>
    public const string VaultAuditJobType = "AbpAdmin.ChainVaultAudit";

    public const int MaxNameLength = 128;

    public const int MaxJobTypeLength = 128;

    /// <summary>Quartz cron 7 段表达式实际很短，64 足够（含年字段与特殊字符）。</summary>
    public const int MaxCronExpressionLength = 64;

    public const int MaxDescriptionLength = 512;

    /// <summary>执行记录与 LastRunMessage 共用 4000：异常堆栈可以很长，不限长会让表迅速膨胀。</summary>
    public const int MaxMessageLength = 4000;
}
