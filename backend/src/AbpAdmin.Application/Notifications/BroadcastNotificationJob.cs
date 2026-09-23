using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAbp.NotificationService.Notifications;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Json;
using Volo.Abp.Linq;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Notifications;

/// <summary>
/// 广播 fan-out 批次作业（T3.5 第 10 步）。每批取一页目标用户、按渠道各发一个创建事件、
/// 推进游标、自入队下一批（并发天然为 1，不会打爆数据库）。
///
/// 关键设计：
/// - 游标以数据库里 NotificationBroadcast.LastProcessedUserId 为准而不是 args——
///   批次级幂等：作业失败被 ABP 重试时从库里的游标续跑，已提交批次的用户不会重复收到通知。
/// - 每批一个事务 UoW：通知记录（本地事件总线内联插入）与游标推进同一事务提交，
///   要么整批成功要么整批回滚，重试不会留下半批。
/// - keyset 游标（Id &gt; 游标）而不是 SkipCount：深分页性能差且期间删人会漏。
///   游标与过滤都是 Guid 直接比较 + OrderBy(Id)：PG uuid&gt;uuid 走 PK 索引；SQLite 的
///   Guid→TEXT 类型映射对列与参数用同一转换（同格式 TEXT，字典序 = Guid 十六进制序），
///   同样可走索引。不再把 Id 投影为文本列比较——lower()/uuid::text 每批都是全表扫描+排序。
///   过滤与排序都下推到 SQL，每批只取一页，不做全量 Id 投影（那会让 N 用户广播累计 O(N²) 行传输）。
/// - CurrentTenant.Change 在外、UoW 在内（00-overview 6.5 的唯一正确顺序）。
/// - 本作业放 Application 层：ETO 构造唯一地点是 NotificationDispatcher（门面隔离验收），
///   Domain 层不能反向引用。
/// - 确定性业务错误（渠道配置类，见 IsDeterministicChannelError）重试无意义：直接置 Failed 终态，
///   只有瞬时异常（网络/DB 超时）交给 ABP 作业重试。
/// - 多链并发兜底：看门狗重入队/重试可能让多条链短暂并发跑同一广播（看门狗有防重入窗口，
///   见 BroadcastNotificationWatchdogJobHandler）。游标写回撞乐观并发冲突（ConcurrencyStamp）
///   的链记警告并终止，不重试不覆盖——输家退出，赢家链继续推进。
/// </summary>
public class BroadcastNotificationJob : IAsyncBackgroundJob<BroadcastNotificationJobArgs>, ITransientDependency
{
    /// <summary>每批用户数（规格建议 500）。</summary>
    public const int BatchSize = 500;

    private readonly IRepository<NotificationBroadcast, Guid> _broadcastRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly INotificationDispatcher _notificationDispatcher;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IClock _clock;
    private readonly ILogger<BroadcastNotificationJob> _logger;

    public BroadcastNotificationJob(
        IRepository<NotificationBroadcast, Guid> broadcastRepository,
        IRepository<IdentityUser, Guid> userRepository,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager,
        IBackgroundJobManager backgroundJobManager,
        INotificationDispatcher notificationDispatcher,
        IJsonSerializer jsonSerializer,
        IAsyncQueryableExecuter asyncExecuter,
        IClock clock,
        ILogger<BroadcastNotificationJob> logger)
    {
        _broadcastRepository = broadcastRepository;
        _userRepository = userRepository;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;
        _backgroundJobManager = backgroundJobManager;
        _notificationDispatcher = notificationDispatcher;
        _jsonSerializer = jsonSerializer;
        _asyncExecuter = asyncExecuter;
        _clock = clock;
        _logger = logger;
    }

