using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace AbpAdmin.Notifications;

/// <summary>
/// 广播断链看门狗（JobType = AbpAdmin.BroadcastNotificationWatchdog）。
/// BroadcastNotificationJob 的续跑依赖「本批提交后自入队下一批」，链条可能断在：
/// 本批 UoW 已提交但自入队失败/进程在两步之间崩溃/瞬时异常重试耗尽——广播永久停在
/// Running，无任何自愈路径（进程重启也不会补跑，作业队列里已没有这条链）。
///
/// 本作业周期扫描 Running 且 LastModificationTime 超过阈值的广播（每批游标推进都会
/// 触发行更新、刷新 LastModificationTime，正常链条几秒一批；1 小时无推进即视为断链），
/// 按其 TenantId 重新入队 BroadcastNotificationJob：续跑从库里的 LastProcessedUserId
/// 游标开始，已提交批次的用户不会重复收到通知（批次级幂等）；作业开头有终态短路，
/// 广播实际已 Completed/Failed 时重入队是一次无害空跑。
///
/// 重入队带防重入窗口（RequeueGuardWindow，见其注释）：入队前把时间戳写进广播的
/// ExtraProperties 持久化，窗口内不重复入队，瞬时故障停滞期间不繁殖并发链。
///
/// 放 Application 层（与 BroadcastNotificationJob 同侧）：需要构造其 JobArgs，
/// Domain 不能反向引用 Application。注册方式与 GdprRequestCleanupJobHandler 同款
/// （[ExposeServices(typeof(IScheduledJobHandler))] + ITransientDependency）。
/// 种子默认停用（同其余 6 个内置作业），部署者按需在定时作业管理里启用。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler))]
public class BroadcastNotificationWatchdogJobHandler : IScheduledJobHandler, ITransientDependency
{
    public const string JobTypeName = ScheduledJobConsts.BroadcastNotificationWatchdogJobType;

    /// <summary>
    /// 断链判定阈值：Running 状态下 LastModificationTime 距今超过该时长即重新入队。
    /// 实体没有独立的 StartTime，用 LastModificationTime（Start()/每次 Advance 都会更新）代替，
    /// 语义等价于「这么久没有任何一批完成」。
    /// </summary>
    public static readonly TimeSpan StuckThreshold = TimeSpan.FromHours(1);

    /// <summary>
    /// 防重入窗口：距上次看门狗重入队不足该时长时跳过，避免链繁殖。瞬时故障（DB 抖动等）
    /// 导致广播停滞超过 StuckThreshold 期间，若看门狗每次扫描（约 15 分钟）都无条件 Enqueue，
    /// 故障持续 1 小时就会繁殖 4 条并发链——故障恢复后多条链跑同一广播，重复通知且游标
    /// 计数互相覆盖。窗口取 30 分钟（约两个扫描周期）：单链恢复能力不受影响（链条真断了，
    /// 最多延迟 30 分钟补一条），而故障期间最多只有 1 条看门狗链在等待。
    /// </summary>
    public static readonly TimeSpan RequeueGuardWindow = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 上次看门狗重入队时间（UTC "O" 格式字符串），记在广播的 ExtraProperties 里随行持久化。
    /// </summary>
    public const string WatchdogRequeuedAtPropertyName = "WatchdogRequeuedAt";

    public string JobType => JobTypeName;

    public string DisplayNameKey => "ScheduledJobType:BroadcastNotificationWatchdog";

    private readonly IRepository<NotificationBroadcast, Guid> _broadcastRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;
    private readonly IClock _clock;
    private readonly ILogger<BroadcastNotificationWatchdogJobHandler> _logger;

    public BroadcastNotificationWatchdogJobHandler(
        IRepository<NotificationBroadcast, Guid> broadcastRepository,
        IBackgroundJobManager backgroundJobManager,
        IAsyncQueryableExecuter asyncExecuter,
        IDataFilter dataFilter,
        IClock clock,
        ILogger<BroadcastNotificationWatchdogJobHandler> logger)
    {
        _broadcastRepository = broadcastRepository;
        _backgroundJobManager = backgroundJobManager;
        _asyncExecuter = asyncExecuter;
        _dataFilter = dataFilter;
        _clock = clock;
        _logger = logger;
    }

    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        // 关多租户过滤器跨租户巡检：看门狗作业行通常建在 host，但广播记录属于各租户；
        // 只按 TenantId 入队、不切换租户上下文（BroadcastNotificationJob 自己会 Change）。
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var cutoff = _clock.Now - StuckThreshold;

