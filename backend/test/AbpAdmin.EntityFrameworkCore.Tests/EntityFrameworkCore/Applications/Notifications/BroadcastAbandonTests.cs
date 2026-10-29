using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.BackgroundJobs;
using AbpAdmin.ScheduledJobs;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Linq;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace AbpAdmin.Notifications;

using AbpAdmin.EntityFrameworkCore;

/// <summary>
/// NV-17 回归锁：管理端 Abandon 掉广播队列记录后，IsAbandoned 只作用于那一条记录——
/// 看门狗重入队的是一条全新（非 abandoned）记录，被叫停的扇出链 1 小时内按「停滞」复活，
/// 继续给余下收件人发通知。修复：Abandon 把停止意图持久化到广播实体（Cancelled）；
/// 看门狗只挑 Running 天然跳过，广播作业与 Completed/Failed 一并短路。
/// 对照半区：未弃单的真停滞 Running 链仍被看门狗补链（修复不得把看门狗打死）。
/// </summary>
public class BroadcastAbandonTests : AbpAdminEntityFrameworkCoreTestBase
{
    private readonly IRepository<NotificationBroadcast, Guid> _broadcastRepository;
    private readonly IRepository<BackgroundJobRecord, Guid> _jobRecordRepository;
    private readonly IBackgroundJobAppService _jobAppService;
    private readonly IScheduledJobHandler _watchdog;
    private readonly BroadcastNotificationJob _job;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;

    public BroadcastAbandonTests()
    {
        _broadcastRepository = GetRequiredService<IRepository<NotificationBroadcast, Guid>>();
        _jobRecordRepository = GetRequiredService<IRepository<BackgroundJobRecord, Guid>>();
        _jobAppService = GetRequiredService<IBackgroundJobAppService>();
        _watchdog = GetRequiredService<IEnumerable<IScheduledJobHandler>>()
            .Single(h => h.JobType == BroadcastNotificationWatchdogJobHandler.JobTypeName);
        _job = GetRequiredService<BroadcastNotificationJob>();
        _asyncExecuter = GetRequiredService<IAsyncQueryableExecuter>();
        _dataFilter = GetRequiredService<IDataFilter>();
    }

    private async Task<Guid> SeedRunningBroadcastAsync(string title)
    {
        var broadcast = new NotificationBroadcast(
            Guid.NewGuid(), null, NotificationBroadcastTargetTypes.All, null,
            InAppNotificationConsts.NotificationMethod,
            title, "正文", null, null, totalCount: 10);
        broadcast.Start();
        await WithUnitOfWorkAsync(async () => await _broadcastRepository.InsertAsync(broadcast, autoSave: true));
        return broadcast.Id;
    }

    private async Task<Guid> SeedQueueRecordAsync(Guid broadcastId)
    {
        var record = new BackgroundJobRecord(Guid.NewGuid())
        {
            JobName = BackgroundJobNameAttribute.GetName(typeof(BroadcastNotificationJobArgs)),
            JobArgs = System.Text.Json.JsonSerializer.Serialize(new BroadcastNotificationJobArgs(null, broadcastId)),
            TryCount = 0,
        };
        await WithUnitOfWorkAsync(async () => await _jobRecordRepository.InsertAsync(record, autoSave: true));
        return record.Id;
    }

    private async Task<int> QueueCountForAsync(Guid broadcastId)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _jobRecordRepository.GetQueryableAsync();
            return await _asyncExecuter.CountAsync(
                queryable.Where(j => j.JobArgs.Contains(broadcastId.ToString())));
        });
    }

    [Fact]
    public async Task Abandoned_Broadcast_Is_Cancelled_And_Not_Revived()
    {
        var b1 = await SeedRunningBroadcastAsync("nv17-abandoned");
        var recordId = await SeedQueueRecordAsync(b1);

        using (_dataFilter.Disable<IMultiTenant>())
        {
            await WithUnitOfWorkAsync(async () => await _jobAppService.AbandonAsync(recordId));
        }

        (await WithUnitOfWorkAsync(async () => (await _broadcastRepository.GetAsync(b1)).State))
            .ShouldBe(NotificationBroadcastStates.Cancelled, "Abandon 必须把停止意图持久化到广播实体");

        var before = await QueueCountForAsync(b1);
        await WithUnitOfWorkAsync(async () => await _watchdog.ExecuteAsync(null!));
        (await QueueCountForAsync(b1)).ShouldBe(before, "看门狗不得复活 Cancelled 广播");

        await WithUnitOfWorkAsync(async () => await _job.ExecuteAsync(new BroadcastNotificationJobArgs(null, b1)));
        (await WithUnitOfWorkAsync(async () => (await _broadcastRepository.GetAsync(b1)).State))
            .ShouldBe(NotificationBroadcastStates.Cancelled, "广播作业不得续跑被弃单的广播");
    }

    [Fact]
    public async Task Stale_Running_Broadcast_Is_Still_Requeued_By_Watchdog()
    {
        var b2 = await SeedRunningBroadcastAsync("nv17-stale");

        // 伪造停滞：LastModificationTime 越过 StuckThreshold。不能走实体 Update——审计拦截器
        // 会在 SaveChanges 把 LastModificationTime 覆盖回 now；用 ExecuteUpdateAsync 原生 SQL
        // 直改列，绕过拦截器（本文件住 EFCore 测试工程正是为此）。
        await WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _broadcastRepository.GetQueryableAsync();
            await queryable.Where(b => b.Id == b2)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.LastModificationTime, (DateTime?)DateTime.UtcNow.AddHours(-2)));
        });

        var before = await QueueCountForAsync(b2);
        await WithUnitOfWorkAsync(async () => await _watchdog.ExecuteAsync(null!));
        (await QueueCountForAsync(b2)).ShouldBeGreaterThan(before, "未弃单的停滞 Running 广播仍需被看门狗补链");
    }
}