    public virtual async Task ExecuteAsync(BroadcastNotificationJobArgs args)
    {
        // 外层租户（00-overview 6.5：UoW 必须在内层，否则 DbContext 按切换前的租户建）
        using (_currentTenant.Change(args.TenantId))
        {
            string[] methods;
            using (var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                var broadcast = await _broadcastRepository.GetAsync(args.BroadcastId);

                if (broadcast.State is NotificationBroadcastStates.Completed or NotificationBroadcastStates.Failed)
                {
                    return;
                }

                methods = broadcast.NotificationMethods.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (methods.Length == 0)
                {
                    // 确定性损坏，重试无意义——直接终态
                    broadcast.Fail(_clock);
                    await _broadcastRepository.UpdateAsync(broadcast);
                    await uow.CompleteAsync();
                    return;
                }

                if (broadcast.State == NotificationBroadcastStates.Pending)
                {
                    broadcast.Start();
                }

                try
                {
                    var pageUsers = await GetTargetUserPageAsync(broadcast);

                    if (pageUsers.Count == 0)
                    {
                        broadcast.Complete(_clock);
                        await _broadcastRepository.UpdateAsync(broadcast);
                        await uow.CompleteAsync();
                        _logger.LogInformation("广播 {BroadcastId} 完成，共 {SentCount}/{TotalCount} 条创建。",
                            broadcast.Id, broadcast.SentCount, broadcast.TotalCount);
                        return;
                    }

                    // 每渠道一个创建事件（一批用户共享一条 NotificationInfo——模块行为，规格第 10 步已说明）。
                    // 本地分布式总线（05 第 9.5 节）：handler 在本 UoW 内联执行，与游标推进同一事务。
                    foreach (var method in methods)
                    {
                        await PublishBatchAsync(method, broadcast, pageUsers);
                    }

                    broadcast.Advance(pageUsers[^1].Id, pageUsers.Count);
                }
                catch (BusinessException ex) when (IsDeterministicChannelError(ex))
                {
                    // 渠道配置类错误（凭据未配置/模板缺失/非法渠道）每批都会在同一位置抛，
                    // 重试耗尽也只会让批次永久停在 Running——直接终态并停止后续批次。
                    broadcast.Fail(_clock);
                    await _broadcastRepository.UpdateAsync(broadcast);
                    await uow.CompleteAsync();
                    _logger.LogError(ex, "广播 {BroadcastId} 因确定性错误 {ErrorCode} 终止，已创建 {SentCount}/{TotalCount} 条。",
                        broadcast.Id, ex.Code, broadcast.SentCount, broadcast.TotalCount);
                    return;
                }

                // 游标推进/SentCount 写回是链间互斥点（实体带 ConcurrencyStamp）：
                // 看门狗重入队/ABP 重试产生多链并发跑同一广播时，输家在此处撞乐观并发冲突——
                // 记警告并终止本链（不重试不覆盖，直接 return 跳过自入队），已提交的赢家链继续走。
                try
                {
                    await _broadcastRepository.UpdateAsync(broadcast);
                    await uow.CompleteAsync();
                }
                catch (AbpDbConcurrencyException ex)
                {
                    _logger.LogWarning(ex,
                        "广播 {BroadcastId} 游标写回遇乐观并发冲突（疑似多链并发），本链终止退出，由并发链继续推进。",
                        args.BroadcastId);
                    return;
                }
            }

            // 自入队下一批：独立 UoW，与本批业务写入分开（00-overview 6.5）
            using (var enqueueUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                await _backgroundJobManager.EnqueueAsync(
                    new BroadcastNotificationJobArgs(args.TenantId, args.BroadcastId));
                await enqueueUow.CompleteAsync();
            }
        }
    }

