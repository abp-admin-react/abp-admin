using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using EasyAbp.NotificationService.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Notifications;

/// <summary>
/// T3.5 广播 fan-out 测试：分批创建、游标续跑（批次级幂等）、站内信发送作业标记成功。
/// 测试环境 IsJobExecutionEnabled=false，作业手动执行。
/// </summary>
public abstract class BroadcastNotificationJobTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    protected static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly INotificationDispatcher _dispatcher;
    private readonly IRepository<NotificationBroadcast, Guid> _broadcastRepository;
    private readonly INotificationRepository _notificationRepository;
    private readonly IRepository<IdentityUser, Guid> _userRepository;
    private readonly IdentityUserManager _userManager;

    protected BroadcastNotificationJobTests()
    {
        _dispatcher = GetRequiredService<INotificationDispatcher>();
        _broadcastRepository = GetRequiredService<IRepository<NotificationBroadcast, Guid>>();
        _notificationRepository = GetRequiredService<INotificationRepository>();
        _userRepository = GetRequiredService<IRepository<IdentityUser, Guid>>();
        _userManager = GetRequiredService<IdentityUserManager>();
    }

    private async Task<IdentityUser> CreateUserAsync(string prefix)
    {
        var name = prefix + Guid.NewGuid().ToString("N")[..6];
        var user = new IdentityUser(Guid.NewGuid(), name, name + "@abp.io");
        (await _userManager.CreateAsync(user, "Test@123456")).Succeeded.ShouldBeTrue();
        return user;
    }

    private async Task RunJobOnceAsync(Guid broadcastId)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var job = GetRequiredService<BroadcastNotificationJob>();
            await job.ExecuteAsync(new BroadcastNotificationJobArgs(null, broadcastId));
        });
    }

    private async Task<NotificationBroadcast> RunUntilCompleteAsync(Guid broadcastId, int maxRounds = 10)
    {
        for (var i = 0; i < maxRounds; i++)
        {
            await RunJobOnceAsync(broadcastId);
            var state = await WithUnitOfWorkAsync(async () =>
                (await _broadcastRepository.GetAsync(broadcastId)).State);
            if (state is NotificationBroadcastStates.Completed or NotificationBroadcastStates.Failed)
            {
                break;
            }
        }

        return await WithUnitOfWorkAsync(async () => await _broadcastRepository.GetAsync(broadcastId));
    }

    [Fact]
    public async Task Broadcast_To_All_Should_Create_Notifications_For_Every_User()
    {
        await CreateUserAsync("t35-b1-");
        await CreateUserAsync("t35-b2-");
        var marker = "ut-bc-" + Guid.NewGuid().ToString("N")[..8];

        var broadcastId = await WithUnitOfWorkAsync(async () =>
            await _dispatcher.BroadcastAsync(new BroadcastNotificationInput
            {
                TargetType = NotificationBroadcastTargetTypes.All,
                NotificationMethods = new List<string> { InAppNotificationConsts.NotificationMethod },
                Title = marker,
                Body = "广播正文"
            }));

        var broadcast = await RunUntilCompleteAsync(broadcastId);

        broadcast.State.ShouldBe(NotificationBroadcastStates.Completed);
        broadcast.TotalCount.ShouldBeGreaterThanOrEqualTo(2); // user1 + user2（测试库未播种 admin 用户行）
        broadcast.SentCount.ShouldBe(broadcast.TotalCount);

        await WithUnitOfWorkAsync(async () =>
        {
            // 本次广播的通知应覆盖当前 AbpUsers 里的每一个用户
            var allUserIds = (await _userRepository.GetListAsync()).Select(u => u.Id).ToList();
            var infoIds = await GetNotificationInfoIdsByTitleAsync(marker);
            var sentUserIds = (await _notificationRepository.GetListAsync(
                    n => infoIds.Contains(n.NotificationInfoId)))
                .Select(n => n.UserId).Distinct().OrderBy(x => x).ToList();
            sentUserIds.ShouldBe(allUserIds.OrderBy(x => x));
        });
    }

    [Fact]
    public async Task Resumed_Job_Should_Skip_Already_Processed_Users()
    {
        // 模拟"上一批已处理到 cursor"：把游标推到当前最小用户 Id，
        // 重跑（重试）时该用户不应再收到通知——LastProcessedUserId 续跑逻辑。
        await CreateUserAsync("t35-b3-");
        var marker = "ut-rs-" + Guid.NewGuid().ToString("N")[..8];

        var broadcastId = await WithUnitOfWorkAsync(async () =>
            await _dispatcher.BroadcastAsync(new BroadcastNotificationInput
            {
                TargetType = NotificationBroadcastTargetTypes.All,
                NotificationMethods = new List<string> { InAppNotificationConsts.NotificationMethod },
                Title = marker,
                Body = "广播正文"
            }));

        // 游标取当前最小用户 Id（十六进制序，与作业的比较规则一致）——
        // 这样跳过集合与续跑集合都确定非空，不依赖 Guid 生成的排序偶然性
        var allUserIds = (await WithUnitOfWorkAsync(async () =>
                (await _userRepository.GetListAsync()).Select(u => u.Id).ToList()))
            .OrderBy(id => id.ToString("D"), StringComparer.OrdinalIgnoreCase)
            .ToList();
        allUserIds.Count.ShouldBeGreaterThanOrEqualTo(2);
        var cursorUserId = allUserIds[0];
        var expectedUserIds = allUserIds.Skip(1).ToList();
        expectedUserIds.ShouldNotBeEmpty();

        await WithUnitOfWorkAsync(async () =>
        {
            var broadcast = await _broadcastRepository.GetAsync(broadcastId);
            broadcast.Start();
            broadcast.Advance(cursorUserId, 1);
            await _broadcastRepository.UpdateAsync(broadcast);
        });

        var broadcast = await RunUntilCompleteAsync(broadcastId);
        broadcast.State.ShouldBe(NotificationBroadcastStates.Completed);

        await WithUnitOfWorkAsync(async () =>
        {
            // 本广播创建的记录应只属于"游标之后"的用户
            var allInfos = await GetNotificationInfoIdsByTitleAsync(marker);
            var notificationsOfThisBroadcast = await _notificationRepository.GetListAsync(
                n => allInfos.Contains(n.NotificationInfoId));
            notificationsOfThisBroadcast.ShouldNotBeEmpty();
            notificationsOfThisBroadcast.Select(n => n.UserId).Distinct().OrderBy(x => x)
                .ShouldBe(expectedUserIds.OrderBy(x => x));
        });
    }

    [Fact]
    public async Task Deterministic_Channel_Error_Should_Fail_Broadcast_With_Terminal_State()
    {
        // 非法渠道是确定性错误（重试无意义）：作业应置 Failed 终态而不是
        // 让批次在 ABP 作业重试耗尽后永久停在 Running
        await CreateUserAsync("t35-det-");
        Guid broadcastId = Guid.Empty;

        await WithUnitOfWorkAsync(async () =>
        {
            var broadcast = new NotificationBroadcast(
                Guid.NewGuid(),
                null,
                NotificationBroadcastTargetTypes.All,
                null,
                "WeirdChannel",
                "标题",
                "正文",
                null,
                null,
                1);
            await _broadcastRepository.InsertAsync(broadcast, true);
            broadcastId = broadcast.Id;
        });

        await RunJobOnceAsync(broadcastId); // 不应抛出

        var broadcastEntity = await WithUnitOfWorkAsync(async () => await _broadcastRepository.GetAsync(broadcastId));
        broadcastEntity.State.ShouldBe(NotificationBroadcastStates.Failed);
        broadcastEntity.CompletionTime.ShouldNotBeNull();
    }

    [Fact]
    public async Task Watchdog_Should_Reenqueue_Stuck_Running_Broadcast_Only()
    {
        // 断链看门狗：Running 且 LastModificationTime 超过阈值（1 小时）的广播被重新入队
        // （按持久化游标续跑，幂等）；仍在推进的 Running 广播不受影响
        var clock = GetRequiredService<Volo.Abp.Timing.IClock>();
        var stuckId = Guid.Empty;
        var freshId = Guid.Empty;

        await WithUnitOfWorkAsync(async () =>
        {
            var stuck = new NotificationBroadcast(
                Guid.NewGuid(), null, NotificationBroadcastTargetTypes.All, null,
                InAppNotificationConsts.NotificationMethod, "断链标题", "正文", null, null, 1);
            stuck.Start();
            // LastModificationTime 每批游标推进都会刷新：正常链条几秒一批，
            // 2 小时无推进即视为断链（实体无独立 StartTime，此字段是等价信号）
            stuck.LastModificationTime = clock.Now.AddHours(-2);
            await _broadcastRepository.InsertAsync(stuck, true);
            stuckId = stuck.Id;

            var fresh = new NotificationBroadcast(
                Guid.NewGuid(), null, NotificationBroadcastTargetTypes.All, null,
                InAppNotificationConsts.NotificationMethod, "在跑标题", "正文", null, null, 1);
            fresh.Start();
            fresh.LastModificationTime = clock.Now;
            await _broadcastRepository.InsertAsync(fresh, true);
            freshId = fresh.Id;
        });

        await WithUnitOfWorkAsync(async () =>
        {
            // handler 带 [ExposeServices(typeof(IScheduledJobHandler))]，具体类型不单独注册，
            // 与 ExpiredAuditLogDeleterTests 同款：按 JobType 从 IScheduledJobHandler 集合里取
            var watchdog = ServiceProvider
                .GetServices<IScheduledJobHandler>()
                .Single(h => h.JobType == BroadcastNotificationWatchdogJobHandler.JobTypeName);
            await watchdog.ExecuteAsync(new ScheduledJobContext
            {
                JobType = BroadcastNotificationWatchdogJobHandler.JobTypeName
            });
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var jobRepository = GetRequiredService<Volo.Abp.BackgroundJobs.IBackgroundJobRepository>();
            var broadcastJobs = (await jobRepository.GetListAsync())
                .Where(j => j.JobName.Contains(nameof(BroadcastNotificationJobArgs)))
                .ToList();

            // 断链广播被重新入队；仍在推进的广播不受影响
            broadcastJobs.Any(j => j.JobArgs.Contains(stuckId.ToString())).ShouldBeTrue();
            broadcastJobs.Any(j => j.JobArgs.Contains(freshId.ToString())).ShouldBeFalse();
        });
    }

    [Fact]
    public async Task Watchdog_Requeue_Guard_Window_Should_Skip_Recent_Marker_In_Both_Shapes()
    {
        // 防重入窗口（round4 前零锚定，且时基修复本身需要回归锚）：
        // 标记 31 分钟前（超窗）→ 允许重入队；标记 10 分钟前（窗内）→ 跳过且不覆盖标记。
        // 两种持久化形态都要认——写入侧是 UTC "O" 字符串，ABP ExtraProperties 转换器
        // 读回时可能还原成装箱 DateTime（Kind 丢失按 UTC 处理），只认一种会让窗口形同虚设。
        // 关键回归点：窗口比较必须与写入侧同为 UTC 墙钟（此前拿 IClock 本地墙钟减 UTC 标记，
        // UTC+8 宿主上恒偏 +8h → 窗口永不生效，每次扫描都重复入队繁殖并发链）。
        var clock = GetRequiredService<Volo.Abp.Timing.IClock>();
        var expiredBoxedId = Guid.Empty;
        var expiredStringId = Guid.Empty;
        var withinWindowId = Guid.Empty;

        await WithUnitOfWorkAsync(async () =>
        {
            NotificationBroadcast CreateStuck(string title)
            {
                var broadcast = new NotificationBroadcast(
                    Guid.NewGuid(), null, NotificationBroadcastTargetTypes.All, null,
                    InAppNotificationConsts.NotificationMethod, title, "正文", null, null, 1);
                broadcast.Start();
                broadcast.LastModificationTime = clock.Now.AddHours(-2);
                return broadcast;
            }

            var expiredBoxed = CreateStuck("超窗-装箱形态");
            expiredBoxed.ExtraProperties[BroadcastNotificationWatchdogJobHandler.WatchdogRequeuedAtPropertyName] =
                DateTime.UtcNow.AddMinutes(-31);
            await _broadcastRepository.InsertAsync(expiredBoxed, true);
            expiredBoxedId = expiredBoxed.Id;

            var expiredString = CreateStuck("超窗-字符串形态");
            expiredString.ExtraProperties[BroadcastNotificationWatchdogJobHandler.WatchdogRequeuedAtPropertyName] =
                DateTime.UtcNow.AddMinutes(-31).ToString("O");
            await _broadcastRepository.InsertAsync(expiredString, true);
            expiredStringId = expiredString.Id;

            var withinWindow = CreateStuck("窗内-装箱形态");
            withinWindow.ExtraProperties[BroadcastNotificationWatchdogJobHandler.WatchdogRequeuedAtPropertyName] =
                DateTime.UtcNow.AddMinutes(-10);
            await _broadcastRepository.InsertAsync(withinWindow, true);
            withinWindowId = withinWindow.Id;
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var watchdog = ServiceProvider
                .GetServices<IScheduledJobHandler>()
                .Single(h => h.JobType == BroadcastNotificationWatchdogJobHandler.JobTypeName);
            await watchdog.ExecuteAsync(new ScheduledJobContext
            {
                JobType = BroadcastNotificationWatchdogJobHandler.JobTypeName
            });
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var jobRepository = GetRequiredService<Volo.Abp.BackgroundJobs.IBackgroundJobRepository>();
            var broadcastJobs = (await jobRepository.GetListAsync())
                .Where(j => j.JobName.Contains(nameof(BroadcastNotificationJobArgs)))
                .ToList();

            // 超窗的两种形态都补链；窗内的跳过
            broadcastJobs.Any(j => j.JobArgs.Contains(expiredBoxedId.ToString())).ShouldBeTrue(
                "31 分钟前的标记（装箱 DateTime 形态）已超 30 分钟窗口，应重入队");
            broadcastJobs.Any(j => j.JobArgs.Contains(expiredStringId.ToString())).ShouldBeTrue(
                "31 分钟前的标记（UTC \"O\" 字符串形态）已超窗口，应重入队");
            broadcastJobs.Any(j => j.JobArgs.Contains(withinWindowId.ToString())).ShouldBeFalse(
                "10 分钟前的标记仍在防重入窗口内，必须跳过");

            // 窗内跳过的广播标记不被覆盖（下次扫描仍能正确判定）
            var skipped = await _broadcastRepository.GetAsync(withinWindowId);
            skipped.ExtraProperties.ContainsKey(
                BroadcastNotificationWatchdogJobHandler.WatchdogRequeuedAtPropertyName).ShouldBeTrue();
        });
    }

    private async Task<List<Guid>> GetNotificationInfoIdsByTitleAsync(string title)
    {
        // ExtraProperties 在实体上是字典（EF 侧是转换列），不能直接 LINQ Contains——测试数据量小，内存过滤
        var infoRepository = GetRequiredService<EasyAbp.NotificationService.NotificationInfos.INotificationInfoRepository>();
        var infos = await infoRepository.GetListAsync();
        return infos.Where(i => i.GetInAppTitle() == title).Select(i => i.Id).ToList();
    }

    [Fact]
    public async Task InApp_Sending_Job_Should_Mark_Notification_Success()
    {
        var title = "ut-send-" + Guid.NewGuid().ToString("N")[..8];
        await WithUnitOfWorkAsync(async () =>
        {
            await _dispatcher.SendInAppAsync(new SendInAppNotificationInput
            {
                UserIds = new List<Guid> { AdminUserId },
                Title = title,
                Body = "正文"
            });
        });

        var notificationId = await WithUnitOfWorkAsync(async () =>
            (await _notificationRepository.GetListAsync(
                    n => n.UserId == AdminUserId && n.NotificationMethod == InAppNotificationConsts.NotificationMethod))
                .OrderByDescending(n => n.CreationTime)
                .First().Id);

        await WithUnitOfWorkAsync(async () =>
        {
            var job = GetRequiredService<InAppNotificationSendingJob>();
            await job.ExecuteAsync(new InAppNotificationSendingJobArgs(null, notificationId));
        });

        await WithUnitOfWorkAsync(async () =>
        {
            var notification = await _notificationRepository.GetAsync(notificationId);
            notification.Success.ShouldBe(true);
            notification.CompletionTime.ShouldNotBeNull();
        });
    }
}