            var queryable = await _broadcastRepository.GetQueryableAsync();
            var stuckBroadcasts = await _asyncExecuter.ToListAsync(
                queryable.Where(b =>
                    b.State == NotificationBroadcastStates.Running &&
                    (b.LastModificationTime == null || b.LastModificationTime < cutoff)));

            foreach (var broadcast in stuckBroadcasts)
            {
                // 防重入窗口：上次重入队还在窗口内说明那条链大概率仍在（或仍在等瞬时故障恢复），
                // 再入队只会繁殖并发链。作业侧另有并发兜底（游标写回遇乐观并发冲突的链退出）。
                if (IsWithinRequeueGuardWindow(broadcast))
                {
                    _logger.LogInformation(
                        "广播 {BroadcastId}（租户 {TenantId}）在 {Window} 防重入窗口内已重入队过，本次跳过。",
                        broadcast.Id, broadcast.TenantId, RequeueGuardWindow);
                    continue;
                }

                // 先落库再入队：窗口标记必须先于 Enqueue 持久化——若只入队不落库，
                // 下次扫描看不到标记仍会繁殖链。写标记本身失败（DB 故障）则本次跳过，
                // 留给下个扫描周期重试，宁可晚补链也不冒双链风险。
                broadcast.ExtraProperties[WatchdogRequeuedAtPropertyName] = DateTime.UtcNow.ToString("O");
                await _broadcastRepository.UpdateAsync(broadcast, autoSave: true);

                await _backgroundJobManager.EnqueueAsync(
                    new BroadcastNotificationJobArgs(broadcast.TenantId, broadcast.Id));

                _logger.LogWarning(
                    "广播 {BroadcastId}（租户 {TenantId}）Running 停滞超过 {Threshold}，已按持久化游标重新入队续跑。",
                    broadcast.Id, broadcast.TenantId, StuckThreshold);
            }
        }
    }

    /// <summary>
    /// 广播是否仍处于防重入窗口内（上次看门狗重入队距今不足 <see cref="RequeueGuardWindow"/>）。
    /// 时间戳损坏/缺失时返回 false（不拦截，保守允许补链）。
    /// </summary>
    protected virtual bool IsWithinRequeueGuardWindow(NotificationBroadcast broadcast)
    {
        // 比较必须与写入侧同为 UTC 墙钟：标记恒以 DateTime.UtcNow 持久化，而 IClock 默认
        // ClockProvider=Unspecified，Now 返回本机本地墙钟——UTC+8 宿主上「本地 − UTC」恒偏 +8h，
        // 窗口判定永远为已超窗 → 防重入形同虚设、停滞期间每次扫描都重复入队（恰是该窗口要防的链繁殖）；
        // 负偏移时区则反向过度拦截。同文件的 cutoff/LastModificationTime 两侧同为 IClock 基准，不动。
        return TryGetWatchdogRequeuedAt(broadcast, out var requeuedAt) &&
               DateTime.UtcNow - requeuedAt < RequeueGuardWindow;
    }

    /// <summary>
    /// 解析 ExtraProperties 里的上次重入队时间（写入时是 UTC "O" 字符串，但 ABP 的
    /// ExtraProperties 值转换器经 System.Text.Json 推断类型反序列化，ISO 日期串会还原成
    /// 装箱 DateTime）——string 与 DateTime 两种形态都要认，只认一种会让窗口形同虚设。
    /// </summary>
    protected virtual bool TryGetWatchdogRequeuedAt(NotificationBroadcast broadcast, out DateTime requeuedAt)
    {
        requeuedAt = default;

        if (!broadcast.ExtraProperties.TryGetValue(WatchdogRequeuedAtPropertyName, out var raw) || raw == null)
        {
            return false;
        }

        switch (raw)
        {
            case DateTime dateTime:
                // 写入时恒为 UTC（UtcDateTime.ToString("O")），Unspecified 只可能是反序列化丢了 Kind
                requeuedAt = dateTime.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
                    : dateTime.ToUniversalTime();
                return true;
            case string text:
                return DateTime.TryParse(text, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out requeuedAt);
            default:
                return false;
        }
    }
}