    /// <summary>
    /// 确定性渠道错误白名单（重试不可能成功）。新增配置类错误码时须同步维护这里；
    /// 瞬时错误（网络、DB 超时）不在此列，交由作业重试。
    /// 注意：这里只可能捕获「广播创建阶段」同步抛出的错误——PublishBatchAsync 只是发布
    /// 创建事件（本地总线内联建 Notification 记录），真正的发送（含 SMS 凭据缺失/模板未配置，
    /// 即 SmsAliyunCredentialNotConfigured / SmsTencentCloudCredentialNotConfigured /
    /// SmsTemplateNotConfigured）发生在每通知一个的异步发送作业里，不会回抛到本作业：
    /// 广播状态只反映创建阶段，发送失败体现在每条 Notification 的发送状态上
    /// （曾把这三个 SMS 错误码列进白名单，属不可达死条目，已删除）。
    /// </summary>
    protected static bool IsDeterministicChannelError(BusinessException ex)
    {
        return ex.Code is AbpAdminDomainErrorCodes.Notifications.InvalidBroadcastMethods;
    }

    /// <summary>
    /// 取下一页目标用户（keyset 游标下推到 SQL，一次只取一页）。返回的用户按 Id 升序。
    /// </summary>
    protected virtual async Task<List<IdentityUser>> GetTargetUserPageAsync(NotificationBroadcast broadcast)
    {
        var queryable = await _userRepository.GetQueryableAsync();

        // 与 NotificationDispatcher.CountTargetUsersAsync 共用同一过滤（口径一致）
        queryable = queryable.ApplyBroadcastTarget(broadcast.TargetType, broadcast.TargetId);

        // Guid 直接比较（不再投影文本列）：
        // - PG：uuid > uuid 原生比较，过滤与排序都能走 Id 的 PK 索引；
        // - SQLite：EF 的 Guid→TEXT 类型映射对列与参数用同一转换（同格式大写 TEXT），
        //   TEXT 上的 > 与 ORDER BY 字典序一致，同样可走索引。
        // 两侧字典序/uuid 序都与 .NET Guid 比较序（十六进制无符号序）一致，与旧文本游标
        // 语义等价——存量广播的 LastProcessedUserId 游标可无缝续跑。
        var cursor = broadcast.LastProcessedUserId;
        if (cursor.HasValue)
        {
            queryable = queryable.Where(u => u.Id > cursor.Value);
        }

        // ORDER BY Id 已保证页内按 Id 升序（游标取页尾 pageUsers[^1].Id），无需二次查询与内存重排
        return await _asyncExecuter.ToListAsync(queryable.OrderBy(u => u.Id).Take(BatchSize));
    }

    protected virtual async Task PublishBatchAsync(string method, NotificationBroadcast broadcast, List<IdentityUser> users)
    {
        var userModels = users.Select(u => new NotificationUserInfoModel(u.Id, u.UserName)).ToList();

        switch (method)
        {
            case NotificationMethodConsts.Mailing:
                await _notificationDispatcher.SendEmailBatchAsync(broadcast.TenantId, userModels, broadcast.Title, broadcast.Body);
                break;
            case NotificationMethodConsts.Sms:
                await _notificationDispatcher.SendSmsBatchAsync(
                    broadcast.TenantId,
                    userModels,
                    broadcast.SmsText ?? broadcast.Body,
                    DeserializeSmsProperties(broadcast));
                break;
            case NotificationMethodConsts.InApp:
                await _notificationDispatcher.SendInAppBatchAsync(broadcast.TenantId, userModels, broadcast.Title, broadcast.Body);
                break;
            default:
                throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.InvalidBroadcastMethods)
                    .WithData("Method", method);
        }
    }

    protected virtual Dictionary<string, object> DeserializeSmsProperties(NotificationBroadcast broadcast)
    {
        return broadcast.SmsPropertiesJson.IsNullOrWhiteSpace()
            ? new Dictionary<string, object>()
            : _jsonSerializer.Deserialize<Dictionary<string, object>>(broadcast.SmsPropertiesJson);
    }
}

[Serializable]
public class BroadcastNotificationJobArgs : IMultiTenant
{
    public Guid? TenantId { get; set; }

    public Guid BroadcastId { get; set; }

    public BroadcastNotificationJobArgs()
    {
    }

    public BroadcastNotificationJobArgs(Guid? tenantId, Guid broadcastId)
    {
        TenantId = tenantId;
        BroadcastId = broadcastId;
    }
}
