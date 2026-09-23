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

    public const int MaxNameLength = 128;

    public const int MaxJobTypeLength = 128;

    /// <summary>Quartz cron 7 段表达式实际很短，64 足够（含年字段与特殊字符）。</summary>
    public const int MaxCronExpressionLength = 64;

    public const int MaxDescriptionLength = 512;

    /// <summary>执行记录与 LastRunMessage 共用 4000：异常堆栈可以很长，不限长会让表迅速膨胀。</summary>
    public const int MaxMessageLength = 4000;
}
